using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// The canvas that floating combat text lives on: spawns <see cref="FloatingDamageText"/> popups from
/// a pool, ages them and returns expired ones to it. Driven by <see cref="DamagePopupPresenter"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class DamagePopupLayer : MonoBehaviour
{
    [SerializeField] private FloatingDamageText popupPrefab;
    [SerializeField, Min(0)] private int prewarmCount = 8;

    private readonly List<FloatingDamageText> active = new();
    private RectTransform canvasRect;
    private ObjectPool<FloatingDamageText> pool;

    internal int ActiveCount => active.Count;

    /// <summary>
    /// Creates the pool and prewarms it with <c>prewarmCount</c> popups.
    /// </summary>
    private void Awake()
    {
        EnsurePool();
        Prewarm();
    }

    /// <summary>
    /// Forgets the active popups and disposes the pool, destroying the popups it holds.
    /// </summary>
    private void OnDestroy()
    {
        active.Clear();
        pool?.Dispose();
    }

    /// <summary>
    /// Shows <paramref name="message"/> in <paramref name="color"/> at <paramref name="worldPosition"/> as seen by
    /// <paramref name="camera"/>, scaled by <paramref name="scale"/>, using a pooled popup. Logs an error and returns
    /// null when no popup prefab is assigned.
    /// </summary>
    public FloatingDamageText Spawn(string message, Color color, Vector3 worldPosition, Camera camera, float scale = 1f)
    {
        EnsurePool();
        if (popupPrefab == null)
        {
            Debug.LogError($"{nameof(DamagePopupLayer)} has no popup prefab assigned.", this);
            return null;
        }

        FloatingDamageText popup = pool.Get();
        popup.Show(message, color, worldPosition, camera, canvasRect, scale);
        active.Add(popup);
        return popup;
    }

    /// <summary>
    /// Ages every active popup by <paramref name="deltaTime"/> and returns those past their lifetime to the pool.
    /// </summary>
    public void Tick(float deltaTime)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            FloatingDamageText popup = active[i];
            if (!popup.Advance(deltaTime))
            {
                continue;
            }

            int last = active.Count - 1;
            active[i] = active[last];
            active.RemoveAt(last);
            pool.Release(popup);
        }
    }

    /// <summary>
    /// Re-projects every active popup onto the canvas, after the camera has moved for the frame.
    /// </summary>
    public void RefreshPositions()
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Refresh();
        }
    }

    /// <summary>
    /// Test setup: sets the popup prefab the pool instantiates.
    /// </summary>
    internal void Configure(FloatingDamageText prefab)
    {
        popupPrefab = prefab;
    }

    /// <summary>
    /// Creates the popup pool on first use: popups are instantiated under this canvas, activated when taken and
    /// deactivated when released.
    /// </summary>
    private void EnsurePool()
    {
        if (pool != null)
        {
            return;
        }

        canvasRect = (RectTransform)transform;
        pool = new ObjectPool<FloatingDamageText>(
            createFunc: () => Instantiate(popupPrefab, canvasRect, false),
            actionOnGet: popup => popup.gameObject.SetActive(true),
            actionOnRelease: popup => popup.gameObject.SetActive(false),
            actionOnDestroy: popup =>
            {
                if (popup != null)
                {
                    Destroy(popup.gameObject);
                }
            },
            defaultCapacity: Mathf.Max(1, prewarmCount));
    }

    /// <summary>
    /// Instantiates <c>prewarmCount</c> popups up front and parks them in the pool; does nothing without a prefab.
    /// </summary>
    private void Prewarm()
    {
        if (popupPrefab == null || prewarmCount <= 0)
        {
            return;
        }

        FloatingDamageText[] warmed = new FloatingDamageText[prewarmCount];
        for (int i = 0; i < warmed.Length; i++)
        {
            warmed[i] = pool.Get();
        }

        for (int i = 0; i < warmed.Length; i++)
        {
            pool.Release(warmed[i]);
        }
    }
}
