using UnityEngine;

/// <summary>
/// Session tuning registered in the Gameplay scope: how long a dead player waits to respawn, how close a player comes to
/// a waystone to discover it and what it is shown then, how far apart players arriving at the same entry stand, how
/// often a world session saves, how far a saved place on ground no longer walkable looks for walkable ground, how
/// long a travelling player stays protected at most, and how players' composed outfits are kept and built.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Gameplay Settings", fileName = "GameplaySettings")]
public class GameplaySettings : ScriptableObject
{
    [SerializeField, Min(0f), Tooltip("Seconds of game time a dead player waits before it travels to the nearest waystone it discovered in its world, whatever the other players do.")]
    private float respawnDelaySeconds = 3f;

    [SerializeField, Min(0f), Tooltip("Horizontal gap between players arriving at the same entry: the first stands on it, the next ones alternately right and left of it.")]
    private float entrySlotSpacing = 0.6f;

    [SerializeField, Min(1f), Tooltip("Seconds of game time between autosaves of a world session, on the machine that saves (offline or the host). The game also saves whenever a player enters a world and as the session ends.")]
    private float autosaveIntervalSeconds = 60f;

    [Header("Trips")]
    [SerializeField, Min(0.1f), Tooltip("Seconds of game time a travelling player stays untargetable, its statuses held, at most: its machine normally ends the protection sooner, as it fades in at the end of the trip. A dead player's respawn trip also fades in once the player is alive again, waiting this long at most.")]
    private float tripProtectionSeconds = 10f;

    [Header("Waystones")]
    [SerializeField, Min(0), Tooltip("A player discovers a waystone entry when its cell is at most this many cells from the entry's cell along each axis.")]
    private int waystoneReachCells = 2;

    [SerializeField, Tooltip("Floating text shown over a player on its own screen as it discovers a waystone.")]
    private string waystoneDiscoveredText = "Waystone discovered";

    [SerializeField, Tooltip("Colour of the waystone discovery text.")]
    private Color waystoneDiscoveredColor = new(0.55f, 0.9f, 1f, 1f);

    [Header("Saved places")]
    [SerializeField, Min(1), Tooltip("A saved place on ground a player can no longer stand on (its world was regenerated or repainted) moves to the nearest walkable cell at most this many cells away (Manhattan distance), or else to the player's nearest discovered waystone. Keep it within the load extents' smaller side (18 cells), the ground loaded around the place.")]
    private int savedPlaceSearchCells = 16;

    [Header("Outfits")]
    [SerializeField, Min(0), Tooltip("Composed outfits nobody wears that are kept, the most recently worn, so a player switching back to one (a weapon's gear, a part put back) shows it at once (Docs/PlayerCharacter.md, Phase 7). Each is about 15 MiB.")]
    private int spareOutfits = 2;

    [SerializeField, Min(1), Tooltip("Sprites of a composed outfit made per frame on the main thread, so building one (2616 sprites for the adventurer) spreads over a few frames.")]
    private int outfitSpritesPerFrame = 500;

    public float RespawnDelaySeconds => respawnDelaySeconds;
    public int WaystoneReachCells => waystoneReachCells;
    public string WaystoneDiscoveredText => waystoneDiscoveredText;
    public Color WaystoneDiscoveredColor => waystoneDiscoveredColor;
    public float EntrySlotSpacing => entrySlotSpacing;
    public float AutosaveIntervalSeconds => autosaveIntervalSeconds;
    public int SavedPlaceSearchCells => savedPlaceSearchCells;
    public float TripProtectionSeconds => tripProtectionSeconds;
    public int SpareOutfits => spareOutfits;
    public int OutfitSpritesPerFrame => outfitSpritesPerFrame;

    /// <summary>
    /// Builds unsaved settings for tests.
    /// </summary>
    internal static GameplaySettings Create(float respawnDelay = 3f, float slotSpacing = 0.6f, float autosaveInterval = 60f, int waystoneReach = 2, float tripProtection = 10f)
    {
        GameplaySettings settings = CreateInstance<GameplaySettings>();
        settings.hideFlags = HideFlags.DontSave;
        settings.respawnDelaySeconds = respawnDelay;
        settings.waystoneReachCells = waystoneReach;
        settings.entrySlotSpacing = slotSpacing;
        settings.autosaveIntervalSeconds = autosaveInterval;
        settings.tripProtectionSeconds = tripProtection;
        return settings;
    }
}
