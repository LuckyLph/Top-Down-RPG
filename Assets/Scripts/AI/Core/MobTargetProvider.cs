using UnityEngine;
using VContainer;

public class MobTargetProvider : MonoBehaviour
{
    [SerializeField] private Transform target;

    public Transform Target => target;
    public bool HasTarget => target != null;

    // Injected by the area's LifetimeScope with the session's player from the Gameplay scope.
    [Inject]
    public void Construct(PlayerController player)
    {
        SetTarget(player != null ? player.transform : null);
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
