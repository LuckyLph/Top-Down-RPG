using UnityEngine;

/// <summary>
/// A layered sprite animator for a character baked into a <see cref="CharacterAnimationSet"/> (Docs/Animation.md,
/// Runtime), driven by gameplay state rather than Mecanim. Each <see cref="Update"/> reads the injected clock, snaps the
/// facing to one of the eight directions with a hysteresis margin, advances the locomotion layer from the speed, and
/// returns the sprite to show: the locomotion layer's, the only layer so far (attacks and casts come as an action layer
/// above it). Allocates nothing per update, so EditMode tests drive it on a manual clock.
/// </summary>
public sealed class CharacterAnimator
{
    private readonly IClock clock;
    private readonly float directionMargin;
    private readonly CharacterLocomotionLayer locomotion;
    private float lastTime;
    private bool started;

    /// <summary>
    /// Builds an animator of <paramref name="set"/>'s clips, picked and paced by <paramref name="settings"/>, on
    /// <paramref name="gameClock"/>.
    /// </summary>
    public CharacterAnimator(CharacterAnimationSet set, CharacterLocomotionSettings settings, IClock gameClock)
    {
        clock = gameClock;
        directionMargin = settings != null ? settings.DirectionMargin : 0f;
        locomotion = new CharacterLocomotionLayer(set, settings);
    }

    public int Direction { get; private set; } = CharacterDirections.South;
    public CharacterGait Gait => locomotion.Gait;
    public float Phase => locomotion.Phase;
    public float PlaybackRate => locomotion.PlaybackRate;
    public int Frame => locomotion.Frame;
    public CharacterAnimationClip Clip => locomotion.Clip;
    public Sprite Sprite { get; private set; }

    /// <summary>
    /// Advances by the clock's time since the last update for a character moving at <paramref name="velocity"/>
    /// (units per second) and facing <paramref name="facing"/>, and returns the sprite to show (null without clips).
    /// The first update snaps to the nearest direction and does not advance.
    /// </summary>
    public Sprite Update(Vector2 velocity, Vector2 facing)
    {
        float now = clock.Time;
        float deltaTime = started ? Mathf.Max(0f, now - lastTime) : 0f;
        lastTime = now;
        Direction = started || facing.sqrMagnitude < 1e-8f
            ? CharacterDirections.Keep(Direction, facing, directionMargin)
            : CharacterDirections.Nearest(facing);
        started = true;

        locomotion.Advance(velocity.magnitude, deltaTime);
        Sprite = locomotion.SpriteFacing(Direction);
        return Sprite;
    }
}
