using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="AbilityBarView"/> to the local player's <see cref="PlayerAbilities"/> (rebinding when
/// <see cref="LocalPlayerTracker"/> changes): slot contents on <c>SlotsChanged</c>, a flash on <c>CastFailed</c>,
/// and cooldowns every frame, pushed only when they change. Key labels follow <see cref="IInputBindingLabels"/>.
/// </summary>
public sealed class AbilityBarPresenter : IStartable, ITickable, IDisposable
{
    public const float FlashSeconds = 0.35f;

    private readonly LocalPlayerTracker localPlayer;
    private readonly AbilityBarView view;
    private readonly IClock clock;
    private readonly IInputBindingLabels keyLabels;
    private readonly float[] shownFraction = new float[AbilitySlots.Count];
    private readonly int[] shownSeconds = new int[AbilitySlots.Count];
    private readonly float[] flashEndTimes = new float[AbilitySlots.Count];
    private PlayerAbilities abilities;

    /// <summary>
    /// Creates a presenter that drives <paramref name="view"/> for whichever player <paramref name="localPlayer"/>
    /// tracks, timing flashes on <paramref name="clock"/>.
    /// </summary>
    public AbilityBarPresenter(LocalPlayerTracker localPlayer, AbilityBarView view, IClock clock, IInputBindingLabels keyLabels)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        this.clock = clock;
        this.keyLabels = keyLabels;
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
    /// Unsubscribes from the tracker, the binding labels and the bound player's abilities.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        keyLabels.BindingsChanged -= RefreshKeys;
        Unbind();
    }

    /// <summary>
    /// Pushes changed cooldowns and fades failure flashes out over <see cref="FlashSeconds"/>. Does nothing while
    /// no player is bound.
    /// </summary>
    public void Tick()
    {
        if (abilities == null)
        {
            return;
        }

        float now = clock.Time;
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
    /// Switches to <paramref name="player"/>'s <see cref="PlayerAbilities"/> and shows its slots, or clears the bar
    /// when there is no player or it has no abilities component.
    /// </summary>
    private void Bind(LocalPlayer player)
    {
        Unbind();
        abilities = player != null ? player.Controller.GetComponent<PlayerAbilities>() : null;
        if (abilities == null)
        {
            view.Clear();
            return;
        }

        abilities.SlotsChanged += RefreshSlots;
        abilities.CastFailed += HandleCastFailed;
        RefreshSlots();
    }

    /// <summary>
    /// Stops listening to the bound player's abilities and forgets them.
    /// </summary>
    private void Unbind()
    {
        if (abilities != null)
        {
            abilities.SlotsChanged -= RefreshSlots;
            abilities.CastFailed -= HandleCastFailed;
        }

        abilities = null;
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
    /// Shows the bound player's ability in every slot and re-pushes each slot's cooldown from scratch.
    /// </summary>
    private void RefreshSlots()
    {
        for (int slot = 0; slot < AbilitySlots.Count; slot++)
        {
            view.SetAbility(slot, abilities.GetAbility(slot));
            shownFraction[slot] = 0f;
            shownSeconds[slot] = 0;
            RefreshCooldown(slot);
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
    /// Starts a full-strength failure flash on <paramref name="slot"/>, whatever the <paramref name="reason"/>.
    /// Out-of-range slots are ignored.
    /// </summary>
    private void HandleCastFailed(int slot, CastOutcome reason)
    {
        if (slot < 0 || slot >= flashEndTimes.Length)
        {
            return;
        }

        flashEndTimes[slot] = clock.Time + FlashSeconds;
        view.SetFlash(slot, 1f);
    }
}
