using UnityEngine;

public class MobTargetProvider : MonoBehaviour
{
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool resolveOnEnable = true;
    [SerializeField] private Transform target;

    public Transform Target => target;
    public bool HasTarget => target != null;

    private void OnEnable()
    {
        if (resolveOnEnable)
        {
            ResolveTarget();
        }
    }

    public void ResolveTarget()
    {
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            return;
        }

        GameObject found = GameObject.FindGameObjectWithTag(targetTag);
        target = found != null ? found.transform : null;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
