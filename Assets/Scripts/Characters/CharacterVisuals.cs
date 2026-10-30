using UnityEngine;
using UnityEngine.Rendering.Universal;
using VContainer;

/// <summary>
/// Draws a character from its baked <see cref="CharacterAnimationSet"/> on the sprite renderer beside it. Its motor
/// feeds it the velocity and facing through <see cref="SetLocomotion"/>, its weapon the attacks to play on their
/// timelines (<see cref="PlayAction"/>) and its abilities the casts (<see cref="PlayCast"/>); each frame, after every
/// motor has moved, it asks its
/// <see cref="CharacterAnimator"/> for the sprite on the game clock and puts it on the renderer when it changed. Logs an error without a set or locomotion settings, keeping the sprite it has.
/// A player's look (<see cref="SetLook"/>) swaps the set and settings; the weapon and the casts look their clips up
/// again in the new set. With a cast light (Docs/Lighting.md, Phase 2), the light each sprite's glow casts shows with
/// that sprite: where the glow shines from, at the cast light's intensity times the frame's strength, in its clip's
/// light colour, and off for a sprite without one. Visual only, on every machine that plays the casts.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class CharacterVisuals : MonoBehaviour
{
    [SerializeField, Tooltip("The character's baked clips, written by its recipe's Generate.")]
    private CharacterAnimationSet animationSet;

    [SerializeField, Tooltip("Gait speed bands, playback rates and the direction margin.")]
    private CharacterLocomotionSettings locomotion;

    [SerializeField, Tooltip("The light a cast's glow casts on what is around it, on a child of this object and off until a frame with a glow shows. None for a character without glowing frames.")]
    private Light2D castLight;

    [SerializeField, Min(0f), Tooltip("The cast light's intensity at a frame's full strength, the glow's brightest.")]
    private float castLightIntensity = 1f;

    private SpriteRenderer spriteRenderer;
    private IClock clock = UnityClock.Shared;
    private CharacterAnimator animator;
    private Vector2 velocity;
    private Vector2 facing = Vector2.down;
    private Sprite shown;

    public CharacterAnimationSet AnimationSet => animationSet;
    public CharacterLocomotionSettings Locomotion => locomotion;
    public Light2D CastLight => castLight;

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
    /// Turns the cast light off while nothing animates.
    /// </summary>
    private void OnDisable()
    {
        if (castLight != null)
        {
            castLight.enabled = false;
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
    /// Draws the character with <paramref name="set"/> at <paramref name="settings"/> from now on: the animator is built
    /// again on the next use, so an action showing stops and the gait starts over. Nothing changes when either is
    /// missing or both are the ones drawn.
    /// </summary>
    public void SetLook(CharacterAnimationSet set, CharacterLocomotionSettings settings)
    {
        if (set == null || settings == null || (set == animationSet && settings == locomotion))
        {
            return;
        }

        animationSet = set;
        locomotion = settings;
        animator = null;
        shown = null;
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
    /// Plays <paramref name="clip"/>, an attack's clip, from now toward <paramref name="direction"/> stretched to
    /// <paramref name="timeline"/>; with <paramref name="holdUntilContact"/> (a copy of another machine's attack) its
    /// windup holds its last frame until <see cref="ReachContact"/>.
    /// </summary>
    public void PlayAction(CharacterAnimationClip clip, Vector2 direction, in AttackTimeline timeline, bool holdUntilContact)
    {
        CharacterAnimator current = EnsureAnimator();
        if (current != null)
        {
            current.PlayAction(clip, direction, timeline, holdUntilContact);
        }
    }

    /// <summary>
    /// Plays <paramref name="clip"/>, a cast's clip, from now on <paramref name="timeline"/>, toward
    /// <paramref name="direction"/> (the current direction when zero), turning with the facing when
    /// <paramref name="followsFacing"/>; <paramref name="movingClip"/> shows while the character moves, and a positive
    /// <paramref name="channelBeat"/> makes a long cast a channel; with <paramref name="untilRelease"/> the release
    /// waits for <see cref="ReachContact"/> (see <see cref="CharacterAnimator.PlayCast"/>).
    /// </summary>
    public void PlayCast(CharacterAnimationClip clip, CharacterAnimationClip movingClip, Vector2 direction, bool followsFacing, in AttackTimeline timeline, float channelBeat, bool untilRelease)
    {
        CharacterAnimator current = EnsureAnimator();
        if (current != null)
        {
            current.PlayCast(clip, movingClip, direction, followsFacing, timeline, channelBeat, untilRelease);
        }
    }

    /// <summary>
    /// Plays the contact (a cast's release) now on an action played until contact.
    /// </summary>
    public void ReachContact()
    {
        CharacterAnimator current = EnsureAnimator();
        if (current != null)
        {
            current.ReachContact();
        }
    }

    /// <summary>
    /// Ends the playing action at once when it is of <paramref name="kind"/>.
    /// </summary>
    public void StopAction(CharacterActionKind kind)
    {
        CharacterAnimator current = EnsureAnimator();
        if (current != null)
        {
            current.StopAction(kind);
        }
    }

    /// <summary>
    /// Holds the sprite on screen for <paramref name="seconds"/> of game time, a hit-stop (see
    /// <see cref="CharacterAnimator.Hold"/>).
    /// </summary>
    public void Hold(float seconds)
    {
        CharacterAnimator current = EnsureAnimator();
        if (current != null)
        {
            current.Hold(seconds);
        }
    }

    /// <summary>
    /// Holds the attack's contact frame for <paramref name="seconds"/> when it shows on the next update; returns
    /// whether it holds (see <see cref="CharacterAnimator.HoldContact"/>).
    /// </summary>
    public bool HoldContact(float seconds)
    {
        CharacterAnimator current = EnsureAnimator();
        return current != null && current.HoldContact(seconds);
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
        ShowLight(current.Light, current.LightColour);
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
    /// Sets the cast light and its intensity at full strength in code, for tests that build the visuals without their
    /// prefab.
    /// </summary>
    internal void Configure(Light2D light, float intensity)
    {
        castLight = light;
        castLightIntensity = intensity;
    }

    /// <summary>
    /// Shows <paramref name="light"/>, the light of the sprite shown, on the cast light: where it shines from, its
    /// strength times the cast light's intensity, in <paramref name="colour"/>; off for a frame without light. Writes
    /// only what changed.
    /// </summary>
    private void ShowLight(CharacterFrameLight light, Color colour)
    {
        if (castLight == null)
        {
            return;
        }

        bool lit = light.IsLit && colour.a > 0f;
        if (castLight.enabled != lit)
        {
            castLight.enabled = lit;
        }

        if (!lit)
        {
            return;
        }

        Transform lightTransform = castLight.transform;
        Vector3 position = new(light.Offset.x, light.Offset.y, 0f);
        if (lightTransform.localPosition != position)
        {
            lightTransform.localPosition = position;
        }

        float intensity = castLightIntensity * light.Strength;
        if (castLight.intensity != intensity)
        {
            castLight.intensity = intensity;
        }

        if (castLight.color != colour)
        {
            castLight.color = colour;
        }
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
