using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Local-only pointer feedback in the Gameplay scene: a pooled <see cref="MoveMarker"/> per move click or attack move,
/// each from its own prefab and pool (a move's chevrons, an attack move's crossed ring), the hover marker under the
/// hovered enemy, the ally hover marker under the hovered ally (Docs/UiRevamp.md, Phase 4) and the attack cursor.
/// Passive: <see cref="PointerFeedbackPresenter"/> decides what to show.
/// </summary>
[DisallowMultipleComponent]
public class PointerFeedbackLayer : MonoBehaviour
{
    [SerializeField, Tooltip("The marker a move order drops.")] private MoveMarker markerPrefab;
    [SerializeField, Tooltip("The marker an attack move order drops.")] private MoveMarker attackMarkerPrefab;
    [SerializeField, Min(0), Tooltip("How many markers of each kind the pools hold from the start.")] private int prewarmCount = 4;
    [SerializeField, Tooltip("The hover marker placed under the hovered enemy; hidden otherwise.")]
    private SpriteRenderer hoverRing;
    [SerializeField, Tooltip("The ally hover marker placed under the hovered ally; hidden otherwise.")]
    private SpriteRenderer allyHoverRing;
    [SerializeField] private Texture2D attackCursor;
    [SerializeField] private Vector2 attackCursorHotspot = new(16f, 16f);

    private readonly List<(MoveMarker marker, bool attack)> active = new();
    private ObjectPool<MoveMarker> pool;
    private ObjectPool<MoveMarker> attackPool;
    private Transform hovered;
    private Transform hoveredAlly;
    private bool attackCursorShown;

    internal int ActiveMarkerCount => active.Count;
    internal int PooledMarkerCount => (pool != null ? pool.CountInactive : 0) + (attackPool != null ? attackPool.CountInactive : 0);
    internal Transform Hovered => hovered;
    internal bool AttackCursorShown => attackCursorShown;
    internal SpriteRenderer HoverRing => hoverRing;
    internal Transform HoveredAlly => hoveredAlly;
    internal SpriteRenderer AllyHoverRing => allyHoverRing;

    /// <summary>
    /// Logs an error when a marker prefab, a hover marker or the attack cursor is missing, prewarms the marker pools
    /// and hides the hover markers.
    /// </summary>
    private void Awake()
    {
        if (markerPrefab == null || attackMarkerPrefab == null || hoverRing == null || allyHoverRing == null || attackCursor == null)
        {
            Debug.LogError($"{name} needs a move marker, an attack move marker, a hover marker, an ally hover marker and an attack cursor.", this);
        }

        EnsurePool();
        Prewarm(pool, markerPrefab);
        Prewarm(attackPool, attackMarkerPrefab);
        SetHovered(null);
        SetHoveredAlly(null);
    }

    /// <summary>
    /// Puts the default system cursor back so the attack cursor never outlives the layer.
    /// </summary>
    private void OnDisable()
    {
        SetAttackCursor(false);
    }

    /// <summary>
    /// Forgets the active markers and disposes the pools, destroying the inactive markers they hold.
    /// </summary>
    private void OnDestroy()
    {
        active.Clear();
        pool?.Dispose();
        attackPool?.Dispose();
    }

    /// <summary>
    /// Shows a pooled marker at <paramref name="position"/>: the attack move marker when <paramref name="attackMove"/> is
    /// set, the move marker otherwise. Does nothing without that marker's prefab.
    /// </summary>
    public void SpawnMarker(Vector2 position, bool attackMove = false)
    {
        EnsurePool();
        if ((attackMove ? attackMarkerPrefab : markerPrefab) == null)
        {
            return;
        }

        MoveMarker marker = (attackMove ? attackPool : pool).Get();
        marker.Show(position);
        active.Add((marker, attackMove));
    }

    /// <summary>
    /// Puts the hover ring under <paramref name="target"/>, or hides it for null.
    /// </summary>
    public void SetHovered(Transform target)
    {
        hovered = target;
        Place(hoverRing, target);
    }

