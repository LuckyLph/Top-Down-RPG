using UnityEngine;

/// <summary>
/// A thin world-space health bar above a unit, in the style of League of Legends minion bars: a dark rounded frame
/// and a rounded fill (both 9-sliced, so the corners keep their radius at any width) that shrinks toward the left.
/// It polls <see cref="Health.CurrentHealth"/> each frame (one integer compare) because network syncs set health
/// silently, and hides while the unit is dead.
/// </summary>
[DisallowMultipleComponent]
public class WorldHealthBar : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private SpriteRenderer frame;
    [SerializeField] private SpriteRenderer fill;

    private float fullWidth;
    private float fillHeight;
    private float leftEdge;
    private int shownHealth = int.MinValue;
    private int shownMaxHealth;

    internal float FillFraction { get; private set; } = 1f;
    internal bool IsShown => frame != null && frame.enabled;

    /// <summary>
    /// Finds the unit's <see cref="Health"/> when unassigned and caches the fill's full size and left edge. Logs an
    /// error and disables itself without a health, a frame or a sliced fill.
    /// </summary>
    private void Awake()
    {
        ResolveHealth();
        if (health == null || frame == null || fill == null || fill.drawMode == SpriteDrawMode.Simple)
        {
            Debug.LogError($"{name} needs a {nameof(Health)} above it, a frame renderer and a sliced fill renderer.", this);
            enabled = false;
            return;
        }

        CacheFillGeometry();
    }

    /// <summary>
    /// Fills in the unit's <see cref="Health"/> in the inspector when none is assigned.
    /// </summary>
    private void OnValidate()
    {
        ResolveHealth();
    }

    /// <summary>
    /// Forces a redraw so the bar is current as soon as it is enabled.
    /// </summary>
    private void OnEnable()
    {
        shownHealth = int.MinValue;
        Refresh();
    }

    /// <summary>
    /// Polls the health after gameplay updates and redraws when it changed.
    /// </summary>
    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>
    /// Redraws the bar if the health changed since it was last shown.
    /// </summary>
    internal void Refresh()
    {
        if (health == null || fill == null)
        {
            return;
        }

        int current = health.CurrentHealth;
        int max = health.MaxHealth;
        if (current == shownHealth && max == shownMaxHealth)
        {
            return;
        }

        shownHealth = current;
        shownMaxHealth = max;
        FillFraction = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;
        bool alive = current > 0;
        frame.enabled = alive;
        fill.enabled = alive;

        fill.size = new Vector2(fullWidth * FillFraction, fillHeight);
        Transform fillTransform = fill.transform;
        Vector3 position = fillTransform.localPosition;
        position.x = leftEdge + fullWidth * FillFraction * 0.5f;
        fillTransform.localPosition = position;
    }

    /// <summary>
    /// Assigns the health and renderers in code, for tests, caches the fill geometry and forces the next refresh to
    /// redraw.
    /// </summary>
    internal void Configure(Health unitHealth, SpriteRenderer frameRenderer, SpriteRenderer fillRenderer)
    {
        health = unitHealth;
        frame = frameRenderer;
        fill = fillRenderer;
        CacheFillGeometry();
        shownHealth = int.MinValue;
    }

    /// <summary>
    /// Records the fill's authored size as the full bar and its left edge, so shrinking keeps it anchored left.
    /// </summary>
    private void CacheFillGeometry()
    {
        fullWidth = fill.size.x;
        fillHeight = fill.size.y;
        leftEdge = fill.transform.localPosition.x - fullWidth * 0.5f;
    }

    /// <summary>
    /// Uses the nearest <see cref="Health"/> on this object or its parents when none is assigned.
    /// </summary>
    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }
}
