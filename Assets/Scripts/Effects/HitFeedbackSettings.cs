using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How hits read on every machine (Docs/Animation.md, Phase 6): the struck unit's flash, hit-stop and nudge, the impact
/// effect where the hit lands, and the local player's camera kick. Read by <see cref="HitFeedbackPresenter"/>; visual
/// only, nothing in gameplay reads it. Immune hits flash in <see cref="CombatSettings.ImmuneColor"/> and damage over time
/// washes toward its type's <see cref="CombatSettings.GetColor"/>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Effects/Hit Feedback Settings", fileName = "HitFeedbackSettings")]
public sealed class HitFeedbackSettings : ScriptableObject
{
    [Header("Flash")]
    [SerializeField, Tooltip("The colour a struck unit turns, unlit, on a direct hit.")]
    private Color hitFlashColor = Color.white;
    [SerializeField, Min(0f), Tooltip("How long a direct hit's flash lasts, in seconds.")]
    private float hitFlashSeconds = 0.05f;
    [SerializeField, Min(0f), Tooltip("How long an immune hit's flash, in the immune colour, lasts, in seconds.")]
    private float immuneFlashSeconds = 0.05f;
    [SerializeField, Range(0f, 1f), Tooltip("How far a damage-over-time tick washes the unit toward its damage type's colour.")]
    private float periodicFlashAmount = 0.4f;
    [SerializeField, Min(0f), Tooltip("How long a damage-over-time tick's wash lasts, in seconds.")]
    private float periodicFlashSeconds = 0.08f;

    [Header("Hit-stop")]
    [SerializeField, Min(0f), Tooltip("How long a struck unit's animation holds on a direct hit, in seconds.")]
    private float holdSeconds = 0.08f;
    [SerializeField, Tooltip("Whether the attacker holds its contact frame too, when its hit lands while the contact shows.")]
    private bool holdAttacker = true;

    [Header("Nudge")]
    [SerializeField, Tooltip("How many whole pixels the struck unit's sprite moves away from the hit, step by step, before it goes back.")]
    private int[] nudgePixels = { 2, 1 };
    [SerializeField, Min(0f), Tooltip("How long each nudge step lasts, in seconds.")]
    private float nudgeStepSeconds = 0.04f;
    [SerializeField, Min(1), Tooltip("Pixels per unit of the art the nudge and the camera kick move by.")]
    private int pixelsPerUnit = 32;

    [Header("Impact")]
    [SerializeField, Tooltip("The impact effect played where a direct hit lands, one variant per damage type.")]
    private EffectPlayer impactEffect;

    [Header("Camera kick")]
    [SerializeField, Min(0f), Tooltip("How many pixels the local player's camera is knocked along a hit it lands or takes.")]
    private float kickPixels = 2f;
    [SerializeField, Min(0f), Tooltip("How long the camera takes to ease back, in seconds.")]
    private float kickSeconds = 0.12f;

    public Color HitFlashColor => hitFlashColor;
    public float HitFlashSeconds => hitFlashSeconds;
    public float ImmuneFlashSeconds => immuneFlashSeconds;
    public float PeriodicFlashAmount => periodicFlashAmount;
    public float PeriodicFlashSeconds => periodicFlashSeconds;
    public float HoldSeconds => holdSeconds;
    public bool HoldAttacker => holdAttacker;
    public IReadOnlyList<int> NudgePixels => nudgePixels;
    public float NudgeStepSeconds => nudgeStepSeconds;
    public int PixelsPerUnit => pixelsPerUnit;
    public EffectPlayer ImpactEffect => impactEffect;
    public float KickDistance => kickPixels / pixelsPerUnit;
    public float KickSeconds => kickSeconds;

    /// <summary>
    /// Creates unsaved settings with this asset's defaults and <paramref name="impact"/> as the impact effect, for
    /// tests.
    /// </summary>
    public static HitFeedbackSettings Create(EffectPlayer impact)
    {
        HitFeedbackSettings settings = CreateInstance<HitFeedbackSettings>();
        settings.impactEffect = impact;
        return settings;
    }
}
