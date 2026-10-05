using UnityEngine;
using VContainer;

/// <summary>
/// Dev-only IMGUI panel for trying statuses in play. F2 toggles it; pick a status from the catalog, then F3 applies
/// it to the unit under the cursor and F4 to the local player. Statuses are applied through
/// <see cref="StatusEffectService"/> without a source, so the faction rules do not get in the way, and only where
/// this machine has authority (offline or the host). Created once per play session; it resolves the Gameplay
/// scope's services when used, because the Gameplay scope cannot reference this assembly.
/// </summary>
[DisallowMultipleComponent]
public class StatusDebugPanel : MonoBehaviour
{
    private const KeyCode ToggleKey = KeyCode.F2;
    private const KeyCode HoveredKey = KeyCode.F3;
    private const KeyCode SelfKey = KeyCode.F4;
    private const float Width = 260f;
    private const float ScopeLookupInterval = 1f;

    private bool open;
    private int selected;
    private string lastResult = string.Empty;
    private Vector2 scroll;
    private GameplayLifetimeScope gameplayScope;
    private float nextScopeLookup;
    private StatusEffectCatalog labelledCatalog;
    private string[] labels = new string[0];

    public bool IsOpen => open;

    /// <summary>
    /// Spawns the panel on a persistent game object after the first scene loads, once per play session.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForPlaySession()
    {
        GameObject panel = new(nameof(StatusDebugPanel));
        DontDestroyOnLoad(panel);
        panel.AddComponent<StatusDebugPanel>();
    }

    /// <summary>
    /// Applies <paramref name="definition"/> to the local player. Returns the outcome, or
    /// <see cref="StatusApplyOutcome.Invalid"/> outside a game session.
    /// </summary>
    public StatusApplyOutcome ApplyToSelf(StatusEffectDefinition definition)
    {
        IObjectResolver gameplay = Gameplay();
        LocalPlayer player = gameplay != null ? gameplay.Resolve<LocalPlayerTracker>().Current : null;
        DamageReceiver target = player != null ? player.Controller.GetComponent<DamageReceiver>() : null;
        return Apply(gameplay, target, definition);
    }

    /// <summary>
    /// Applies <paramref name="definition"/> to the unit closest to <paramref name="screenPoint"/> (pick rules of
    /// <see cref="PointerTargetPicker"/>). <see cref="StatusApplyOutcome.Invalid"/> when there is none.
    /// </summary>
    public StatusApplyOutcome ApplyToUnitAt(Vector2 screenPoint, StatusEffectDefinition definition)
    {
        IObjectResolver gameplay = Gameplay();
        if (gameplay == null)
        {
            return StatusApplyOutcome.Invalid;
        }

        Camera camera = gameplay.Resolve<Camera>();
        Vector2 world = camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -camera.transform.position.z));
        UnitTarget unit = gameplay.Resolve<PointerTargetPicker>().Pick(world);
        DamageReceiver target = unit.Health != null ? unit.Health.GetComponent<DamageReceiver>() : null;
        return Apply(gameplay, target, definition);
    }

    /// <summary>
    /// Handles the panel's hotkeys and, while open, draws the status list, the clear button and the last result.
    /// </summary>
    private void OnGUI()
    {
        Event current = Event.current;
        if (current.type == EventType.KeyDown)
        {
            HandleKey(current.keyCode, current.mousePosition);
        }

        if (!open)
        {
            return;
        }

        StatusEffectCatalog catalog = Catalog();
        GUILayout.BeginArea(new Rect(10f, 10f, Width, Screen.height - 20f), GUI.skin.box);
        GUILayout.Label("Statuses (F2 hide)");
        GUILayout.Label("F3: unit under cursor   F4: yourself");
        if (catalog == null)
        {
            GUILayout.Label("Start a game to use statuses.");
            GUILayout.EndArea();
            return;
        }

        scroll = GUILayout.BeginScrollView(scroll);
        for (int i = 0; i < catalog.Count; i++)
        {
            StatusEffectDefinition definition = catalog.Get(i);
            if (GUILayout.Toggle(i == selected, Label(catalog, i), GUI.skin.button))
            {
                selected = i;
            }
        }

        GUILayout.EndScrollView();
        if (GUILayout.Button("Clear yourself"))
        {
            ClearSelf();
        }

        GUILayout.Label(lastResult);
        GUILayout.EndArea();
    }

    /// <summary>
    /// Toggles the panel on F2; while open, applies the selected status to the hovered unit (F3) or the local
    /// player (F4) and records the outcome. <paramref name="guiMousePosition"/> is in GUI space (y down).
    /// </summary>
    private void HandleKey(KeyCode key, Vector2 guiMousePosition)
    {
        if (key == ToggleKey)
        {
            open = !open;
            return;
        }

        if (!open || (key != HoveredKey && key != SelfKey))
        {
            return;
        }

        StatusEffectCatalog catalog = Catalog();
        StatusEffectDefinition definition = catalog != null ? catalog.Get(selected) : null;
        if (definition == null)
        {
            return;
        }

        Vector2 screenPoint = new(guiMousePosition.x, Screen.height - guiMousePosition.y);
        StatusApplyOutcome outcome = key == SelfKey ? ApplyToSelf(definition) : ApplyToUnitAt(screenPoint, definition);
        lastResult = $"{definition.DisplayName}: {outcome}";
    }

    /// <summary>
    /// Removes every status from the local player. Does nothing outside a game session.
    /// </summary>
    private void ClearSelf()
    {
        IObjectResolver gameplay = Gameplay();
        LocalPlayer player = gameplay != null ? gameplay.Resolve<LocalPlayerTracker>().Current : null;
        if (player != null)
        {
            gameplay.Resolve<StatusEffectService>().ClearAll(player.Controller.GetComponent<DamageReceiver>());
            lastResult = "Cleared";
        }
    }

    /// <summary>
    /// Applies <paramref name="definition"/> to <paramref name="target"/> through the Gameplay scope's
    /// <see cref="StatusEffectService"/>; <see cref="StatusApplyOutcome.Invalid"/> when either is missing.
    /// </summary>
    private static StatusApplyOutcome Apply(IObjectResolver gameplay, DamageReceiver target, StatusEffectDefinition definition)
    {
        if (gameplay == null || target == null)
        {
            return StatusApplyOutcome.Invalid;
        }

        return gameplay.Resolve<StatusEffectService>().Apply(target, definition);
    }

    /// <summary>
    /// The status catalog from the Gameplay scope's <see cref="CombatSettings"/>, or null outside a game session.
    /// </summary>
    private StatusEffectCatalog Catalog()
    {
        IObjectResolver gameplay = Gameplay();
        return gameplay != null ? gameplay.Resolve<CombatSettings>().StatusCatalog : null;
    }

    /// <summary>
    /// Button label for the status at <paramref name="index"/>, prefixed with + for buffs and - for debuffs.
    /// Labels are cached and rebuilt only when the catalog or its size changes.
    /// </summary>
    private string Label(StatusEffectCatalog catalog, int index)
    {
        if (labelledCatalog != catalog || labels.Length != catalog.Count)
        {
            labelledCatalog = catalog;
            labels = new string[catalog.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                StatusEffectDefinition definition = catalog.Get(i);
                labels[i] = (definition.Kind == StatusKind.Buff ? "+ " : "- ") + definition.DisplayName;
            }
        }

        return labels[index];
    }

    /// <summary>
    /// The Gameplay scope's container, or null when no Gameplay scope exists. A missing scope is searched for at
    /// most once per <see cref="ScopeLookupInterval"/> seconds.
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
