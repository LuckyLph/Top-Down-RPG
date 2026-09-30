using System;
using TMPro;
using UnityEngine;

// Pooled damage number view. DamagePopupLayer owns the canvas and pool; this only animates itself.
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
    private Action<FloatingDamageText> finished;

    public float Lifetime => lifetime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Finish();
        }
    }

    private void LateUpdate()
    {
        Refresh();
    }

    public void Show(int amount, Vector3 startWorldPosition, Camera camera, RectTransform parentCanvas, Action<FloatingDamageText> onFinished)
    {
        ResolveReferences();

        worldPosition = startWorldPosition;
        targetCamera = camera;
        canvasRect = parentCanvas;
        finished = onFinished;
        elapsed = 0f;

        text.text = amount.ToString();
        text.color = textColor;
        canvasGroup.alpha = 1f;
        Refresh();
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

    private void Finish()
    {
        Action<FloatingDamageText> callback = finished;
        finished = null;

        if (callback != null)
        {
            callback(this);
        }
        else
        {
            Destroy(gameObject);
        }
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
