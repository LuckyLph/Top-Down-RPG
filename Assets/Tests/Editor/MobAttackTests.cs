using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for a mob's attack on its timeline (Docs/Animation.md, Phase 7): a <see cref="MeleeDamageDealer"/> on a
/// <see cref="ManualClock"/> with the Weasel's footprint collider. Its hit lands at contact and not before, on every unit
/// then in the area ahead of its collider along the direction locked at the start, and never on a unit that left it,
/// stands beside or behind it, or is of its faction; the cooldown runs from the start; cancelling the windup (a stun,
/// death) lands nothing and gives the cooldown back, while cancelling the recovery keeps it; the Weasel's animator,
/// fed as the brain feeds it, shows the contact frame on the update the hit lands; attacking allocates nothing once
/// warm.
/// </summary>
public class MobAttackTests
{
    private const string SetPath = "Assets/Sprites/Characters/Weasel/Weasel_AnimationSet.asset";
    private const string LocomotionPath = "Assets/Data/Characters/Locomotion_Weasel.asset";

    private readonly List<Object> created = new();
    private ManualClock clock;
    private MobConfig config;
    private MeleeDamageDealer dealer;
    private CombatEvents events;
    private int started;
    private int contacts;
    private int cancelled;

    /// <summary>
    /// Builds a mob of the mobs' faction at the origin with the Weasel's collider, its dealer initialised from a config
    /// of 10 damage, a 0.45 s windup, a 0.05 s contact, a 0.25 s recovery, a 1 by 0.75 area and the given interval, on an
    /// authoritative attack service and a manual clock, counting its events.
    /// </summary>
    private void CreateMob(float interval = 0.75f)
    {
        clock = new ManualClock();
        events = new CombatEvents();
        config = Track(MobConfig.Create(attackDamage: 10, attackInterval: interval, attackWindup: 0.45f, attackContact: 0.05f, attackRecovery: 0.25f, attackArea: new Vector2(1f, 0.75f), attackClip: "DaggerSlash"));
        DamageReceiver mob = Unit("Mob", Faction.Mobs, Vector2.zero, new Vector2(0.534f, 0.3f), new Vector2(-0.017f, 0.15f));
        dealer = mob.gameObject.AddComponent<MeleeDamageDealer>();
        dealer.Construct(clock, TestCombat.CreateAttackService(FixedGameAuthority.Authoritative, events));
        dealer.Initialize(config);
        dealer.AttackStarted += _ => started++;
        dealer.AttackContacted += () => contacts++;
        dealer.AttackCancelled += () => cancelled++;
    }

