using System;
using System.Text;
using UnityEngine;

/// <summary>
/// What the lighting dev panel does (Docs/Lighting.md), apart from its keys and its drawing, so it can be tested without
/// input: moves this machine's time of day to a light key, steps it, holds it, runs it faster and lets it follow the
/// session again, through the <see cref="DayClock"/>'s local offset and rate; forces the sun's shadows, the prop lights,
/// the ground's glow, the flicker and the glowing pixels through the overrides of <see cref="DayNightPresenter"/> and
/// <see cref="PropLighting"/>; teleports the local player to the entries of its world; and writes the readout. Nothing
/// it changes is saved or sent.
/// </summary>
public sealed class LightingDebugControls
{
    public const float TimeStep = 0.01f;
    public const float FineTimeStep = 0.001f;

    private const float KeyTolerance = 0.0005f;

    private static readonly float[] Rates = { 1f, 10f, 60f };
    private static readonly string[] KeyNames = { "sunrise", "early morning", "morning", "noon", "afternoon", "evening", "sunset", "dusk", "night", "before dawn" };

    private readonly DayClock clock;
    private readonly DayNightPresenter presenter;
    private readonly PropLighting propLighting;
    private readonly Func<WorldDefinition> currentWorld;
    private readonly Action<Vector2> teleport;
    private readonly StringBuilder text = new();
    private float runningRate = 1f;
    private int spot = -1;

    /// <summary>
    /// Creates the controls over <paramref name="dayClock"/>, <paramref name="dayNight"/> and
    /// <paramref name="props"/> (any may be null, leaving its controls without effect), teleporting the local player
    /// with <paramref name="teleportPlayer"/> to the entries of <paramref name="world"/>.
    /// </summary>
    public LightingDebugControls(DayClock dayClock, DayNightPresenter dayNight, PropLighting props, Func<WorldDefinition> world, Action<Vector2> teleportPlayer)
    {
        clock = dayClock;
        presenter = dayNight;
        propLighting = props;
        currentWorld = world;
        teleport = teleportPlayer;
    }

    public DayClock Clock => clock;
    public DayNightPresenter Presenter => presenter;
    public PropLighting PropLighting => propLighting;

    /// <summary>
    /// The name of light key <paramref name="index"/>: the shipped day's ten keys by name, any other by its number.
    /// </summary>
    public string KeyName(int index)
    {
        return clock != null && clock.Settings.KeyCount == KeyNames.Length ? KeyNames[index] : $"key {index + 1}";
    }

    /// <summary>
    /// Moves this machine's time of day to light key <paramref name="index"/>. Returns false without such a key.
    /// </summary>
    public bool ShowKey(int index)
    {
        if (clock == null || index < 0 || index >= clock.Settings.KeyCount)
        {
            return false;
        }

        clock.ShowPhase(clock.Settings.GetKey(index).Time);
        return true;
    }

    /// <summary>
    /// Moves this machine's time of day by <paramref name="steps"/> steps, fine ones with <paramref name="fine"/>.
    /// </summary>
    public void StepTime(int steps, bool fine)
    {
        if (clock != null)
        {
            clock.ShowPhase(clock.Phase + steps * (fine ? FineTimeStep : TimeStep));
        }
    }

    /// <summary>
    /// Holds this machine's time of day, or lets it run on at the rate it ran at.
    /// </summary>
    public void ToggleHold()
    {
        if (clock == null)
        {
            return;
        }

        if (clock.LocalRate == 0f)
        {
            clock.LocalRate = runningRate;
        }
        else
        {
            runningRate = clock.LocalRate;
            clock.LocalRate = 0f;
        }
    }

    /// <summary>
    /// Runs this machine's time of day at the next of its rates (1, 10 and 60 times the session's), letting it run if
    /// it was held.
    /// </summary>
    public void NextRate()
    {
        if (clock == null)
        {
            return;
        }

        int current = Array.IndexOf(Rates, clock.LocalRate == 0f ? runningRate : clock.LocalRate);
        runningRate = Rates[(current + 1) % Rates.Length];
        clock.LocalRate = runningRate;
    }

    /// <summary>
    /// This machine's time of day follows the session's again.
    /// </summary>
    public void FollowSession()
    {
        runningRate = 1f;
        clock?.FollowSession();
    }

    /// <summary>
    /// The sun's shadows follow the day, or are kept off.
    /// </summary>
    public void CycleSunShadows()
    {
        if (presenter != null)
        {
            presenter.SunShadowsOverride = presenter.SunShadowsOverride == LightingOverride.Off ? LightingOverride.Follow : LightingOverride.Off;
        }
    }

    /// <summary>
    /// The prop lights follow the day, then burn at full, then not at all.
    /// </summary>
    public void CyclePropLights()
    {
        if (propLighting != null)
        {
            propLighting.PropLightsOverride = Next(propLighting.PropLightsOverride);
        }
    }

    /// <summary>
    /// The ground's glow follows the day, then burns at full, then not at all.
    /// </summary>
    public void CycleGroundGlow()
    {
        if (propLighting != null)
        {
            propLighting.GroundGlowOverride = Next(propLighting.GroundGlowOverride);
        }
    }

