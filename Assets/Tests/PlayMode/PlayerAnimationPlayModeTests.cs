using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the player's character animation in Testlands, driven by scripted commands: its sprite comes
/// from the rogue's baked set and follows the speed it moves at (idle standing, the run at its base speed and chilled,
/// the walk under a heavy slow, the sprint under a haste) and the direction it faces; a cast plays its clip and shows
/// its release frame on the frame its effect resolves; a Continue cast while running keeps the legs running; and a stun
/// mid-cast stops the cast's clip.
/// </summary>
public class PlayerAnimationPlayModeTests
{
    private const int BoltSlot = 0;
    private const int MendSlot = 2;
    private const int WhirlSlot = 4;

    private readonly List<Object> created = new();
    private PlayerController player;
    private CharacterVisuals visuals;
    private SpriteRenderer spriteRenderer;
    private ScriptedCommands commands;

    /// <summary>
    /// Destroys the statuses the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.Destroy(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Standing, the player shows the idle facing its way. Sent far east it runs east at its base speed, the run
    /// playing at speed over its natural speed and its frames moving on; chilled it still runs, slower; a heavy slow
    /// makes it walk and a haste sprint. Told to stop it stands idle facing east, and turned north or north-east it
    /// shows the idle that way. Every frame its renderer shows the animator's sprite.
    /// </summary>
    [UnityTest]
    public IEnumerator ThePlayersSprite_FollowsItsSpeedAndFacing()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        CharacterAnimationSet set = visuals.AnimationSet;
        Assert.That(animator, Is.Not.Null);
        yield return null;
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.Nearest(player.FacingDirection)));
        AssertShows(set, CharacterGait.Idle);

        commands.Click((Vector2)player.transform.position + Vector2.right * 50f);
        yield return new WaitForSeconds(0.3f);
        float baseSpeed = player.ControlSettings.MoveSpeed;
        CharacterAnimationClip run = set.Locomotion(CharacterGait.Run);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));
        Assert.That(animator.PlaybackRate, Is.EqualTo(baseSpeed / run.NaturalSpeed).Within(0.01f));
        AssertShows(set, CharacterGait.Run);

        HashSet<int> frames = new();
        for (float until = Time.time + 0.6f; Time.time < until;)
        {
            frames.Add(animator.Frame);
            AssertShows(set, CharacterGait.Run);
            yield return null;
        }

        Assert.That(frames.Count, Is.GreaterThanOrEqualTo(3), "The run's frames move on.");

        StatusEffectService statuses = SceneBootTestHelper.ResolveFromGameplay<StatusEffectService>();
        DamageReceiver receiver = player.GetComponent<DamageReceiver>();
        StatusEffectDefinition chill = SceneBootTestHelper.FindStatus("Chill");
        yield return WithStatus(statuses, receiver, chill, null);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), "A chilled player still runs.");
        Assert.That(animator.PlaybackRate, Is.EqualTo(baseSpeed * chill.MoveSpeedMultiplier / run.NaturalSpeed).Within(0.01f));
        AssertShows(set, CharacterGait.Run);
        statuses.Remove(receiver, chill);

        StatusEffectDefinition heavySlow = Track(StatusEffectDefinition.Create("HeavySlow", StatusKind.Debuff, 5f, moveSpeedMultiplier: 0.3f));
        yield return WithStatus(statuses, receiver, heavySlow, null);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        AssertShows(set, CharacterGait.Walk);
        statuses.Remove(receiver, heavySlow);

        StatusEffectDefinition haste = Track(StatusEffectDefinition.Create("Haste", StatusKind.Buff, 5f, moveSpeedMultiplier: 1.3f));
        yield return WithStatus(statuses, receiver, haste, player.gameObject);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Sprint));
        AssertShows(set, CharacterGait.Sprint);
        statuses.Remove(receiver, haste);

        commands.Stop();
        yield return null;
        yield return null;
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));
        AssertShows(set, CharacterGait.Idle);

        player.Face(Vector2.up);
        yield return null;
        yield return null;
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.North));
        AssertShows(set, CharacterGait.Idle);

        player.Face(new Vector2(1f, 1f));
        yield return null;
        yield return null;
        Assert.That(animator.Direction, Is.EqualTo(1), "North-east.");
        AssertShows(set, CharacterGait.Idle);
    }

    /// <summary>
    /// Casting Bolt toward the east, the player plays its cast clip facing east, and shows the clip's release frame on
    /// the frame the host's ability service resolves the cast's effect.
    /// </summary>
    [UnityTest]
    public IEnumerator ACast_ShowsItsReleaseFrame_WhenItsEffectResolves()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        AbilityDefinition bolt = abilities.GetAbility(BoltSlot);
        CharacterAnimationClip clip = visuals.AnimationSet.Find(bolt.CastClip);
        Assert.That(clip, Is.Not.Null);
        AbilityService service = SceneBootTestHelper.ResolveFromGameplay<AbilityService>();
        int resolvedFrame = -1;
        System.Action<PlayerAbilities, AbilityCast> onResolved = (caster, cast) =>
        {
            if (caster == abilities && cast.Slot == BoltSlot)
            {
                resolvedFrame = Time.frameCount;
            }
        };
        service.CastResolved += onResolved;
        try
        {
            commands.Cast(BoltSlot, (Vector2)player.transform.position + Vector2.right * 3f, default);
            bool sawGather = false;
            for (float until = Time.time + 2f; Time.time < until && (resolvedFrame < 0 || Time.frameCount == resolvedFrame);)
            {
                yield return null;
                sawGather |= resolvedFrame < 0 && animator.IsActing && animator.Clip == clip && animator.ActionPhase == AttackPhase.Windup;
            }

            Assert.That(resolvedFrame, Is.GreaterThanOrEqualTo(0), "The cast resolves.");
            Assert.That(sawGather, Is.True, "The cast's gather plays first.");
            Assert.That(animator.Clip, Is.SameAs(clip));
            Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));
            Assert.That(animator.Frame, Is.EqualTo(clip.ContactFrame), "The frame the effect resolved on shows the release.");
            Assert.That(spriteRenderer.sprite, Is.EqualTo(clip.SpriteAt(CharacterDirections.East, clip.ContactFrame)));
        }
        finally
        {
            service.CastResolved -= onResolved;
        }
    }

    /// <summary>
    /// Running east, the player casts Whirl (Continue): through the cast it keeps moving, showing the cast drawn over the
    /// run with the legs' phase moving on, and runs on after it.
    /// </summary>
    [UnityTest]
    public IEnumerator AContinueCast_WhileRunning_KeepsTheLegsRunning()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        AbilityDefinition whirl = abilities.GetAbility(WhirlSlot);
        Assert.That(whirl.CastMovement, Is.EqualTo(CastMovement.Continue));
        CharacterAnimationClip moving = visuals.AnimationSet.Find(whirl.MovingCastClip);
        commands.Click((Vector2)player.transform.position + Vector2.right * 50f);
        yield return new WaitForSeconds(0.3f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));

        float startX = player.transform.position.x;
        commands.Cast(WhirlSlot, (Vector2)player.transform.position + Vector2.right * 3f, default);
        HashSet<int> legFrames = new();
        int movingFrames = 0;
        for (float until = Time.time + whirl.CastTime + 0.2f; Time.time < until;)
        {
            yield return null;
            if (animator.IsActing && animator.ShowsMovingCast)
            {
                movingFrames++;
                Assert.That(animator.Clip, Is.SameAs(moving));
                Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
                legFrames.Add((int)(animator.Phase * moving.GaitFrames));
                Assert.That(spriteRenderer.sprite, Is.EqualTo(moving.SpriteAt(animator.Direction, animator.Frame, (int)(animator.Phase * moving.GaitFrames) % moving.GaitFrames)));
            }
        }

        Assert.That(movingFrames, Is.GreaterThan(3), "The cast shows over the run.");
        Assert.That(legFrames.Count, Is.GreaterThanOrEqualTo(3), "The legs keep running through the cast.");
        Assert.That(player.transform.position.x - startX, Is.GreaterThan(1f), "The player kept moving while casting.");
        yield return new WaitForSeconds(0.4f);
        Assert.That(animator.IsActing, Is.False);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run), "It runs on after the cast.");
        commands.Stop();
    }

    /// <summary>
    /// A stun in the middle of Mend (cast on the player itself) stops the cast's clip at once, and the cast never
    /// resolves.
    /// </summary>
    [UnityTest]
    public IEnumerator AStunMidCast_StopsTheClip()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        AbilityDefinition mend = abilities.GetAbility(MendSlot);
        CharacterAnimationClip clip = visuals.AnimationSet.Find(mend.CastClip);
        int resolved = 0;
        abilities.CastCompleted += _ => resolved++;
        UnitTarget self = new(player.GetComponent<Health>(), player.GetComponent<Collider2D>(), UnitTeam.Ally);
        commands.Cast(MendSlot, player.transform.position, self);
        yield return new WaitForSeconds(0.15f);
        Assert.That(animator.IsActing, Is.True);
        Assert.That(animator.Clip, Is.SameAs(clip));

        StatusEffectService statuses = SceneBootTestHelper.ResolveFromGameplay<StatusEffectService>();
        DamageReceiver receiver = player.GetComponent<DamageReceiver>();
        StatusEffectDefinition stun = SceneBootTestHelper.FindStatus("Stun");
        Assert.That(statuses.Apply(receiver, stun, null), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return null;
        yield return null;
        Assert.That(animator.IsActing, Is.False, "The stun stops the cast's clip.");
        AssertShows(visuals.AnimationSet, CharacterGait.Idle);
        yield return new WaitForSeconds(mend.CastTime);
        Assert.That(resolved, Is.Zero, "The stunned cast never completes.");
        statuses.Remove(receiver, stun);
    }

    /// <summary>
    /// Boots into Testlands with every mob deactivated, caches the local player, its visuals and their renderer, and
    /// switches the player to scripted commands.
    /// </summary>
    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        visuals = player.GetComponentInChildren<CharacterVisuals>();
        Assert.That(visuals, Is.Not.Null);
        spriteRenderer = visuals.GetComponent<SpriteRenderer>();
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
    }

    /// <summary>
    /// Applies <paramref name="status"/> to the player and waits for its movement to settle at the new speed.
    /// </summary>
    private static IEnumerator WithStatus(StatusEffectService statuses, DamageReceiver receiver, StatusEffectDefinition status, GameObject source)
    {
        Assert.That(statuses.Apply(receiver, status, source), Is.EqualTo(StatusApplyOutcome.Landed), status.name);
        yield return new WaitForSeconds(0.2f);
    }

    /// <summary>
    /// Asserts the renderer shows the animator's current frame of <paramref name="gait"/>'s clip in its direction.
    /// </summary>
    private void AssertShows(CharacterAnimationSet set, CharacterGait gait)
    {
        CharacterAnimator animator = visuals.Animator;
        Sprite expected = set.Locomotion(gait).SpriteAt(animator.Direction, animator.Frame);
        Assert.That(spriteRenderer.sprite, Is.EqualTo(expected), $"{gait} {CharacterDirections.Name(animator.Direction)} {animator.Frame}");
    }

    /// <summary>
    /// Remembers <paramref name="instance"/> for destruction after the test.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }

    /// <summary>
    /// <see cref="IPlayerCommandSource"/> that the test feeds one move click, stop or ability press at a time.
    /// </summary>
    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        /// <summary>
        /// Queues a single move click on the ground at <paramref name="point"/> for the next read.
        /// </summary>
        public void Click(Vector2 point)
        {
            next = new PlayerCommand(point, default, movePressed: true, moveHeld: false, stopPressed: false);
        }

        /// <summary>
        /// Queues a single press of ability <paramref name="slot"/> with the pointer at <paramref name="point"/> over
        /// <paramref name="target"/> (none for the ground) for the next read.
        /// </summary>
        public void Cast(int slot, Vector2 point, UnitTarget target)
        {
            next = new PlayerCommand(point, target, movePressed: false, moveHeld: false, stopPressed: false, abilitySlot: slot);
        }

        /// <summary>
        /// Queues a single stop for the next read.
        /// </summary>
        public void Stop()
        {
            next = new PlayerCommand(Vector2.zero, default, movePressed: false, moveHeld: false, stopPressed: true);
        }

        /// <summary>
        /// Returns the queued command and clears it, so each is read once; an empty command otherwise.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
