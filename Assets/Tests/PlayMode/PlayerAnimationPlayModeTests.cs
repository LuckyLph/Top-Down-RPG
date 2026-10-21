using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the player's character animation in Testlands, driven by scripted commands: its sprite comes
/// from the rogue's baked set and follows the speed it moves at (idle standing, the run at its base speed and chilled,
/// the walk under a heavy slow, the sprint under a haste) and the direction it faces.
/// </summary>
public class PlayerAnimationPlayModeTests
{
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
    /// <see cref="IPlayerCommandSource"/> that the test feeds one move click or stop at a time.
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
