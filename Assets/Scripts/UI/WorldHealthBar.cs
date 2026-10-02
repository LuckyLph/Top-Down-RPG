using UnityEngine;

/// <summary>
/// A thin world-space health bar above a unit, in the style of League of Legends minion bars: a dark frame and a
/// fill that shrinks toward the left. It polls <see cref="Health.CurrentHealth"/> each frame (one integer compare)
/// because network syncs set health silently, and hides while the unit is dead.
/// </summary>
[DisallowMultipleComponent]
public class WorldHealthBar : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private SpriteRenderer frame;
    [SerializeField] private SpriteRenderer fill;

    private float fullScaleX;
    private float fullWidth;
    private float leftEdge;
    private int shownHealth = int.MinValue;
    private int shownMaxHealth;

    internal float FillFraction { get; private set; } = 1f;
    internal bool IsShown => frame != null && frame.enabled;

    private void Awake()
    {
        ResolveHealth();
        if (health == null || frame == null || fill == null)
        {
            Debug.LogError($"{name} needs a {nameof(Health)} above it and frame and fill renderers.", this);
            enabled = false;
            return;
        }

        CacheFillGeometry();
    }

    private void OnValidate()
    {
        ResolveHealth();
    }

    private void OnEnable()
    {
        shownHealth = int.MinValue;
        Refresh();
    }

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

        Transform fillTransform = fill.transform;
        Vector3 scale = fillTransform.localScale;
        scale.x = fullScaleX * FillFraction;
        fillTransform.localScale = scale;
        Vector3 position = fillTransform.localPosition;
        position.x = leftEdge + fullWidth * FillFraction * 0.5f;
        fillTransform.localPosition = position;
    }

    internal void Configure(Health unitHealth, SpriteRenderer frameRenderer, SpriteRenderer fillRenderer)
    {
        health = unitHealth;
        frame = frameRenderer;
        fill = fillRenderer;
        CacheFillGeometry();
        shownHealth = int.MinValue;
    }

    private void CacheFillGeometry()
    {
        Transform fillTransform = fill.transform;
        fullScaleX = fillTransform.localScale.x;
        float spriteWidth = fill.sprite != null ? fill.sprite.bounds.size.x : 1f;
        fullWidth = spriteWidth * fullScaleX;
        leftEdge = fillTransform.localPosition.x - fullWidth * 0.5f;
    }

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }
    }
}
