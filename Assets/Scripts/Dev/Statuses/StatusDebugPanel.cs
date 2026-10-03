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

    private bool open;
    private int selected;
    private string lastResult = string.Empty;
    private Vector2 scroll;
    private GameplayLifetimeScope gameplayScope;

    public bool IsOpen => open;

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
            string label = definition.Kind == StatusKind.Buff ? "+ " + definition.DisplayName : "- " + definition.DisplayName;
            if (GUILayout.Toggle(i == selected, label, GUI.skin.button))
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

    private static StatusApplyOutcome Apply(IObjectResolver gameplay, DamageReceiver target, StatusEffectDefinition definition)
    {
        if (gameplay == null || target == null)
        {
            return StatusApplyOutcome.Invalid;
        }

        return gameplay.Resolve<StatusEffectService>().Apply(target, definition);
    }

    private StatusEffectCatalog Catalog()
    {
        IObjectResolver gameplay = Gameplay();
        return gameplay != null ? gameplay.Resolve<CombatSettings>().StatusCatalog : null;
    }

    private IObjectResolver Gameplay()
    {
        if (gameplayScope == null)
        {
            gameplayScope = FindAnyObjectByType<GameplayLifetimeScope>();
        }

        return gameplayScope != null ? gameplayScope.Container : null;
    }
}
