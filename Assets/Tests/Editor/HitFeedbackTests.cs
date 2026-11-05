using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for hit feedback (Docs/Animation.md, Phase 6): <see cref="HitFeedbackPresenter"/> reacting to
/// <see cref="CombatEvents.DamageApplied"/> (a direct hit flashing, holding and nudging the struck unit away from its
/// source and playing its type's impact where it came in, a hit from a point turning all of it from that point; an
/// immune hit's grey flash, an immune tick's included, and a tick's wash with nothing else; the camera kicking only for
/// the local player's hits; a client's predicted hit holding its attacker's contact frame); the character animator's hold
/// (an attacker holding its contact frame and its next attack still showing contact at contact, a hold in the windup
/// ending at contact, and an attacker held in its windup holding its contact frame when its hit lands);
/// hit-stop changing nothing in gameplay (attack phases and ready times, health, projectile flight); the flash, nudge,
/// hold, impact and kick allocating nothing once warm; the camera kick easing back; the effect recipes baking what is
/// written; and the shipped units and settings being wired for it.
/// </summary>
public class HitFeedbackTests
{
    private const int Contact = 4;
    private const float Step = 1f / 60f;
    private static readonly AttackTimeline Sword = new(0.15f, 0.05f, 0.15f);

    private readonly List<Object> created = new();
    private readonly List<EffectPlayer> played = new();
    private ManualClock clock;
    private CharacterLocomotionSettings locomotion;
    private CharacterAnimationSet set;
    private CombatEvents events;
    private DamageService damage;
    private EffectPool effects;
    private EffectPlayer impactPrefab;
    private HitFeedbackSettings settings;
    private LocalPlayerTracker localPlayer;
    private FakeCameraKick cameraKick;
    private HitFeedbackPresenter presenter;

