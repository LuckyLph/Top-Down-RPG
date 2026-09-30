using TMPro;
using UnityEngine;

// Pooled damage number view. DamagePopupLayer owns the canvas, pool and timing; this only renders itself.
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(TextMeshProUGUI))]
public class FloatingDamageText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0.01f)] private float lifetime = 1f;
    [SerializeField] private Vector3 riseOffset = new(0f, 0.9f, 0f);
    [SerializeField] private Color textColor = new(0.7f, 0.12f, 0.12f, 1f);

    private RectTransform canvasRect;
    private Camera targetCamera;
    private Vector3 worldPosition;
    private float elapsed;

    public float Lifetime => lifetime;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Show(int amount, Vector3 startWorldPosition, Camera camera, RectTransform parentCanvas)
    {
        ResolveReferences();

        worldPosition = startWorldPosition;
        targetCamera = camera;
        canvasRect = parentCanvas;
        elapsed = 0f;

        text.text = amount.ToString();
        text.color = textColor;
        canvasGroup.alpha = 1f;
        Refresh();
    }

    // Returns true once the popup has lived out its lifetime and can go back to the pool.
    internal bool Advance(float deltaTime)
    {
        elapsed += deltaTime;
        return elapsed >= lifetime;
    }

    // Projects the rising world position onto the overlay canvas and fades out over the lifetime.
    internal void Refresh()
    {
        if (targetCamera == null || canvasRect == null)
        {
            return;
        }

        float t = Mathf.Clamp01(elapsed / lifetime);
        Vector3 currentWorldPosition = Vector3.Lerp(worldPosition, worldPosition + riseOffset, t);
        Vector3 screenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);

        if (screenPoint.z <= 0f)
        {
            canvasGroup.alpha = 0f;
            return;
        }

        // Overlay canvases take a null camera for screen-to-local conversion.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint);
        rectTransform.anchoredPosition = localPoint;
        canvasGroup.alpha = 1f - t;
    }

    private void ResolveReferences()
    {
        if (text == null)
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }
    }
}
