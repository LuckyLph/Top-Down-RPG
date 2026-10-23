using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for the greatsword and the longbow (Docs/Animation.md, Phase 4) in Testlands, the booted test world,
/// with the player switched through its catalog and driven by scripted commands: the greatsword's cleave hitting every
/// mob in its area once at contact, the longbow's attack order stopping at its range with its arrow hitting the mob,
/// and a move order cancelling the bow's windup with no arrow fired.
/// </summary>
public class WeaponsPlayModeTests
{
    private const int GreatswordIndex = 1;
    private const int LongbowIndex = 2;
    private const float TimeoutSeconds = 8f;

    private IObjectResolver gameplay;
    private PlayerController player;
    private PlayerWeaponController weapon;
    private ScriptedCommands commands;

    /// <summary>
    /// With the greatsword equipped, an attack order on a mob beside the player cleaves at contact: the start chunk's
    /// two mobs, both in its area, each take one hit of its damage on the frame of contact, a unit past its reach takes
    /// nothing, and the character plays the greatsword's attack.
    /// </summary>
    [UnityTest]
    public IEnumerator TheGreatsword_HitsEveryMobInItsAreaOnce_AtContact()
    {
        yield return Boot();
        Assert.That(weapon.EquipFromCatalog(GreatswordIndex), Is.True);
        PlayerWeapon greatsword = weapon.CurrentWeapon;
        List<MobController> mobs = KeepMobs(2);
        Vector2 start = player.transform.position;
        MoveMob(mobs[0], start + new Vector2(0.75f, 0.2f));
        MoveMob(mobs[1], start + new Vector2(0.8f, -0.6f));
        Health beyond = CreateDummy(start + new Vector2(2.6f, 0.3f));
        List<Health> units = new() { mobs[0].GetComponent<Health>(), mobs[1].GetComponent<Health>(), beyond };
        int[] hits = new int[3];
        float[] hitTimes = { -1f, -1f, -1f };
        List<System.Action<Health.DamageEvent>> listeners = new();
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            System.Action<Health.DamageEvent> listener = _ =>
            {
                hits[index]++;
                hitTimes[index] = Time.time;
            };
            listeners.Add(listener);
            units[i].Damaged += listener;
        }