    /// <summary>
    /// Puts the ally hover marker under <paramref name="target"/>, or hides it for null.
    /// </summary>
    public void SetHoveredAlly(Transform target)
    {
        hoveredAlly = target;
        Place(allyHoverRing, target);
    }

    /// <summary>
    /// Switches between the attack cursor and the default system cursor; only calls into the OS when the state
    /// changes.
    /// </summary>
    public void SetAttackCursor(bool shown)
    {
        if (shown == attackCursorShown)
        {
            return;
        }

        attackCursorShown = shown;
        Cursor.SetCursor(shown ? attackCursor : null, shown ? attackCursorHotspot : Vector2.zero, CursorMode.Auto);
    }

    /// <summary>
    /// Advances the markers and returns finished ones to the pool.
    /// </summary>
    public void Tick(float deltaTime)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            (MoveMarker marker, bool attack) = active[i];
            if (!marker.Advance(deltaTime))
            {
                continue;
            }

            int last = active.Count - 1;
            active[i] = active[last];
            active.RemoveAt(last);
            (attack ? attackPool : pool).Release(marker);
        }
    }

    /// <summary>
    /// Assigns the marker prefabs, the hover markers and the attack cursor in code, for tests that build the layer
    /// without a prefab.
    /// </summary>
    internal void Configure(MoveMarker prefab, MoveMarker attackPrefab, SpriteRenderer ring, SpriteRenderer allyRing, Texture2D cursor)
    {
        markerPrefab = prefab;
        attackMarkerPrefab = attackPrefab;
        hoverRing = ring;
        allyHoverRing = allyRing;
        attackCursor = cursor;
        EnsurePool();
    }

    /// <summary>
    /// Shows <paramref name="marker"/> under <paramref name="target"/>, keeping its z, or hides it for null.
    /// </summary>
    private static void Place(SpriteRenderer marker, Transform target)
    {
        if (marker == null)
        {
            return;
        }

        marker.enabled = target != null;
        if (target != null)
        {
            Vector3 position = target.position;
            marker.transform.position = new Vector3(position.x, position.y, marker.transform.position.z);
        }
    }

    /// <summary>
    /// Creates the marker pools on first use; markers are activated when taken and deactivated when returned.
    /// </summary>
    private void EnsurePool()
    {
        pool ??= new ObjectPool<MoveMarker>(() => CreateMarker(markerPrefab), Activate, Deactivate, DestroyMarker);
        attackPool ??= new ObjectPool<MoveMarker>(() => CreateMarker(attackMarkerPrefab), Activate, Deactivate, DestroyMarker);
    }

    /// <summary>
    /// Fills <paramref name="markers"/> with the configured number of inactive copies of <paramref name="prefab"/> so
    /// the first clicks do not instantiate.
    /// </summary>
    private void Prewarm(ObjectPool<MoveMarker> markers, MoveMarker prefab)
    {
        if (prefab == null)
        {
            return;
        }

        List<MoveMarker> warm = new(prewarmCount);
        for (int i = 0; i < prewarmCount; i++)
        {
            warm.Add(markers.Get());
        }

        foreach (MoveMarker marker in warm)
        {
            markers.Release(marker);
        }
    }

    /// <summary>
    /// Pool factory: instantiates an inactive copy of <paramref name="prefab"/> under this layer.
    /// </summary>
    private MoveMarker CreateMarker(MoveMarker prefab)
    {
        MoveMarker marker = Instantiate(prefab, transform);
        marker.gameObject.SetActive(false);
        return marker;
    }

    /// <summary>
    /// Pool callback: shows a marker taken from its pool.
    /// </summary>
    private static void Activate(MoveMarker marker)
    {
        marker.gameObject.SetActive(true);
    }

    /// <summary>
    /// Pool callback: hides a marker returned to its pool.
    /// </summary>
    private static void Deactivate(MoveMarker marker)
    {
        marker.gameObject.SetActive(false);
    }

    /// <summary>
    /// Pool destroy callback: destroys the marker's game object if it still exists.
    /// </summary>
    private static void DestroyMarker(MoveMarker marker)
    {
        if (marker != null)
        {
            Destroy(marker.gameObject);
        }
    }
}
