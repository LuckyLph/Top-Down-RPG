using System.Collections.Generic;
using UnityEngine;
using VContainer;

/// <summary>
/// Shows a pooled <see cref="StatusVisual"/> on the unit for each active status that has one, on every machine:
/// reconciled whenever the statuses change, kept at the unit's position each frame, and returned to the pool when
/// a status ends or the unit is disabled (death, despawn).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(StatusEffects))]
public class StatusVisuals : MonoBehaviour
{
    [SerializeField, Tooltip("Where visuals sit relative to the unit's pivot (its feet).")] private Vector3 offset;

    private readonly List<StatusEffectDefinition> shownDefinitions = new();
    private readonly List<StatusVisual> shown = new();
    private StatusEffects statuses;
    private StatusVisualPool pool;
    private bool subscribed;

    internal int ShownCount => shown.Count;

    private void Awake()
    {
        ResolveStatuses();
    }

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void Start()
    {
        if (pool == null)
        {
            Debug.LogError($"{name} was not injected with a {nameof(StatusVisualPool)}; its statuses will show no visuals.", this);
        }
    }

    private void OnDisable()
    {
        if (statuses != null && subscribed)
        {
            statuses.Changed -= Refresh;
        }

        subscribed = false;
        ReleaseAll();
    }

    private void LateUpdate()
    {
        if (shown.Count == 0)
        {
            return;
        }

        Vector3 position = transform.position + offset;
        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].Follow(position);
        }
    }

    [Inject]
    public void Construct(StatusVisualPool visualPool)
    {
        pool = visualPool;
        Refresh();
    }

    /// <summary>
    /// Returns the visuals of statuses that ended and shows one for each newly active status with a visual.
    /// </summary>
    internal void Refresh()
    {
        ResolveStatuses();
        if (pool == null || statuses == null)
        {
            return;
        }

        for (int i = shown.Count - 1; i >= 0; i--)
        {
            if (statuses.GetStacks(shownDefinitions[i]) == 0)
            {
                pool.Release(shown[i]);
                shown.RemoveAt(i);
                shownDefinitions.RemoveAt(i);
            }
        }

        Vector3 position = transform.position + offset;
        for (int i = 0; i < statuses.Count; i++)
        {
            StatusEffectDefinition definition = statuses.GetSnapshot(i).Definition;
            if (definition.Visual == null || shownDefinitions.Contains(definition))
            {
                continue;
            }

            StatusVisual visual = pool.Get(definition.Visual);
            visual.Follow(position);
            visual.Show(definition, shown.Count);
            shown.Add(visual);
            shownDefinitions.Add(definition);
        }

        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].SetLayer(i);
        }
    }

    internal StatusVisual GetShown(int index)
    {
        return shown[index];
    }

    internal void ReleaseAll()
    {
        if (pool != null)
        {
            for (int i = 0; i < shown.Count; i++)
            {
                pool.Release(shown[i]);
            }
        }

        shown.Clear();
        shownDefinitions.Clear();
    }

    private void Subscribe()
    {
        ResolveStatuses();
        if (statuses != null && !subscribed)
        {
            statuses.Changed += Refresh;
            subscribed = true;
        }
    }

    private void ResolveStatuses()
    {
        if (statuses == null)
        {
            statuses = GetComponent<StatusEffects>();
        }
    }
}
