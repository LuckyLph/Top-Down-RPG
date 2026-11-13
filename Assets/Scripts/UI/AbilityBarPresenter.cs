using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="AbilityBarView"/> to the local player (rebinding when <see cref="LocalPlayerTracker"/> changes):
/// slot contents on <c>SlotsChanged</c>, cooldowns every frame, pushed only when they change, and the answers to a
/// press (Docs/UiRevamp.md, Decisions: Failures and Slot states): a flash on <c>CastFailed</c> with its reason on the
/// message line for <see cref="HudSettings.MessageSeconds"/> (<see cref="CastFailureMessages"/>), the frame of the slot
/// whose cast runs (<c>CastStarted</c>, <c>CastEnded</c>), the brackets of the slot whose cast waits in the buffer
/// (<see cref="PlayerController.BufferedCastChanged"/>) and the hatch of every slot the player's crowd control blocks
/// (<see cref="StatusEffects.Controls"/>). All of it is on the machine that runs the player's orders, a joined client's
/// own player included. Key labels follow <see cref="IInputBindingLabels"/>.
/// </summary>
public sealed class AbilityBarPresenter : IStartable, ITickable, IDisposable
{
    public const float FlashSeconds = 0.35f;

    private readonly LocalPlayerTracker localPlayer;
    private readonly AbilityBarView view;
    private readonly IClock clock;
    private readonly IInputBindingLabels keyLabels;
    private readonly HudSettings settings;
    private readonly float[] shownFraction = new float[AbilitySlots.Count];
    private readonly int[] shownSeconds = new int[AbilitySlots.Count];
    private readonly float[] flashEndTimes = new float[AbilitySlots.Count];
    private PlayerAbilities abilities;
    private PlayerController controller;
    private StatusEffects statuses;
    private int castingSlot = -1;
    private int queuedSlot = -1;
    private StatusControls controls;
    private bool messageShown;
    private float messageEndTime;