    /// <summary>
    /// Destroys every object the test created.
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
        started = contacts = cancelled = 0;
    }

    /// <summary>
    /// The hit lands on the target in reach at contact, the windup's end, and not on any update before it; the start
    /// and the contact are each raised once.
    /// </summary>
    [Test]
    public void TheHit_LandsAtContact_AndNotBefore()
    {
        CreateMob();
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f));

        Assert.That(dealer.TryStartAttack(player.transform), Is.True);
        Assert.That(dealer.Phase, Is.EqualTo(AttackPhase.Windup));
        for (int i = 0; i < 4; i++)
        {
            clock.Advance(0.11f);
            dealer.Tick();
            Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth), $"Nothing lands {clock.Time:F2} s into the windup.");
        }

        clock.Advance(0.02f);
        dealer.Tick();
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth - 10));
        Assert.That(dealer.Phase, Is.EqualTo(AttackPhase.Contact));
        Assert.That(started, Is.EqualTo(1));
        Assert.That(contacts, Is.EqualTo(1));

        clock.Advance(0.3f);
        dealer.Tick();
        Assert.That(dealer.IsAttacking, Is.False, "The attack ends after its recovery.");
        Assert.That(contacts, Is.EqualTo(1));
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth - 10), "It lands once.");
    }

    /// <summary>
    /// A target that steps out of the area before contact takes nothing; so do units beside the mob, behind it and of
    /// its own faction in the area, while every player in the area is hit.
    /// </summary>
    [Test]
    public void OnlyThePlayersInTheAreaAtContact_AreHit()
    {
        CreateMob();
        DamageReceiver steppedBack = Player(new Vector2(0.75f, 0f));
        DamageReceiver alongside = Player(new Vector2(0.9f, -0.3f));
        DamageReceiver beside = Player(new Vector2(0f, 0.65f));
        DamageReceiver behind = Player(new Vector2(-0.75f, 0.1f));
        DamageReceiver ally = Unit("Ally", Faction.Mobs, new Vector2(0.7f, 0.4f), new Vector2(0.534f, 0.3f), new Vector2(-0.017f, 0.15f));

        dealer.TryStartAttack(steppedBack.transform);
        clock.Advance(0.3f);
        dealer.Tick();
        steppedBack.transform.position = new Vector3(1.6f, 0f, 0f);
        Physics2D.SyncTransforms();
        clock.Advance(0.15f);
        dealer.Tick();

        Assert.That(contacts, Is.EqualTo(1));
        Assert.That(steppedBack.Health.CurrentHealth, Is.EqualTo(steppedBack.Health.MaxHealth), "Stepping out of reach dodges it.");
        Assert.That(alongside.Health.CurrentHealth, Is.EqualTo(alongside.Health.MaxHealth - 10), "A player in the area is hit.");
        Assert.That(beside.Health.CurrentHealth, Is.EqualTo(beside.Health.MaxHealth), "Beside the mob is outside its area.");
        Assert.That(behind.Health.CurrentHealth, Is.EqualTo(behind.Health.MaxHealth));
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth), "Mobs do not hit their own.");
    }

    /// <summary>
    /// The direction locks at the start: a target that walks round the mob during the windup is not followed, and the
    /// area at contact lies along the first direction, starting at the edge of the mob's collider.
    /// </summary>
    [Test]
    public void TheDirection_LocksAtTheStart()
    {
        CreateMob();
        DamageReceiver player = Player(new Vector2(0.75f, 0f));
        dealer.TryStartAttack(player.transform);
        player.transform.position = new Vector3(-0.02f, 0.9f, 0f);
        Physics2D.SyncTransforms();

        clock.Advance(0.45f);
        dealer.Tick();

        Assert.That(dealer.AttackDirection.x, Is.GreaterThan(0.99f));
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth), "Circling round the mob dodges it.");
        Bounds body = dealer.GetComponent<Collider2D>().bounds;
        Assert.That(dealer.AreaCentre(Vector2.right).x, Is.EqualTo(body.max.x + 0.375f).Within(1e-4f));
        Assert.That(dealer.AreaCentre(Vector2.up).y, Is.EqualTo(body.max.y + 0.375f).Within(1e-4f));
    }

    /// <summary>
    /// The cooldown runs from the attack's start: with an interval longer than the timeline, no attack starts after
    /// the recovery until the interval has passed since the last start.
    /// </summary>
    [Test]
    public void TheCooldown_RunsFromTheStart()
    {
        CreateMob(1.5f);
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f));

        TestCombat.Strike(dealer, clock, player.transform);
        clock.Advance(0.31f);
        dealer.Tick();
        Assert.That(dealer.IsAttacking, Is.False);
        Assert.That(dealer.TryStartAttack(player.transform), Is.False, "0.76 s after the start.");
        clock.Advance(0.73f);
        Assert.That(dealer.TryStartAttack(player.transform), Is.False, "1.49 s after the start.");
        clock.Advance(0.02f);
        Assert.That(dealer.TryStartAttack(player.transform), Is.True, "1.51 s after the start.");
    }

    /// <summary>
    /// Cancelling in the windup (a stun) lands nothing, raises the cancel and gives the cooldown back; cancelling in
    /// the recovery keeps the cooldown, the hit having landed.
    /// </summary>
    [Test]
    public void CancellingTheWindup_LandsNothing_AndGivesTheCooldownBack()
    {
        CreateMob(1.5f);
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f));

        dealer.TryStartAttack(player.transform);
        clock.Advance(0.3f);
        dealer.Tick();
        dealer.CancelAttack();
        clock.Advance(0.3f);
        dealer.Tick();
        Assert.That(cancelled, Is.EqualTo(1));
        Assert.That(contacts, Is.Zero);
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth));
        Assert.That(dealer.TryStartAttack(player.transform), Is.True, "Its cooldown was given back.");

        clock.Advance(0.6f);
        dealer.Tick();
        dealer.CancelAttack();
        Assert.That(cancelled, Is.EqualTo(2));
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth - 10));
        Assert.That(dealer.TryStartAttack(player.transform), Is.False, "A hit that landed keeps its cooldown.");
    }

    /// <summary>
    /// A mob that dies in its windup lands nothing: its next tick cancels the attack and gives the cooldown back.
    /// </summary>
    [Test]
    public void DeathInTheWindup_LandsNothing_AndCancelsTheAttack()
    {
        CreateMob(1.5f);
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f));

        dealer.TryStartAttack(player.transform);
        clock.Advance(0.2f);
        Health mob = dealer.GetComponent<Health>();
        mob.ApplyDamage(mob.MaxHealth);
        dealer.Tick();
        clock.Advance(0.3f);
        dealer.Tick();

        Assert.That(dealer.IsAttacking, Is.False);
        Assert.That(cancelled, Is.EqualTo(1));
        Assert.That(contacts, Is.Zero);
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth));
        Assert.That(dealer.ReadyTime, Is.LessThanOrEqualTo(clock.Time), "Its cooldown was given back.");
    }

    /// <summary>
    /// The Weasel's animator, playing the attack as the brain does from the start event on the same clock, shows the
    /// contact frame on exactly the update the hit lands, and the windup's frames on every update before it.
    /// </summary>
    [Test]
    public void TheAnimator_ShowsTheContactFrame_OnTheUpdateTheHitLands()
    {
        CreateMob();
        MobMotor2D motor = WithVisuals(out CharacterVisuals visuals);
        dealer.AttackStarted += direction => motor.PlayAttack(direction, dealer.Timeline, false);
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f));
        int landedFrame = -1;
        int updates = 0;
        int landedUpdate = -1;
        events.DamageApplied += _ => landedUpdate = updates;

        dealer.TryStartAttack(player.transform);
        for (; updates < 60 && landedUpdate < 0; updates++)
        {
            clock.Advance(1f / 60f);
            dealer.Tick();
            visuals.Tick();
            if (landedUpdate < 0)
            {
                Assert.That(visuals.Animator.Frame, Is.LessThan(visuals.Animator.Clip.ContactFrame), $"update {updates}");
            }
            else
            {
                landedFrame = visuals.Animator.Frame;
            }
        }

        Assert.That(landedUpdate, Is.GreaterThanOrEqualTo(0), "The hit landed.");
        Assert.That(landedFrame, Is.EqualTo(visuals.Animator.Clip.ContactFrame));
        Assert.That(visuals.Animator.Clip.Name, Is.EqualTo("DaggerSlash"));
    }

    /// <summary>
    /// Once warm, starting attacks, ticking them through windup, contact (landing on a player in reach) and recovery,
    /// and cancelling one allocate nothing.
    /// </summary>
    [Test]
    public void Attacking_AllocatesNothingOnceWarm()
    {
        CreateMob();
        DamageReceiver player = Player(new Vector2(0.75f, 0.1f), 100000);
        System.Action attack = () =>
        {
            dealer.TryStartAttack(player.transform);
            for (int i = 0; i < 50; i++)
            {
                clock.Advance(1f / 60f);
                dealer.Tick();
            }

            dealer.TryStartAttack(player.transform);
            dealer.CancelAttack();
        };

        attack();
        attack();
        Assert.That(() => attack(), Is.Not.AllocatingGCMemory());
        Assert.That(contacts, Is.EqualTo(3));
    }

    /// <summary>
    /// Gives the mob a Visuals child drawing the Weasel's baked set on the test's clock and a motor initialised from the
    /// config, as the prefab has them.
    /// </summary>
    private MobMotor2D WithVisuals(out CharacterVisuals visuals)
    {
        GameObject mob = dealer.gameObject;
        GameObject child = new("Visuals");
        child.transform.SetParent(mob.transform, false);
        child.AddComponent<SpriteRenderer>();
        visuals = child.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterAnimationSet>(SetPath);
        serialized.FindProperty("locomotion").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterLocomotionSettings>(LocomotionPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);
        MobMotor2D motor = mob.AddComponent<MobMotor2D>();
        motor.Initialize(config);
        return motor;
    }

    /// <summary>
    /// A player unit at <paramref name="position"/> with the player's feet collider and <paramref name="maxHealth"/>.
    /// </summary>
    private DamageReceiver Player(Vector2 position, int maxHealth = 100)
    {
        DamageReceiver player = Unit("Player", Faction.Players, position, new Vector2(0.35f, 0.15f), new Vector2(0f, 0.07f));
        SerializedObject health = new(player.Health);
        health.FindProperty("maxHealth").intValue = maxHealth;
        health.ApplyModifiedPropertiesWithoutUndo();
        return player;
    }

    /// <summary>
    /// A unit of <paramref name="faction"/> at <paramref name="position"/>: health, a receiver of that faction and a
    /// box collider of <paramref name="size"/> at <paramref name="offset"/> on a kinematic body.
    /// </summary>
    private DamageReceiver Unit(string name, Faction faction, Vector2 position, Vector2 size, Vector2 offset)
    {
        GameObject unit = Track(new GameObject(name));
        unit.transform.position = position;
        unit.AddComponent<Health>();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        receiver.SetProfile(Track(CombatProfile.Create(faction)));
        BoxCollider2D body = unit.AddComponent<BoxCollider2D>();
        body.size = size;
        body.offset = offset;
        unit.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        Physics2D.SyncTransforms();
        return receiver;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
