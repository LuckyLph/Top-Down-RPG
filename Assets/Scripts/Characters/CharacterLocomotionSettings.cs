using UnityEngine;

/// <summary>
/// How a character's movement picks and plays its locomotion clips (Docs/Animation.md, Runtime): the speeds where it
/// starts walking, running and sprinting, the hysteresis around those boundaries, the range its gaits' playback rate
/// is clamped to, how far past a boundary between two directions its facing must turn before the drawn direction
/// changes, how long a copy of another machine's attack or cast waits for its contact past its windup before it goes
/// back to its locomotion, and how long the character stands still before its first idle animation and between the
/// next ones (Docs/PlayerCharacter.md, Phase 3). Referenced by <see cref="CharacterVisuals"/>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Locomotion Settings", fileName = "Locomotion")]
public sealed class CharacterLocomotionSettings : ScriptableObject
{
    [Header("Gaits (units per second)")]
    [SerializeField, Min(0f), Tooltip("Below this speed the character stands idle; from it, it walks.")]
    private float walkFrom = 0.1f;

    [SerializeField, Min(0f), Tooltip("From this speed the character runs.")]
    private float runFrom = 2.4f;

    [SerializeField, Min(0f), Tooltip("From this speed the character sprints.")]
    private float sprintFrom = 5.6f;

    [SerializeField, Range(0f, 0.5f), Tooltip("A boundary is crossed upward only this share above it and downward only this share below it, so a speed near it does not flicker between gaits.")]
    private float gaitHysteresis = 0.08f;

    [Header("Playback")]
    [SerializeField, Min(0.01f), Tooltip("The slowest a gait plays, as a share of its natural rate.")]
    private float minRate = 0.5f;

    [SerializeField, Min(0.01f), Tooltip("The fastest a gait plays, as a share of its natural rate.")]
    private float maxRate = 1.5f;

    [Header("Direction")]
    [SerializeField, Range(0f, 22.5f), Tooltip("Degrees past the boundary between two directions the facing must turn before the drawn direction changes.")]
    private float directionMargin = 8f;

    [Header("Copies")]
    [SerializeField, Min(0f), Tooltip("Seconds past its windup an action played until its contact (a copy of another machine's attack or cast) holds its last windup frame waiting for the owner's contact or completion before it ends and the character goes back to its locomotion; a later contact still lands its hit.")]
    private float contactWaitSeconds = 0.25f;

    [Header("Idle animations")]
    [SerializeField, Min(0f), Tooltip("Seconds the character stands still before its first idle animation plays.")]
    private float idleAnimationAfter = 8f;
    [SerializeField, Tooltip("The fewest and most seconds from the end of one idle animation to the start of the next while the character keeps standing still.")]
    private Vector2 idleAnimationGap = new(10f, 16f);

    public float WalkFrom => walkFrom;
    public float RunFrom => Mathf.Max(walkFrom, runFrom);
    public float SprintFrom => Mathf.Max(RunFrom, sprintFrom);
    public float GaitHysteresis => gaitHysteresis;
    public float MinRate => Mathf.Min(minRate, maxRate);
    public float MaxRate => maxRate;
    public float DirectionMargin => directionMargin;
    public float ContactWaitSeconds => contactWaitSeconds;
    public float IdleAnimationAfter => idleAnimationAfter;
    public float IdleAnimationGapMin => Mathf.Max(0f, Mathf.Min(idleAnimationGap.x, idleAnimationGap.y));
    public float IdleAnimationGapMax => Mathf.Max(0f, Mathf.Max(idleAnimationGap.x, idleAnimationGap.y));

    /// <summary>
    /// Creates unsaved settings, for tests.
    /// </summary>
    public static CharacterLocomotionSettings Create(float walk, float run, float sprint, float hysteresis, float slowestRate, float fastestRate, float margin, float contactWait = 0.25f, float idleAfter = 8f, float idleGapMin = 10f, float idleGapMax = 16f)
    {
        CharacterLocomotionSettings settings = CreateInstance<CharacterLocomotionSettings>();
        settings.walkFrom = walk;
        settings.runFrom = run;
        settings.sprintFrom = sprint;
        settings.gaitHysteresis = hysteresis;
        settings.minRate = slowestRate;
        settings.maxRate = fastestRate;
        settings.directionMargin = margin;
        settings.contactWaitSeconds = contactWait;
        settings.idleAnimationAfter = idleAfter;
        settings.idleAnimationGap = new Vector2(idleGapMin, idleGapMax);
        return settings;
    }

    /// <summary>
    /// The speed from which <paramref name="gait"/> is picked: 0 for idle, and the gait's own boundary for the others.
    /// </summary>
    public float From(CharacterGait gait)
    {
        switch (gait)
        {
            case CharacterGait.Walk:
                return WalkFrom;
            case CharacterGait.Run:
                return RunFrom;
            case CharacterGait.Sprint:
                return SprintFrom;
            default:
                return 0f;
        }
    }
}
