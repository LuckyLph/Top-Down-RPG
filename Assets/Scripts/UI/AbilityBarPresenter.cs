using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Binds the <see cref="AbilityBarView"/> to the local player's <see cref="PlayerAbilities"/> (rebinding when
/// <see cref="LocalPlayerTracker"/> changes): slot contents on <c>SlotsChanged</c>, a flash on <c>CastFailed</c>,
/// and cooldowns every frame, pushed only when they change.
/// </summary>
public sealed class AbilityBarPresenter : IStartable, ITickable, IDisposable
{
    public const float FlashSeconds = 0.35f;

    private readonly LocalPlayerTracker localPlayer;
    private readonly AbilityBarView view;
    private readonly IClock clock;
    private readonly float[] shownFraction = new float[AbilitySlots.Count];
    private readonly int[] shownSeconds = new int[AbilitySlots.Count];
    private readonly float[] flashEndTimes = new float[AbilitySlots.Count];
    private PlayerAbilities abilities;

    public AbilityBarPresenter(LocalPlayerTracker localPlayer, AbilityBarView view, IClock clock)
    {
        this.localPlayer = localPlayer;
        this.view = view;
        this.clock = clock;
    }

    public void Start()
    {
        localPlayer.Changed += Bind;
        Bind(localPlayer.Current);
    }

    public void Dispose()
    {
        localPlayer.Changed -= Bind;
        Unbind();
    }

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

    private void Unbind()
    {
        if (abilities != null)
        {
            abilities.SlotsChanged -= RefreshSlots;
            abilities.CastFailed -= HandleCastFailed;
        }

        abilities = null;
    }

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
