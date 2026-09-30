using UnityEngine;
using UnityEngine.Pool;

// Overlay canvas in the Gameplay scene that hosts pooled damage numbers.
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class DamagePopupLayer : MonoBehaviour
{
    [SerializeField] private FloatingDamageText popupPrefab;
    [SerializeField, Min(0)] private int prewarmCount = 8;

    private RectTransform canvasRect;
    private ObjectPool<FloatingDamageText> pool;

    private void Awake()
    {
        EnsurePool();
        Prewarm();
    }

    private void OnDestroy()
    {
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
        popup.Show(amount, worldPosition, camera, canvasRect, Release);
        return popup;
    }

    internal void Configure(FloatingDamageText prefab)
    {
        popupPrefab = prefab;
    }

    private void Release(FloatingDamageText popup)
    {
        pool.Release(popup);
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