    /// <summary>
    /// Builds a character set (an idle and an eight-frame attack with its contact on frame 4), a presenter over fresh
    /// combat events and an authoritative damage service, an effect pool and an impact "prefab" of six variants in
    /// eight directions, with no local player.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        locomotion = Track(CharacterLocomotionSettings.Create(0.1f, 2.4f, 5.6f, 0.1f, 0.5f, 1.5f, 8f));
        set = Track(CharacterAnimationSet.Create(new[] { Clip("Idle", CharacterGait.Idle, new[] { 0.25f }, -1), Clip("Slash", CharacterGait.None, new[] { 0.25f, 0.125f, 0.0625f, 0.0625f, 0.125f, 0.125f, 0.0625f, 0.0625f }, Contact) }));
        events = new CombatEvents();
        damage = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, events);
        effects = new EffectPool(default);
        effects.Played += played.Add;
        impactPrefab = CreateEffectPrefab(6, 8, 4);
        settings = Track(HitFeedbackSettings.Create(impactPrefab));
        localPlayer = new LocalPlayerTracker();
        cameraKick = new FakeCameraKick();
        presenter = new HitFeedbackPresenter(events, settings, TestCombat.Settings, localPlayer, cameraKick, effects);
        presenter.Start();
    }

    /// <summary>
    /// Stops the presenter and destroys every effect, unit and asset the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        presenter.Dispose();
        foreach (EffectPlayer effect in Object.FindObjectsByType<EffectPlayer>(FindObjectsInactive.Include))
        {
            if (effect != null && effect.SourcePrefab != null)
            {
                Object.DestroyImmediate(effect.gameObject);
            }
        }

        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made is Component component ? component.gameObject : made);
            }
        }

        created.Clear();
        played.Clear();
    }

    /// <summary>
    /// A direct hit flashes the struck unit white and holds its animation for the hit-stop, nudges its sprite two whole
    /// pixels away from the source, then one, then back, and plays the impact of its damage type facing along the hit
    /// where the line from the source to the unit's middle enters its collider, raised to its impact height and sorted
    /// with its feet; each starts whole on the frame it starts. The attacker, not in an attack, does not hold.
    /// </summary>
    [Test]
    public void ADirectHit_FlashesHoldsAndNudgesTheUnit_AndPlaysItsImpactWhereItCameIn()
    {
        DamageReceiver attacker = CreateUnit("Attacker", Vector2.zero, Faction.Players);
        DamageReceiver target = CreateUnit("Target", new Vector2(0.9f, 0.2f), Faction.Mobs);
        HitFeedback struck = target.GetComponent<HitFeedback>();

        damage.ApplyDamage(target, 1, DamageType.Fire, attacker.gameObject);

        Assert.That(struck.IsFlashing, Is.True);
        Assert.That(struck.FlashColor, Is.EqualTo(settings.HitFlashColor));
        Assert.That(struck.FlashAmount, Is.EqualTo(1f));
        Assert.That(struck.IsHolding, Is.True);
        Assert.That(attacker.GetComponent<HitFeedback>().HoldCount, Is.Zero, "An attacker not in its contact does not hold.");
        Vector2 away = new Vector2(0.9f, 0.2f).normalized;
        Assert.That(struck.LastNudgeDirection.x, Is.EqualTo(away.x).Within(1e-4f));
        AssertOffset(struck, 2, 0);

        Assert.That(played.Count, Is.EqualTo(1));
        EffectPlayer impact = played[0];
        Assert.That(impact.Variant, Is.EqualTo((int)DamageType.Fire));
        Assert.That(impact.Direction, Is.EqualTo(impactPrefab.Sprites.DirectionOf(away)));
        Bounds body = target.GetComponent<Collider2D>().bounds;
        Vector2 ground = (Vector2)impact.GetComponentInChildren<SpriteRenderer>().transform.position - Vector2.up * struck.ImpactHeight;
        Assert.That(ground.x, Is.EqualTo(body.min.x).Within(1e-4f), "The impact lands on the face toward the source.");
        Assert.That(Cross(ground, body.center), Is.EqualTo(0f).Within(1e-4f), "It lies on the line from the source to the unit's middle.");
        Assert.That(impact.transform.position.y, Is.EqualTo(0.2f).Within(1e-4f), "It sorts with the struck unit's feet, where its root stands.");

        CharacterVisuals animated = target.GetComponentInChildren<CharacterVisuals>();
        animated.Tick();
        struck.Tick(0f);
        clock.Advance(0.04f);
        animated.Tick();
        struck.Tick(0.04f);
        Assert.That(struck.IsHolding, Is.True, "The animation holds through the hit-stop.");
        Assert.That(struck.IsFlashing, Is.True);
        AssertOffset(struck, 1, 0);
        clock.Advance(0.02f);
        animated.Tick();
        struck.Tick(0.02f);
        Assert.That(struck.IsHolding, Is.True);
        Assert.That(struck.IsFlashing, Is.False, "The flash lasts its time.");
        AssertOffset(struck, 1, 0);
        clock.Advance(0.03f);
        animated.Tick();
        struck.Tick(0.03f);
        Assert.That(struck.IsHolding, Is.False, "The hold lets go after its time.");
        AssertOffset(struck, 0, 0);
        Assert.That(target.transform.position, Is.EqualTo(new Vector3(0.9f, 0.2f, 0f)), "The body never moves.");
    }

    /// <summary>
    /// A hit that carries the point it came from (Docs/KnownGaps.md, Phase 4) turns its nudge, its impact's side and the
    /// camera kick from that point, not from where its source stands now: a shooter that moved three units after its
    /// shot still kicks its camera and nudges the target along the shot. A hit replicated to a client with its point and
    /// no source it knows turns the same way there.
    /// </summary>
    [Test]
    public void AHitFromAPoint_TurnsItsFeedbackFromThatPoint_WhereverItsSourceStandsNow()
    {
        DamageReceiver shooter = CreatePlayer("Local", new Vector2(0f, 3f));
        localPlayer.Assign(new LocalPlayer(shooter.GetComponent<PlayerController>()));
        DamageReceiver target = CreateUnit("Target", new Vector2(2f, 0f), Faction.Mobs);
        HitFeedback struck = target.GetComponent<HitFeedback>();

        damage.ApplyDamage(target, 1, DamageType.Physical, shooter.gameObject, DamageFlags.None, Vector2.zero);
        Assert.That(struck.LastNudgeDirection.x, Is.EqualTo(1f).Within(1e-4f), "Away from where it was shot.");
        Assert.That(struck.LastNudgeDirection.y, Is.EqualTo(0f).Within(1e-4f), "Not from where the shooter stands now.");
        Assert.That(cameraKick.LastOffset.x, Is.EqualTo(settings.KickDistance).Within(1e-5f), "The shooter's camera kicks along the shot.");
        Assert.That(cameraKick.LastOffset.y, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(played[0].Direction, Is.EqualTo(impactPrefab.Sprites.DirectionOf(Vector2.right)));
        Vector2 ground = (Vector2)played[0].GetComponentInChildren<SpriteRenderer>().transform.position - Vector2.up * struck.ImpactHeight;
        Assert.That(ground.x, Is.EqualTo(target.GetComponent<Collider2D>().bounds.min.x).Within(1e-4f), "The impact lands on the face toward the shot.");

        damage.ApplyReplicatedHit(target, 1, DamageType.Physical, DamageFlags.None, null, new Vector2(2f, 2f));
        Assert.That(struck.NudgeCount, Is.EqualTo(2));
        Assert.That(struck.LastNudgeDirection.y, Is.EqualTo(-1f).Within(1e-4f), "A replicated hit turns from its point without a source.");
    }

    /// <summary>
    /// A client's own melee hit it predicts at its contact (<see cref="CombatEvents.HitPredicted"/>) holds its attacker's
    /// contact frame there and then, as a landed hit does, and nothing in its windup; nothing else shows until the host's
    /// hit arrives.
    /// </summary>
    [Test]
    public void APredictedHit_HoldsTheAttackersContactFrame()
    {
        DamageReceiver attacker = CreatePlayer("Local", Vector2.zero);
        HitFeedback feedback = attacker.GetComponent<HitFeedback>();
        CharacterVisuals visuals = attacker.GetComponentInChildren<CharacterVisuals>();
        Sprite contact = set.Find("Slash").SpriteAt(CharacterDirections.East, Contact);
        visuals.Tick();
        visuals.PlayAction(set.Find("Slash"), Vector2.right, Sword, false);
        visuals.Tick();

        events.PublishPredicted(attacker.gameObject);
        Assert.That(feedback.HoldCount, Is.Zero, "Nothing holds in the windup.");
        clock.Advance(Sword.Windup);
        events.PublishPredicted(attacker.gameObject);
        Assert.That(feedback.HoldCount, Is.EqualTo(1), "The predicted hit holds the contact frame.");
        visuals.Tick();
        clock.Advance(Sword.Contact + 0.02f);
        visuals.Tick();
        Assert.That(visuals.Animator.Sprite, Is.SameAs(contact), "Held past the contact's own time.");
        Assert.That(feedback.FlashCount, Is.Zero);
        Assert.That(feedback.NudgeCount, Is.Zero);
        Assert.That(played, Is.Empty);
        Assert.That(cameraKick.Count, Is.Zero);
    }

    /// <summary>
    /// An immune hit only flashes the unit in the immune colour, and a damage-over-time tick only washes it part of the
    /// way toward its damage type's colour, unless the unit is immune to the tick, which flashes it in the immune colour
    /// as its popup reads Immune: none holds, nudges, plays an impact or kicks the camera, even for the local player's
    /// hits.
    /// </summary>
    [Test]
    public void ImmuneHitsFlashGrey_AndTicksWashTowardTheirType_WithNothingElse()
    {
        DamageReceiver attacker = CreatePlayer("Local", Vector2.zero);
        localPlayer.Assign(new LocalPlayer(attacker.GetComponent<PlayerController>()));
        DamageReceiver target = CreateUnit("Target", new Vector2(0.9f, 0f), Faction.Mobs);
        target.SetProfile(Track(CombatProfile.Create(Faction.Mobs, DamageTypeMask.Frost)));
        HitFeedback struck = target.GetComponent<HitFeedback>();

        damage.ApplyDamage(target, 5, DamageType.Frost, attacker.gameObject);
        Assert.That(struck.FlashColor, Is.EqualTo(TestCombat.Settings.ImmuneColor));
        Assert.That(struck.FlashAmount, Is.EqualTo(1f));
        AssertNothingElse(struck);

        damage.ApplyPeriodicDamage(target, 1, DamageType.Fire, attacker.gameObject, 1f);
        Assert.That(struck.FlashCount, Is.EqualTo(2));
        Assert.That(struck.FlashColor, Is.EqualTo(TestCombat.Settings.GetColor(DamageType.Fire)));
        Assert.That(struck.FlashAmount, Is.EqualTo(settings.PeriodicFlashAmount));
        AssertNothingElse(struck);

        damage.ApplyPeriodicDamage(target, 1, DamageType.Frost, attacker.gameObject, 1f);
        Assert.That(struck.FlashCount, Is.EqualTo(3));
        Assert.That(struck.FlashColor, Is.EqualTo(TestCombat.Settings.ImmuneColor), "A tick the unit is immune to.");
        Assert.That(struck.FlashAmount, Is.EqualTo(1f));
        AssertNothingElse(struck);
    }

    /// <summary>
    /// The camera kicks along the hit when the local player lands a hit (toward the target) or takes one (away from the
    /// source), and never for hits between other units.
    /// </summary>
    [Test]
    public void TheCamera_KicksOnlyForTheLocalPlayersHits()
    {
        DamageReceiver local = CreatePlayer("Local", Vector2.zero);
        DamageReceiver other = CreatePlayer("Other", new Vector2(5f, 0f));
        DamageReceiver mob = CreateUnit("Mob", new Vector2(0.9f, 0f), Faction.Mobs);
        DamageReceiver otherMob = CreateUnit("OtherMob", new Vector2(5.9f, 0f), Faction.Mobs);
        localPlayer.Assign(new LocalPlayer(local.GetComponent<PlayerController>()));

        damage.ApplyDamage(mob, 1, DamageType.Physical, local.gameObject);
        Assert.That(cameraKick.Count, Is.EqualTo(1));
        Assert.That(cameraKick.LastOffset.x, Is.EqualTo(settings.KickDistance).Within(1e-5f), "Toward the target the local player hit.");
        Assert.That(cameraKick.LastOffset.y, Is.EqualTo(0f).Within(1e-5f));
        Assert.That(cameraKick.LastSeconds, Is.EqualTo(settings.KickSeconds));

        damage.ApplyDamage(local, 1, DamageType.Physical, mob.gameObject);
        Assert.That(cameraKick.Count, Is.EqualTo(2));
        Assert.That(cameraKick.LastOffset.x, Is.EqualTo(-settings.KickDistance).Within(1e-5f), "Away from the mob that hit the local player.");

        damage.ApplyDamage(otherMob, 1, DamageType.Physical, other.gameObject);
        damage.ApplyDamage(other, 1, DamageType.Physical, otherMob.gameObject);
        Assert.That(cameraKick.Count, Is.EqualTo(2), "Other players' hits do not kick this camera.");
    }

    /// <summary>
    /// An attacker whose hit lands as its contact frame shows holds that frame for the hit-stop (and cannot hold it in
    /// its windup), then shows the frame for the real time; its next attack shows its contact frame on exactly the update
    /// the attack state reaches contact.
    /// </summary>
    [Test]
    public void AnAttacker_HoldsItsContactFrame_AndItsNextAttackShowsContactAtContact()
    {
        const float step = 1f / 64f;
        const float hold = 0.08f;
        CharacterAnimationClip slash = set.Find("Slash");
        CharacterAnimator animator = new(set, locomotion, clock);
        AttackState state = new(clock);
        Sprite contact = slash.SpriteAt(CharacterDirections.East, Contact);
        animator.Update(Vector2.zero, Vector2.right);
        for (int attack = 0; attack < 2; attack++)
        {
            float startedAt = clock.Time;
            Assert.That(state.TryStart(0, Vector2.right, Sword, 0.35f, true, true), Is.True);
            animator.PlayAction(slash, Vector2.right, Sword, false);
            Assert.That(animator.HoldContact(hold), Is.False, "Nothing holds in the windup.");
            float contactedAt = -1f;
            while (contactedAt < 0f || !state.CanStart)
            {
                if ((state.Advance() & AttackStep.Contact) != 0)
                {
                    contactedAt = clock.Time;
                    if (attack == 0)
                    {
                        Assert.That(animator.HoldContact(hold), Is.True, "The attacker holds the contact its hit landed on.");
                    }
                }

                animator.Update(Vector2.zero, Vector2.right);
                bool inContact = contactedAt >= 0f && clock.Time - startedAt < Sword.Windup + Sword.Contact;
                bool held = attack == 0 && contactedAt >= 0f && clock.Time < contactedAt + hold;
                Assert.That(animator.Sprite == contact, Is.EqualTo(inContact || held), $"Attack {attack} at {clock.Time - startedAt:0.000} s.");
                if (contactedAt >= 0f && !held && animator.IsActing)
                {
                    Assert.That(animator.Sprite, Is.SameAs(slash.SpriteAt(CharacterDirections.East, animator.Frame)), "Out of a hold the frame is the real time's.");
                }

                clock.Advance(step);
            }
        }
    }

    /// <summary>
    /// A unit struck during its own windup holds its sprite, but the hold ends on the update its attack reaches contact,
    /// which shows the contact frame as its hit lands.
    /// </summary>
    [Test]
    public void AHoldInTheWindup_EndsWhenTheAttackReachesContact()
    {
        CharacterAnimationClip slash = set.Find("Slash");
        CharacterAnimator animator = new(set, locomotion, clock);
        AttackState state = new(clock);
        Assert.That(state.TryStart(0, Vector2.left, Sword, 0.35f, true, true), Is.True);
        animator.PlayAction(slash, Vector2.left, Sword, false);
        animator.Update(Vector2.zero, Vector2.left);
        clock.Advance(0.05f);
        animator.Hold(0.5f);
        animator.Update(Vector2.zero, Vector2.left);
        Sprite held = animator.Sprite;
        bool contacted = false;
        for (int step = 0; step < 20 && !contacted; step++)
        {
            clock.Advance(Step);
            contacted = (state.Advance() & AttackStep.Contact) != 0;
            animator.Update(Vector2.zero, Vector2.left);
            if (!contacted)
            {
                Assert.That(animator.Sprite, Is.SameAs(held), $"The windup holds at step {step}.");
                Assert.That(animator.IsHolding, Is.True);
            }
        }

        Assert.That(contacted, Is.True);
        Assert.That(animator.Sprite, Is.SameAs(slash.SpriteAt(CharacterDirections.West, Contact)), "The contact frame shows as the hit lands.");
        Assert.That(animator.IsHolding, Is.False);
    }

    /// <summary>
    /// A unit struck during its own windup, whose hit then lands at its contact and asks it to hold its contact frame
    /// (as the presenter does, before that frame's update), holds the contact frame for the whole hit-stop from then,
    /// rather than letting go as its attack reaches contact.
    /// </summary>
    [Test]
    public void AnAttackerHeldInItsWindup_HoldsItsContactFrame_WhenItsHitLands()
    {
        const float hold = 0.08f;
        CharacterAnimationClip slash = set.Find("Slash");
        Sprite contact = slash.SpriteAt(CharacterDirections.West, Contact);
        CharacterAnimator animator = new(set, locomotion, clock);
        AttackState state = new(clock);
        Assert.That(state.TryStart(0, Vector2.left, Sword, 0.35f, true, true), Is.True);
        animator.PlayAction(slash, Vector2.left, Sword, false);
        animator.Update(Vector2.zero, Vector2.left);
        clock.Advance(0.1f);
        animator.Hold(hold);
        animator.Update(Vector2.zero, Vector2.left);
        float contactedAt = -1f;
        for (int step = 0; step < 20 && contactedAt < 0f; step++)
        {
            clock.Advance(Step);
            if ((state.Advance() & AttackStep.Contact) != 0)
            {
                contactedAt = clock.Time;
                Assert.That(animator.HoldContact(hold), Is.True, "Its hit lands as its contact frame is next.");
            }

            animator.Update(Vector2.zero, Vector2.left);
        }

        Assert.That(contactedAt, Is.GreaterThan(0f));
        Assert.That(animator.Sprite, Is.SameAs(contact), "The contact frame shows as the hit lands.");
        while (clock.Time + Step < contactedAt + hold)
        {
            clock.Advance(Step);
            animator.Update(Vector2.zero, Vector2.left);
            Assert.That(animator.IsHolding, Is.True, $"{clock.Time - contactedAt:0.000} s after contact.");
            Assert.That(animator.Sprite, Is.SameAs(contact), $"Held {clock.Time - contactedAt:0.000} s after contact.");
        }
    }

    /// <summary>
    /// Hit-stop is visual only: continuous attacks whose hits land on a unit and arrows flying into another, with every
    /// hit flashing, holding and nudging (the attacker holding its contacts), go through exactly the same attack phases
    /// and ready times, health and projectile flight as without any feedback.
    /// </summary>
    [Test]
    public void HitStop_ChangesNothingInGameplay()
    {
        presenter.Dispose();
        string without = Duel(false, out _);
        string with = Duel(true, out int holds);
        Assert.That(holds, Is.GreaterThan(2), "The run with feedback held its units.");
        Assert.That(with, Is.EqualTo(without));
    }

    /// <summary>
    /// Once warm, showing a direct hit (flash, hold, nudge, impact and camera kick) and playing it out allocates no
    /// managed memory, and the impact reuses its pooled instance.
    /// </summary>
    [Test]
    public void ShowingHits_AllocatesNothingOnceWarm()
    {
        DamageReceiver attacker = CreatePlayer("Local", Vector2.zero);
        localPlayer.Assign(new LocalPlayer(attacker.GetComponent<PlayerController>()));
        DamageReceiver target = CreateUnit("Target", new Vector2(0.9f, 0f), Faction.Mobs);
        HitFeedback struck = target.GetComponent<HitFeedback>();
        EffectPlayer last = null;
        effects.Played -= played.Add;
        effects.Played += effect => last = effect;
        DamageReport report = new(target.Health, 1, DamageType.Physical, DamageFlags.None, attacker.gameObject, Vector3.zero);
        System.Action show = () =>
        {
            presenter.Show(report);
            for (int i = 0; i < 6; i++)
            {
                struck.Tick(0.05f);
                last.Tick(0.05f);
            }
        };

        show();
        show();
        Assert.That(last.IsPlaying, Is.False);
        Assert.That(effects.CreatedCount, Is.EqualTo(1), "The impact is reused.");
        Assert.That(() => show(), Is.Not.AllocatingGCMemory());
        Assert.That(cameraKick.Count, Is.EqualTo(3));
    }

    /// <summary>
    /// A kick knocks the camera off at once, shows whole on its first frame, eases back as the cube of the time left and
    /// is gone after its time; a weaker kick meanwhile waits, a stronger one replaces it.
    /// </summary>
    [Test]
    public void TheCameraKick_EasesBackOverItsTime_AndAWeakerKickWaits()
    {
        CameraFollow2D camera = Track(new GameObject("Camera")).AddComponent<CameraFollow2D>();
        camera.Kick(new Vector2(0.0625f, 0f), 0.12f);
        Assert.That(camera.KickOffset, Is.EqualTo(new Vector2(0.0625f, 0f)));
        Assert.That(camera.AdvanceKick(0.02f), Is.EqualTo(new Vector2(0.0625f, 0f)), "The frame it starts on shows it whole.");
        Assert.That(camera.AdvanceKick(0.06f).x, Is.EqualTo(0.0625f * 0.125f).Within(1e-6f));
        camera.Kick(new Vector2(0f, 0.001f), 0.12f);
        Assert.That(camera.KickOffset.x, Is.EqualTo(0.0625f * 0.125f).Within(1e-6f), "A weaker kick waits.");
        camera.Kick(new Vector2(0f, -0.0625f), 0.12f);
        Assert.That(camera.KickOffset, Is.EqualTo(new Vector2(0f, -0.0625f)), "A stronger one replaces it.");
        camera.AdvanceKick(0f);
        Assert.That(camera.AdvanceKick(0.12f), Is.EqualTo(Vector2.zero));
        Assert.That(camera.AdvanceKick(0.1f), Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// The impact and death puff recipes bake the same pixels twice and exactly what their written sheets hold; their
    /// sprite sets hold every frame at the recipe's size and pivot; the impact has a variant per damage type, every
    /// frame off its edge and a core on its middle in its first; the puff pops white over its feet first.
    /// </summary>
    [Test]
    public void TheEffectRecipes_BakeWhatIsWritten()
    {
        foreach (string filter in new[] { "t:" + nameof(ImpactEffectRecipe), "t:" + nameof(PuffEffectRecipe) })
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            Assert.That(guids.Length, Is.EqualTo(1), filter);
            EffectRecipe recipe = AssetDatabase.LoadAssetAtPath<EffectRecipe>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(recipe.CanGenerate(out string problem), Is.True, problem);
            Color32[] sheet = EffectSheetWriter.Bake(recipe);
            Assert.That(EffectSheetWriter.Bake(recipe), Is.EqualTo(sheet), recipe.name);
            int width = recipe.FrameWidth * recipe.FrameCount;
            int height = recipe.FrameHeight * recipe.VariantCount * recipe.DirectionCount;
            string path = EffectSheetWriter.SheetPath(recipe);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(TerrainMaterialWriter.EncodePng(width, height, sheet)), path);

            EffectSprites sprites = AssetDatabase.LoadAssetAtPath<EffectSprites>(EffectSheetWriter.SpritesPath(recipe));
            Assert.That(sprites, Is.Not.Null);
            Assert.That(sprites.VariantCount, Is.EqualTo(recipe.VariantCount));
            Assert.That(sprites.DirectionCount, Is.EqualTo(recipe.DirectionCount));
            Assert.That(sprites.FrameCount, Is.EqualTo(recipe.FrameCount));
            Assert.That(sprites.FrameSeconds, Is.EqualTo(recipe.FrameSeconds));
            Vector2 pivot = new(recipe.Pivot.x * recipe.FrameWidth, recipe.Pivot.y * recipe.FrameHeight);
            for (int variant = 0; variant < recipe.VariantCount; variant++)
            {
                for (int direction = 0; direction < recipe.DirectionCount; direction++)
                {
                    for (int frame = 0; frame < recipe.FrameCount; frame++)
                    {
                        Sprite sprite = sprites.Get(variant, direction, frame);
                        Assert.That(sprite, Is.Not.Null, $"{recipe.name} {variant} {direction} {frame}");
                        Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(recipe.FrameWidth, recipe.FrameHeight)));
                        Assert.That(sprite.pivot, Is.EqualTo(pivot));
                        Assert.That(sprite.pixelsPerUnit, Is.EqualTo(32f));
                    }
                }
            }

            if (recipe is ImpactEffectRecipe impact)
            {
                Assert.That(impact.VariantCount, Is.EqualTo(System.Enum.GetValues(typeof(DamageType)).Length));
                for (int variant = 0; variant < impact.VariantCount; variant++)
                {
                    for (int direction = 0; direction < impact.DirectionCount; direction++)
                    {
                        for (int frame = 0; frame < impact.FrameCount; frame++)
                        {
                            Color32[] pixels = impact.BakeFrame(variant, direction, frame);
                            int size = impact.FrameWidth;
                            bool atEdge = false;
                            for (int i = 0; i < pixels.Length; i++)
                            {
                                int x = i % size;
                                int y = i / size;
                                atEdge |= pixels[i].a > 0 && (x == 0 || y == 0 || x == size - 1 || y == size - 1);
                            }

                            Assert.That(atEdge, Is.False, $"Impact {variant} {direction} {frame} reaches its edge.");

                            if (frame == 0)
                            {
                                Assert.That(pixels[size / 2 * size + size / 2].a, Is.GreaterThan(0), "The first frame's core is on the middle.");
                            }
                        }
                    }
                }
            }
            else
            {
                PuffEffectRecipe puff = (PuffEffectRecipe)recipe;
                Color32[] pop = puff.BakeFrame(0, 0, 0);
                Color32 above = pop[(puff.Feet.y + (int)puff.PopHeight) * puff.FrameWidth + puff.Feet.x];
                Assert.That(above, Is.EqualTo(new Color32(255, 255, 255, 255)), "The puff pops white over its feet.");
            }
        }
    }

    /// <summary>
    /// The player and the Weasel show hits: their hit feedback flashes the body renderer under their root through the
    /// flash shader, nudges that renderer's transform (never the root), lands impacts on the root's collider and holds
    /// through their character visuals, and each sorts as a group at its root, so a nudge never changes its sorting; the
    /// Weasel's death plays the pooled death puff, sorted where the mob stood; the settings' impact has a variant per
    /// damage type and sorts in front of the unit it hits.
    /// </summary>
    [Test]
    public void TheShippedUnitsAndSettings_AreWiredForHitFeedback()
    {
        foreach (string path in new[] { "Assets/Prefabs/Player/Player.prefab", "Assets/Prefabs/Mobs/Weasel.prefab" })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            HitFeedback feedback = prefab.GetComponent<HitFeedback>();
            Assert.That(feedback, Is.Not.Null, path);
            SerializedObject serialized = new(feedback);
            SpriteRenderer body = (SpriteRenderer)serialized.FindProperty("body").objectReferenceValue;
            Transform visuals = (Transform)serialized.FindProperty("visuals").objectReferenceValue;
            Assert.That(body, Is.Not.Null, path);
            Assert.That(body.sharedMaterial.shader.name, Is.EqualTo("TopDownRPG/Sprite Pixel Lit"), path);
            Assert.That(visuals, Is.SameAs(body.transform), path);
            Assert.That(visuals, Is.Not.SameAs(prefab.transform), path);
            Assert.That(serialized.FindProperty("bodyCollider").objectReferenceValue, Is.SameAs(prefab.GetComponent<Collider2D>()), path);
            Assert.That(serialized.FindProperty("characterVisuals").objectReferenceValue, Is.EqualTo(prefab.GetComponentInChildren<CharacterVisuals>()), path);
            Assert.That(prefab.GetComponent<SortingGroup>(), Is.Not.Null, $"{path} sorts as a group at its root.");
        }

        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        EffectPlayer death = weasel.GetComponent<DestroyMobOnDeath>().DeathEffect;
        Assert.That(death, Is.Not.Null);
        Assert.That(AssetDatabase.GetAssetPath(death.Sprites), Is.EqualTo("Assets/Sprites/Effects/DeathPuff_Smoke_Sprites.asset"));
        Assert.That(death.transform.localScale, Is.EqualTo(Vector3.one));

        string[] guids = AssetDatabase.FindAssets("t:" + nameof(HitFeedbackSettings));
        Assert.That(guids.Length, Is.EqualTo(1));
        HitFeedbackSettings shipped = AssetDatabase.LoadAssetAtPath<HitFeedbackSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        Assert.That(shipped.ImpactEffect, Is.Not.Null);
        Assert.That(shipped.ImpactEffect.Sprites.VariantCount, Is.EqualTo(System.Enum.GetValues(typeof(DamageType)).Length));
        Assert.That(shipped.ImpactEffect.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(new SerializedObject(shipped.ImpactEffect).FindProperty("sortNudge").floatValue, Is.GreaterThan(0f), "An impact sorts in front of the unit it hits.");
        Assert.That(new SerializedObject(death).FindProperty("sortNudge").floatValue, Is.Zero, "A death puff sorts where the mob stood.");
    }

    /// <summary>
    /// Runs 1.5 s of continuous sword attacks on a unit beside the attacker and an arrow every half second into a unit
    /// four units away, every hit through the hit service, with or without the presenter showing them; returns every
    /// step's attack phase, ready time, both units' health and the arrows' positions, and how many holds the units took.
    /// </summary>
    private string Duel(bool feedback, out int holds)
    {
        ManualClock time = new();
        CombatEvents duelEvents = new();
        HitService hits = TestCombat.CreateHitService(FixedGameAuthority.Authoritative, duelEvents);
        HitFeedbackPresenter shown = new(duelEvents, settings, TestCombat.Settings, new LocalPlayerTracker(), new FakeCameraKick(), effects);
        if (feedback)
        {
            shown.Start();
        }

        clock = time;
        DamageReceiver attacker = CreateUnit("Attacker", Vector2.zero, Faction.Players);
        DamageReceiver near = CreateUnit("Near", new Vector2(0.9f, 0f), Faction.Mobs, 1000);
        DamageReceiver far = CreateUnit("Far", new Vector2(4f, 0f), Faction.Mobs, 1000);
        HitFeedback[] units = { attacker.GetComponent<HitFeedback>(), near.GetComponent<HitFeedback>(), far.GetComponent<HitFeedback>() };
        CharacterVisuals visuals = attacker.GetComponentInChildren<CharacterVisuals>();
        AttackState state = new(time);
        ProjectileService projectiles = new(FixedGameAuthority.Authoritative, hits, new LineQueries(far, 0.267f), time, null);
        ProjectileSpec arrow = new(18f, 6.5f, 0.1f, 0.5f, false, null);
        Hit hit = new(1, DamageType.Physical, null);
        StringBuilder log = new();
        for (int step = 0; step < 90; step++)
        {
            if (state.CanStart && state.TryStart(0, Vector2.right, Sword, 0.35f, true, true))
            {
                visuals.PlayAction(set.Find("Slash"), Vector2.right, Sword, false);
            }

            if ((state.Advance() & AttackStep.Contact) != 0)
            {
                hits.ApplyHit(near, hit, attacker.gameObject);
            }

            if (step % 30 == 0)
            {
                projectiles.Launch(attacker.gameObject, arrow, hit, Vector2.zero, Vector2.right, default);
            }

            projectiles.Advance(Step);
            visuals.Tick();
            foreach (HitFeedback unit in units)
            {
                unit.Tick(Step);
            }

            log.Append(state.Phase).Append('|').Append(state.ReadyTime.ToString("R")).Append('|').Append(near.Health.CurrentHealth).Append('|').Append(far.Health.CurrentHealth);
            for (int i = 0; i < projectiles.FlyingCount; i++)
            {
                log.Append('|').Append(projectiles.Flying(i).Position.x.ToString("R"));
            }

            log.Append('\n');
            time.Advance(Step);
        }

        shown.Dispose();
        holds = units[0].HoldCount + units[1].HoldCount + units[2].HoldCount;
        Assert.That(near.Health.CurrentHealth, Is.LessThan(1000));
        Assert.That(far.Health.CurrentHealth, Is.LessThan(1000));
        if (feedback)
        {
            Assert.That(units[0].HoldCount, Is.GreaterThan(0), "The attacker held its contacts.");
            Assert.That(units[2].FlashCount, Is.GreaterThan(0), "The arrows' hits flashed.");
        }

        return log.ToString();
    }

    /// <summary>
    /// Asserts <paramref name="struck"/>'s sprite sits <paramref name="x"/>, <paramref name="y"/> whole pixels off its
    /// rest place.
    /// </summary>
    private static void AssertOffset(HitFeedback struck, int x, int y)
    {
        Assert.That(struck.NudgeOffset.x, Is.EqualTo(x / 32f).Within(1e-5f));
        Assert.That(struck.NudgeOffset.y, Is.EqualTo(y / 32f).Within(1e-5f));
    }

    /// <summary>
    /// Asserts nothing but a flash showed: no hold, nudge, impact or camera kick.
    /// </summary>
    private void AssertNothingElse(HitFeedback struck)
    {
        Assert.That(struck.HoldCount, Is.Zero);
        Assert.That(struck.NudgeCount, Is.Zero);
        Assert.That(played, Is.Empty);
        Assert.That(cameraKick.Count, Is.Zero);
    }

    /// <summary>
    /// The cross product of <paramref name="point"/> and <paramref name="towards"/> on the plane: zero when the point lies
    /// on the line from the origin through <paramref name="towards"/>.
    /// </summary>
    private static float Cross(Vector2 point, Vector2 towards)
    {
        return point.x * towards.y - point.y * towards.x;
    }

    /// <summary>
    /// A unit of <paramref name="faction"/> at <paramref name="position"/>: health (<paramref name="maxHealth"/>), a
    /// receiver, the Weasel's footprint collider and a sprite child on its feet with character visuals on the manual
    /// clock, which its hit feedback flashes, nudges and holds.
    /// </summary>
    private DamageReceiver CreateUnit(string name, Vector2 position, Faction faction, int maxHealth = 100)
    {
        GameObject unit = Track(new GameObject(name));
        unit.transform.position = position;
        Health health = unit.AddComponent<Health>();
        SerializedObject healthSettings = new(health);
        healthSettings.FindProperty("maxHealth").intValue = maxHealth;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        receiver.SetProfile(Track(CombatProfile.Create(faction)));
        BoxCollider2D body = unit.AddComponent<BoxCollider2D>();
        body.size = new Vector2(0.534f, 0.3f);
        body.offset = new Vector2(0f, 0.15f);
        GameObject sprite = new("Visuals");
        sprite.transform.SetParent(unit.transform, false);
        SpriteRenderer renderer = sprite.AddComponent<SpriteRenderer>();
        renderer.sprite = CreateSprite(name);
        CharacterVisuals visuals = sprite.AddComponent<CharacterVisuals>();
        SerializedObject serialized = new(visuals);
        serialized.FindProperty("animationSet").objectReferenceValue = set;
        serialized.FindProperty("locomotion").objectReferenceValue = locomotion;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        visuals.Construct(clock);

        unit.AddComponent<HitFeedback>().Configure(renderer, sprite.transform, visuals, body, 0.7f);
        Physics2D.SyncTransforms();
        return receiver;
    }

    /// <summary>
    /// A player unit (a <see cref="PlayerController"/> on a unit of the players' faction with character visuals) at
    /// <paramref name="position"/>.
    /// </summary>
    private DamageReceiver CreatePlayer(string name, Vector2 position)
    {
        DamageReceiver player = CreateUnit(name, position, Faction.Players);
        player.gameObject.AddComponent<PlayerController>();
        return player;
    }

    /// <summary>
    /// A hidden effect "prefab" (a scene object the pool copies) of <paramref name="variants"/> variants in
    /// <paramref name="directions"/> directions of <paramref name="frames"/> frames of 0.04 s.
    /// </summary>
    private EffectPlayer CreateEffectPrefab(int variants, int directions, int frames)
    {
        Sprite[] sprites = new Sprite[variants * directions * frames];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = CreateSprite("Effect" + i);
        }

        EffectSprites effectSprites = Track(EffectSprites.Create(sprites, variants, directions, frames, 0.04f));
        GameObject root = Track(new GameObject("ImpactPrefab"));
        GameObject drawn = new("Sprite");
        drawn.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = drawn.AddComponent<SpriteRenderer>();
        EffectPlayer effect = root.AddComponent<EffectPlayer>();
        effect.Configure(effectSprites, renderer);
        renderer.enabled = false;
        return effect;
    }

    /// <summary>
    /// A clip named <paramref name="name"/> of the given frame times, a sprite of its own for every direction and
    /// frame, with its contact on <paramref name="contact"/> (-1 for none).
    /// </summary>
    private CharacterAnimationClip Clip(string name, CharacterGait gait, float[] frameSeconds, int contact)
    {
        Sprite[] sprites = new Sprite[CharacterDirections.Count * frameSeconds.Length];
        for (int i = 0; i < sprites.Length; i++)
        {
            sprites[i] = CreateSprite($"{name}_{i}");
        }

        return contact >= 0
            ? CharacterAnimationClip.Create(name, gait, true, sprites, frameSeconds, 0f, contact)
            : CharacterAnimationClip.Create(name, gait, false, sprites, frameSeconds, 0f);
    }

    /// <summary>
    /// A sprite of its own over a small texture.
    /// </summary>
    private Sprite CreateSprite(string name)
    {
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite sprite = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), Vector2.zero, 32f));
        sprite.name = name;
        return sprite;
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
    /// <see cref="IProjectileQueries"/> with one unit on the x axis: a projectile flying right meets it where its
    /// sweep touches the unit's face, <paramref name="radius"/> left of its position.
    /// </summary>
    private sealed class LineQueries : IProjectileQueries
    {
        private readonly DamageReceiver unit;
        private readonly float radius;

        /// <summary>
        /// Places <paramref name="target"/> with a face <paramref name="halfWidth"/> left of its position.
        /// </summary>
        public LineQueries(DamageReceiver target, float halfWidth)
        {
            unit = target;
            radius = halfWidth;
        }

        /// <summary>
        /// The unit, when a step flying right crosses its face.
        /// </summary>
        public void Cast(Vector2 origin, Vector2 direction, float distance, float sweep, List<ProjectileContact> results)
        {
            results.Clear();
            float face = unit.transform.position.x - radius - sweep;
            if (direction.x > 0f && origin.x <= face && origin.x + direction.x * distance >= face)
            {
                results.Add(new ProjectileContact((face - origin.x) / direction.x, unit));
            }
        }
    }
}
