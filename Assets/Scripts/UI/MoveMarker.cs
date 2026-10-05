using UnityEngine;

/// <summary>
/// The pooled ring shown where a move order was given: it shrinks and fades over its lifetime.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class MoveMarker : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0.01f)] private float lifetime = 0.45f;
    [SerializeField, Min(0f)] private float startScale = 1.3f;
    [SerializeField, Min(0f)] private float endScale = 0.5f;
    [SerializeField] private Color color = new(0.45f, 1f, 0.55f, 0.9f);

    private float elapsed;

    public float Lifetime => lifetime;

    /// <summary>
    /// Falls back to the sprite renderer on this object when none is assigned.
    /// </summary>
    private void Awake()
    {
        ResolveRenderer();
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
    /// Sets scale and colour for normalised progress <paramref name="t"/>: from the start scale in the marker
    /// colour to the end scale fully transparent.
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