    /// <summary>
    /// The lights and the glowing pixels flicker, or stand still.
    /// </summary>
    public void CycleFlicker()
    {
        if (propLighting != null)
        {
            propLighting.FlickerOverride = propLighting.FlickerOverride == LightingOverride.Off ? LightingOverride.Follow : LightingOverride.Off;
        }
    }

    /// <summary>
    /// Glowing pixels glow, or draw lit like any other.
    /// </summary>
    public void CycleGlowingPixels()
    {
        if (propLighting != null)
        {
            propLighting.GlowingPixelsOverride = propLighting.GlowingPixelsOverride == LightingOverride.Off ? LightingOverride.Follow : LightingOverride.Off;
        }
    }

    /// <summary>
    /// Teleports the local player to the entry <paramref name="steps"/> entries on from the last one it was sent to in
    /// its world (the first, going forward, when none was). Returns the entry's name, or null when its world has none.
    /// </summary>
    public string Teleport(int steps)
    {
        WorldDefinition world = currentWorld?.Invoke();
        if (world == null || world.Entries.Count == 0)
        {
            return null;
        }

        int count = world.Entries.Count;
        int from = spot >= 0 && spot < count ? spot : steps > 0 ? -1 : 0;
        spot = ((from + steps) % count + count) % count;
        WorldDefinition.Entry entry = world.Entries[spot];
        teleport?.Invoke(world.ToWorld(entry.Position));
        return entry.Name;
    }

    /// <summary>
    /// The last entry the local player was sent to in <see cref="Teleport"/>, as "index/count name", or a dash.
    /// </summary>
    public string SpotText()
    {
        WorldDefinition world = currentWorld?.Invoke();
        return world != null && spot >= 0 && spot < world.Entries.Count ? $"{spot + 1}/{world.Entries.Count} {world.Entries[spot].Name}" : "-";
    }

    /// <summary>
    /// The readout: the time of day, how it runs and whether the sun is up and casts, the sky's and the sun's colours,
    /// the night lights' share, how many chunks' lights burn, the spot and <paramref name="fps"/>.
    /// </summary>
    public string Readout(float fps)
    {
        text.Clear();
        text.Append(fps.ToString("F0")).Append(" fps\n");
        if (clock == null)
        {
            text.Append("No day clock: start a game in a world.");
            return text.ToString();
        }

        float phase = clock.Phase;
        SunState sun = clock.Sun;
        DayLight light = clock.Light;
        text.Append("Time ").Append(phase.ToString("F3")).Append(", ").Append((phase * clock.Settings.CycleSeconds / 60f).ToString("F1"))
            .Append(" min after sunrise, ").Append(KeyAround(phase)).Append('\n');
        text.Append(RateText()).Append('\n');
        text.Append("Sun ").Append(sun.IsUp ? $"up, {sun.Elevation:F2} high" : "down").Append(presenter != null && presenter.Casting ? ", casting" : ", not casting").Append('\n');
        text.Append("Sky ").Append(Rgb(light.Sky)).Append("   Sun ").Append(Rgb(light.Sun)).Append('\n');
        text.Append("Night lights ").Append(light.NightLights.ToString("F2"));
        if (propLighting != null)
        {
            text.Append(", PropLighting holds ").Append(propLighting.GroupCount).Append(propLighting.GroupCount == 1 ? " chunk" : " chunks");
        }

        text.Append("\nSpot ").Append(SpotText());
        return text.ToString();
    }

    /// <summary>
    /// How this machine's time of day runs: following the session, held, or at a rate, and offset or not.
    /// </summary>
    public string RateText()
    {
        if (clock == null)
        {
            return "-";
        }

        if (clock.LocalRate == 0f)
        {
            return "held";
        }

        if (clock.LocalRate != 1f)
        {
            return $"running x{clock.LocalRate:F0}";
        }

        return clock.LocalOffset == 0f ? "following the session" : "running, moved from the session";
    }

    /// <summary>
    /// The state an override is shown in: "follow", "ON" or "OFF".
    /// </summary>
    public static string StateText(LightingOverride mode)
    {
        return mode switch
        {
            LightingOverride.On => "ON",
            LightingOverride.Off => "OFF",
            _ => "follow"
        };
    }

    /// <summary>
    /// Follow, then on, then off, then follow again.
    /// </summary>
    private static LightingOverride Next(LightingOverride mode)
    {
        return mode switch
        {
            LightingOverride.Follow => LightingOverride.On,
            LightingOverride.On => LightingOverride.Off,
            _ => LightingOverride.Follow
        };
    }

    /// <summary>
    /// The light key at or before <paramref name="phase"/> and the one after it, by name.
    /// </summary>
    private string KeyAround(float phase)
    {
        DayNightSettings settings = clock.Settings;
        int count = settings.KeyCount;
        if (count == 0)
        {
            return "no keys";
        }

        int before = count - 1;
        for (int i = 0; i < count; i++)
        {
            float time = settings.GetKey(i).Time;
            if (Mathf.Abs(time - phase) < KeyTolerance)
            {
                return KeyName(i);
            }

            if (time <= phase)
            {
                before = i;
            }
        }

        return $"{KeyName(before)} to {KeyName((before + 1) % count)}";
    }

    /// <summary>
    /// A colour's red, green and blue to two decimals.
    /// </summary>
    private static string Rgb(Color colour)
    {
        return $"{colour.r:F2} {colour.g:F2} {colour.b:F2}";
    }
}
