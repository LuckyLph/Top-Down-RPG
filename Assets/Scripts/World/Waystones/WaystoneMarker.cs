using UnityEngine;

/// <summary>
/// The standing stone that marks one of its world's waystones (Docs/KnownGaps.md, Phase 3), a prop placed in a chunk
/// scene by the World Editor's Place markers step near the entry it names. It shows its sleeping sprite until this
/// machine's own player has discovered its waystone, then its awake one, whose runes glow, and lets its light burn: its
/// <c>Light</c> child, inactive until then, whose <see cref="PropLight"/> the chunk hands <see cref="PropLighting"/> like
/// any prop's. <see cref="WaystoneMarkers"/> sets it from <see cref="KnownWaystones"/>. Both sprites are slices of one
/// sheet, so switching keeps its glow and normals. Nothing about it runs per frame or is sent: each machine shows what
/// its own player discovered.
/// </summary>
[DisallowMultipleComponent]
public sealed class WaystoneMarker : MonoBehaviour
{
    [SerializeField, Tooltip("The name of the world entry this stone marks; the Place markers step sets it.")]
    private string entryName;
    [SerializeField, Tooltip("The renderer drawing the stone.")]
    private SpriteRenderer visuals;
    [SerializeField, Tooltip("The stone's sprite until its waystone is discovered.")]
    private Sprite dormantSprite;
    [SerializeField, Tooltip("The stone's sprite once its waystone is discovered, its runes glowing.")]
    private Sprite awakeSprite;
    [SerializeField, Tooltip("The stone's light, inactive until its waystone is discovered.")]
    private GameObject glow;

    public string EntryName => entryName;
    public SpriteRenderer Visuals => visuals;
    public Sprite DormantSprite => dormantSprite;
    public Sprite AwakeSprite => awakeSprite;
    public GameObject Glow => glow;

    /// <summary>
    /// Whether it shows its waystone discovered.
    /// </summary>
    public bool IsAwake => visuals != null && awakeSprite != null && visuals.sprite == awakeSprite;

    /// <summary>
    /// Shows its waystone discovered when <paramref name="awake"/>, its runes glowing and its light let burn, or not
    /// discovered.
    /// </summary>
    public void SetAwake(bool awake)
    {
        Sprite shown = awake ? awakeSprite : dormantSprite;
        if (visuals == null || shown == null)
        {
            Debug.LogError($"{name} misses its renderer or its {(awake ? "awake" : "sleeping")} sprite; write it again with its Marker Recipe.", this);
            return;
        }

        visuals.sprite = shown;

        if (glow != null && glow.activeSelf != awake)
        {
            glow.SetActive(awake);
        }
    }

    /// <summary>
    /// Draws through <paramref name="renderer"/> in <paramref name="dormant"/> or <paramref name="awake"/> and lets
    /// <paramref name="light"/> burn while awake, for the prop writer and tests.
    /// </summary>
    internal void Configure(SpriteRenderer renderer, Sprite dormant, Sprite awake, GameObject light)
    {
        visuals = renderer;
        dormantSprite = dormant;
        awakeSprite = awake;
        glow = light;
    }

    /// <summary>
    /// Marks the entry named <paramref name="entry"/>, for tests.
    /// </summary>
    internal void SetEntry(string entry)
    {
        entryName = entry;
    }

    /// <summary>
    /// Points at the renderer of its <c>Visuals</c> child and its <c>Light</c> child when it is added in the editor.
    /// </summary>
    private void Reset()
    {
        Transform child = transform.Find("Visuals");
        visuals = child != null ? child.GetComponent<SpriteRenderer>() : null;
        Transform light = transform.Find("Light");
        glow = light != null ? light.gameObject : null;
    }

    /// <summary>
    /// Warns in the editor when the stone has no renderer.
    /// </summary>
    private void OnValidate()
    {
        if (visuals == null)
        {
            Debug.LogWarning($"{name} has no renderer to show its waystone by; write it again with its Marker Recipe.", this);
        }
    }
}
