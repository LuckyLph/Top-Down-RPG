using UnityEngine;

/// <summary>
/// A pooled one-shot effect on its prefab (Docs/Animation.md, Phase 6): plays one variant and direction of its baked
/// <see cref="EffectSprites"/> frame by frame on <see cref="Time.deltaTime"/> (visual only), sorted by the ground height
/// it is given, then goes back to its <see cref="EffectPool"/>. Nothing rotates or scales it. A pooled effect stays
/// active with its renderer off, since activating it again would make its <see cref="YPositionSorter"/> look its
/// renderers up anew.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class EffectPlayer : MonoBehaviour
{
    [SerializeField, Tooltip("The effect's baked frames, written by its recipe's Generate.")]
    private EffectSprites sprites;
    [SerializeField, Tooltip("The renderer the frames show on.")]
    private SpriteRenderer spriteRenderer;
    [SerializeField, Tooltip("Sorts the effect by the ground height it is played at.")]
    private YPositionSorter sorter;

    private EffectPool pool;
    private int variant;
    private int direction;
    private int frame;
    private float elapsed;

    public EffectSprites Sprites => sprites;
    public bool IsPlaying { get; private set; }
    public int Variant => variant;
    public int Direction => direction;
    public int Frame => frame;

    /// <summary>
    /// The prefab this instance was made from, for its pool.
    /// </summary>
    internal EffectPlayer SourcePrefab { get; set; }

    /// <summary>
    /// Fails loudly when the frames, the renderer or the sorter are missing.
    /// </summary>
    private void Awake()
    {
        if (sprites == null || spriteRenderer == null || sorter == null)
        {
            Debug.LogError($"{name} needs its {nameof(EffectSprites)}, sprite renderer and {nameof(YPositionSorter)} to play.", this);
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
        own.position = new Vector3(position.x, position.y, own.position.z);
        if (sorter != null)
        {
            sorter.SetSortingReferenceY(sortingY);
        }

        if (sprites != null && spriteRenderer != null)
        {
            spriteRenderer.sprite = sprites.Get(variant, direction, 0);
            spriteRenderer.enabled = true;
        }
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
    }

    /// <summary>
    /// Sets the frames, renderer and sorter in code, for tests that build an effect without its prefab.
    /// </summary>
    internal void Configure(EffectSprites effectSprites, SpriteRenderer renderer, YPositionSorter effectSorter)
    {
        sprites = effectSprites;
        spriteRenderer = renderer;
        sorter = effectSorter;
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
