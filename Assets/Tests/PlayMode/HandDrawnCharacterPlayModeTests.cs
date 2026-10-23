using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for a character drawn by hand in play (Docs/AsepriteCharacters.md, Phase 2) in Testlands, the local
/// player made the Bandit (the stand-in imported from Aseprite) through its local settings, driven by scripted commands:
/// it draws the Bandit's set, running east at its base speed at the run's rate and walking under a heavy slow, every
/// sprite the Bandit's; a sword hit on a Weasel shows the Bandit's contact frame on the update it lands and holds it,
/// flashing the Weasel, and a hit the Bandit takes flashes and holds it; Bolt shows the Bandit's release frame on the
/// update its effect resolves; and Whirl, for which the Bandit has no clip over the run, plays its standing clip.
/// </summary>
public class HandDrawnCharacterPlayModeTests
{
    private const string Bandit = "Bandit";
    private const int BoltSlot = 0;
    private const int WhirlSlot = 4;

    private readonly List<Object> created = new();
    private readonly List<MobController> mobs = new();
    private IObjectResolver gameplay;
    private PlayerController player;
    private CharacterVisuals visuals;
    private SpriteRenderer spriteRenderer;
    private CharacterAnimationSet banditSet;
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
        mobs.Clear();
    }

    /// <summary>
    /// Chosen in the settings, the Bandit's look draws on the player at once. Sent far east it runs east at its base
    /// speed, the run playing at that speed over the Bandit's run's natural speed, its frames moving on; a heavy slow
    /// makes it walk, and told to stop it stands idle facing east, every sprite from the Bandit's set.
    /// </summary>
    [UnityTest]
    public IEnumerator TheBandit_RunsAndWalksAtItsSpeeds()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        Assert.That(visuals.AnimationSet, Is.EqualTo(banditSet));
        yield return null;
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        AssertShows(CharacterGait.Idle);

        commands.Click((Vector2)player.transform.position + Vector2.right * 50f);
        yield return new WaitForSeconds(0.3f);
        CharacterAnimationClip run = banditSet.Locomotion(CharacterGait.Run);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Run));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));
        Assert.That(animator.PlaybackRate, Is.EqualTo(player.ControlSettings.MoveSpeed / run.NaturalSpeed).Within(0.01f));
        HashSet<int> frames = new();
        for (float until = Time.time + 0.6f; Time.time < until;)
        {
            frames.Add(animator.Frame);
            AssertShows(CharacterGait.Run);
            yield return null;
        }

        Assert.That(frames.Count, Is.GreaterThanOrEqualTo(3), "The run's frames move on.");

        StatusEffectService statuses = gameplay.Resolve<StatusEffectService>();
        DamageReceiver receiver = player.GetComponent<DamageReceiver>();
        StatusEffectDefinition heavySlow = Track(StatusEffectDefinition.Create("HeavySlow", StatusKind.Debuff, 5f, moveSpeedMultiplier: 0.3f));
        Assert.That(statuses.Apply(receiver, heavySlow, null), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return new WaitForSeconds(0.2f);
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Walk));
        AssertShows(CharacterGait.Walk);
        statuses.Remove(receiver, heavySlow);

        commands.Stop();
        yield return null;
        yield return null;
        Assert.That(animator.Gait, Is.EqualTo(CharacterGait.Idle));
        Assert.That(animator.Direction, Is.EqualTo(CharacterDirections.East));
        AssertShows(CharacterGait.Idle);
    }

    /// <summary>
    /// An attack order on a Weasel beside the Bandit lands with the sword; on the update it lands the Bandit shows its
    /// slash's contact frame and holds it while the Weasel flashes; a hit the Bandit then takes flashes it white and
    /// holds its frame.
    /// </summary>
    [UnityTest]
    public IEnumerator TheBanditsHits_ShowTheirContactFrame_AndItsFlashAndHoldWork()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        MobController mob = KeepOneMob();
        Vector2 stood = (Vector2)player.transform.position + new Vector2(0.8f, 0f);
        Rigidbody2D mobBody = mob.GetComponent<Rigidbody2D>();
        mobBody.position = stood;
        mobBody.linearVelocity = Vector2.zero;
        mob.transform.position = stood;
        Physics2D.SyncTransforms();
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        CharacterAnimationClip slash = banditSet.Find(weapon.CurrentWeapon.AttackClip);
        HitFeedback struck = mob.GetComponent<HitFeedback>();
        HitFeedback bandit = player.GetComponent<HitFeedback>();
        int contactFrame = -1;
        System.Action<int, Vector2, Vector2, UnitTarget> onContact = (_, _, _, _) => contactFrame = contactFrame < 0 ? Time.frameCount : contactFrame;
        weapon.AttackContacted += onContact;
        try
        {
            UnitTarget target = gameplay.Resolve<PointerTargetPicker>().Pick(mob.GetComponent<Collider2D>().bounds.center);
            commands.Click(mob.transform.position, target);
            for (float until = Time.time + 8f; Time.time < until && (contactFrame < 0 || Time.frameCount == contactFrame);)
            {
                yield return null;
            }

            commands.Stop();
            Assert.That(contactFrame, Is.GreaterThanOrEqualTo(0), "The sword's hit lands.");
            Assert.That(animator.Clip, Is.EqualTo(slash), "The Bandit plays its own slash.");
            Assert.That(animator.Frame, Is.EqualTo(slash.ContactFrame), "The update the hit landed showed the contact frame.");
            Assert.That(spriteRenderer.sprite, Is.EqualTo(slash.SpriteAt(animator.Direction, slash.ContactFrame)));
            Assert.That(bandit.HoldCount, Is.EqualTo(1), "The Bandit holds its contact frame.");
            Assert.That(struck.FlashCount, Is.EqualTo(1), "The Weasel flashes.");
        }
        finally
        {
            weapon.AttackContacted -= onContact;
        }

        yield return new WaitForSeconds(0.6f);
        int holds = bandit.HoldCount;
        Assert.That(gameplay.Resolve<HitService>().ApplyHit(player.GetComponent<DamageReceiver>(), new Hit(1, DamageType.Physical), mob.gameObject), Is.True);
        yield return null;
        Assert.That(bandit.FlashCount, Is.EqualTo(1), "The Bandit flashes when struck.");
        Assert.That(bandit.IsFlashing, Is.True);
        Assert.That(bandit.HoldCount, Is.EqualTo(holds + 1), "The Bandit holds when struck.");
        Assert.That(bandit.IsHolding, Is.True);
        Sprite held = spriteRenderer.sprite;
        yield return null;
        Assert.That(spriteRenderer.sprite, Is.EqualTo(held), "The held frame stays on screen.");
    }

    /// <summary>
    /// Bolt cast east shows the Bandit's cast clip and its release frame on the update the cast's effect resolves;
    /// Whirl, a Continue cast whose clip over the run the Bandit lacks, plays the Bandit's standing clip.
    /// </summary>
    [UnityTest]
    public IEnumerator TheBanditsCasts_ReleaseOnCompletion()
    {
        yield return Boot();
        CharacterAnimator animator = visuals.Animator;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        AbilityDefinition bolt = abilities.GetAbility(BoltSlot);
        CharacterAnimationClip clip = banditSet.Find(bolt.CastClip);
        Assert.That(clip, Is.Not.Null);
        AbilityService service = gameplay.Resolve<AbilityService>();
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
            commands.Cast(BoltSlot, (Vector2)player.transform.position + Vector2.right * 3f);
            for (float until = Time.time + 2f; Time.time < until && (resolvedFrame < 0 || Time.frameCount == resolvedFrame);)
            {
                yield return null;
            }

            Assert.That(resolvedFrame, Is.GreaterThanOrEqualTo(0), "The cast resolves.");
            Assert.That(animator.Clip, Is.EqualTo(clip));
            Assert.That(animator.Frame, Is.EqualTo(clip.ContactFrame), "The update the effect resolved showed the release.");
            Assert.That(spriteRenderer.sprite, Is.EqualTo(clip.SpriteAt(CharacterDirections.East, clip.ContactFrame)));
        }
        finally
        {
            service.CastResolved -= onResolved;
        }

        yield return new WaitForSeconds(1f);
        AbilityDefinition whirl = abilities.GetAbility(WhirlSlot);
        Assert.That(banditSet.Find(whirl.MovingCastClip), Is.Null);
        commands.Cast(WhirlSlot, (Vector2)player.transform.position + Vector2.right * 3f);
        yield return new WaitForSeconds(0.1f);
        Assert.That(animator.IsActing, Is.True);
        Assert.That(animator.Clip, Is.EqualTo(banditSet.Find(whirl.CastClip)), "Whirl plays the Bandit's standing clip.");
    }

    /// <summary>
    /// Boots into Testlands with every mob deactivated, makes the local player the Bandit through the local settings,
    /// caches it, its visuals, their renderer and the Bandit's set, and switches it to scripted commands.
    /// </summary>
    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
            mobs.Add(mob);
        }

        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        visuals = player.GetComponentInChildren<CharacterVisuals>();
        spriteRenderer = visuals.GetComponent<SpriteRenderer>();
        CharacterLookCatalog looks = player.GetComponent<PlayerLook>().Catalog;
        banditSet = looks.Get(looks.IndexOf(Bandit)).AnimationSet;
        Assert.That(visuals.AnimationSet, Is.Not.EqualTo(banditSet), "The player starts as the rogue.");
        SceneBootTestHelper.ResolveFromMain<UserSettings>().SetCharacterLook(Bandit);
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
    }

    /// <summary>
    /// Activates again one of the mobs the boot deactivated, with its AI off, and returns it.
    /// </summary>
    private MobController KeepOneMob()
    {
        Assert.That(mobs, Is.Not.Empty, "A mob should be loaded around the start.");
        MobController mob = mobs[0];
        mob.gameObject.SetActive(true);
        mob.enabled = false;
        return mob;
    }

    /// <summary>
    /// Asserts the renderer shows the animator's current frame of the Bandit's <paramref name="gait"/> clip in its
    /// direction.
    /// </summary>
    private void AssertShows(CharacterGait gait)
    {
        CharacterAnimator animator = visuals.Animator;
        Sprite expected = banditSet.Locomotion(gait).SpriteAt(animator.Direction, animator.Frame);
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
        /// Queues a single move click at <paramref name="point"/> on <paramref name="target"/> (none for the ground)
        /// for the next read.
        /// </summary>
        public void Click(Vector2 point, UnitTarget target = default)
        {
            next = new PlayerCommand(point, target, movePressed: true, moveHeld: false, stopPressed: false);
        }

        /// <summary>
        /// Queues a single press of ability <paramref name="slot"/> with the pointer on the ground at
        /// <paramref name="point"/> for the next read.
        /// </summary>
        public void Cast(int slot, Vector2 point)
        {
            next = new PlayerCommand(point, default, movePressed: false, moveHeld: false, stopPressed: false, abilitySlot: slot);
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
