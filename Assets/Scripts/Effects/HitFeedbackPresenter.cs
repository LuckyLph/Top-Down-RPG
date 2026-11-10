using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Makes hits read on this machine (Docs/Animation.md, Phase 6) from <see cref="CombatEvents.DamageApplied"/>, which
/// every machine raises for every hit it learns of: the host from <see cref="DamageService"/>, clients from
/// <see cref="NetworkHealth"/>, whose hits name their source. A direct hit flashes the struck unit, holds its
/// animation (and the attacker's contact frame, when the hit lands while it shows), nudges it away from the source by
/// whole pixels, plays the impact effect of its damage type where it came in, and kicks the camera along the
/// hit when the local player landed or took it. An immune hit, a tick included, only flashes in the immune colour (as
/// its popup reads Immune), and a damage-over-time tick only washes the unit toward its damage type's colour.
/// Directions come from the point the hit came from as the host resolved it (where an arrow was shot from, where an
/// attacker or caster stood; Docs/KnownGaps.md, Phase 4), the same on every machine, to where this machine shows the
/// target; a hit without one comes from where this machine shows its source. A client's own melee hit it predicts
/// (<see cref="CombatEvents.HitPredicted"/>) holds its attacker's contact frame at once, the rest of the hit waiting for
/// the host's. This machine's preferences (Docs/DesignSystem.md, Phase 6) shape what it shows: with Reduce flashing on,
/// every flash shows at the settings' reduced share of its strength, and with Reduce motion on the camera does not
/// kick. Visual only: nothing here changes gameplay or goes over the network. Allocates nothing once its effects are
/// warm.
/// </summary>
public sealed class HitFeedbackPresenter : IStartable, IDisposable
{
    private readonly CombatEvents combatEvents;
    private readonly HitFeedbackSettings settings;
    private readonly CombatSettings combatSettings;
    private readonly LocalPlayerTracker localPlayer;
    private readonly ICameraKick cameraKick;
    private readonly EffectPool effects;
    private readonly UserSettings preferences;

    /// <summary>
    /// Captures the hits to react to, how they look, the local player whose camera kicks, the camera, the pool impacts
    /// play from and this machine's <paramref name="preferences"/>.
    /// </summary>
    public HitFeedbackPresenter(
        CombatEvents combatEvents,
        HitFeedbackSettings settings,
        CombatSettings combatSettings,
        LocalPlayerTracker localPlayer,
        ICameraKick cameraKick,
        EffectPool effects,
        UserSettings preferences)
    {
        this.combatEvents = combatEvents;
        this.settings = settings;
        this.combatSettings = combatSettings;
        this.localPlayer = localPlayer;
        this.cameraKick = cameraKick;
        this.effects = effects;
        this.preferences = preferences;
    }

    /// <summary>
    /// Starts reacting to hits.
    /// </summary>
    public void Start()
    {
        combatEvents.DamageApplied += Show;
        combatEvents.HitPredicted += ShowPredicted;
    }

    /// <summary>
    /// Stops reacting to hits when the scope is disposed.
    /// </summary>
    public void Dispose()
    {
        combatEvents.DamageApplied -= Show;
        combatEvents.HitPredicted -= ShowPredicted;
    }

    /// <summary>
    /// Shows <paramref name="report"/>: an immune hit's flash (an immune tick's too), a tick's wash, or a direct hit's
    /// flash, holds, nudge and camera kick, then its impact.
    /// </summary>
    internal void Show(DamageReport report)
    {
        Health target = report.Target;
        if (target == null)
        {
            return;
        }

        HitFeedback struck = target.TryGetComponent(out HitFeedback found) ? found : null;
        float strength = preferences.ReduceFlashing ? settings.ReducedFlashShare : 1f;
        if (report.IsImmune)
        {
            if (struck != null)
            {
                struck.Flash(combatSettings.ImmuneColor, strength, settings.ImmuneFlashSeconds);
            }

            return;
        }

        if ((report.Flags & DamageFlags.Periodic) != 0)
        {
            if (struck != null)
            {
                struck.Flash(combatSettings.GetColor(report.Type), settings.PeriodicFlashAmount * strength, settings.PeriodicFlashSeconds);
            }

            return;
        }

        Transform source = UnitOf(report.Source);
        Vector2 targetPosition = target.transform.position;
        bool hasSource = source != null || report.Origin.HasValue;
        Vector2 sourcePosition = report.Origin ?? (source != null ? (Vector2)source.position : targetPosition);
        Vector2 direction = targetPosition - sourcePosition;
        direction = hasSource && direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.zero;
        if (struck != null)
        {
            struck.Flash(settings.HitFlashColor, strength, settings.HitFlashSeconds);
            struck.Hold(settings.HoldSeconds);
            struck.Nudge(direction, settings.NudgePixels, settings.NudgeStepSeconds, settings.PixelsPerUnit);
        }

        if (source != null && settings.HoldAttacker && source.TryGetComponent(out HitFeedback attacker))
        {
            attacker.HoldContact(settings.HoldSeconds);
        }

        Kick(target, source, direction);
        if (struck != null)
        {
            PlayImpact(struck, report.Type, direction, sourcePosition, hasSource);
        }
    }

    /// <summary>
    /// Holds the contact frame of <paramref name="attacker"/>, a client's own player whose melee hit it predicts at its
    /// contact, as a landed hit holds an attacker's.
    /// </summary>
    internal void ShowPredicted(GameObject attacker)
    {
        Transform unit = UnitOf(attacker);
        if (unit != null && settings.HoldAttacker && unit.TryGetComponent(out HitFeedback feedback))
        {
            feedback.HoldContact(settings.HoldSeconds);
        }
    }

    /// <summary>
    /// Plays the impact of <paramref name="type"/> facing <paramref name="direction"/> where the line from
    /// <paramref name="sourcePosition"/> enters <paramref name="struck"/>'s collider (its middle without a source),
    /// raised to its impact height and sorted with the struck unit's feet, so it shows over the unit whichever side the
    /// hit came from.
    /// </summary>
    private void PlayImpact(HitFeedback struck, DamageType type, Vector2 direction, Vector2 sourcePosition, bool hasSource)
    {
        EffectPlayer impact = settings.ImpactEffect;
        if (impact == null || impact.Sprites == null || effects == null)
        {
            return;
        }

        Vector2 ground = struck.ImpactGround(sourcePosition, hasSource);
        effects.Play(impact, (int)type, impact.Sprites.DirectionOf(direction), ground + Vector2.up * struck.ImpactHeight, struck.transform.position.y);
    }

    /// <summary>
    /// Kicks the camera along <paramref name="direction"/> when the local player took the hit or its source is the
    /// local player; nothing without a direction, or while this machine reduces motion.
    /// </summary>
    private void Kick(Health target, Transform source, Vector2 direction)
    {
        LocalPlayer local = localPlayer.Current;
        if (local == null || cameraKick == null || direction == Vector2.zero || preferences.ReduceMotion)
        {
            return;
        }

        bool taken = target == local.Handle.Health;
        bool landed = source != null && source == local.Transform;
        if (taken || landed)
        {
            cameraKick.Kick(direction * settings.KickDistance, settings.KickSeconds);
        }
    }

    /// <summary>
    /// The unit a hit's source belongs to (its <see cref="DamageReceiver"/>, else the source itself); null for none.
    /// </summary>
    private static Transform UnitOf(GameObject source)
    {
        if (source == null)
        {
            return null;
        }

        DamageReceiver unit = CombatFactions.UnitOf(source);
        return unit != null ? unit.transform : source.transform;
    }
}
