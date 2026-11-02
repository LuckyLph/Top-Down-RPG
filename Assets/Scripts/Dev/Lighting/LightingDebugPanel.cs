using UnityEngine;
using VContainer;

/// <summary>
/// Dev-only lighting panel (Docs/Lighting.md, Architecture.md's Dev tools): F8 toggles an IMGUI panel at the top right
/// with a readout of the time of day, the sky, the sun, the region skies, the clouds, the night's lights and the frame
/// rate, and the key map; while it is open its keys move this machine's time of day, force parts of the lighting on or
/// off and teleport the local player to the entries of its world, through <see cref="LightingDebugControls"/>. Its
/// keys are none that gameplay binds by default. Nothing it changes is saved or sent. Created once per play session; it finds the Gameplay scope
/// when used, because the Gameplay scope cannot reference this assembly, and while closed it only watches for F8.
/// </summary>
[DisallowMultipleComponent]
public class LightingDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F8;
    private const float Width = 380f;
    private const float Height = 280f;
    private const float Top = 90f;
    private const float ScopeLookupInterval = 1f;
    private const float FpsInterval = 0.5f;

    private static readonly KeyCode[] KeyTimeKeys =
    {
        KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals
    };

    private bool open;
    private GameplayLifetimeScope gameplayScope;
    private LightingDebugControls controls;
    private float nextScopeLookup;
    private float fps;
    private float fpsTime;
    private int fpsFrames;

    public bool IsOpen => open;

    /// <summary>
    /// Spawns the panel on a persistent game object after the first scene loads, once per play session.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForPlaySession()
    {
        GameObject panel = new(nameof(LightingDebugPanel));
        DontDestroyOnLoad(panel);
        panel.AddComponent<LightingDebugPanel>();
    }

    /// <summary>
    /// The controls over the current Gameplay scope's lighting, or null outside a game session; made again for each
    /// session.
    /// </summary>
    public LightingDebugControls Controls()
    {
        GameplayLifetimeScope scope = GameplayScope();
        if (scope == null || scope.Container == null)
        {
            controls = null;
            return null;
        }

        if (controls == null)
        {
            IObjectResolver resolver = scope.Container;
            resolver.TryResolve(out DayClock clock);
            resolver.TryResolve(out DayNightPresenter presenter);
            resolver.TryResolve(out PropLighting propLighting);
            resolver.TryResolve(out GameFlow gameFlow);
            resolver.TryResolve(out LocalPlayerTracker players);
            controls = new LightingDebugControls(clock, presenter, propLighting, () => gameFlow != null ? gameFlow.CurrentWorld : null, position => TeleportLocalPlayer(players, position));
        }

        return controls;
    }

    /// <summary>
    /// Handles the hotkey and, while open, the panel's keys, and draws it.
    /// </summary>
    private void OnGUI()
    {
        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == ToggleKey)
        {
            open = !open;
            current.Use();
        }

        if (!open)
        {
            return;
        }

        LightingDebugControls lighting = Controls();
        if (current.type == EventType.KeyDown && lighting != null && Handle(lighting, current.keyCode, current.shift))
        {
            current.Use();
        }

        if (current.type == EventType.Repaint)
        {
            CountFrame();
        }

        GUILayout.BeginArea(new Rect(Screen.width - Width - 10f, Top, Width, Height), GUI.skin.box);
        GUILayout.Label("Lighting (F8 hide)");
        if (lighting == null)
        {
            GUILayout.Label("Start a game in a world to light it.");
            GUILayout.EndArea();
            return;
        }

        GUILayout.Label(lighting.Readout(fps));
        GUILayout.Label(KeyMap(lighting));
        GUILayout.EndArea();
    }

    /// <summary>
    /// Runs the control on <paramref name="key"/> (with <paramref name="shift"/> held). Returns whether the key is one of
    /// the panel's.
    /// </summary>
    private static bool Handle(LightingDebugControls lighting, KeyCode key, bool shift)
    {
        int keyTime = System.Array.IndexOf(KeyTimeKeys, key);
        if (keyTime >= 0)
        {
            lighting.ShowKey(keyTime);
            return true;
        }

        switch (key)
        {
            case KeyCode.Comma:
                lighting.StepTime(-1, shift);
                return true;
            case KeyCode.Period:
                lighting.StepTime(1, shift);
                return true;
            case KeyCode.P:
                lighting.ToggleHold();
                return true;
            case KeyCode.F:
                lighting.NextRate();
                return true;
            case KeyCode.Backspace:
                lighting.FollowSession();
                return true;
            case KeyCode.Z:
                lighting.CycleSunShadows();
                return true;
            case KeyCode.X:
                lighting.CyclePropLights();
                return true;
            case KeyCode.C:
                lighting.CycleGroundGlow();
                return true;
            case KeyCode.V:
                lighting.CycleFlicker();
                return true;
            case KeyCode.B:
                lighting.CycleGlowingPixels();
                return true;
            case KeyCode.N:
                lighting.CycleLightShadows();
                return true;
            case KeyCode.M:
                lighting.CycleClouds();
                return true;
            case KeyCode.T:
                lighting.Teleport(shift ? -1 : 1);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The panel's keys, with each override's state.
    /// </summary>
    private static string KeyMap(LightingDebugControls lighting)
    {
        DayNightPresenter presenter = lighting.Presenter;
        PropLighting props = lighting.PropLighting;
        string shadows = presenter != null ? LightingDebugControls.StateText(presenter.SunShadowsOverride) : "-";
        string clouds = presenter != null ? LightingDebugControls.StateText(presenter.CloudsOverride) : "-";
        string lights = props != null ? LightingDebugControls.StateText(props.PropLightsOverride) : "-";
        string glow = props != null ? LightingDebugControls.StateText(props.GroundGlowOverride) : "-";
        string flicker = props != null ? LightingDebugControls.StateText(props.FlickerOverride) : "-";
        string pixels = props != null ? LightingDebugControls.StateText(props.GlowingPixelsOverride) : "-";
        string lightShadows = props != null ? LightingDebugControls.StateText(props.LightShadowsOverride) : "-";
        return "3 4 5 6 7 8 9 0 - =  sunrise, early morning, morning, noon,\n"
            + "        afternoon, evening, sunset, dusk, night, before dawn\n"
            + ", .  step 15 s (Shift 1.5 s)   P hold   F x1 / x10 / x60\n"
            + "Backspace  follow the session\n"
            + $"Z sun shadows {shadows}    M clouds {clouds}    X prop lights {lights}\n"
            + $"C lava glow {glow}    V flicker {flicker}    B glowing pixels {pixels}\n"
            + $"N light shadows {lightShadows}    T / Shift+T  next / previous spot";
    }

    /// <summary>
    /// Teleports the local player of <paramref name="players"/> to <paramref name="position"/>, when there is one.
    /// </summary>
    private static void TeleportLocalPlayer(LocalPlayerTracker players, Vector2 position)
    {
        LocalPlayer local = players?.Current;
        if (local != null && local.Controller != null)
        {
            local.Controller.Teleport(position);
        }
    }

    /// <summary>
    /// Counts a drawn frame and updates the frame rate every <see cref="FpsInterval"/> seconds of real time.
    /// </summary>
    private void CountFrame()
    {
        fpsFrames++;
        fpsTime += Time.unscaledDeltaTime;
        if (fpsTime >= FpsInterval)
        {
            fps = fpsFrames / fpsTime;
            fpsFrames = 0;
            fpsTime = 0f;
        }
    }

    /// <summary>
    /// The Gameplay scope, or null when none exists. A missing scope is searched for at most once per
    /// <see cref="ScopeLookupInterval"/> seconds.
    /// </summary>
    private GameplayLifetimeScope GameplayScope()
    {
        if (gameplayScope == null && Time.unscaledTime >= nextScopeLookup)
        {
            gameplayScope = FindAnyObjectByType<GameplayLifetimeScope>();
            nextScopeLookup = Time.unscaledTime + ScopeLookupInterval;
            controls = null;
        }

        return gameplayScope;
    }
}
