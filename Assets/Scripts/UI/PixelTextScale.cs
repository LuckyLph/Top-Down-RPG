using TMPro;
using UnityEngine;
using VContainer;

/// <summary>
/// Draws its text smaller than the UI zoom draws text while keeping its pixel font crisp (Docs/UiRevamp.md, Phase 3):
/// each font pixel covers a whole number of display pixels, the one nearest <see cref="TargetScale"/> of the zoom's
/// (half up, never fewer than two unless the zoom is smaller, never more than the zoom's), so its scale is that number
/// over the zoom: 2/3 at x3, 3/4 at x4, 4/5 at x5, 5/6 at x6, and unchanged at x2. Its <see cref="ThemedText"/> then
/// snaps the glyphs to whole display pixels. Follows <see cref="UiScale.Changed"/> once injected, through its scope's
/// auto-inject objects; without a zoom it keeps its saved scale.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class PixelTextScale : MonoBehaviour
{
    private const int MinDisplayPixels = 2;

    [SerializeField, Range(0.5f, 1f), Tooltip("The scale aimed at; each zoom takes the nearest scale whose font pixels are whole display pixels.")]
    private float targetScale = 0.75f;

    private TMP_Text text;
    private UiScale scale;
    private bool following;

    public float TargetScale => targetScale;

    /// <summary>
    /// Receives the zoom to follow, and follows it at once when enabled.
    /// </summary>
    [Inject]
    public void Construct(UiScale uiScale)
    {
        scale = uiScale;
        if (enabled && gameObject.activeInHierarchy)
        {
            Follow();
        }
    }

    /// <summary>
    /// Follows the zoom again once injected.
    /// </summary>
    private void OnEnable()
    {
        if (scale != null)
        {
            Follow();
        }
    }

    /// <summary>
    /// Stops following the zoom.
    /// </summary>
    private void OnDisable()
    {
        if (following)
        {
            scale.Changed -= Apply;
            following = false;
        }
    }

    /// <summary>
    /// The scale of a text whose font pixels are whole display pixels at <paramref name="zoom"/>, nearest
    /// <paramref name="target"/>: the display pixels per font pixel rounded half up, at least two (or the zoom, when
    /// smaller) and at most the zoom, over the zoom.
    /// </summary>
    internal static float ScaleFor(int zoom, float target)
    {
        int clampedZoom = Mathf.Max(1, zoom);
        int pixels = Mathf.Clamp(Mathf.FloorToInt(clampedZoom * target + 0.5f), Mathf.Min(MinDisplayPixels, clampedZoom), clampedZoom);
        return (float)pixels / clampedZoom;
    }

    /// <summary>
    /// Scales the text for <paramref name="zoom"/> and rebuilds it, so its glyphs snap at the new scale.
    /// </summary>
    internal void Apply(int zoom)
    {
        float shown = ScaleFor(zoom, targetScale);
        transform.localScale = new Vector3(shown, shown, 1f);
        if (text == null)
        {
            text = GetComponent<TMP_Text>();
        }

        if (text != null && text.isActiveAndEnabled)
        {
            text.ForceMeshUpdate(true);
        }
    }

    /// <summary>
    /// Subscribes to the zoom's changes once and applies it now.
    /// </summary>
    private void Follow()
    {
        if (!following)
        {
            scale.Changed += Apply;
            following = true;
        }

        Apply();
    }

    /// <summary>
    /// Applies the zoom it follows.
    /// </summary>
    private void Apply()
    {
        Apply(scale.Factor);
    }
}
