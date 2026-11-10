using UnityEngine;

/// <summary>
/// How the UI zoom is chosen (Docs/DesignSystem.md, Foundations: Scale): the size in UI px every layout fits, the display
/// lines per step of the readable zoom, and how many steps the UI size setting adds at most. Assigned on the
/// <c>MainLifetimeScope</c>.
/// </summary>
[CreateAssetMenu(fileName = "UiScale", menuName = "TopDownRPG/UI/UI Scale Settings")]
public sealed class UiScaleSettings : ScriptableObject
{
    [SerializeField, Tooltip("The smallest size in UI px every layout fits. The zoom is lowered until it fits the display.")]
    private Vector2Int minimumLayout = new(480, 270);

    [SerializeField, Min(1), Tooltip("The readable zoom is the display height divided by this, rounded up: the smallest whole zoom at which the Caption text size has a body height of 18 px per 1080 display lines.")]
    private int linesPerZoom = 390;

    [SerializeField, Range(0, 4), Tooltip("How many zoom steps above the readable one the UI size setting offers, where the display fits them.")]
    private int maxSizeStep = 2;

    public Vector2Int MinimumLayout => new(Mathf.Max(1, minimumLayout.x), Mathf.Max(1, minimumLayout.y));
    public int LinesPerZoom => Mathf.Max(1, linesPerZoom);
    public int MaxSizeStep => Mathf.Max(0, maxSizeStep);

    /// <summary>
    /// Builds an unsaved, hidden instance with the given values (today's by default), for tests that must not touch
    /// project assets.
    /// </summary>
    public static UiScaleSettings Create(int layoutWidth = 480, int layoutHeight = 270, int lines = 390, int maxStep = 2)
    {
        UiScaleSettings settings = CreateInstance<UiScaleSettings>();
        settings.minimumLayout = new Vector2Int(layoutWidth, layoutHeight);
        settings.linesPerZoom = lines;
        settings.maxSizeStep = maxStep;
        settings.hideFlags = HideFlags.HideAndDontSave;
        return settings;
    }
}
