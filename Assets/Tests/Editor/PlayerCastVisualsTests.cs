using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PlayerCastVisuals"/> (Docs/Animation.md, Phase 5) on a player built in the test, with
/// <see cref="PlayerOrders"/> running its casts on a manual clock: the owner's cast shows its release frame on exactly
/// the tick its orders complete it, at several cast times and instantly; a Continue cast while running shows its clip
/// over the run; after a look change the attack and the cast play the new set's clips, a cast whose set has no clip over
/// the run standing; a copy's cast releases when the owner's completion arrives; a cancelled cast, a copy's cancelled
/// cast and a disabled player stop the clip at once; ending an
/// attack leaves a cast playing; a buffered cast shows the first cast's release for its time before its own gather and
/// releases on the tick its orders complete it (Docs/KnownGaps.md, Phase 7); and casting, buffered casts too, plays out
/// the same in gameplay (casts, completions, failures, cooldowns and effects) with the visuals as without them.
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
            Clip("CastRun", CharacterGait.None, 8, 8, Release, 0f),
            Clip("Second", CharacterGait.None, 8, 0, Release, 0f)
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
    /// At cast times of 0 (instant), 0.15, 0.25, 0.3, 0.35, 0.45, 0.5 and 1 s, cast early and late in a session (where
    /// the clock's values round differently), driven at 60 Hz through the orders, the player shows its cast's release
    /// frame on exactly the tick its cast completes, and the gather before it, toward its aim.
    /// </summary>
    [Test]
    public void TheOwnersCast_ShowsItsRelease_OnTheTickItsOrdersCompleteIt()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        foreach (float start in new[] { 0f, 20f, 300f })
        {
            foreach (float castTime in new[] { 0f, 0.15f, 0.25f, 0.3f, 0.35f, 0.45f, 0.5f, 1f })
            {
                clock.Time = start;
                CreatePlayer(Ability("Bolt", castTime), true);
                bool completed = false;
                abilities.CastCompleted += _ => completed = true;
                Tick(Press(0));
                for (int step = 0; step < 120; step++)
                {
                    bool shows = spriteRenderer.sprite == cast.SpriteAt(CharacterDirections.East, Release);
                    Assert.That(shows, Is.EqualTo(completed), $"{castTime} s from {start} s at step {step}");
                    Assert.That(visuals.Animator.IsActing, Is.True, $"{castTime} s from {start} s at step {step}");
                    if (completed)
                    {
                        break;
                    }

                    clock.Advance(Step);
                    Tick(default);
                }

                Assert.That(completed, Is.True, $"{castTime} s from {start} s");
            }
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
    /// A copy plays another machine's cast from its message toward its aim, holding its gather past the cast time until
    /// the owner's completion arrives and releasing on that update (Docs/KnownGaps.md, Phase 4); a completion arriving
    /// before the cast time cuts the gather short; it stops at once when the owner's cancel arrives, never releasing.
    /// </summary>
    [Test]
    public void ACopy_ReleasesTheOwnersCastOnItsCompletion_AndStopsItOnTheCancel()
    {
        CharacterAnimationClip cast = set.Find("Cast");
        CreatePlayer(Ability("Bolt", 0.25f), true);
        abilities.PlayRemoteCast(0, new CastAim(Vector2.zero, Vector2.up, default));
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.True);
        Assert.That(visuals.Animator.Direction, Is.EqualTo(CharacterDirections.North));
        clock.Advance(0.3f);
        visuals.Tick();
        Assert.That(spriteRenderer.sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.North, Release - 1)), "Past the cast time, the copy's gather waits for the completion.");
        abilities.CompleteRemoteCast();
        visuals.Tick();
        Assert.That(spriteRenderer.sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.North, Release)), "The copy releases on the update the completion arrives.");

        abilities.PlayRemoteCast(0, new CastAim(Vector2.zero, Vector2.up, default));
        clock.Advance(0.2f);
        abilities.CompleteRemoteCast();
        visuals.Tick();
        Assert.That(spriteRenderer.sprite, Is.SameAs(cast.SpriteAt(CharacterDirections.North, Release)), "An early completion cuts the gather short.");

        abilities.PlayRemoteCast(0, new CastAim(Vector2.zero, Vector2.up, default));
        clock.Advance(0.3f);
        visuals.Tick();
        abilities.StopRemoteCast();
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.False, "A cancel stops the held gather without a release.");
    }

    /// <summary>
    /// After the visuals take another set (the player's look changed), the weapon's attack and the cast play that set's
    /// clips; the set has no clip over the run, so the Continue cast plays standing while the player moves, and nothing
    /// is logged for the missing clip.
    /// </summary>
    [Test]
    public void ALookChange_PlaysTheNewSetsClips_AndACastWithoutItsRunClipStanding()
    {
        CreatePlayer(Ability("Whirl", 0.3f, AbilityTargeting.None, CastMovement.Continue, "CastRun"), true);
        Assert.That(weapon.TryAttack(Vector2.right), Is.True);
        visuals.Tick();
        Assert.That(visuals.Animator.Clip, Is.SameAs(set.Find("Slash")));
        weapon.CancelAttack();

        CharacterAnimationSet other = Track(CharacterAnimationSet.Create(new[]
        {
            Clip("Idle", CharacterGait.Idle, 1, 0, -1, 0f),
            Clip("Run", CharacterGait.Run, 8, 0, -1, 4f),
            Clip("Slash", CharacterGait.None, 8, 0, 4, 0f),
            Clip("Cast", CharacterGait.None, 8, 0, Release, 0f)
        }));
        visuals.SetLook(other, locomotion);
        clock.Advance(1f);
        Assert.That(weapon.TryAttack(Vector2.right), Is.True);
        visuals.Tick();
        Assert.That(visuals.Animator.Clip, Is.SameAs(other.Find("Slash")), "The attack plays the new set's clip.");
        weapon.CancelAttack();

        Assert.That(abilities.TryCast(0, new CastAim(Vector2.zero, Vector2.zero, default)), Is.EqualTo(CastOutcome.Started));
        visuals.SetLocomotion(Vector2.right * 4f, Vector2.right);
        visuals.Tick();
        Assert.That(visuals.Animator.IsActing, Is.True);
        Assert.That(visuals.Animator.Clip, Is.SameAs(other.Find("Cast")), "The cast plays the new set's standing clip.");
        Assert.That(visuals.Animator.ShowsMovingCast, Is.False);
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
    /// A bufferable cast pressed during a 0.25 s cast, driven through the orders at 60 Hz, starts on the tick the first
    /// completes; the first cast's release shows from that tick for its 60 ms, the buffered cast's gather after it, and
    /// the buffered cast's release on exactly the tick its orders complete it, its cast time after it started.
    /// </summary>
    [Test]
    public void ABufferedCast_ShowsTheFirstCastsReleaseForItsTime_ThenReleasesOnTheTickItCompletes()
    {
        CharacterAnimationClip first = set.Find("Cast");
        CharacterAnimationClip second = set.Find("Second");
        CreatePlayer(Ability("Bolt", 0.25f), true, Buffered());
        int tick = 0;
        int firstDone = -1;
        int secondStarted = -1;
        int secondDone = -1;
        abilities.CastCompleted += cast =>
        {
            if (cast.Slot == 0)
            {
                firstDone = tick;
            }
            else
            {
                secondDone = tick;
            }
        };
        abilities.CastStarted += cast =>
        {
            if (cast.Slot == 1)
            {
                secondStarted = tick;
            }
        };
        List<Sprite> shown = new();
        List<CharacterAnimationClip> clips = new();
        Tick(Press(0));
        shown.Add(spriteRenderer.sprite);
        clips.Add(visuals.Animator.Clip);
        for (tick = 1; tick < 60; tick++)
        {
            clock.Advance(Step);
            Tick(tick == 6 ? Press(1) : default);
            shown.Add(spriteRenderer.sprite);
            clips.Add(visuals.Animator.Clip);
        }

        Assert.That(firstDone, Is.GreaterThan(0));
        Assert.That(secondStarted, Is.EqualTo(firstDone), "The buffered cast fires on the tick the first completes.");
        Assert.That(secondDone, Is.InRange(secondStarted + 18, secondStarted + 19), "It completes its 0.3 s later.");
        for (int i = firstDone; i < firstDone + 4; i++)
        {
            Assert.That(shown[i], Is.SameAs(first.SpriteAt(CharacterDirections.East, Release)), $"tick {i}: the first cast's release shows its 60 ms.");
        }

        Assert.That(clips[firstDone + 4], Is.SameAs(second), "Then the buffered cast's gather.");
        for (int i = firstDone + 4; i < secondDone; i++)
        {
            Assert.That(shown[i], Is.Not.SameAs(second.SpriteAt(CharacterDirections.East, Release)), $"tick {i}: no release before the cast completes.");
        }

        Assert.That(shown[secondDone], Is.SameAs(second.SpriteAt(CharacterDirections.East, Release)), "Its release shows on the tick it completes.");
    }

    /// <summary>
    /// The same presses with buffered casts (a cast, a bufferable one pressed during it, again after both cooldowns)
    /// start, complete and resolve on the same ticks with the same cooldowns with the cast visuals as without them.
    /// </summary>
    [Test]
    public void BufferedCasts_PlayOutTheSameInGameplay_WithTheVisuals()
    {
        List<string> without = PlayBufferedScript(false);
        List<string> with = PlayBufferedScript(true);
        Assert.That(with, Is.EqualTo(without));
        Assert.That(without, Has.Some.StartsWith("completed 1"));
    }

    /// <summary>
    /// Runs presses of a 0.25 s cast and a bufferable 0.3 s one pressed during it, twice, on a new player with or
    /// without its cast visuals, and returns what happened on which tick, the cooldowns at the end and the effects
    /// resolved.
    /// </summary>
    private List<string> PlayBufferedScript(bool withVisuals)
    {
        clock.Time = 0f;
        resolved = 0;
        CreatePlayer(Ability("Bolt", 0.25f, cooldown: 1f), withVisuals, Buffered());
        List<string> log = new();
        int tick = 0;
        abilities.CastStarted += cast => log.Add($"started {cast.Slot} {tick}");
        abilities.CastCompleted += cast => log.Add($"completed {cast.Slot} {tick}");
        abilities.CastFailed += (slot, reason) => log.Add($"failed {slot} {reason} {tick}");
        for (tick = 0; tick < 180; tick++)
        {
            int press = tick == 0 || tick == 90 ? 0 : tick == 8 || tick == 100 ? 1 : -1;
            Tick(press >= 0 ? Press(press) : default);
            clock.Advance(Step);
        }

        log.Add($"cooldowns {abilities.RemainingCooldown(0):0.000} {abilities.RemainingCooldown(1):0.000} resolved {resolved}");
        return log;
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
    /// Builds an authoritative player with a class of <paramref name="ability"/> (and <paramref name="second"/> in the
    /// second slot, when given), a sword whose attack plays the set's slash, its visuals on a child (and its cast visuals
    /// when <paramref name="withVisuals"/>), and orders over its abilities.
    /// </summary>
    private void CreatePlayer(AbilityDefinition ability, bool withVisuals, AbilityDefinition second = null)
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
        weapon.Construct(TestCombat.CreateAttackService(FixedGameAuthority.Authoritative), null, clock, null);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.5f, new Vector2[4], windup: 0.15f, contact: 0.05f, recovery: 0.15f, attackClipName: "Slash")));
        AbilityService service = TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative);
        service.CastResolved += (_, _) => resolved++;
        abilities = player.AddComponent<PlayerAbilities>();
        abilities.Construct(clock, service);
        abilities.Configure(Track(PlayerClass.Create("Test", ability, second, null, null)));
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
    /// A tracked bufferable 0.3 s cast aimed nowhere that plays the set's second cast clip.
    /// </summary>
    private AbilityDefinition Buffered()
    {
        return Track(AbilityDefinition.Create("Whirl", 1f, 0.3f, AbilityTargeting.None, bufferable: true, castClip: "Second"));
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
