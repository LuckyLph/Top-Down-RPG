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

    private void OnDisable()
    {
        SetAttackCursor(false);
    }

    private void OnDestroy()
    {
        active.Clear();
        pool?.Dispose();
    }

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

    internal void Configure(MoveMarker prefab, SpriteRenderer ring, Texture2D cursor)
    {
        markerPrefab = prefab;
        hoverRing = ring;
        attackCursor = cursor;
        EnsurePool();
    }

    private void EnsurePool()
    {
        pool ??= new ObjectPool<MoveMarker>(CreateMarker, marker => marker.gameObject.SetActive(true), marker => marker.gameObject.SetActive(false), DestroyMarker);
    }

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

    private MoveMarker CreateMarker()
    {
        MoveMarker marker = Instantiate(markerPrefab, transform);
        marker.gameObject.SetActive(false);
        return marker;
    }

    private static void DestroyMarker(MoveMarker marker)
    {
        if (marker != null)
        {
            Destroy(marker.gameObject);
        }
    }
}
