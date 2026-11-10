using UnityEngine;

/// <summary>
/// The pooled marker shown where a move or attack move order was given (Docs/DesignSystem.md, Components: Marker): its
/// prefab's shape in its own <see cref="UiTheme"/> role, read once when it wakes, shrinking and fading over its lifetime.
/// The move marker and the attack move marker are two prefabs, each with a shape of its own, so colour is never the
/// only cue.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class MoveMarker : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0.01f)] private float lifetime = 0.45f;
    [SerializeField, Min(0f)] private float startScale = 1.3f;
    [SerializeField, Min(0f)] private float endScale = 0.5f;
    [SerializeField] private UiTheme theme;
    [SerializeField, Tooltip("The role the marker shows in: Positive for a move, Danger for an attack move.")]
    private UiColour role = UiColour.Positive;

    private Color color;
    private bool themeRead;
    private float elapsed;

    public float Lifetime => lifetime;
    public UiColour Role => role;

    /// <summary>
    /// Falls back to the sprite renderer on this object when none is assigned and reads the marker's colour from the
    /// theme; logs an error without a theme.
    /// </summary>
    private void Awake()
    {
        ResolveRenderer();
        if (theme == null)
        {
            Debug.LogError($"{name} has no UI theme.", this);
        }

        ReadTheme();
    }

    /// <summary>
    /// Fills in the sprite renderer on this object in the inspector when none is assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveRenderer();
    }

    /// <summary>
    /// Moves the marker to <paramref name="position"/>, keeping its z, and restarts the shrink and fade from the
    /// beginning.
    /// </summary>
    public void Show(Vector2 position)
    {
        ResolveRenderer();
        if (!themeRead)
        {
            ReadTheme();
        }

        transform.position = new Vector3(position.x, position.y, transform.position.z);
        elapsed = 0f;
        Apply(0f);
    }

    /// <summary>
    /// Advances the animation; true once the lifetime is over.
    /// </summary>
    public bool Advance(float deltaTime)
    {
        elapsed += deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);
        Apply(t);
        return elapsed >= lifetime;
    }

    /// <summary>
    /// Sets scale and colour for normalised progress <paramref name="t"/>: from the start scale in the role's colour to
    /// the end scale fully transparent.
    /// </summary>
    private void Apply(float t)
    {
        float scale = Mathf.Lerp(startScale, endScale, t);
        transform.localScale = new Vector3(scale, scale, 1f);
        Color current = color;
        current.a *= 1f - t;
        spriteRenderer.color = current;
    }

    /// <summary>
    /// Sets the theme and role in code and reads the colour, for tests that build the marker without a prefab.
    /// </summary>
    internal void ConfigureTheme(UiTheme uiTheme, UiColour colourRole)
    {
        theme = uiTheme;
        role = colourRole;
        ReadTheme();
    }

    /// <summary>
    /// Reads the role's colour from the theme, when there is one; a pooled copy made outside play mode never wakes, so
    /// <see cref="Show"/> reads it the first time instead.
    /// </summary>
    private void ReadTheme()
    {
        if (theme != null)
        {
            color = theme.Colour(role);
            themeRead = true;
        }
    }

    /// <summary>
    /// Uses the sprite renderer on this object when none is assigned.
    /// </summary>
    private void ResolveRenderer()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }
    }
}
