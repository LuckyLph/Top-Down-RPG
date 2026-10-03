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

    private void Awake()
    {
        EnsureInitialized();
        if (spriteRenderer == null)
        {
            Debug.LogError($"{name} has no sprite renderer to show its status.", this);
        }
    }

    private void Update()
    {
        Advance(Time.deltaTime);
    }

    public void Show(StatusEffectDefinition definition, int layer)
    {
        EnsureInitialized();
        color = definition != null ? definition.Color : Color.white;
        phase = 0f;
        SetLayer(layer);
        gameObject.SetActive(true);
        Advance(0f);
    }

    public void SetLayer(int layer)
    {
        EnsureInitialized();
        Layer = Mathf.Max(0, layer);
        transform.localScale = baseScale * (1f + Layer * layerScaleStep);
    }

    public void Follow(Vector3 position)
    {
        transform.position = position;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

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
