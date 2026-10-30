using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A pooled one-shot effect on its prefab (Docs/Animation.md, Phase 6): plays one variant and direction of its baked
/// <see cref="EffectSprites"/> frame by frame on <see cref="Time.deltaTime"/> (visual only), then goes back to its
/// <see cref="EffectPool"/>. It sorts by the 2D renderer's custom axis (Docs/WorldScale.md, Phase 1): its root, whose
/// <see cref="SortingGroup"/> sorts at where it stands, stands at the ground height it is given, nudged toward the
/// camera by its sort nudge, and its sprite, on a child, is drawn where it is played. Nothing rotates or scales it. With
/// a light (Docs/Lighting.md, Phase 2), a variant its frames give a light colour lights what is around it in that
/// colour, from its light intensity when it starts down to nothing as it ends; other variants leave it off. A pooled
/// effect stays active with its renderer and light off.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SortingGroup))]
public class EffectPlayer : MonoBehaviour
{
    [SerializeField, Tooltip("The effect's baked frames, written by its recipe's Generate.")]
    private EffectSprites sprites;
    [SerializeField, Tooltip("The renderer the frames show on, on a child moved to where the effect is played.")]
    private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f), Tooltip("How far toward the camera the effect sorts from the ground height it is played at, in units: above 0 it draws over what stands there.")]
    private float sortNudge;
    [SerializeField, Tooltip("The light the effect casts, under its sprite and off until it plays a variant with a light colour. None for an effect that never lights.")]
    private Light2D effectLight;
    [SerializeField, Min(0f), Tooltip("The light's intensity as the effect starts; it fades to nothing over the effect's frames.")]
    private float lightIntensity = 1f;

    private EffectPool pool;
    private int variant;
    private int direction;
    private int frame;
    private float elapsed;
    private bool lit;

    public EffectSprites Sprites => sprites;
    public bool IsPlaying { get; private set; }
    public int Variant => variant;
    public int Direction => direction;
    public int Frame => frame;
    public Light2D EffectLight => effectLight;

    /// <summary>
    /// The prefab this instance was made from, for its pool.
    /// </summary>
    internal EffectPlayer SourcePrefab { get; set; }

    /// <summary>
    /// Fails loudly when the frames or the renderer are missing, or the renderer is not on a child.
    /// </summary>
    private void Awake()
    {
        if (sprites == null || spriteRenderer == null)
        {
            Debug.LogError($"{name} needs its {nameof(EffectSprites)} and sprite renderer to play.", this);
        }
        else if (spriteRenderer.transform.parent != transform)
        {
            Debug.LogError($"{name}'s sprite renderer should be on a child, so the effect can sort apart from where it is drawn.", this);
        }
    }

    /// <summary>
    /// Moves the effect on by the frame's time.
    /// </summary>
    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// Shows the first frame of <paramref name="effectVariant"/> facing <paramref name="effectDirection"/> at
    /// <paramref name="position"/>, sorted as if standing at height <paramref name="sortingY"/>, and plays it to its end,
    /// then goes back to <paramref name="owner"/> (or hides without one).
    /// </summary>
    public void Play(int effectVariant, int effectDirection, Vector2 position, float sortingY, EffectPool owner)
    {
        pool = owner;
        variant = effectVariant;
        direction = effectDirection;
        frame = 0;
        elapsed = 0f;
        IsPlaying = true;
        Transform own = transform;
        float standing = sortingY - sortNudge;
        own.position = new Vector3(position.x, standing, own.position.z);
        if (sprites != null && spriteRenderer != null)
        {
            Transform drawn = spriteRenderer.transform;
            drawn.localPosition = new Vector3(0f, position.y - standing, drawn.localPosition.z);
            spriteRenderer.sprite = sprites.Get(variant, direction, 0);
            spriteRenderer.enabled = true;
        }

        StartLight();
    }

    /// <summary>
    /// Moves the effect on by <paramref name="deltaTime"/> seconds: shows the frame that time falls in, and finishes once
    /// past the last.
    /// </summary>
    internal void Tick(float deltaTime)
    {
        if (!IsPlaying || sprites == null)
        {
            return;
        }

        elapsed += deltaTime;
        int reached = Mathf.FloorToInt(elapsed / sprites.FrameSeconds);
        if (reached >= sprites.FrameCount)
        {
            Finish();
            return;
        }

        if (lit)
        {
            effectLight.intensity = lightIntensity * (1f - elapsed / sprites.Duration);
        }

        if (reached != frame)
        {
            frame = reached;
            spriteRenderer.sprite = sprites.Get(variant, direction, frame);
        }
    }

    /// <summary>
    /// Hides the effect until it is played again.
    /// </summary>
    public void Hide()
    {
        IsPlaying = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }

        lit = false;
        if (effectLight != null)
        {
            effectLight.enabled = false;
        }
    }

    /// <summary>
    /// Sets the frames, the renderer (on a child), the sort nudge and the light with its starting intensity in code, for
    /// tests that build an effect without its prefab.
    /// </summary>
    internal void Configure(EffectSprites effectSprites, SpriteRenderer renderer, float nudge = 0f, Light2D light = null, float intensity = 1f)
    {
        sprites = effectSprites;
        spriteRenderer = renderer;
        sortNudge = nudge;
        effectLight = light;
        lightIntensity = intensity;
    }

    /// <summary>
    /// Turns the light on at its brightest in the colour the frames give the variant playing, or off for a variant
    /// without one.
    /// </summary>
    private void StartLight()
    {
        if (effectLight == null)
        {
            return;
        }

        Color colour = sprites != null ? sprites.LightColour(variant) : Color.clear;
        lit = colour.a > 0f;
        effectLight.enabled = lit;
        if (lit)
        {
            effectLight.color = colour;
            effectLight.intensity = lightIntensity;
        }
    }

    /// <summary>
    /// Ends the effect: back to its pool, or hidden without one.
    /// </summary>
    private void Finish()
    {
        IsPlaying = false;
        if (pool != null)
        {
            pool.Release(this);
            return;
        }

        Hide();
    }
}
