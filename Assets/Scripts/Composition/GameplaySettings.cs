using UnityEngine;

[CreateAssetMenu(menuName = "TopDownRPG/Gameplay Settings", fileName = "GameplaySettings")]
public class GameplaySettings : ScriptableObject
{
    [SerializeField, Min(0f), Tooltip("Seconds between the player dying and the area restarting.")]
    private float restartDelaySeconds = 1.5f;

    public float RestartDelaySeconds => restartDelaySeconds;
}
