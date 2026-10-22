using UnityEngine;

/// <summary>
/// How a projectile looks on one machine (Docs/Animation.md, Phase 4): on a pooled prefab whose root stands where the
/// projectile is on the ground, so its sprite sorts by that point, and whose sprite child sits at the height it flies
/// at. Shows the baked sprite of the direction nearest its flight, set only when that direction changes; nothing
/// rotates or scales it. Visual only: <see cref="ProjectileService"/> moves it, and it never decides a hit.
/// </summary>
[DisallowMultipleComponent]
public class ProjectileVisual : MonoBehaviour
{
    [SerializeField, Tooltip("The projectile drawn in each direction, baked by the weapon sprite writer.")]
    private DirectionalSprites sprites;
    [SerializeField, Tooltip("The sprite child, raised to the height the projectile flies at.")]
    private SpriteRenderer spriteRenderer;

    private int shownDirection = -1;

    /// <summary>
    /// The prefab this instance was made from, for its pool.
    /// </summary>
    internal ProjectileVisual SourcePrefab { get; set; }

    /// <summary>
    /// Fails loudly when the sprites or the renderer are missing.
    /// </summary>
    private void Awake()
    {
        if (sprites == null || spriteRenderer == null)
        {
            Debug.LogError($"{name} needs its {nameof(DirectionalSprites)} and sprite renderer to show a projectile.", this);
        }
    }

    /// <summary>
    /// Shows the projectile on the ground at <paramref name="position"/>, flying along <paramref name="direction"/>.
    /// </summary>
    public void Show(Vector2 position, Vector2 direction)
    {
        shownDirection = -1;
        Move(position, direction);
        gameObject.SetActive(true);
    }

    /// <summary>
    /// Moves the projectile to <paramref name="position"/> and shows the sprite of the direction nearest
    /// <paramref name="direction"/>.
    /// </summary>
    public void Move(Vector2 position, Vector2 direction)
    {
        Transform own = transform;
        own.position = new Vector3(position.x, position.y, own.position.z);
        if (sprites == null || spriteRenderer == null)
        {
            return;
        }

        int index = sprites.IndexOf(direction);
        if (index != shownDirection)
        {
            shownDirection = index;
            spriteRenderer.sprite = sprites.Get(index);
        }
    }

    /// <summary>
    /// Hides the projectile until it is shown again.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Sets the sprites and the renderer in code, for tests that build a visual without the prefab.
    /// </summary>
    internal void Configure(DirectionalSprites directionSprites, SpriteRenderer renderer)
    {
        sprites = directionSprites;
        spriteRenderer = renderer;
    }
}
