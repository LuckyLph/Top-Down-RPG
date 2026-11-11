using TMPro;
using UnityEngine;

/// <summary>
/// Gives a TMP text its theme (Docs/DesignSystem.md, Components): when it wakes it sets its own font, size, colour and,
/// for text read against <see cref="UiColour.Outline"/>, the font's outlined material from the <see cref="UiTheme"/>.
/// Each time the text's mesh is built it moves every glyph onto the whole UI px of its root canvas, so the pixel font
/// stays crisp wherever its rect lands. It has no <c>Update</c>; views that change the size call
/// <see cref="SetSize"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class ThemedText : MonoBehaviour
{
    [SerializeField] private UiTheme theme;
    [SerializeField] private UiColour colour = UiColour.Text;
    [SerializeField, Tooltip("The role the text is read against, for its contrast; Outline outlines it.")]
    private UiColour background = UiColour.SurfaceRaised;
    [SerializeField] private UiTextSize size = UiTextSize.Body;

    private TMP_Text text;
    private bool subscribed;

    public UiTheme Theme => theme;
    public UiColour Colour => colour;
    public UiColour Background => background;
    public UiTextSize Size => size;
    public UiTextSize ShownSize { get; private set; }

    /// <summary>
    /// Applies the theme to the text; logs an error and keeps the text as saved without a theme.
    /// </summary>
    private void Awake()
    {
        Apply();
    }

    /// <summary>
    /// Snaps the glyphs of every mesh the text builds from now on.
    /// </summary>
    private void OnEnable()
    {
        ResolveText();
        if (text != null && !subscribed)
        {
            text.OnPreRenderText += SnapToPixels;
            subscribed = true;
        }
    }

    /// <summary>
    /// Stops snapping the text's glyphs.
    /// </summary>
    private void OnDisable()
    {
        if (text != null && subscribed)
        {
            text.OnPreRenderText -= SnapToPixels;
        }

        subscribed = false;
    }

    /// <summary>
    /// Shows the text at <paramref name="shown"/>: that size's font, size and, when outlined, outlined material.
    /// </summary>
    public void SetSize(UiTextSize shown)
    {
        ResolveText();
        if (theme == null || text == null)
        {
            return;
        }

        UiTheme.TextStyle style = theme.Style(shown);
        if (style.Font == null)
        {
            return;
        }

        ShownSize = shown;
        text.font = style.Font;
        text.fontSize = style.Pixels;
        text.fontSharedMaterial = background == UiColour.Outline && style.Outlined != null ? style.Outlined : style.Font.material;
    }

    /// <summary>
    /// Shows the text at its own size again.
    /// </summary>
    public void ResetSize()
    {
        SetSize(size);
    }

    /// <summary>
    /// Sets the theme and roles in code and applies them, for tests that build the text without a prefab.
    /// </summary>
    internal void Configure(UiTheme uiTheme, UiColour textColour, UiColour backgroundColour, UiTextSize textSize)
    {
        theme = uiTheme;
        colour = textColour;
        background = backgroundColour;
        size = textSize;
        Apply();
    }

    /// <summary>
    /// Moves every visible glyph of <paramref name="info"/> by what is left of its origin under a whole UI px, counted
    /// from the root canvas's corner, so each font pixel covers whole display pixels. A text drawn at another scale than
    /// its canvas (<see cref="PixelTextScale"/>, whose font pixels are whole display pixels) snaps to whole display
    /// pixels instead, which a whole UI px of its own would not land on.
    /// </summary>
    internal void SnapToPixels(TMP_TextInfo info)
    {
        Canvas canvas = text != null ? text.canvas : null;
        if (info == null || canvas == null)
        {
            return;
        }

        RectTransform root = (RectTransform)canvas.rootCanvas.transform;
        Vector2 corner = root.rect.min;
        Transform own = transform;
        float relative = root.lossyScale.y != 0f ? Mathf.Abs(own.lossyScale.y / root.lossyScale.y) : 1f;
        float step = Mathf.Abs(relative - 1f) > 0.001f && canvas.rootCanvas.scaleFactor > 0f ? 1f / canvas.rootCanvas.scaleFactor : 1f;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible)
            {
                continue;
            }

            Vector3 onCanvas = root.InverseTransformPoint(own.TransformPoint(new Vector3(character.origin, character.baseLine, 0f)));
            float x = onCanvas.x - corner.x;
            float y = onCanvas.y - corner.y;
            Vector3 offset = own.InverseTransformVector(root.TransformVector(new Vector3(Mathf.Round(x / step) * step - x, Mathf.Round(y / step) * step - y, 0f)));
            Vector3[] vertices = info.meshInfo[character.materialReferenceIndex].vertices;
            for (int v = character.vertexIndex; v < character.vertexIndex + 4; v++)
            {
                vertices[v] += offset;
            }
        }
    }

    /// <summary>
    /// Sets the text's colour and its own size from the theme.
    /// </summary>
    private void Apply()
    {
        ResolveText();
        if (theme == null)
        {
            Debug.LogError($"{name} has no UI theme.", this);
            return;
        }

        text.color = theme.Colour(colour);
        SetSize(size);
    }

    /// <summary>
    /// Finds the text on this object.
    /// </summary>
    private void ResolveText()
    {
        if (text == null)
        {
            text = GetComponent<TMP_Text>();
        }
    }
}
