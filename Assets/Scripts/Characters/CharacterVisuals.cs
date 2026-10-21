using UnityEngine;
using VContainer;

/// <summary>
/// Draws a character from its baked <see cref="CharacterAnimationSet"/> on the sprite renderer beside it. Its motor
/// feeds it the velocity and facing through <see cref="SetLocomotion"/>; each frame, after every motor has moved, it
/// asks its <see cref="CharacterAnimator"/> for the sprite on the game clock and puts it on the renderer when it
/// changed. Logs an error without a set or locomotion settings, keeping the sprite it has.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterVisuals : MonoBehaviour
{
    [SerializeField, Tooltip("The character's baked clips, written by its recipe's Generate.")]
    private CharacterAnimationSet animationSet;

    [SerializeField, Tooltip("Gait speed bands, playback rates and the direction margin.")]
    private CharacterLocomotionSettings locomotion;

    private SpriteRenderer spriteRenderer;
    private IClock clock = UnityClock.Shared;
    private CharacterAnimator animator;
    private Vector2 velocity;
    private Vector2 facing = Vector2.down;
    private Sprite shown;

    public CharacterAnimationSet AnimationSet => animationSet;

    /// <summary>
    /// The animator, built on first use; null without a set or settings.
    /// </summary>
    internal CharacterAnimator Animator => EnsureAnimator();

    /// <summary>
    /// Caches the sprite renderer and fails loudly on a missing set or settings.
    /// </summary>
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (animationSet == null || locomotion == null)
        {
            Debug.LogError($"{name} needs a {nameof(CharacterAnimationSet)} and {nameof(CharacterLocomotionSettings)} to animate.", this);
        }
    }

    /// <summary>
    /// Shows the sprite for the frame's movement once every motor has moved.
    /// </summary>
    private void LateUpdate()
    {
        Tick();
    }

    /// <summary>
    /// Takes the game clock the animation runs on; null falls back to the shared Unity clock.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock ?? UnityClock.Shared;
        animator = null;
    }

    /// <summary>
    /// The movement to show: <paramref name="moveVelocity"/> in units per second and the facing
    /// <paramref name="moveFacing"/>.
    /// </summary>
    public void SetLocomotion(Vector2 moveVelocity, Vector2 moveFacing)
    {
        velocity = moveVelocity;
        facing = moveFacing;
    }

    /// <summary>
    /// Advances the animator and puts its sprite on the renderer when it changed.
    /// </summary>
    internal void Tick()
    {
        CharacterAnimator current = EnsureAnimator();
        if (current == null)
        {
            return;
        }

        Sprite sprite = current.Update(velocity, facing);
        if (sprite == null || ReferenceEquals(sprite, shown))
        {
            return;
        }

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        shown = sprite;
        spriteRenderer.sprite = sprite;
    }

    /// <summary>
    /// Builds the animator when there is none and both the set and the settings are assigned.
    /// </summary>
    private CharacterAnimator EnsureAnimator()
    {
        if (animator == null && animationSet != null && locomotion != null)
        {
            animator = new CharacterAnimator(animationSet, locomotion, clock);
        }

        return animator;
    }
}
