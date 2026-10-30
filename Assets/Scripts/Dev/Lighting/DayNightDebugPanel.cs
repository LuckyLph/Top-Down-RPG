using UnityEngine;
using VContainer;

/// <summary>
/// Dev-only way to move the time of day on this machine (Docs/Lighting.md, Phase 3): F7 toggles an IMGUI panel with a
/// slider over the cycle (0 at sunrise, sunset at the day's share) showing where the day is, and a button to follow the
/// session's time again. It moves the <see cref="DayClock"/>'s local offset only, so no other machine's sky changes and
/// nothing is saved. Created once per play session; it finds the Gameplay scope when used, because the Gameplay scope
/// cannot reference this assembly.
/// </summary>
[DisallowMultipleComponent]
public class DayNightDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F7;
    private const float Width = 260f;
    private const float ScopeLookupInterval = 1f;

    private bool open;
    private GameplayLifetimeScope gameplayScope;
    private float nextScopeLookup;

    public bool IsOpen => open;

    /// <summary>
    /// Spawns the panel on a persistent game object after the first scene loads, once per play session.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForPlaySession()
    {
        GameObject panel = new(nameof(DayNightDebugPanel));
        DontDestroyOnLoad(panel);
        panel.AddComponent<DayNightDebugPanel>();
    }

    /// <summary>
    /// Moves this machine's time of day to <paramref name="phase"/> of the cycle, as the session's clock runs on.
    /// Returns false outside a game session.
    /// </summary>
    public bool ShowTimeOfDay(float phase)
    {
        DayClock clock = Clock();
        if (clock == null)
        {
            return false;
        }

        clock.LocalOffset = phase - clock.SessionPhase;
        return true;
    }

    /// <summary>
    /// Handles the hotkey and, while open, draws the slider and the button.
    /// </summary>
    private void OnGUI()
    {
        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == ToggleKey)
        {
            open = !open;
        }

        if (!open)
        {
            return;
        }

        DayClock clock = Clock();
        GUILayout.BeginArea(new Rect(10f, Screen.height - 120f, Width, 110f), GUI.skin.box);
        GUILayout.Label("Time of day (F7 hide)");
        if (clock == null)
        {
            GUILayout.Label("Start a game to move the time of day.");
            GUILayout.EndArea();
            return;
        }

        float phase = clock.Phase;
        float minutes = phase * clock.Settings.CycleSeconds / 60f;
        GUILayout.Label($"{phase:F3} of the cycle, {minutes:F1} min after sunrise ({(clock.Sun.IsUp ? "day" : "night")})");
        float chosen = GUILayout.HorizontalSlider(phase, 0f, 0.999f);
        if (!Mathf.Approximately(chosen, phase))
        {
            ShowTimeOfDay(chosen);
        }

        if (GUILayout.Button("Follow the session"))
        {
            clock.LocalOffset = 0f;
        }

        GUILayout.EndArea();
    }

    /// <summary>
    /// The Gameplay scope's day clock, or null when no Gameplay scope exists or it has none.
    /// </summary>
    private DayClock Clock()
    {
        IObjectResolver gameplay = Gameplay();
        return gameplay != null && gameplay.TryResolve(out DayClock clock) ? clock : null;
    }

    /// <summary>
    /// The Gameplay scope's container, or null when no Gameplay scope exists. A missing scope is searched for at most
    /// once per <see cref="ScopeLookupInterval"/> seconds.
    /// </summary>
    private IObjectResolver Gameplay()
    {
        if (gameplayScope == null && Time.unscaledTime >= nextScopeLookup)
        {
            gameplayScope = FindAnyObjectByType<GameplayLifetimeScope>();
            nextScopeLookup = Time.unscaledTime + ScopeLookupInterval;
        }

        return gameplayScope != null ? gameplayScope.Container : null;
    }
}
