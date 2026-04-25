using System;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[ExecuteAlways]
public class AdjustDepthToHeigth : MonoBehaviour
{
    [SerializeField] private SortingGroup sortingGroup;
    [SerializeField] private bool includeChildRenderers = true;
    [SerializeField, Min(1)] private int sortingOrderMultiplier = 100;
    [SerializeField] private int sortingOrderOffset;

    private Renderer[] cachedRenderers = Array.Empty<Renderer>();
    private int[] cachedOrderOffsets = Array.Empty<int>();
    private int lastAppliedOrder = int.MinValue;

    private void Awake()
    {
        CacheTargets();
    }

    private void OnEnable()
    {
        CacheTargets();
        ApplySortingOrder();
    }

    private void OnValidate()
    {
        CacheTargets();
        ApplySortingOrder();
    }

    private void LateUpdate()
    {
        ApplySortingOrder();
    }

    private void CacheTargets()
    {
        if (sortingGroup == null)
        {
            sortingGroup = GetComponent<SortingGroup>();
        }

        cachedRenderers = includeChildRenderers
            ? GetComponentsInChildren<Renderer>(true)
            : GetComponents<Renderer>();

        cachedOrderOffsets = new int[cachedRenderers.Length];

        int baseOrder = sortingGroup != null
            ? sortingGroup.sortingOrder
            : GetLowestSortingOrder(cachedRenderers);

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer currentRenderer = cachedRenderers[i];
            cachedOrderOffsets[i] = currentRenderer != null
                ? currentRenderer.sortingOrder - baseOrder
                : 0;
        }

        lastAppliedOrder = int.MinValue;
    }

    private void ApplySortingOrder()
    {
        int targetOrder = sortingOrderOffset - Mathf.RoundToInt(transform.position.y * sortingOrderMultiplier);
        if (targetOrder == lastAppliedOrder)
        {
            return;
        }

        if (sortingGroup != null)
        {
            sortingGroup.sortingOrder = targetOrder;
            lastAppliedOrder = targetOrder;
            return;
        }

        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            Renderer currentRenderer = cachedRenderers[i];
            if (currentRenderer == null)
            {
                continue;
            }

            currentRenderer.sortingOrder = targetOrder + cachedOrderOffsets[i];
        }

        lastAppliedOrder = targetOrder;
    }

    private static int GetLowestSortingOrder(Renderer[] renderers)
    {
        int lowestSortingOrder = 0;
        bool foundRenderer = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer currentRenderer = renderers[i];
            if (currentRenderer == null)
            {
                continue;
            }

            if (!foundRenderer || currentRenderer.sortingOrder < lowestSortingOrder)
            {
                lowestSortingOrder = currentRenderer.sortingOrder;
                foundRenderer = true;
            }
        }

        return lowestSortingOrder;
    }
}