        float contactTime = -1f;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, _, _, _) => contactTime = contactTime < 0f ? Time.time : contactTime;
        weapon.AttackContacted += onContact;
        CharacterAnimator animation = player.GetComponentInChildren<CharacterVisuals>().Animator;
        try
        {
            commands.Click(mobs[0].transform.position, PickMob(mobs[0]));
            yield return SceneBootTestHelper.WaitUntil(() => weapon.IsAttacking, "the cleave to start");
            yield return null;
            Assert.That(animation.Clip.Name, Is.EqualTo(greatsword.AttackClip), "The character plays the greatsword's attack.");
            yield return SceneBootTestHelper.WaitUntil(() => contactTime >= 0f, "the cleave's contact");
            commands.Stop();
            yield return new WaitForSeconds(0.3f);

            for (int i = 0; i < 2; i++)
            {
                Assert.That(hits[i], Is.EqualTo(1), $"Mob {i} in the area is hit once.");
                Assert.That(hitTimes[i], Is.EqualTo(contactTime), $"Mob {i} is hit at contact.");
                Health health = mobs[i].GetComponent<Health>();
                Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - greatsword.Damage));
            }

            Assert.That(hits[2], Is.Zero, "The unit past the cleave's reach is not hit.");
        }
        finally
        {
            weapon.AttackContacted -= onContact;
            for (int i = 0; i < 3; i++)
            {
                units[i].Damaged -= listeners[i];
            }

            Object.Destroy(beyond.gameObject);
        }
    }

    /// <summary>
    /// With the longbow equipped, an attack order on a mob nine units away chases it only until the colliders are
    /// within the bow's range, then stands and shoots: one arrow leaves at contact and hits the mob once with the bow's
    /// damage.
    /// </summary>
    [UnityTest]
    public IEnumerator TheLongbow_StopsAtItsRange_AndItsArrowHitsTheMob()
    {
        yield return Boot();
        Assert.That(weapon.EquipFromCatalog(LongbowIndex), Is.True);
        PlayerWeapon longbow = weapon.CurrentWeapon;
        MobController mob = KeepMobs(1)[0];
        Vector2 start = player.transform.position;
        MoveMob(mob, start + new Vector2(9f, 0f));
        Health mobHealth = mob.GetComponent<Health>();
        Collider2D playerBody = player.GetComponent<Collider2D>();
        Collider2D mobBody = mob.GetComponent<Collider2D>();
        ProjectileService projectiles = gameplay.Resolve<ProjectileService>();
        float startedAt = -1f;
        int flyingAtContact = -1;
        int hits = 0;
        System.Action<int, Vector2, float> onStarted = (_, _, _) => startedAt = startedAt < 0f ? playerBody.Distance(mobBody).distance : startedAt;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, _, _, _) => flyingAtContact = flyingAtContact < 0 ? projectiles.FlyingCount : flyingAtContact;
        System.Action<Health.DamageEvent> onDamaged = _ => hits++;
        weapon.AttackStarted += onStarted;
        weapon.AttackContacted += onContact;
        mobHealth.Damaged += onDamaged;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (hits == 0)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The arrow should hit the mob.");
                yield return null;
            }

            commands.Stop();
            Assert.That(startedAt, Is.LessThanOrEqualTo(longbow.AttackRange + 0.05f), "The bow shoots once within its range.");
            Assert.That(startedAt, Is.GreaterThan(longbow.AttackRange - 1f), "It stops chasing at its range, not next to the mob.");
            Assert.That(flyingAtContact, Is.EqualTo(1), "One arrow leaves at contact.");
            Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth - longbow.Damage));
            yield return new WaitForSeconds(1f);
            Assert.That(hits, Is.EqualTo(1), "The arrow hits once.");
        }
        finally
        {
            weapon.AttackStarted -= onStarted;
            weapon.AttackContacted -= onContact;
            mobHealth.Damaged -= onDamaged;
        }
    }

    /// <summary>
    /// A move click during the longbow's windup cancels the shot: no arrow is ever launched and the mob takes nothing.
    /// </summary>
    [UnityTest]
    public IEnumerator AMoveOrder_CancelsTheBowsWindup_AndNoArrowIsFired()
    {
        yield return Boot();
        Assert.That(weapon.EquipFromCatalog(LongbowIndex), Is.True);
        MobController mob = KeepMobs(1)[0];
        Vector2 start = player.transform.position;
        MoveMob(mob, start + new Vector2(3f, 0f));
        Health mobHealth = mob.GetComponent<Health>();
        ProjectileService projectiles = gameplay.Resolve<ProjectileService>();
        int cancelled = 0;
        int contacts = 0;
        System.Action<int, Vector2, float> moveAway = (_, _, _) => commands.Click(start + new Vector2(-4f, 0f), default);
        System.Action onCancelled = () => cancelled++;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, _, _, _) => contacts++;
        weapon.AttackStarted += moveAway;
        weapon.AttackCancelled += onCancelled;
        weapon.AttackContacted += onContact;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            yield return SceneBootTestHelper.WaitUntil(() => cancelled > 0, "the move click to cancel the windup");
            weapon.AttackStarted -= moveAway;
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
            float until = Time.time + weapon.CurrentWeapon.Timeline.Duration + 0.5f;
            while (Time.time < until)
            {
                Assert.That(projectiles.FlyingCount, Is.Zero, "No arrow is fired.");
                yield return null;
            }

            Assert.That(contacts, Is.Zero);
            Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth), "Nothing lands.");
        }
        finally
        {
            weapon.AttackStarted -= moveAway;
            weapon.AttackCancelled -= onCancelled;
            weapon.AttackContacted -= onContact;
        }
    }

    /// <summary>
    /// Boots into Testlands, caches the gameplay container, the local player and its weapon, and switches the player to
    /// scripted commands.
    /// </summary>
    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        weapon = player.GetComponent<PlayerWeaponController>();
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
    }

    /// <summary>
    /// Keeps the first <paramref name="count"/> mobs of the loaded chunks active with their AI off and deactivates the
    /// others, so none walks or attacks during the test.
    /// </summary>
    private static List<MobController> KeepMobs(int count)
    {
        List<MobController> kept = new();
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            bool keep = kept.Count < count;
            mob.gameObject.SetActive(keep);
            if (keep)
            {
                mob.enabled = false;
                kept.Add(mob);
            }
        }

        Assert.That(kept.Count, Is.EqualTo(count), "Enough mobs should be loaded around the start.");
        return kept;
    }

    /// <summary>
    /// A unit of the mobs' faction on the <c>Enemy</c> layer with the Weasel's footprint, health and a receiver,
    /// standing at <paramref name="position"/>.
    /// </summary>
    private static Health CreateDummy(Vector2 position)
    {
        GameObject dummy = new("CleaveDummy") { layer = LayerMask.NameToLayer("Enemy") };
        dummy.transform.position = position;
        dummy.AddComponent<BoxCollider2D>().size = new Vector2(0.534f, 0.3f);
        dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        Health health = dummy.AddComponent<Health>();
        dummy.AddComponent<DamageReceiver>();
        Physics2D.SyncTransforms();
        return health;
    }

    /// <summary>
    /// Puts <paramref name="mob"/>'s body and transform at <paramref name="position"/>, at rest.
    /// </summary>
    private static void MoveMob(MobController mob, Vector2 position)
    {
        Rigidbody2D body = mob.GetComponent<Rigidbody2D>();
        body.position = position;
        body.linearVelocity = Vector2.zero;
        mob.transform.position = position;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// The mob as a click on it picks it.
    /// </summary>
    private UnitTarget PickMob(MobController mob)
    {
        UnitTarget target = gameplay.Resolve<PointerTargetPicker>().Pick(mob.GetComponent<Collider2D>().bounds.center);
        Assert.That(target.Health, Is.SameAs(mob.GetComponent<Health>()), "Clicking the mob should pick it.");
        return target;
    }

    /// <summary>
    /// <see cref="IPlayerCommandSource"/> that the test feeds one click or stop at a time.
    /// </summary>
    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        /// <summary>
        /// Queues a single move click at <paramref name="point"/> on <paramref name="target"/> for the next read.
        /// </summary>
        public void Click(Vector2 point, UnitTarget target)
        {
            next = new PlayerCommand(point, target, movePressed: true, moveHeld: false, stopPressed: false);
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
