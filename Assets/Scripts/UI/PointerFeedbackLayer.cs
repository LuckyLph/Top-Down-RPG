using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Local-only pointer feedback in the Gameplay scene: a pooled <see cref="MoveMarker"/> per move click, a ring under
/// the hovered enemy and the attack cursor. Passive: <see cref="PointerFeedbackPresenter"/> decides what to show.
/// </summary>
[DisallowMultipleComponent]
public class PointerFeedbackLayer : MonoBehaviour
{
    [SerializeField] private MoveMarker markerPrefab;
    [SerializeField, Min(0)] private int prewarmCount = 4;
    [SerializeField, Tooltip("Ring placed under the hovered enemy; hidden otherwise.")]
    private SpriteRenderer hoverRing;
    [SerializeField] private Texture2D attackCursor;
    [SerializeField] private Vector2 attackCursorHotspot = new(16f, 16f);

    private readonly List<MoveMarker> active = new();
    private ObjectPool<MoveMarker> pool;
    private Transform hovered;
    private bool attackCursorShown;

    internal int ActiveMarkerCount => active.Count;
    internal int PooledMarkerCount => pool != null ? pool.CountInactive : 0;
    internal Transform Hovered => hovered;
    internal bool AttackCursorShown => attackCursorShown;
    internal SpriteRenderer HoverRing => hoverRing;

    /// <summary>
    /// Logs an error when the marker prefab, hover ring or attack cursor is missing, prewarms the marker pool and
    /// hides the hover ring.
    /// </summary>
    private void Awake()
    {
        if (markerPrefab == null || hoverRing == null || attackCursor == null)
        {
            Debug.LogError($"{name} needs a marker prefab, a hover ring and an attack cursor.", this);
        }

        EnsurePool();
        Prewarm();
        SetHovered(null);
    }

    /// <summary>
    /// Puts the default system cursor back so the attack cursor never outlives the layer.
    /// </summary>
    private void OnDisable()
    {
        SetAttackCursor(false);
    }

    /// <summary>
    /// Forgets the active markers and disposes the pool, destroying the inactive markers it holds.
    /// </summary>
    private void OnDestroy()
    {
        active.Clear();
        pool?.Dispose();
    }

    /// <summary>
    /// Shows a pooled move marker at <paramref name="position"/>. Does nothing without a marker prefab.
    /// </summary>
    public void SpawnMarker(Vector2 position)
    {
        EnsurePool();
        if (markerPrefab == null)
        {
            return;
        }

        MoveMarker marker = pool.Get();
        marker.Show(position);
        active.Add(marker);
    }

    /// <summary>
    /// Puts the hover ring under <paramref name="target"/>, or hides it for null.
    /// </summary>
    public void SetHovered(Transform target)
    {
        hovered = target;
        if (hoverRing == null)
        {
            return;
        }

        hoverRing.enabled = target != null;
        if (target != null)
        {
            Vector3 position = target.position;
            hoverRing.transform.position = new Vector3(position.x, position.y, hoverRing.transform.position.z);
        }
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
            MoveMarker marker = active[i];
            if (!marker.Advance(deltaTime))
            {
                continue;
            }

            int last = active.Count - 1;
            active[i] = active[last];
            active.RemoveAt(last);
            pool.Release(marker);
        }
    }

    /// <summary>
    /// Assigns the marker prefab, hover ring and attack cursor in code, for tests that build the layer without a
    /// prefab.
    /// </summary>
    internal void Configure(MoveMarker prefab, SpriteRenderer ring, Texture2D cursor)
    {
        markerPrefab = prefab;
        hoverRing = ring;
        attackCursor = cursor;
        EnsurePool();
    }

    /// <summary>
    /// Creates the marker pool on first use; markers are activated when taken and deactivated when returned.
    /// </summary>
    private void EnsurePool()
    {
        pool ??= new ObjectPool<MoveMarker>(CreateMarker, marker => marker.gameObject.SetActive(true), marker => marker.gameObject.SetActive(false), DestroyMarker);
    }

    /// <summary>
    /// Fills the pool with the configured number of inactive markers so the first clicks do not instantiate.
    /// </summary>
    private void Prewarm()
    {
        if (markerPrefab == null)
        {
            return;
        }

        List<MoveMarker> warm = new(prewarmCount);
        for (int i = 0; i < prewarmCount; i++)
        {
            warm.Add(pool.Get());
        }

        foreach (MoveMarker marker in warm)
        {
            pool.Release(marker);
        }
    }

    /// <summary>
    /// Pool factory: instantiates an inactive marker under this layer.
    /// </summary>
    private MoveMarker CreateMarker()
    {
        MoveMarker marker = Instantiate(markerPrefab, transform);
        marker.gameObject.SetActive(false);
        return marker;
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
