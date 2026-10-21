using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="CharacterAnimator"/>'s locomotion layer and directions and for <see cref="CharacterVisuals"/>,
/// on a set of test sprites and a manual clock: the facing snaps to the nearest of eight directions and keeps it until
/// a margin past the boundary; the gait follows the speed bands with hysteresis around each boundary; switching gaits
/// keeps the phase while starting and stopping restart it; the playback rate is speed over natural speed, clamped; a
/// missing gait shows the nearest slower one; updates allocate nothing once warm; the visuals put the sprite on their
/// renderer.
/// </summary>
public class CharacterAnimatorTests
{
    private const float WalkNatural = 1f;
    private const float RunNatural = 4f;
    private const float SprintNatural = 5.6f;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private CharacterLocomotionSettings settings;
    private CharacterAnimationSet set;

    /// <summary>
    /// Builds settings (walk from 0.1, run from 2.4, sprint from 5.6 units per second, 10% hysteresis, rates 0.5 to
    /// 1.5, an 8 degree direction margin) and a set of idle (4 frames of 0.25 s), walk (8 of 0.125 s), run (8 of
    /// 0.1 s) and sprint (8 of 0.075 s) clips, each sprite its own.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        settings = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        set = Track(CharacterAnimationSet.Create(new[]
        {
            Clip("Idle", CharacterGait.Idle, 4, 0.25f, 0f),
            Clip("Walk", CharacterGait.Walk, 8, 0.125f, WalkNatural),
            Clip("Run", CharacterGait.Run, 8, 0.1f, RunNatural),
            Clip("Sprint", CharacterGait.Sprint, 8, 0.075f, SprintNatural)
        }));
    }

    /// <summary>
    /// Destroys every sprite, texture, set and object the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A facing shows as the nearest of the eight directions, counterclockwise from east, from the first update.
    /// </summary>
    [Test]
    public void Direction_SnapsToTheNearestOfEight()
    {
        for (int direction = 0; direction < CharacterDirections.Count; direction++)
        {
            foreach (float offset in new[] { -20f, 0f, 20f })
            {
                Vector2 facing = Facing(CharacterDirections.Angle(direction) + offset);
                Assert.That(CharacterDirections.Nearest(facing), Is.EqualTo(direction), $"{direction} {offset}");
                CharacterAnimator animator = new(set, settings, clock);
                animator.Update(Vector2.zero, facing);
                Assert.That(animator.Direction, Is.EqualTo(direction), $"{direction} {offset}");
            }
        }
    }

    /// <summary>
    /// The direction holds until the facing is more than the margin past the boundary with the next one, either way,
    /// and a zero facing keeps it.
    /// </summary>
    [Test]
    public void Direction_KeepsUntilTheFacingTurnsAMarginPastTheBoundary()
    {
        CharacterAnimator animator = new(set, settings, clock);
        animator.Update(Vector2.zero, Facing(0f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));

        animator.Update(Vector2.zero, Facing(29f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East), "6.5 degrees past the boundary is within the margin.");
        animator.Update(Vector2.zero, Facing(31f));
        Assert.That(animator.Direction, Is.EqualTo(1), "8.5 degrees past it turns to north-east.");

        animator.Update(Vector2.zero, Facing(16f));
        Assert.That(animator.Direction, Is.EqualTo(1), "Turning back holds north-east within the margin.");
        animator.Update(Vector2.zero, Facing(14f));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));

        animator.Update(Vector2.zero, Vector2.zero);
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East), "No facing keeps the direction.");
    }

    /// <summary>
    /// Standing is idle, and each band's speed picks its gait: walk, run and sprint.
    /// </summary>
    [Test]
    public void Gait_FollowsTheSpeedBands()
    {
        Assert.That(GaitAt(0f), Is.EqualTo(CharacterGait.Idle));
        Assert.That(GaitAt(0.05f), Is.EqualTo(CharacterGait.Idle));
        Assert.That(GaitAt(1f), Is.EqualTo(CharacterGait.Walk));
        Assert.That(GaitAt(3f), Is.EqualTo(CharacterGait.Run));
        Assert.That(GaitAt(5f), Is.EqualTo(CharacterGait.Run));
        Assert.That(GaitAt(6.5f), Is.EqualTo(CharacterGait.Sprint));
    }

    /// <summary>
    /// A speed just past a boundary keeps the gait it came from: a run slowing to just under the run boundary still
    /// runs, and walks only a hysteresis share below it; a walk speeding up just past it still walks, and runs only a
    /// share above it. The same holds between idle and walking, and between running and sprinting.
    /// </summary>
    [Test]
    public void Gait_HoldsNearABoundary_UntilTheHysteresisIsCrossed()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, 3f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Step(animator, 2.3f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), "Under the boundary, within the hysteresis.");
        Step(animator, 2.1f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Step(animator, 2.5f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk), "Over the boundary, within the hysteresis.");
        Step(animator, 2.7f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));

        Step(animator, 6f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Step(animator, 6.2f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Step(animator, 5.2f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Step(animator, 5f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));

        Step(animator, 0.095f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Step(animator, 0f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle), "Standing still is always idle.");
        Step(animator, 0.105f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Step(animator, 0.12f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
    }

    /// <summary>
    /// Starting to move begins the gait at its first frame (the left foot planted in front); a switch between gaits
    /// keeps the normalised phase, so the run goes on from where the walk was and shows its own frame there; stopping
    /// begins the idle at its first frame.
    /// </summary>
    [Test]
    public void Phase_IsKeptAcrossGaitSwitches_AndRestartsOnStartingAndStopping()
    {
        CharacterAnimator animator = new(set, settings, clock);
        Vector2 east = Vector2.right;
        animator.Update(Vector2.zero, east);
        clock.Advance(0.3f);
        animator.Update(Vector2.zero, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f));

        clock.Advance(0.1f);
        animator.Update(east * WalkNatural, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        Assert.That(animator.Phase, Is.EqualTo(0.1f).Within(1e-4f), "Starting to walk begins at the first frame.");
        clock.Advance(0.2f);
        animator.Update(east * WalkNatural, east);
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f));

        animator.Update(east * RunNatural, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Phase, Is.EqualTo(0.3f).Within(1e-4f), "The switch keeps the phase.");
        Assert.That(animator.Frame, Is.EqualTo(2));
        Assert.That(animator.Sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.East, 2)));

        clock.Advance(0.08f);
        animator.Update(east * RunNatural, east);
        Assert.That(animator.Phase, Is.EqualTo(0.4f).Within(1e-4f), "The run's 0.8 s cycle at its natural rate.");
        Assert.That(animator.Frame, Is.EqualTo(3));

        animator.Update(Vector2.zero, east);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Phase, Is.Zero, "Stopping begins the idle at its first frame.");
        Assert.That(animator.Sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.East, 0)));
    }

    /// <summary>
    /// A gait plays at speed over its natural speed, clamped to the settings' range; idle plays at its own rate.
    /// </summary>
    [Test]
    public void PlaybackRate_IsSpeedOverNaturalSpeed_Clamped()
    {
        Assert.That(RateAt(5f), Is.EqualTo(5f / RunNatural).Within(1e-5f));
        Assert.That(RateAt(3f), Is.EqualTo(3f / RunNatural).Within(1e-5f));
        Assert.That(RateAt(1.2f), Is.EqualTo(1.2f).Within(1e-5f));
        Assert.That(RateAt(2f), Is.EqualTo(1.5f).Within(1e-5f), "A fast walk is clamped to the fastest rate.");
        Assert.That(RateAt(0.2f), Is.EqualTo(0.5f).Within(1e-5f), "A slow walk is clamped to the slowest rate.");
        Assert.That(RateAt(10f), Is.EqualTo(1.5f).Within(1e-5f));
        Assert.That(RateAt(0f), Is.EqualTo(1f));

        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, 2f);
        clock.Advance(0.1f);
        animator.Update(Vector2.right * 2f, Vector2.right);
        Assert.That(animator.Phase, Is.EqualTo(0.15f).Within(1e-4f), "The phase advances at the clamped rate.");
    }

    /// <summary>
    /// A gait the set has no clip for is shown with the nearest slower gait's clip, at that clip's natural speed.
    /// </summary>
    [Test]
    public void AMissingGait_ShowsTheNearestSlowerOne()
    {
        CharacterAnimationSet withoutSprint = Track(CharacterAnimationSet.Create(new[]
        {
            set.Locomotion(CharacterGait.Idle),
            set.Locomotion(CharacterGait.Walk),
            set.Locomotion(CharacterGait.Run)
        }));
        CharacterAnimator animator = new(withoutSprint, settings, clock);
        Step(animator, 7f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        Assert.That(animator.Clip, Is.SameAs(set.Locomotion(CharacterGait.Run)));
        Assert.That(animator.PlaybackRate, Is.EqualTo(1.5f).Within(1e-5f));
    }

    /// <summary>
    /// Once warm, updating with changing speeds, facings and gaits allocates no managed memory.
    /// </summary>
    [Test]
    public void Update_DoesNotAllocate_OnceWarm()
    {
        CharacterAnimator animator = new(set, settings, clock);
        float[] speeds = { 0f, 1f, 3f, 6.5f, 2f, 0f };
        TestDelegate run = () =>
        {
            for (int i = 0; i < 60; i++)
            {
                clock.Advance(1f / 60f);
                animator.Update(Facing(i * 17f) * speeds[i % speeds.Length], Facing(i * 17f));
            }
        };
        run();

        Assert.That(run, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// The visuals feed their animator the movement they were given and put its sprite on their renderer, on the
    /// injected clock.
    /// </summary>
    [Test]
    public void Visuals_PutTheAnimatorsSpriteOnTheirRenderer()
    {
        GameObject owner = Track(new GameObject("Visuals"));
        SpriteRenderer renderer = owner.AddComponent<SpriteRenderer>();
        CharacterVisuals visuals = owner.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);

        visuals.SetLocomotion(Vector2.up * RunNatural, Vector2.up);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.North, 0)));

        clock.Advance(0.25f);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Run).SpriteAt(CharacterDirections.North, 2)));

        visuals.SetLocomotion(Vector2.zero, Vector2.left);
        visuals.Tick();
        Assert.That(renderer.sprite, Is.SameAs(set.Locomotion(CharacterGait.Idle).SpriteAt(CharacterDirections.West, 0)));
    }

    /// <summary>
    /// The gait a new animator picks at <paramref name="speed"/>.
    /// </summary>
    private CharacterGait GaitAt(float speed)
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, speed);
        return animator.Gait;
    }

    /// <summary>
    /// The playback rate a new animator plays at <paramref name="speed"/>.
    /// </summary>
    private float RateAt(float speed)
    {
        CharacterAnimator animator = new(set, settings, clock);
        Step(animator, speed);
        return animator.PlaybackRate;
    }

    /// <summary>
    /// One update moving east at <paramref name="speed"/>, without advancing the clock.
    /// </summary>
    private static void Step(CharacterAnimator animator, float speed)
    {
        animator.Update(Vector2.right * speed, Vector2.right);
    }

    /// <summary>
    /// The unit facing <paramref name="degrees"/> counterclockwise from east.
    /// </summary>
    private static Vector2 Facing(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    /// <summary>
    /// A clip of <paramref name="frames"/> frames of <paramref name="seconds"/> each, with a sprite of its own for every
    /// direction and frame.
    /// </summary>
    private CharacterAnimationClip Clip(string name, CharacterGait gait, int frames, float seconds, float naturalSpeed)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite[] sprites = new Sprite[CharacterDirections.Count * frames];
        float[] frameSeconds = new float[frames];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.zero, 32f));
            sprites[i].name = $"{name}_{i / frames}_{i % frames}";
        }

        for (int frame = 0; frame < frames; frame++)
        {
            frameSeconds[frame] = seconds;
        }

        return CharacterAnimationClip.Create(name, gait, false, sprites, frameSeconds, naturalSpeed);
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
