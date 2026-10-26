using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sorts a sprite object by its height in the world: sets its sorting order to an offset minus its y times a
/// multiplier, so lower objects draw in front. The y is its own, or a sorting point's (the root of a unit or prop whose
/// sprite sits above where it stands), or a reference set in code. Writes a <see cref="SortingGroup"/> when there is
/// one, else every cached renderer, keeping their relative orders. Runs in edit mode too. Something that never moves (a
/// prop) sorts once: in play it sorts when it starts and then disables itself, so it costs nothing per frame, while in
/// the editor it keeps sorting as it is moved.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class YPositionSorter : MonoBehaviour
{
    [SerializeField] private SortingGroup sortingGroup;
    [SerializeField] private bool includeChildRenderers = true;
    [SerializeField, Min(1)] private int sortingOrderMultiplier = 100;
    [SerializeField] private int sortingOrderOffset;
    [SerializeField, Tooltip("Sorts by this transform's height instead of its own: the root of a unit or prop whose sprite sits above where it stands.")]
    private Transform sortingPoint;
    [SerializeField] private bool useSortingReferenceY;
    [SerializeField] private float sortingReferenceY;
    [SerializeField, Tooltip("Sorts once in play, when it starts, and then disables itself, for something that never moves (a prop); in the editor it keeps sorting.")]
    private bool sortsOnce;

    private Renderer[] cachedRenderers = Array.Empty<Renderer>();
    private int[] cachedOrderOffsets = Array.Empty<int>();
    private int lastAppliedOrder = int.MinValue;

    /// <summary>
    /// The height it sorts by: the reference set in code, else the sorting point's, else its own.
    /// </summary>
    public float SortingY => useSortingReferenceY ? sortingReferenceY : (sortingPoint != null ? sortingPoint.position.y : transform.position.y);

    /// <summary>
    /// Caches the sorting group and renderers.
    /// </summary>
    private void Awake()
    {
        CacheTargets();
    }

    /// <summary>
    /// Caches the targets again and applies the sorting order at once.
    /// </summary>
    private void OnEnable()
    {
        CacheTargets();
        ApplySortingOrder();
    }

    /// <summary>
    /// In play, when it sorts once, sorts where it stands after the frame it was enabled in (so a position set right
    /// after it was created counts) and disables itself.
    /// </summary>
    private void Start()
    {
        if (!sortsOnce || !Application.isPlaying)
        {
            return;
        }

        ApplySortingOrder();
        enabled = false;
    }

    /// <summary>
    /// Caches the targets again and reapplies the sorting order after an inspector change.
    /// </summary>
    private void OnValidate()
    {
        CacheTargets();
        ApplySortingOrder();
    }

    /// <summary>
    /// Reapplies the sorting order after the frame's movement; writes only when the order changed.
    /// </summary>
    private void LateUpdate()
    {
        ApplySortingOrder();
    }

    /// <summary>
    /// Sets the constant added to the sorting order and reapplies it.
    /// </summary>
    public void SetSortingOrderOffset(int offset)
    {
        sortingOrderOffset = offset;
        ApplySortingOrder();
    }

    /// <summary>
    /// Sorts by <paramref name="referenceY"/> instead of the object's own y until <see cref="ClearSortingReferenceY"/>.
    /// </summary>
    public void SetSortingReferenceY(float referenceY)
    {
        useSortingReferenceY = true;
        sortingReferenceY = referenceY;
        ApplySortingOrder();
    }

    /// <summary>
    /// Goes back to sorting by the object's own y.
    /// </summary>
    public void ClearSortingReferenceY()
    {
        useSortingReferenceY = false;
        ApplySortingOrder();
    }

    /// <summary>
    /// Finds the sorting group and the renderers (children included when set), records each renderer's order relative
    /// to the group's order or the lowest renderer order, and forces the next apply to write.
    /// </summary>
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

    /// <summary>
    /// Computes the order from the y position and writes it to the sorting group, or to each renderer plus its
    /// recorded offset; skips the write when the order is unchanged.
    /// </summary>
    private void ApplySortingOrder()
    {
        int targetOrder = sortingOrderOffset - Mathf.RoundToInt(SortingY * sortingOrderMultiplier);
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

    /// <summary>
    /// The lowest sorting order among <paramref name="renderers"/>, or 0 when there are none.
    /// </summary>
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
