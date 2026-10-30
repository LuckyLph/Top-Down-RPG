using UnityEngine;

/// <summary>
/// The day and night (Docs/Lighting.md, Phase 3): how long the day and the night last on the world clock, where in the
/// cycle a new game starts, the sky's and the sun's colours over the cycle, where the sun's shadows fall, and how often
/// the host sends its world clock to the clients again. The cycle runs from 0 at sunrise, through sunset at the day's
/// share of it, to the next sunrise at 1. Runtime code only reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Lighting/Day Night Settings", fileName = "DayNight")]
public sealed class DayNightSettings : ScriptableObject
{
    [Header("Cycle")]
    [SerializeField, Min(1f), Tooltip("How long the day lasts, sunrise to sunset, in seconds of the world clock.")]
    private float daySeconds = 1200f;
    [SerializeField, Min(1f), Tooltip("How long the night lasts, sunset to sunrise, in seconds of the world clock.")]
    private float nightSeconds = 300f;
    [SerializeField, Range(0f, 1f), Tooltip("Where in the cycle a new game starts, the world clock at zero: 0 at sunrise, the day's share at sunset.")]
    private float startTime = 0.16f;

    [Header("Light")]
    [SerializeField, Tooltip("The sky's and the sun's colours at times of the cycle, in order of time. Between two keys they blend; past the last, toward the first.")]
    private DayLightKey[] keys = new DayLightKey[0];

    [Header("Sun")]
    [SerializeField, Min(0f), Tooltip("How long a shadow is at noon, per unit of its caster's height.")]
    private float noonShadowLength = 0.5f;
    [SerializeField, Min(0f), Tooltip("How long a shadow may grow as the sun gets low, per unit of its caster's height.")]
    private float maxShadowLength = 2f;
    [SerializeField, Range(0f, 1f), Tooltip("How far a shadow cast across the screen at sunrise or sunset leans toward the camera, as a share of its length across.")]
    private float shadowLean = 0.5f;

    [Header("Sharing")]
    [SerializeField, Min(1f), Tooltip("How often the host sends its world clock to the clients again, in seconds of the world clock, so their day never drifts from its own.")]
    private float resyncSeconds = 60f;

    public float DaySeconds => daySeconds;
    public float NightSeconds => nightSeconds;
    public float StartTime => startTime;
    public float ResyncSeconds => resyncSeconds;
    public int KeyCount => keys.Length;

    /// <summary>
    /// The length of a whole cycle, a day and a night, in seconds.
    /// </summary>
    public float CycleSeconds => daySeconds + nightSeconds;

    /// <summary>
    /// The day's share of the cycle: where sunset lies.
    /// </summary>
    public float DayShare => daySeconds / CycleSeconds;

    /// <summary>
    /// The key at <paramref name="index"/>, in order of time.
    /// </summary>
    public DayLightKey GetKey(int index)
    {
        return keys[index];
    }

    /// <summary>
    /// Where in the cycle the world clock at <paramref name="worldSeconds"/> lies: the start time plus the cycles played,
    /// in [0, 1).
    /// </summary>
    public float PhaseAt(double worldSeconds)
    {
        double cycles = startTime + worldSeconds / CycleSeconds;
        return Wrap((float)(cycles - System.Math.Floor(cycles)));
    }

    /// <summary>
    /// The light at <paramref name="phase"/> of the cycle: each colour blended between the keys either side of it, by
    /// how far it lies from one to the next, round past the last key to the first. Exactly a key's at its time.
    /// </summary>
    public DayLight LightAt(float phase)
    {
        int count = keys.Length;
        if (count == 0)
        {
            return default;
        }

        phase = Wrap(phase);
        int next = 0;
        while (next < count && keys[next].Time <= phase)
        {
            next++;
        }

        DayLightKey from = keys[(next - 1 + count) % count];
        DayLightKey to = keys[next % count];
        float span = to.Time - from.Time;
        if (span <= 0f)
        {
            span += 1f;
        }

        float into = phase - from.Time;
        if (into < 0f)
        {
            into += 1f;
        }

        float share = Mathf.Clamp01(into / span);
        return new DayLight(Color.Lerp(from.Sky, to.Sky, share), Color.Lerp(from.Sun, to.Sun, share));
    }

    /// <summary>
    /// The sun at <paramref name="phase"/> of the cycle. By day it rises in the east and sets in the west, its height
    /// the sine of the day's progress; a shadow points away from it, across the screen as the cosine (to the left at
    /// sunrise, to the right at sunset), leaning toward the camera by the lean at either end (down by that share of its
    /// way across) and straight down at noon, and on the screen it is as long as the noon length over the sun's height,
    /// up to the longest allowed. At night it is down and casts nothing.
    /// </summary>
    public SunState SunAt(float phase)
    {
        phase = Wrap(phase);
        float dayShare = DayShare;
        if (phase >= dayShare)
        {
            return new SunState(false, 0f, Vector2.zero);
        }

        float angle = Mathf.PI * phase / dayShare;
        float elevation = Mathf.Sin(angle);
        float across = -Mathf.Cos(angle);
        float length = Mathf.Min(noonShadowLength / Mathf.Max(elevation, 1e-4f), maxShadowLength);
        float down = 1f - (1f - shadowLean) * Mathf.Abs(across);
        return new SunState(true, elevation, new Vector2(across, -down).normalized * length);
    }

    /// <summary>
    /// Creates unsaved settings for tests.
    /// </summary>
    public static DayNightSettings Create(float day, float night, float start, DayLightKey[] lightKeys, float noonShadow = 0.5f, float maxShadow = 2f, float lean = 0.5f, float resync = 60f)
    {
        DayNightSettings settings = CreateInstance<DayNightSettings>();
        settings.daySeconds = day;
        settings.nightSeconds = night;
        settings.startTime = start;
        settings.keys = lightKeys;
        settings.noonShadowLength = noonShadow;
        settings.maxShadowLength = maxShadow;
        settings.shadowLean = lean;
        settings.resyncSeconds = resync;
        return settings;
    }

    /// <summary>
    /// Warns when the keys are out of order of time.
    /// </summary>
    private void OnValidate()
    {
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i].Time < keys[i - 1].Time)
            {
                Debug.LogWarning($"{name}: the light keys must be in order of time; key {i} comes before key {i - 1}.", this);
                return;
            }
        }
    }

    /// <summary>
    /// <paramref name="phase"/> wrapped into [0, 1).
    /// </summary>
    private static float Wrap(float phase)
    {
        phase -= Mathf.Floor(phase);
        return phase >= 1f ? 0f : phase;
    }
}
