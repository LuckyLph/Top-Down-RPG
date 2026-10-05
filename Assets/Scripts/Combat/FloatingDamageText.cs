using TMPro;
using UnityEngine;

/// <summary>
/// One pooled combat popup on the <see cref="DamagePopupLayer"/> canvas: text anchored to a world position that
/// rises by <c>riseOffset</c> and fades out over its lifetime.
/// </summary>
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

    private RectTransform canvasRect;
    private Camera targetCamera;
    private Vector3 worldPosition;
    private float elapsed;

    public float Lifetime => lifetime;

    /// <summary>
    /// Caches the text, rect transform and canvas group when unassigned.
    /// </summary>
    private void Awake()
    {
        ResolveReferences();
    }

    /// <summary>
    /// Restarts the popup with <paramref name="message"/> in <paramref name="color"/> at
    /// <paramref name="startWorldPosition"/>, projected through <paramref name="camera"/> into
    /// <paramref name="parentCanvas"/>, scaled by <paramref name="scale"/>.
    /// </summary>
    public void Show(string message, Color color, Vector3 startWorldPosition, Camera camera, RectTransform parentCanvas, float scale = 1f)
    {
        ResolveReferences();

        worldPosition = startWorldPosition;
        targetCamera = camera;
        canvasRect = parentCanvas;
        elapsed = 0f;

        text.text = message;
        text.color = color;
        canvasGroup.alpha = 1f;
        rectTransform.localScale = Vector3.one * scale;
        Refresh();
    }

    /// <summary>
    /// Ages the popup by <paramref name="deltaTime"/>; true once its lifetime is over.
    /// </summary>
    internal bool Advance(float deltaTime)
    {
        elapsed += deltaTime;
        return elapsed >= lifetime;
    }

    /// <summary>
    /// Places the popup at its current risen world position projected onto the canvas and fades it by age; hides it
    /// when that point is behind the camera. Does nothing before <see cref="Show"/>.
    /// </summary>
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

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint);
        rectTransform.anchoredPosition = localPoint;
        canvasGroup.alpha = 1f - t;
    }

    /// <summary>
    /// Looks up the sibling text, rect transform and canvas group when not assigned.
    /// </summary>
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
