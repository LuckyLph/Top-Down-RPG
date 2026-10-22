using UnityEngine;
using VContainer;

/// <summary>
/// Dev-only way to switch the local player's weapon in play (Docs/Animation.md, Phase 4): F5 equips the next weapon
/// of its catalog, and F6 toggles an IMGUI list of the catalog with a button per weapon. The switch goes through the
/// player's own <see cref="PlayerWeaponController"/>, as gameplay will, so it reaches the other machines like any
/// weapon change. Created once per play session; it finds the Gameplay scope when used, because the Gameplay scope
/// cannot reference this assembly.
/// </summary>
[DisallowMultipleComponent]
public class WeaponDebugPanel : MonoBehaviour
{
    private const KeyCode NextKey = KeyCode.F5;
    private const KeyCode ToggleKey = KeyCode.F6;
    private const float Width = 200f;
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
        GameObject panel = new(nameof(WeaponDebugPanel));
        DontDestroyOnLoad(panel);
        panel.AddComponent<WeaponDebugPanel>();
    }

    /// <summary>
    /// Equips the weapon after the local player's in its catalog, wrapping around. Returns the weapon equipped, or null
    /// outside a game session or without a catalog.
    /// </summary>
    public PlayerWeapon EquipNext()
    {
        PlayerWeaponController weapons = LocalWeapons();
        PlayerWeapon next = weapons != null && weapons.Catalog != null ? weapons.Catalog.Next(weapons.CurrentWeapon) : null;
        if (next != null)
        {
            weapons.Equip(next);
        }

        return next;
    }

    /// <summary>
    /// Handles the hotkeys and, while open, draws a button per catalog weapon, the equipped one marked.
    /// </summary>
    private void OnGUI()
    {
        Event current = Event.current;
        if (current.type == EventType.KeyDown)
        {
            if (current.keyCode == ToggleKey)
            {
                open = !open;
            }
            else if (current.keyCode == NextKey)
            {
                EquipNext();
            }
        }

        if (!open)
        {
            return;
        }

        PlayerWeaponController weapons = LocalWeapons();
        WeaponCatalog catalog = weapons != null ? weapons.Catalog : null;
        GUILayout.BeginArea(new Rect(Screen.width - Width - 10f, 10f, Width, Screen.height - 20f), GUI.skin.box);
        GUILayout.Label("Weapons (F6 hide, F5 next)");
        if (catalog == null)
        {
            GUILayout.Label("Start a game to switch weapons.");
            GUILayout.EndArea();
            return;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            PlayerWeapon weapon = catalog.Get(i);
            if (weapon != null && GUILayout.Toggle(weapon == weapons.CurrentWeapon, weapon.DisplayName, GUI.skin.button) && weapon != weapons.CurrentWeapon)
            {
                weapons.Equip(weapon);
            }
        }

        GUILayout.EndArea();
    }

    /// <summary>
    /// The local player's weapon controller, or null outside a game session or before this machine's player exists.
    /// </summary>
    private PlayerWeaponController LocalWeapons()
    {
        IObjectResolver gameplay = Gameplay();
        LocalPlayer player = gameplay != null ? gameplay.Resolve<LocalPlayerTracker>().Current : null;
        return player != null ? player.Weapon : null;
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
