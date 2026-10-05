using UnityEngine;

/// <summary>
/// Party death tuning registered in the Gameplay scope: how long a dead player waits to respawn while a teammate
/// lives, and how long after the last player dies the area restarts.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Gameplay Settings", fileName = "GameplaySettings")]
public class GameplaySettings : ScriptableObject
{
    [SerializeField, Min(0f), Tooltip("Seconds between the last living player dying and the area restarting.")]
    private float restartDelaySeconds = 1.5f;

    [SerializeField, Min(0f), Tooltip("Seconds a dead player waits before respawning at the area's spawn point while a teammate is still alive.")]
    private float respawnDelaySeconds = 3f;

    public float RestartDelaySeconds => restartDelaySeconds;
    public float RespawnDelaySeconds => respawnDelaySeconds;
}
