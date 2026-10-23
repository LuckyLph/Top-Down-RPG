using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PlayerCastVisuals"/> (Docs/Animation.md, Phase 5) on a player built in the test, with
/// <see cref="PlayerOrders"/> running its casts on a manual clock: the owner's cast shows its release frame on exactly
/// the tick its orders complete it, at several cast times and instantly; a Continue cast while running shows its clip
/// over the run; a cancelled cast, a copy's cancelled cast and a disabled player stop the clip at once; ending an attack
/// leaves a cast playing; and casting plays out the same in gameplay (casts, completions, failures, cooldowns and
/// effects) with the visuals as without them.
/// </summary>
public class PlayerCastVisualsTests
{
    private const int Release = 4;
    private const float Step = 1f / 60f;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private CharacterAnimationSet set;
    private CharacterLocomotionSettings locomotion;
    private PlayerControlSettings controlSettings;
    private PlayerAbilities abilities;
    private PlayerWeaponController weapon;
    private CharacterVisuals visuals;
    private SpriteRenderer spriteRenderer;
    private PlayerOrders orders;
    private int resolved;

    /// <summary>
    /// Builds a manual clock, locomotion settings, control settings and a set of an idle, a run, an attack clip and a
    /// cast clip (standing and over eight run frames).
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        controlSettings = Track(TestPlayerControlSettings.Create());
        set = Track(CharacterAnimationSet.Create(new[]
        {
            Clip("Idle", CharacterGait.Idle, 1, 0, -1, 0f),
            Clip("Run", CharacterGait.Run, 8, 0, -1, 4f),
            Clip("Slash", CharacterGait.None, 8, 0, 4, 0f),
            Clip("Cast", CharacterGait.None, 8, 0, Release, 0f),
            Clip("CastRun", CharacterGait.None, 8, 8, Release, 0f)
        }));
    }

    /// <summary>
    /// Destroys everything the test made.
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
    /// At cast times of 0 (instant), 0.15, 0.25, 0.5 and 1 s, driven at 60 Hz through the orders, the player shows its
    /// cast's release frame on exactly the tick its cast completes, and the gather before it, toward its aim.
    /// </summary>
    [Test]
    public void TheOwnersCast_ShowsItsRelease_OnTheTickItsOrdersCompleteIt()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        foreach (float castTime in new[] { 0f, 0.15f, 0.25f, 0.5f, 1f })
        {
            CreatePlayer(Ability("Bolt", castTime), true);
            bool completed = false;
            abilities.CastCompleted += _ => completed = true;
            Tick(Press(0));
            for (int step = 0; step < 120; step++)
            {
                bool shows = spriteRenderer.sprite == cast.SpriteAt(CharacterDirections.East, Release);
                Assert.That(shows, Is.EqualTo(completed), $"{castTime} s at step {step}");
                Assert.That(visuals.Animator.IsActing, Is.True, $"{castTime} s at step {step}");
                if (completed)
                {
                    break;
                }

                clock.Advance(Step);
                Tick(default);
            }

            Assert.That(completed, Is.True, $"{castTime} s");
        }
    }

    /// <summary>
    /// A Continue cast aimed nowhere, made while the player runs, shows its clip drawn over the run.
    /// </summary>
    [Test]
    public void AContinueCast_WhileRunning_ShowsItsClipOverTheRun()
    {
        CreatePlayer(Ability("Whirl", 0.3f, AbilityTargeting.None, CastMovement.Continue, "CastRun"), true);
        visuals.SetLocomotion(Vector2.right * 4f, Vector2.right);
        visuals.Tick();
        clock.Advance(Step);
        Tick(Press(0));
        clock.Advance(Step);
        Tick(default);
        Assert.That(visuals.Animator.ShowsMovingCast, Is.True);
        Assert.That(visuals.Animator.Clip, Is.SameAs(set.Find("CastRun")));
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Run));
    }

    /// <summary>
    /// A cast the orders cancel (death, a stun) stops its clip on the next frame, and the player stands idle.
    /// </summary>
    [Test]
    public void ACancelledCast_StopsItsClipAtOnce()
    {
        CreatePlayer(Ability("Slow", 1f), true);
        Tick(Press(0));
        clock.Advance(0.2f);
        Tick(default);
        Assert.That(visuals.Animator.IsActing, Is.True);

        orders.Clear();
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.False);
        Assert.That(spriteRenderer.sprite, Is.SameAs(set.Find("Idle").SpriteAt(CharacterDirections.East, 0)));
    }

    /// <summary>
    /// A copy plays another machine's cast from its message toward its aim, releasing on its own clock after the cast
    /// time, and stops it at once when the owner's cancel arrives.
    /// </summary>
    [Test]
    public void ACopy_PlaysTheOwnersCast_AndStopsItOnTheCancel()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CreatePlayer(Ability("Bolt", 0.25f), true);
        abilities.PlayRemoteCast(0, new CastAim(Vector2.zero, Vector2.up, default));
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.True);
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.North));
        clock.Advance(0.25f);
        visuals.Tick();
        Assert.That(spriteRenderer.sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.North, Release)), "The copy releases on its own clock.");

        abilities.PlayRemoteCast(0, new CastAim(Vector2.zero, Vector2.up, default));
        clock.Advance(0.1f);
        visuals.Tick();
        abilities.StopRemoteCast();
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.False);
    }

    /// <summary>
    /// A cast started while an attack shows replaces it, and cancelling the attack afterwards leaves the cast playing.
    /// </summary>
    [Test]
    public void CancellingAnAttack_LeavesACastPlaying()
    {
        CreatePlayer(Ability("Bolt", 0.5f), true);
        Assert.That(weapon.TryAttack(Vector2.right), Is.True);
        visuals.Tick();
        Assert.That(visuals.Animator.ActionKind, Is.EqualTo(CharacterActionKind.Attack));
        Assert.That(abilities.TryCast(0, new CastAim(Vector2.right, Vector2.right, default)), Is.EqualTo(CastOutcome.Started));
        weapon.CancelAttack();
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.True);
        Assert.That(visuals.Animator.ActionKind, Is.EqualTo(CharacterActionKind.Cast));
        Assert.That(visuals.Animator.Clip, Is.SameAs(set.Find("Cast")));
    }

    /// <summary>
    /// The same presses over 3 s of 60 Hz ticks (a 0.5 s cast, a press on cooldown, a cast cancelled by clearing the
    /// orders, a cast that completes) start, complete, fail, cancel and resolve on the same ticks and leave the same
    /// cooldowns with the cast visuals as without them.
    /// </summary>
    [Test]
    public void Casting_PlaysOutTheSameInGameplay_WithTheVisuals()
    {
        List<string> without = PlayScript(false);
        List<string> with = PlayScript(true);
        Assert.That(with, Is.EqualTo(without));
        Assert.That(without, Has.Some.StartsWith("completed"));
        Assert.That(without, Has.Some.StartsWith("cancelled"));
        Assert.That(without, Has.Some.StartsWith("failed"));
    }

    /// <summary>
    /// Runs the scripted presses on a new player, with or without its cast visuals, and returns what happened on which
    /// tick, the cooldowns at the end and the effects resolved.
    /// </summary>
    private List<string> PlayScript(bool withVisuals)
    {
        clock.Time = 0f;
        resolved = 0;
        CreatePlayer(Ability("Bolt", 0.5f, cooldown: 1f), withVisuals);
        List<string> log = new();
        int tick = 0;
        abilities.CastStarted += cast => log.Add($"started {cast.Slot} {tick}");
        abilities.CastCompleted += cast => log.Add($"completed {cast.Slot} {tick}");
        abilities.CastEnded += (slot, done) => log.Add($"{(done ? "ended" : "cancelled")} {slot} {tick}");
        abilities.CastFailed += (slot, reason) => log.Add($"failed {slot} {reason} {tick}");
        for (tick = 0; tick < 180; tick++)
        {
            bool press = tick == 0 || tick == 40 || tick == 70 || tick == 140;
            if (tick == 100)
            {
                orders.Clear();
            }

            Tick(press ? Press(0) : default);
            clock.Advance(Step);
        }

        log.Add($"cooldown {abilities.RemainingCooldown(0):0.000} resolved {resolved}");
        return log;
    }

    /// <summary>
    /// One tick: the orders with an empty command or a press, then the visuals.
    /// </summary>
    private void Tick(PlayerCommand command)
    {
        orders.Tick(command, new PlayerOrderContext(Vector2.zero, clock.Time, true, 0f, 0.3f));
        visuals.Tick();
    }

    /// <summary>
    /// A press of ability <paramref name="slot"/> with the pointer east of the player.
    /// </summary>
    private static PlayerCommand Press(int slot)
    {
        return new PlayerCommand(Vector2.right, default, false, false, false, abilitySlot: slot);
    }

    /// <summary>
    /// Builds an authoritative player with a class of <paramref name="ability"/> alone, a sword whose attack plays the
    /// set's slash, its visuals on a child (and its cast visuals when <paramref name="withVisuals"/>), and orders
    /// over its abilities.
    /// </summary>
    private void CreatePlayer(AbilityDefinition ability, bool withVisuals)
    {
        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<Rigidbody2D>();
        player.AddComponent<Health>();
        GameObject child = new("Visuals");
        child.transform.SetParent(player.transform, false);
        spriteRenderer = child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = locomotion;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);
        weapon = player.AddComponent<PlayerWeaponController>();
        weapon.Construct(TestCombat.CreateAttackService(FixedGameAuthority.Authoritative), null, clock);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.5f, new Vector2[4], windup: 0.15f, contact: 0.05f, recovery: 0.15f, attackClipName: "Slash")));
        AbilityService service = TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative);
        service.CastResolved += (_, _) => resolved++;
        abilities = player.AddComponent<PlayerAbilities>();
        abilities.Construct(clock, service);
        abilities.Configure(Track(PlayerClass.Create("Test", ability, null, null, null)));
        if (withVisuals)
        {
            player.AddComponent<PlayerCastVisuals>().Configure();
        }

        orders = new PlayerOrders(controlSettings, new PlayerUnitQueries(null), abilities);
        visuals.SetLocomotion(Vector2.zero, Vector2.right);
        visuals.Tick();
    }

    /// <summary>
    /// A tracked ability that plays the set's cast clip.
    /// </summary>
    private AbilityDefinition Ability(string name, float castTime, AbilityTargeting targeting = AbilityTargeting.Direction, CastMovement movement = CastMovement.Stop, string movingClip = null, float cooldown = 0.1f)
    {
        return Track(AbilityDefinition.Create(name, cooldown, castTime, targeting, castMovement: movement, castClip: "Cast", movingCastClip: movingClip));
    }

    /// <summary>
    /// A clip of <paramref name="frames"/> frames of 0.1 s with a sprite of its own for every direction, frame and gait
    /// frame, its contact frame at <paramref name="contact"/> (-1 for none).
    /// </summary>
    private CharacterAnimationClip Clip(string name, CharacterGait gait, int frames, int gaitFrames, int contact, float naturalSpeed)
    {
        float[] seconds = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            seconds[i] = 0.1f;
        }

        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite[] sprites = new Sprite[CharacterDirections.Count * frames * Mathf.Max(1, gaitFrames)];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.zero, 32f));
            sprites[i].name = $"{name}_{i}";
        }

        return CharacterAnimationClip.Create(name, gait, false, sprites, seconds, naturalSpeed, contact, gaitFrames);
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
