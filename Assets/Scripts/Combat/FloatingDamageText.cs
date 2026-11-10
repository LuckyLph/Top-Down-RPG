using TMPro;
using UnityEngine;

/// <summary>
/// One pooled combat popup on the <see cref="DamagePopupLayer"/> canvas: outlined text anchored to a world position
/// that rises by <c>riseOffset</c>, or stays in place when shown without rising (Reduce motion, Docs/DesignSystem.md,
/// Phase 6), and fades out over its lifetime, placed on whole UI px so its pixel font stays crisp.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(TextMeshProUGUI))]
[RequireComponent(typeof(ThemedText))]
public class FloatingDamageText : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private ThemedText themedText;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0.01f)] private float lifetime = 1f;
    [SerializeField] private Vector3 riseOffset = new(0f, 0.9f, 0f);

    private RectTransform canvasRect;
    private Camera targetCamera;
    private Vector3 worldPosition;
    private float elapsed;
    private bool rises = true;

    public float Lifetime => lifetime;
    internal bool Rises => rises;

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
    /// <paramref name="parentCanvas"/>, at text size <paramref name="size"/>, or the popup's own size when null, rising
    /// as it fades when <paramref name="rising"/> and fading in place otherwise.
    /// </summary>
    public void Show(string message, Color color, Vector3 startWorldPosition, Camera camera, RectTransform parentCanvas, UiTextSize? size = null, bool rising = true)
    {
        ResolveReferences();

        worldPosition = startWorldPosition;
        targetCamera = camera;
        canvasRect = parentCanvas;
        elapsed = 0f;
        rises = rising;

        if (size.HasValue)
        {
            themedText.SetSize(size.Value);
        }
        else
        {
            themedText.ResetSize();
        }

        text.text = message;
        text.color = color;
        canvasGroup.alpha = 1f;
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
    /// Places the popup at its current world position, risen by its age when it rises, projected onto the canvas, on the
    /// nearest whole UI px from the canvas's corner, and fades it by age; hides it when that point is behind the camera.
    /// Does nothing before <see cref="Show"/>.
    /// </summary>
    internal void Refresh()
    {
        if (targetCamera == null || canvasRect == null)
        {
            return;
        }

        float t = Mathf.Clamp01(elapsed / lifetime);
        Vector3 currentWorldPosition = rises ? Vector3.Lerp(worldPosition, worldPosition + riseOffset, t) : worldPosition;
        Vector3 screenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);

        if (screenPoint.z <= 0f)
        {
            canvasGroup.alpha = 0f;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 localPoint);
        Vector2 corner = canvasRect.rect.min;
        rectTransform.anchoredPosition = new Vector2(Mathf.Round(localPoint.x - corner.x) + corner.x, Mathf.Round(localPoint.y - corner.y) + corner.y);
        canvasGroup.alpha = 1f - t;
    }

    /// <summary>
    /// Looks up the sibling text, themed text, rect transform and canvas group when not assigned.
    /// </summary>
    private void ResolveReferences()
    {
        if (text == null)
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        if (themedText == null)
        {
            themedText = GetComponent<ThemedText>();
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
