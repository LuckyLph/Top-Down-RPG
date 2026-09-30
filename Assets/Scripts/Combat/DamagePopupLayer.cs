using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// Overlay canvas in the Gameplay scene that hosts pooled damage numbers and drives all active ones.
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

    private void Awake()
    {
        EnsurePool();
        Prewarm();
    }

    private void OnDestroy()
    {
        active.Clear();
        pool?.Dispose();
    }

    public FloatingDamageText Spawn(int amount, Vector3 worldPosition, Camera camera)
    {
        EnsurePool();
        if (popupPrefab == null)
        {
            Debug.LogError($"{nameof(DamagePopupLayer)} has no popup prefab assigned.", this);
            return null;
        }

        FloatingDamageText popup = pool.Get();
        popup.Show(amount, worldPosition, camera, canvasRect);
        active.Add(popup);
        return popup;
    }

    // Advances every active popup and returns the finished ones to the pool.
    public void Tick(float deltaTime)
    {
        // Backwards with swap-remove: the swapped-in popup has already been advanced this tick.
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

    // Projects active popups onto the canvas; run after the camera has moved for the frame.
    public void RefreshPositions()
    {
        for (int i = 0; i < active.Count; i++)
        {
            active[i].Refresh();
        }
    }

    internal void Configure(FloatingDamageText prefab)
    {
        popupPrefab = prefab;
    }

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