    /// <summary>
    /// Creates a presenter that drives <paramref name="view"/> for whichever player <paramref name="localPlayer"/>
    /// tracks, timing flashes and messages on <paramref name="clock"/> and messages by <paramref name="settings"/>.
    /// </summary>
    public AbilityBarPresenter(LocalPlayerTracker localPlayer, AbilityBarView view, IClock clock, IInputBindingLabels keyLabels, HudSettings settings)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        this.clock = clock;
        this.keyLabels = keyLabels;
        this.settings = settings;
    }

    /// <summary>
    /// Subscribes to local player and binding changes, shows the current key labels and binds the current player.
    /// </summary>
    public void Start()
    {
        localPlayer.Changed += Bind;
        keyLabels.BindingsChanged += RefreshKeys;
        RefreshKeys();
        Bind(localPlayer.Current);
    }

    /// <summary>
    /// Unsubscribes from the tracker, the binding labels and the bound player.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        keyLabels.BindingsChanged -= RefreshKeys;
        Unbind();
    }

    /// <summary>
    /// Hides a message whose time is up, pushes changed cooldowns and fades failure flashes out over
    /// <see cref="FlashSeconds"/>. Pushes nothing while no player is bound.
    /// </summary>
    public void Tick()
    {
        float now = clock.Time;
        if (messageShown && now >= messageEndTime)
        {
            HideMessage();
        }

        if (abilities == null)
        {
            return;
        }

        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            RefreshCooldown(slot);
            float flashLeft = flashEndTimes[slot] - now;
            if (flashLeft > -FlashSeconds)
            {
                view.SetFlash(slot, Mathf.Clamp01(flashLeft / FlashSeconds));
            }
        }
    }

    /// <summary>
    /// Switches to <paramref name="player"/>'s abilities, orders and statuses and shows its slots and their states, or
    /// clears the bar when there is no player or it has no abilities component.
    /// </summary>
    private void Bind(LocalPlayer player)
    {
        Unbind();
        abilities = player != null ? player.Abilities : null;
        if (abilities == null)
        {
            view.Clear();
            return;
        }

        controller = player.Controller;
        statuses = controller.GetComponent<StatusEffects>();
        abilities.SlotsChanged += RefreshSlots;
        abilities.CastFailed += HandleCastFailed;
        abilities.CastStarted += HandleCastStarted;
        abilities.CastEnded += HandleCastEnded;
        controller.BufferedCastChanged += HandleBufferedCastChanged;
        if (statuses != null)
        {
            statuses.Changed += HandleStatusesChanged;
        }

        RefreshSlots();
    }

    /// <summary>
    /// Stops listening to the bound player, forgets it and hides the message line.
    /// </summary>
    private void Unbind()
    {
        if (abilities != null)
        {
            abilities.SlotsChanged -= RefreshSlots;
            abilities.CastFailed -= HandleCastFailed;
            abilities.CastStarted -= HandleCastStarted;
            abilities.CastEnded -= HandleCastEnded;
        }

        if (controller != null)
        {
            controller.BufferedCastChanged -= HandleBufferedCastChanged;
        }

        if (statuses != null)
        {
            statuses.Changed -= HandleStatusesChanged;
        }

        abilities = null;
        controller = null;
        statuses = null;
        castingSlot = -1;
        queuedSlot = -1;
        controls = StatusControls.None;
        HideMessage();
    }

    /// <summary>
    /// Relabels every slot with the key currently bound to it.
    /// </summary>
    private void RefreshKeys()
    {
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            view.SetKey(slot, keyLabels.GetAbilityKeyLabel(slot));
        }
    }

    /// <summary>
    /// Shows the bound player's ability in every slot, re-pushes each slot's cooldown from scratch, and shows which
    /// cast runs, which waits and which slots crowd control blocks.
    /// </summary>
    private void RefreshSlots()
    {
        castingSlot = abilities.RunningSlot;
        queuedSlot = controller != null ? controller.BufferedSlot : -1;
        controls = statuses != null ? statuses.Controls : StatusControls.None;
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            view.SetAbility(slot, abilities.GetAbility(slot));
            shownFraction[slot] = 0f;
            shownSeconds[slot] = 0;
            RefreshCooldown(slot);
            view.SetCasting(slot, slot == castingSlot);
            view.SetQueued(slot, slot == queuedSlot);
            view.SetDisabled(slot, IsBlocked(slot));
        }
    }

    /// <summary>
    /// Sends <paramref name="slot"/>'s remaining cooldown fraction and whole seconds to the view, only when they
    /// differ from what it last showed. An empty slot or a zero-length cooldown shows no fill.
    /// </summary>
    private void RefreshCooldown(int slot)
    {
        AbilityDefinition ability = abilities.GetAbility(slot);
        float remaining = ability != null ? abilities.RemainingCooldown(slot) : 0f;
        float fraction = ability != null && ability.Cooldown > 0f ? remaining / ability.Cooldown : 0f;
        int seconds = Mathf.CeilToInt(remaining);
        if (Mathf.Approximately(fraction, shownFraction[slot]) && seconds == shownSeconds[slot])
        {
            return;
        }

        shownFraction[slot] = fraction;
        shownSeconds[slot] = seconds;
        view.SetCooldown(slot, fraction, seconds);
    }

    /// <summary>
    /// Whether the player's crowd control blocks <paramref name="slot"/>'s ability: every ability while stunned or
    /// silenced, and those whose cast moves the caster while rooted. An empty slot is never blocked.
    /// </summary>
    private bool IsBlocked(int slot)
    {
        AbilityDefinition ability = abilities.GetAbility(slot);
        if (ability == null)
        {
            return false;
        }

        if ((controls & (StatusControls.Stun | StatusControls.Silence)) != 0)
        {
            return true;
        }

        return (controls & StatusControls.Root) != 0 && ability.CastMovement == CastMovement.Ability;
    }

    /// <summary>
    /// Starts a full-strength failure flash on <paramref name="slot"/> (out-of-range slots are ignored) and shows the
    /// <paramref name="reason"/>'s words on the message line, when it has any.
    /// </summary>
    private void HandleCastFailed(int slot, CastOutcome reason)
    {
        if (slot >= 0 && slot < flashEndTimes.Length)
        {
            flashEndTimes[slot] = clock.Time + FlashSeconds;
            view.SetFlash(slot, 1f);
        }

        string message = CastFailureMessages.For(reason);
        if (message == null)
        {
            return;
        }

        view.ShowMessage(message);
        messageShown = true;
        messageEndTime = clock.Time + settings.MessageSeconds;
    }

    /// <summary>
    /// Frames the slot whose cast started, unframing the one before.
    /// </summary>
    private void HandleCastStarted(AbilityCast cast)
    {
        if (castingSlot >= 0)
        {
            view.SetCasting(castingSlot, false);
        }

        castingSlot = cast.Slot;
        view.SetCasting(castingSlot, true);
    }

    /// <summary>
    /// Unframes the slot whose cast ended, completed or not.
    /// </summary>
    private void HandleCastEnded(int slot, bool completed)
    {
        view.SetCasting(slot, false);
        if (slot == castingSlot)
        {
            castingSlot = -1;
        }
    }

    /// <summary>
    /// Moves the brackets to the slot whose cast was buffered, or takes them off when the buffer cleared (-1).
    /// </summary>
    private void HandleBufferedCastChanged(int slot)
    {
        if (queuedSlot >= 0)
        {
            view.SetQueued(queuedSlot, false);
        }

        queuedSlot = slot;
        if (queuedSlot >= 0)
        {
            view.SetQueued(queuedSlot, true);
        }
    }

    /// <summary>
    /// Re-hatches the slots when the player's crowd control changed.
    /// </summary>
    private void HandleStatusesChanged()
    {
        StatusControls current = statuses.Controls;
        if (current == controls)
        {
            return;
        }

        controls = current;
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            view.SetDisabled(slot, IsBlocked(slot));
        }
    }

    /// <summary>
    /// Hides the message line.
    /// </summary>
    private void HideMessage()
    {
        messageShown = false;
        view.HideMessage();
    }
}
