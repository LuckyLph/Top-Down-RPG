using UnityEngine;

/// <summary>
/// Session tuning registered in the Gameplay scope: how long a dead player waits to respawn, how far apart players
/// arriving at the same entry stand, and how often a world session saves.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Gameplay Settings", fileName = "GameplaySettings")]
public class GameplaySettings : ScriptableObject
{
    [SerializeField, Min(0f), Tooltip("Seconds of game time a dead player waits before it respawns, whatever the other players do.")]
    private float respawnDelaySeconds = 3f;

    [SerializeField, Min(0f), Tooltip("Horizontal gap between players arriving at the same entry: the first stands on it, the next ones alternately right and left of it.")]
    private float entrySlotSpacing = 0.6f;

    [SerializeField, Min(1f), Tooltip("Seconds of game time between autosaves of a world session, on the machine that saves (offline or the host). The game also saves whenever a player enters a world and as the session ends.")]
    private float autosaveIntervalSeconds = 60f;

    public float RespawnDelaySeconds => respawnDelaySeconds;
    public float EntrySlotSpacing => entrySlotSpacing;
    public float AutosaveIntervalSeconds => autosaveIntervalSeconds;

    /// <summary>
    /// Builds unsaved settings for tests.
    /// </summary>
    internal static GameplaySettings Create(float respawnDelay = 3f, float slotSpacing = 0.6f, float autosaveInterval = 60f)
    {
        GameplaySettings settings = CreateInstance<GameplaySettings>();
        settings.hideFlags = HideFlags.DontSave;
        settings.respawnDelaySeconds = respawnDelay;
        settings.entrySlotSpacing = slotSpacing;
        settings.autosaveIntervalSeconds = autosaveInterval;
        return settings;
    }
}
