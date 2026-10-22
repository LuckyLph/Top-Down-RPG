using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for hit feedback (Docs/Animation.md, Phase 6) in Testlands, the booted test world, with the player
/// driven by scripted commands: a sword hit on a Weasel flashing, holding and nudging it and playing an impact on its
/// side facing the player at contact, the player holding its contact frame; a longbow arrow's hit doing the same where
/// the arrow stopped, the shooter not holding; and the local player's camera kicking on its hit and on a hit it takes.
/// </summary>
public class HitFeedbackPlayModeTests
{
    private const int LongbowIndex = 2;
    private const float TimeoutSeconds = 8f;

    private IObjectResolver gameplay;
    private PlayerController player;
    private PlayerWeaponController weapon;
    private ScriptedCommands commands;

    /// <summary>
    /// An attack order on a Weasel beside the player lands at contact and, on that frame, flashes the Weasel white,
    /// holds it, nudges it away from the player, plays the Physical impact on its collider's face toward the player at
    /// its impact height, and holds the player's contact frame; the Weasel's body stays where it stood and the impact
    /// plays out.
    /// </summary>
    [UnityTest]
    public IEnumerator ASwordHitOnAWeasel_FlashesItAndPlaysAnImpactAtContact()
    {
        yield return Boot();
        MobController mob = KeepMobs(1)[0];
        Vector2 start = player.transform.position;
        Vector2 stood = start + new Vector2(0.8f, 0f);
        MoveMob(mob, stood);
        HitFeedback struck = mob.GetComponent<HitFeedback>();
        HitFeedback attacker = player.GetComponent<HitFeedback>();
        HitFeedbackSettings settings = gameplay.Resolve<HitFeedbackSettings>();
        EffectPool effects = gameplay.Resolve<EffectPool>();
        Shown shown = new(settings, struck, attacker);
        float contactTime = -1f;
        System.Action<int, Vector2, Vector2> onContact = (_, _, _) => contactTime = contactTime < 0f ? Time.time : contactTime;
        weapon.AttackContacted += onContact;
        effects.Played += shown.Record;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            yield return SceneBootTestHelper.WaitUntil(() => shown.Impact != null, "the sword's hit to show");
            commands.Stop();
            Assert.That(shown.At, Is.EqualTo(contactTime), "The hit shows at contact.");
            Assert.That(shown.Flashes, Is.EqualTo(1));
            Assert.That(shown.FlashColor, Is.EqualTo(settings.HitFlashColor));
            Assert.That(shown.Holds, Is.EqualTo(1), "The Weasel holds.");
            Assert.That(shown.AttackerHolds, Is.EqualTo(1), "The player holds its contact frame.");
            Assert.That(shown.NudgeDirection.x, Is.GreaterThan(0.9f), "The Weasel is nudged away from the player.");
            Assert.That(shown.Impact.Variant, Is.EqualTo((int)DamageType.Physical));
            Bounds body = mob.GetComponent<Collider2D>().bounds;
            Vector2 ground = shown.Position - Vector2.up * struck.ImpactHeight;
            Assert.That(ground.x, Is.EqualTo(body.min.x).Within(0.02f), "The impact lands on the Weasel's side facing the player.");
            Assert.That(ground.y, Is.InRange(body.min.y, body.max.y));

            yield return new WaitForSeconds(0.5f);
            Assert.That(shown.Impact.IsPlaying, Is.False, "The impact plays out and goes back to its pool.");
            Assert.That(struck.IsFlashing, Is.False);
            Assert.That(struck.NudgeOffset, Is.EqualTo(Vector2.zero));
            Assert.That(Vector2.Distance(mob.GetComponent<Rigidbody2D>().position, stood), Is.LessThan(1e-4f), "The body never moved.");
        }
        finally
        {
            weapon.AttackContacted -= onContact;
            effects.Played -= shown.Record;
        }
    }

    /// <summary>
    /// With the longbow equipped, an arrow's hit on a Weasel four units away flashes and nudges it away from the shooter
    /// and plays the impact where the arrow stopped (within its sweep), while the shooter, long past its contact, does
    /// not hold.
    /// </summary>
    [UnityTest]
    public IEnumerator ALongbowArrowsHit_FlashesTheWeaselAndPlaysAnImpactWhereTheArrowStopped()
    {
        yield return Boot();
        Assert.That(weapon.EquipFromCatalog(LongbowIndex), Is.True);
        MobController mob = KeepMobs(1)[0];
        Vector2 start = player.transform.position;
        MoveMob(mob, start + new Vector2(4f, 0f));
        HitFeedback struck = mob.GetComponent<HitFeedback>();
        HitFeedback shooter = player.GetComponent<HitFeedback>();
        HitFeedbackSettings settings = gameplay.Resolve<HitFeedbackSettings>();
        EffectPool effects = gameplay.Resolve<EffectPool>();
        ProjectileService projectiles = gameplay.Resolve<ProjectileService>();
        Shown shown = new(settings, struck, shooter);
        Vector2 stopped = new(float.NaN, float.NaN);
        System.Action<EffectPlayer> onPlayed = effect =>
        {
            if (effect.Sprites == settings.ImpactEffect.Sprites && projectiles.FlyingCount > 0)
            {
                stopped = projectiles.Flying(0).Position;
            }
        };
        effects.Played += onPlayed;
        effects.Played += shown.Record;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (shown.Impact == null)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The arrow's hit should show.");
                yield return null;
            }

            commands.Stop();
            Assert.That(shown.Flashes, Is.EqualTo(1));
            Assert.That(shown.Holds, Is.EqualTo(1));
            Assert.That(shown.AttackerHolds, Is.Zero, "The shooter, past its contact, does not hold.");
            Assert.That(shown.NudgeDirection.x, Is.GreaterThan(0.9f), "The Weasel is nudged away from the shooter.");
            Assert.That(float.IsNaN(stopped.x), Is.False, "The arrow was still where it stopped as its hit showed.");
            Vector2 ground = shown.Position - Vector2.up * struck.ImpactHeight;
            Assert.That(Vector2.Distance(ground, stopped), Is.LessThan(0.15f), "The impact plays where the arrow stopped.");
        }
        finally
        {
            effects.Played -= onPlayed;
            effects.Played -= shown.Record;
        }
    }

    /// <summary>
    /// The local player's camera kicks toward the Weasel the player hits, on the frame the hit lands, and eases back;
    /// the Weasel's hit on the player kicks it away from the Weasel.
    /// </summary>
    [UnityTest]
    public IEnumerator TheLocalPlayersCamera_KicksOnItsHit()
    {
        yield return Boot();
        MobController mob = KeepMobs(1)[0];
        Vector2 start = player.transform.position;
        MoveMob(mob, start + new Vector2(0.8f, 0f));
        CameraFollow2D camera = SceneBootTestHelper.ResolveFromMain<CameraFollow2D>();
        HitFeedbackSettings settings = gameplay.Resolve<HitFeedbackSettings>();
        EffectPool effects = gameplay.Resolve<EffectPool>();
        Vector2 kicked = Vector2.zero;
        bool landed = false;
        System.Action<EffectPlayer> onPlayed = effect =>
        {
            if (!landed && effect.Sprites == settings.ImpactEffect.Sprites)
            {
                landed = true;
                kicked = camera.KickOffset;
            }
        };
        effects.Played += onPlayed;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            yield return SceneBootTestHelper.WaitUntil(() => landed, "the player's hit to land");
            commands.Stop();
            Assert.That(kicked.x, Is.EqualTo(settings.KickDistance).Within(0.002f), "The camera kicks toward the Weasel.");
            Assert.That(kicked.y, Is.EqualTo(0f).Within(0.002f));
            yield return new WaitForSeconds(settings.KickSeconds + 0.3f);
            Assert.That(camera.KickOffset, Is.EqualTo(Vector2.zero), "The kick eases back.");

            DamageService damage = gameplay.Resolve<DamageService>();
            damage.ApplyDamage(player.GetComponent<DamageReceiver>(), 1, DamageType.Physical, mob.gameObject);
            Assert.That(camera.KickOffset.x, Is.EqualTo(-settings.KickDistance).Within(0.002f), "A hit taken kicks it away from the Weasel.");
        }
        finally
        {
            effects.Played -= onPlayed;
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
    /// What the first impact of the settings' effect showed, recorded as it played: when, where, and what the struck
    /// unit and the attacker showed with it.
    /// </summary>
    private sealed class Shown
    {
        private readonly HitFeedbackSettings settings;
        private readonly HitFeedback struck;
        private readonly HitFeedback attacker;

        /// <summary>
        /// Watches for <paramref name="hitSettings"/>' impact on <paramref name="struckUnit"/>, hit by
        /// <paramref name="attackingUnit"/>.
        /// </summary>
        public Shown(HitFeedbackSettings hitSettings, HitFeedback struckUnit, HitFeedback attackingUnit)
        {
            settings = hitSettings;
            struck = struckUnit;
            attacker = attackingUnit;
        }

        public EffectPlayer Impact { get; private set; }
        public float At { get; private set; }
        public Vector2 Position { get; private set; }
        public int Flashes { get; private set; }
        public Color FlashColor { get; private set; }
        public int Holds { get; private set; }
        public int AttackerHolds { get; private set; }
        public Vector2 NudgeDirection { get; private set; }

        /// <summary>
        /// Records the first impact of the settings' effect and what the units showed with it.
        /// </summary>
        public void Record(EffectPlayer effect)
        {
            if (Impact != null || effect.Sprites != settings.ImpactEffect.Sprites)
            {
                return;
            }

            Impact = effect;
            At = Time.time;
            Position = effect.transform.position;
            Flashes = struck.FlashCount;
            FlashColor = struck.FlashColor;
            Holds = struck.HoldCount;
            AttackerHolds = attacker.HoldCount;
            NudgeDirection = struck.LastNudgeDirection;
        }
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
