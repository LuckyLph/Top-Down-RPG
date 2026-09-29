using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(TextMeshProUGUI))]
public class FloatingDamageText : MonoBehaviour
{
    private const float DefaultLifetime = 1f;
    private static readonly Vector3 DefaultRiseOffset = new(0f, 0.9f, 0f);
    private static readonly Color DefaultColor = new(0.7f, 0.12f, 0.12f, 1f);
    private const int DefaultFontSize = 32;
    private static readonly Vector2 DefaultSize = new(160f, 40f);
    private const int CanvasSortingOrder = 5000;

    private static Canvas popupCanvas;
    private static RectTransform popupCanvasRect;
    private static TMP_FontAsset popupFont;

    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Min(0.01f)] private float lifetime = DefaultLifetime;
    [SerializeField] private Vector3 riseOffset = DefaultRiseOffset;
    [SerializeField] private Color textColor = DefaultColor;

    private Camera targetCamera;
    private Vector3 worldPosition;
    private float elapsed;

    // Domain reload is disabled, so statics would otherwise survive between play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        popupCanvas = null;
        popupCanvasRect = null;
        popupFont = null;
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveCamera();
        UpdateVisualState();
    }

    private void Update()
    {
        if (lifetime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        elapsed += Time.deltaTime;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private void LateUpdate()
    {
        UpdateVisualState();
    }

    public void Initialize(int amount, Color color, float displayLifetime, Vector3 displayRiseOffset, Vector3 startWorldPosition)
    {
        ResolveReferences();

        lifetime = Mathf.Max(0.01f, displayLifetime);
        riseOffset = displayRiseOffset;
        textColor = color;
        worldPosition = startWorldPosition;
        elapsed = 0f;

        text.text = amount.ToString();
        text.color = textColor;
        canvasGroup.alpha = 1f;

        ResolveCamera();
        UpdateVisualState();
    }

    public static FloatingDamageText Spawn(int amount, Vector3 startWorldPosition)
    {
        EnsureCanvas();

        GameObject popup = new(
            $"DamagePopup_{amount}",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI),
            typeof(CanvasGroup));

        RectTransform popupRect = popup.GetComponent<RectTransform>();
        popupRect.SetParent(popupCanvasRect, false);
        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = DefaultSize;

        TextMeshProUGUI popupText = popup.GetComponent<TextMeshProUGUI>();
        popupText.alignment = TextAlignmentOptions.Center;
        popupText.font = GetPopupFont();
        popupText.fontSize = DefaultFontSize;
        popupText.textWrappingMode = TextWrappingModes.NoWrap;
        popupText.overflowMode = TextOverflowModes.Overflow;
        popupText.raycastTarget = false;
        popupText.richText = false;

        FloatingDamageText floatingText = popup.AddComponent<FloatingDamageText>();
        floatingText.rectTransform = popupRect;
        floatingText.text = popupText;
        floatingText.canvasGroup = popup.GetComponent<CanvasGroup>();
        floatingText.Initialize(amount, DefaultColor, DefaultLifetime, DefaultRiseOffset, startWorldPosition);
        return floatingText;
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

    private void ResolveCamera()
    {
        if (targetCamera != null && targetCamera.isActiveAndEnabled)
        {
            return;
        }

        targetCamera = Camera.main;
        if (targetCamera == null && Camera.allCamerasCount > 0)
        {
            targetCamera = Camera.allCameras[0];
        }
    }

    private void UpdateVisualState()
    {
        ResolveReferences();
        ResolveCamera();
        if (targetCamera == null || popupCanvasRect == null)
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

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            popupCanvasRect,
            screenPoint,
            popupCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera,
            out Vector2 localPoint);

        rectTransform.anchoredPosition = localPoint;
        canvasGroup.alpha = 1f - t;
        text.color = textColor;
    }

    private static void EnsureCanvas()
    {
        if (popupCanvas != null && popupCanvasRect != null)
        {
            return;
        }

        Canvas existingCanvas = FindAnyObjectByType<Canvas>();
        if (existingCanvas != null && existingCanvas.name == "DamagePopupCanvas")
        {
            popupCanvas = existingCanvas;
            popupCanvasRect = existingCanvas.GetComponent<RectTransform>();
            return;
        }

        GameObject canvasObject = new(
            "DamagePopupCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));

        popupCanvas = canvasObject.GetComponent<Canvas>();
        popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        popupCanvas.overrideSorting = true;
        popupCanvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        popupCanvasRect = canvasObject.GetComponent<RectTransform>();
    }

    private static TMP_FontAsset GetPopupFont()
    {
        if (popupFont == null)
        {
            TMP_Settings.LoadDefaultSettings();
            popupFont = TMP_Settings.GetFontAsset();
        }

        if (popupFont == null)
        {
            popupFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        if (popupFont == null)
        {
            try
            {
                popupFont = TMP_FontAsset.CreateFontAsset("Arial", "Regular");
                popupFont.hideFlags = HideFlags.HideAndDontSave;
            }
            catch
            {
                popupFont = null;
            }
        }

        return popupFont;
    }
}
