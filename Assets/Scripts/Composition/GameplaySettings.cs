using UnityEngine;

/// <summary>
/// Session tuning registered in the Gameplay scope: how long a dead player waits to respawn while a teammate lives, how
/// long after the last player dies the area restarts, and how often a world session saves.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Gameplay Settings", fileName = "GameplaySettings")]
public class GameplaySettings : ScriptableObject
{
    [SerializeField, Min(0f), Tooltip("Seconds between the last living player dying and the area restarting.")]
    private float restartDelaySeconds = 1.5f;

    [SerializeField, Min(0f), Tooltip("Seconds a dead player waits before respawning at the area's spawn point while a teammate is still alive.")]
    private float respawnDelaySeconds = 3f;

    [SerializeField, Min(1f), Tooltip("Seconds of game time between autosaves of a world session, on the machine that saves (offline or the host). The game also saves whenever a player enters a world and as the session ends.")]
    private float autosaveIntervalSeconds = 60f;

    public float RestartDelaySeconds => restartDelaySeconds;
    public float RespawnDelaySeconds => respawnDelaySeconds;
    public float AutosaveIntervalSeconds => autosaveIntervalSeconds;
}
