using UnityEngine;

/// <summary>
/// A pooled, purely visual effect that follows a unit while one of its statuses is active: a sprite tinted with the
/// status colour that pulses its alpha, drawn larger for each further status shown on the same unit. Advances with
/// <see cref="Time.deltaTime"/> because it is visual only.
/// </summary>
[DisallowMultipleComponent]
public class StatusVisual : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f), Tooltip("Radians per second of the alpha pulse.")] private float pulseSpeed = 4f;
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.35f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.85f;
    [SerializeField, Min(0f), Tooltip("Extra scale per status already shown on the unit, so stacked visuals stay visible.")]
    private float layerScaleStep = 0.2f;

    private Vector3 baseScale;
    private bool initialized;
    private Color color = Color.white;
    private float phase;

    internal StatusVisual SourcePrefab { get; set; }
    internal Color Color => color;
    internal int Layer { get; private set; }

    /// <summary>
    /// Records the base scale, finds the sprite renderer and fails loudly when there is none.
    /// </summary>
    private void Awake()
    {
        EnsureInitialized();
        if (spriteRenderer == null)
        {
            Debug.LogError($"{name} has no sprite renderer to show its status.", this);
        }
    }

    /// <summary>
    /// Advances the alpha pulse by the frame time.
    /// </summary>
    private void Update()
    {
        Advance(Time.deltaTime);
    }

    /// <summary>
    /// Activates the visual for <paramref name="definition"/>: tints it with the status colour (white without one),
    /// restarts the pulse and scales it for <paramref name="layer"/>.
    /// </summary>
    public void Show(StatusEffectDefinition definition, int layer)
    {
        EnsureInitialized();
        color = definition != null ? definition.Color : Color.white;
        phase = 0f;
        SetLayer(layer);
        gameObject.SetActive(true);
        Advance(0f);
    }

    /// <summary>
    /// Sets how many statuses are shown under this one on the unit (negative counts as 0) and grows the scale by one
    /// step per layer.
    /// </summary>
    public void SetLayer(int layer)
    {
        EnsureInitialized();
        Layer = Mathf.Max(0, layer);
        transform.localScale = baseScale * (1f + Layer * layerScaleStep);
    }

    /// <summary>
    /// Moves the visual to <paramref name="position"/> in world space.
    /// </summary>
    public void Follow(Vector3 position)
    {
        transform.position = position;
    }

    /// <summary>
    /// Deactivates the visual so it can wait in the pool.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Advances the pulse phase by <paramref name="deltaTime"/> and applies the tint with the pulsed alpha.
    /// </summary>
    internal void Advance(float deltaTime)
    {
        phase += deltaTime * pulseSpeed;
        if (spriteRenderer == null)
        {
            return;
        }

        Color tinted = color;
        tinted.a = Mathf.Lerp(minAlpha, maxAlpha, 0.5f + 0.5f * Mathf.Sin(phase));
        spriteRenderer.color = tinted;
    }

    /// <summary>
    /// On first use, records the current scale as the base for layering and falls back to the sprite renderer on
    /// this object.
    /// </summary>
    private void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        baseScale = transform.localScale;
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        initialized = true;
    }
}
