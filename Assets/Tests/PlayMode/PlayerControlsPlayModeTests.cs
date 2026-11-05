using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using VContainer;

/// <summary>
/// PlayMode tests for the click-to-move controls in Testlands, the booted test world, driven by scripted commands:
/// pathing, attack orders (a bow walking round a wall before it shoots), the attack timeline (the contact frame showing as the hit lands, standing still through the
/// windup, a move cancelling it, death ending it), attack moves, player overlap, the ability and status HUD, the dev
/// status panel and death.
/// </summary>
public class PlayerControlsPlayModeTests
{
    private const float WalkTimeoutSeconds = 10f;

    private IObjectResolver gameplay;
    private PlayerController player;
    private INavigation2D navigation;
    private Scene startChunk;
    private NavigationGrid2D chunkGrid;
    private TerrainMovementProfile2D profile;
    private ScriptedCommands commands;

    /// <summary>
    /// After booting into Testlands, the player paths on its world's navigation, which has the start chunk's page.
    /// </summary>
    [UnityTest]
    public IEnumerator EnteringTestlands_GivesThePlayerItsWorldsNavigation()
    {
        yield return Boot();

        Assert.That(navigation, Is.Not.Null);
        Assert.That(gameplay.Resolve<PlayerNavigation>().Of(player.transform), Is.SameAs(navigation));
        Assert.That(chunkGrid, Is.Not.Null);
        Assert.That(navigation.IsCellWalkable(navigation.WorldToCell(player.transform.position), profile), Is.True);
    }

    /// <summary>
    /// A move click behind a runtime-built wall gives a path with several waypoints around it, and the player arrives
    /// at the goal.
    /// </summary>
    [UnityTest]
    public IEnumerator RightClickBehindAWall_PathsAroundIt()
    {
        yield return Boot();
        DisableMobs();
        Vector2 goal = BuildWallInFrontOfThePlayer();

        commands.Click(goal, default);
        yield return null;

        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(player.GetComponent<PlayerMotor2D>().Follower.Waypoints.Count, Is.GreaterThan(1), "The path should turn around the wall.");
        yield return WaitUntilIdle();

        Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.2f), "The player should arrive behind the wall.");
    }

    /// <summary>
    /// Clicking a mob behind a wall picks it as the target and gives an attack order that walls and distance do not
    /// cancel; the order kills the mob and ends with it.
    /// </summary>
    [UnityTest]
    public IEnumerator RightClickOnAMobBehindAWall_PathsAroundAndKillsIt()
    {
        yield return Boot();
        MobController mob = MobOfTheStartChunk();
        Assert.That(mob, Is.Not.Null);
        foreach (MobController other in Object.FindObjectsByType<MobController>())
        {
            other.gameObject.SetActive(other == mob);
        }

        Health mobHealth = mob.GetComponent<Health>();
        Vector2 hidingSpot = BuildWallInFrontOfThePlayer();
        Rigidbody2D mobBody = mob.GetComponent<Rigidbody2D>();
        mobBody.position = hidingSpot;
        mob.transform.position = hidingSpot;
        Physics2D.SyncTransforms();

        UnitTarget target = gameplay.Resolve<PointerTargetPicker>().Pick(hidingSpot + Vector2.up * 0.15f);
        Assert.That(target.Health, Is.SameAs(mobHealth), "Clicking the mob should pick it.");
        commands.Click(hidingSpot, target);
        yield return null;
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack));

        float deadline = Time.realtimeSinceStartup + 20f;
        while (mobHealth != null && !mobHealth.IsDead)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack order should kill the mob.");
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack), "Walls and distance must not cancel the order.");
            yield return null;
        }

        yield return null;
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Idle), "The order ends with its target.");
    }

    /// <summary>
    /// A longbow ordered at a mob within its range but behind a wall does not shoot into the wall: the player walks
    /// round the wall's end until nothing lies between them, and only then shoots, its first arrow reaching the mob.
    /// </summary>
    [UnityTest]
    public IEnumerator ABowOrderedAtAMobBehindAWall_WalksRoundItBeforeItShoots()
    {
        yield return Boot();
        MobController mob = MobOfTheStartChunk();
        Assert.That(mob, Is.Not.Null);
        foreach (MobController other in Object.FindObjectsByType<MobController>())
        {
            other.gameObject.SetActive(other == mob);
        }

        mob.enabled = false;
        Vector2 hidingSpot = BuildWallInFrontOfThePlayer();
        MoveMob(mob, hidingSpot);
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        PlayerWeapon bow = RangedWeaponOf(weapon.Catalog);
        weapon.Equip(bow);
        Collider2D mobCollider = mob.GetComponent<Collider2D>();
        Health mobHealth = mob.GetComponent<Health>();
        Vector2 start = player.transform.position;
        Assert.That(player.GetComponent<Collider2D>().Distance(mobCollider).distance, Is.LessThan(bow.AttackRange), "The mob stands within the bow's range.");
        Assert.That(WallBetween(start, mobCollider.bounds.center), Is.True, "The wall lies between them.");

        int attacks = 0;
        Vector2 shotFrom = default;
        System.Action<int, Vector2, float> onStarted = (_, _, _) =>
        {
            if (attacks++ == 0)
            {
                shotFrom = player.transform.position;
            }
        };
        weapon.AttackStarted += onStarted;
        try
        {
            commands.Click(hidingSpot, PickMob(mob));
            yield return null;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (mobHealth.CurrentHealth == mobHealth.MaxHealth)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The bow should hit the mob.");
                Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack), "Walls and distance must not cancel the order.");
                yield return null;
            }

            Assert.That(attacks, Is.EqualTo(1), "The first arrow reaches the mob.");
            Assert.That(Vector2.Distance(shotFrom, start), Is.GreaterThan(1f), "The player walks before it shoots.");
            Assert.That(WallBetween(shotFrom, mobCollider.bounds.center), Is.False, "It shoots once nothing lies between them.");
        }
        finally
        {
            weapon.AttackStarted -= onStarted;
        }
    }

    /// <summary>
    /// An attack move past a mob standing beside the way engages it, kills it, then walks on to its point and ends
    /// there.
    /// </summary>
    [UnityTest]
    public IEnumerator AttackMove_PastAMob_KillsIt_ThenWalksOnToItsPoint()
    {
        yield return Boot();
        MobController mob = MobOfTheStartChunk();
        Assert.That(mob, Is.Not.Null);
        foreach (MobController other in Object.FindObjectsByType<MobController>())
        {
            other.gameObject.SetActive(other == mob);
        }

        Vector2 start = player.transform.position;
        Vector2 goal = FindClearGoal(start, 5);
        Vector2 along = (goal - start).normalized;
        Vector2 roadside = Vector2.Lerp(start, goal, 0.6f) + new Vector2(along.y, along.x);
        mob.GetComponent<Rigidbody2D>().position = roadside;
        mob.transform.position = roadside;
        Physics2D.SyncTransforms();
        Health mobHealth = mob.GetComponent<Health>();

        commands.AttackMove(goal);
        yield return null;
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.AttackMove));

        float deadline = Time.realtimeSinceStartup + 20f;
        while (mobHealth != null && !mobHealth.IsDead)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack move should kill the mob on its way.");
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.AttackMove));
            yield return null;
        }

        yield return WaitUntilIdle();
        Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.2f), "The player should walk on to the point.");
    }

    /// <summary>
    /// An attack order on a mob beside the player swings the sword: the hit lands no sooner than the windup after the
    /// attack's start, and the frame it lands on shows the attack clip's contact frame in the attack's direction.
    /// </summary>
    [UnityTest]
    public IEnumerator AnAttack_ShowsItsContactFrame_WhenItsHitLands()
    {
        yield return Boot();
        MobController mob = PlaceAMobBesideThePlayer(out _);
        Health mobHealth = mob.GetComponent<Health>();
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        CharacterVisuals visuals = player.GetComponentInChildren<CharacterVisuals>();
        SpriteRenderer renderer = visuals.GetComponent<SpriteRenderer>();
        CharacterAnimationClip clip = visuals.AnimationSet.Find(weapon.CurrentWeapon.AttackClip);
        float started = -1f;
        float landed = -1f;
        System.Action<int, Vector2, float> onStarted = (_, _, _) => started = started < 0f ? Time.time : started;
        System.Action<Health.DamageEvent> onDamaged = _ => landed = landed < 0f ? Time.time : landed;
        weapon.AttackStarted += onStarted;
        mobHealth.Damaged += onDamaged;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (landed < 0f)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack should land.");
                yield return null;
            }

            int direction = visuals.Animator.Direction;
            yield return null;
            Assert.That(renderer.sprite, Is.EqualTo(clip.SpriteAt(direction, clip.ContactFrame)), "The frame the hit lands on shows the contact frame.");
            Assert.That(landed - started, Is.GreaterThanOrEqualTo(weapon.CurrentWeapon.Timeline.Windup - 0.0001f), "The hit lands at contact, not as the attack starts.");
        }
        finally
        {
            weapon.AttackStarted -= onStarted;
            mobHealth.Damaged -= onDamaged;
        }
    }

    /// <summary>
    /// The player stands still through an attack's windup even when its target walks out of range (here moved away,
    /// with a sword whose windup is slow enough to watch), and chases it once the hit has passed.
    /// </summary>
    [UnityTest]
    public IEnumerator ThePlayer_StandsStillDuringTheWindup_ThenChases()
    {
        yield return Boot();
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        PlayerWeapon slowSword = SlowSword(weapon.CurrentWeapon);
        weapon.Equip(slowSword);
        MobController mob = PlaceAMobBesideThePlayer(out Vector2 along);
        Health mobHealth = mob.GetComponent<Health>();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!weapon.IsAttacking)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack should start.");
                yield return null;
            }

            Vector2 standing = body.position;
            MoveMob(mob, standing + along * 3f);
            int framesInWindup = 0;
            while (weapon.AttackPhase == AttackPhase.Windup)
            {
                Assert.That(Vector2.Distance(body.position, standing), Is.LessThan(0.001f), "The player stands still through the windup.");
                Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack));
                framesInWindup++;
                yield return null;
            }

            Assert.That(framesInWindup, Is.GreaterThan(1));
            Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth), "The mob left the hit area before contact.");
            deadline = Time.realtimeSinceStartup + 5f;
            while (Vector2.Distance(body.position, standing) < 0.3f)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The player should chase once the hit has passed.");
                yield return null;
            }
        }
        finally
        {
            Object.Destroy(slowSword);
        }
    }

    /// <summary>
    /// A move click during the windup cancels the attack: the mob takes no damage, the character stops showing the
    /// attack and the player takes the move order.
    /// </summary>
    [UnityTest]
    public IEnumerator AMoveOrder_CancelsTheWindup_AndTheMobTakesNoDamage()
    {
        yield return Boot();
        MobController mob = PlaceAMobBesideThePlayer(out Vector2 along);
        Health mobHealth = mob.GetComponent<Health>();
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        CharacterAnimator animation = player.GetComponentInChildren<CharacterVisuals>().Animator;
        Vector2 goal = (Vector2)player.transform.position + along * 4f;
        int cancelled = 0;
        System.Action<int, Vector2, float> moveAway = (_, _, _) => commands.Click(goal, default);
        System.Action onCancelled = () => cancelled++;
        weapon.AttackStarted += moveAway;
        weapon.AttackCancelled += onCancelled;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (cancelled == 0)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The move click should cancel the windup.");
                yield return null;
            }

            weapon.AttackStarted -= moveAway;
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
            Assert.That(weapon.IsAttacking, Is.False);
            yield return null;
            Assert.That(animation.IsActing, Is.False, "The character stops showing the attack.");
            yield return new WaitForSeconds(weapon.CurrentWeapon.Timeline.Duration + 0.3f);
            Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth), "Nothing lands.");
        }
        finally
        {
            weapon.AttackStarted -= moveAway;
            weapon.AttackCancelled -= onCancelled;
        }
    }

    /// <summary>
    /// A player that dies during an attack's windup ends the attack: nothing lands and the character stops showing it.
    /// </summary>
    [UnityTest]
    public IEnumerator Death_EndsARunningAttack()
    {
        yield return Boot();
        PlayerWeaponController weapon = player.GetComponent<PlayerWeaponController>();
        PlayerWeapon slowSword = SlowSword(weapon.CurrentWeapon);
        weapon.Equip(slowSword);
        MobController mob = PlaceAMobBesideThePlayer(out _);
        Health mobHealth = mob.GetComponent<Health>();
        CharacterAnimator animation = player.GetComponentInChildren<CharacterVisuals>().Animator;
        try
        {
            commands.Click(mob.transform.position, PickMob(mob));
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!weapon.IsAttacking)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The attack should start.");
                yield return null;
            }

            Health health = player.GetComponent<Health>();
            health.ApplyDamage(health.MaxHealth);
            yield return null;
            Assert.That(weapon.IsAttacking, Is.False);
            Assert.That(animation.IsActing, Is.False);
            yield return new WaitForSeconds(slowSword.Timeline.Windup + 0.2f);
            Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealth.MaxHealth), "Nothing lands.");
        }
        finally
        {
            Object.Destroy(slowSword);
        }
    }

    /// <summary>
    /// The player walks straight through a body on the Player layer standing on its path, without pushing it.
    /// </summary>
    [UnityTest]
    public IEnumerator Players_WalkThroughEachOther()
    {
        yield return Boot();
        DisableMobs();
        Vector2 start = player.transform.position;
        Vector2 goal = FindClearGoal(start, 3);
        Vector2 middle = Vector2.Lerp(start, goal, 0.5f);
        GameObject teammate = new("TeammateBody") { layer = LayerMask.NameToLayer("Player") };
        teammate.transform.position = middle;
        teammate.AddComponent<BoxCollider2D>().size = new Vector2(0.35f, 0.15f);
        Rigidbody2D teammateBody = teammate.AddComponent<Rigidbody2D>();
        teammateBody.gravityScale = 0f;
        teammateBody.freezeRotation = true;

        try
        {
            commands.Click(goal, default);
            yield return null;
            Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
            yield return WaitUntilIdle();

            Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.2f), "The player should walk straight through its teammate.");
            Assert.That(Vector2.Distance(teammateBody.position, middle), Is.LessThan(0.01f), "The teammate should not be pushed.");
        }
        finally
        {
            Object.Destroy(teammate);
        }
    }

    /// <summary>
    /// The ability bar has one slot per ability labelled with its name and bound key and shows the cooldown fill and
    /// seconds after a cast; the pointer feedback layer has prewarmed move markers and the attack cursor hidden.
    /// </summary>
    [UnityTest]
    public IEnumerator Hud_ShowsTheLoadoutAndCooldowns_AndPointerFeedbackIsReady()
    {
        yield return Boot();
        AbilitySlotView[] slots = Object.FindAnyObjectByType<AbilityBarView>().GetComponentsInChildren<AbilitySlotView>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();

        Assert.That(slots.Length, Is.EqualTo(AbilitySlots.Count));
        for (int i = 0; i < slots.Length; i++)
        {
            Assert.That(slots[i].NameText.text, Is.EqualTo(abilities.GetAbility(i).DisplayName), "Blanks have no icon, so their names show.");
            Assert.That(slots[i].KeyText.text, Is.Not.Empty, "Each slot is labelled with its bound key.");
        }

        Assert.That(abilities.TryCast(0, new CastAim(Vector2.zero, Vector2.right, default)), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(0);
        yield return null;
        yield return null;

        Assert.That(slots[0].CooldownFill.fillAmount, Is.GreaterThan(0.5f));
        Assert.That(slots[0].CooldownText.text, Is.EqualTo(Mathf.CeilToInt(abilities.RemainingCooldown(0)).ToString()));

        PointerFeedbackLayer feedback = Object.FindAnyObjectByType<PointerFeedbackLayer>();
        Assert.That(feedback, Is.Not.Null);
        Assert.That(feedback.PooledMarkerCount, Is.GreaterThan(0), "Move markers are prewarmed.");
        Assert.That(feedback.AttackCursorShown, Is.False);
    }

    /// <summary>
    /// The player's statuses show on the HUD bar with buffs first and a Burn visual that follows the player; a mob's
    /// debuff shows above its health bar; clearing the player's statuses hides the icons and pools the visual.
    /// </summary>
    [UnityTest]
    public IEnumerator Statuses_ShowOnTheHud_OnTheMobsBar_AndAsVisuals()
    {
        yield return Boot();
        StatusEffectService statuses = SceneBootTestHelper.ResolveFromGameplay<StatusEffectService>();
        StatusEffectDefinition burn = SceneBootTestHelper.FindStatus("Burn");
        StatusEffectDefinition fortify = SceneBootTestHelper.FindStatus("Fortify");
        StatusBarView bar = Object.FindAnyObjectByType<StatusBarView>();
        Assert.That(bar, Is.Not.Null);

        Assert.That(statuses.Apply(player.GetComponent<DamageReceiver>(), fortify, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(statuses.Apply(player.GetComponent<DamageReceiver>(), burn), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return null;
        Assert.That(bar.Icon(0).gameObject.activeSelf && bar.Icon(0).Shown == fortify, Is.True, "Buffs come first on the HUD.");
        Assert.That(bar.Icon(1).gameObject.activeSelf && bar.Icon(1).Shown == burn, Is.True);
        Assert.That(bar.Icon(2).gameObject.activeSelf, Is.False);
        Assert.That(player.GetComponent<StatusVisuals>().ShownCount, Is.EqualTo(1), "Burn has a visual, Fortify does not.");
        StatusVisual aura = player.GetComponent<StatusVisuals>().GetShown(0);
        Assert.That(Vector2.Distance(aura.transform.position, player.transform.position), Is.LessThan(0.01f), "The visual follows the player.");

        MobController mob = MobOfTheStartChunk();
        Assert.That(statuses.Apply(mob.GetComponent<DamageReceiver>(), burn, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return null;
        Assert.That(mob.GetComponentInChildren<WorldStatusIcons>().ShownCount, Is.EqualTo(1), "The mob shows its debuff above its health bar.");

        statuses.ClearAll(player.GetComponent<DamageReceiver>());
        yield return null;
        Assert.That(bar.Icon(0).gameObject.activeSelf, Is.False);
        Assert.That(aura.gameObject.activeSelf, Is.False, "The visual returns to the pool.");
    }

    /// <summary>
    /// The hidden dev status panel applies statuses to the player and to the unit under a screen point (Invalid when
    /// there is none); Blank Smite deals 18 to a Vulnerable mob and stuns it, and Blank Mend heals 25 and fortifies.
    /// </summary>
    [UnityTest]
    public IEnumerator DevPanel_AppliesStatuses_AndSmiteAndMendHaveEffects()
    {
        yield return Boot();
        StatusDebugPanel panel = Object.FindAnyObjectByType<StatusDebugPanel>();
        Assert.That(panel, Is.Not.Null, "Every play session gets the dev status panel.");
        Assert.That(panel.IsOpen, Is.False, "It starts hidden.");

        StatusEffectDefinition chill = SceneBootTestHelper.FindStatus("Chill");
        Assert.That(panel.ApplyToSelf(chill), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(player.GetComponent<StatusEffects>().GetStacks(chill), Is.EqualTo(1));

        MobController mob = MobOfTheStartChunk();
        mob.enabled = false;
        Collider2D mobCollider = mob.GetComponent<Collider2D>();
        Vector2 mobScreenPoint = gameplay.Resolve<Camera>().WorldToScreenPoint(mobCollider.bounds.center);
        Assert.That(panel.ApplyToUnitAt(mobScreenPoint, SceneBootTestHelper.FindStatus("Vulnerable")), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(panel.ApplyToUnitAt(mobScreenPoint + new Vector2(5000f, 0f), chill), Is.EqualTo(StatusApplyOutcome.Invalid), "Nothing under the cursor.");

        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        Health mobHealth = mob.GetComponent<Health>();
        CastAim atMob = new(mob.transform.position, Vector2.right, new UnitTarget(mobHealth, mobCollider, UnitTeam.Enemy));
        Assert.That(abilities.GetAbility(3).DisplayName, Is.EqualTo("Blank Smite"));
        int mobHealthBefore = mobHealth.CurrentHealth;
        Assert.That(abilities.TryCast(3, atMob), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(3);
        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(mobHealthBefore - 18), "15 damage, 1.2x on a Vulnerable mob.");
        Assert.That(mob.IsStunned, Is.True, "Smite stuns.");

        Health playerHealth = player.GetComponent<Health>();
        int playerHealthBefore = playerHealth.CurrentHealth;
        gameplay.Resolve<DamageService>().ApplyDamage(player.GetComponent<DamageReceiver>(), 40);
        CastAim atSelf = new(player.transform.position, Vector2.zero, new UnitTarget(playerHealth, player.GetComponent<Collider2D>(), UnitTeam.Ally));
        Assert.That(abilities.GetAbility(2).DisplayName, Is.EqualTo("Blank Mend"));
        Assert.That(abilities.TryCast(2, atSelf), Is.EqualTo(CastOutcome.Started));
        abilities.CompleteCast(2);
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(playerHealthBefore - 15), "Mend heals 25.");
        Assert.That(player.GetComponent<StatusEffects>().GetStacks(SceneBootTestHelper.FindStatus("Fortify")), Is.EqualTo(1), "And fortifies.");
        yield return null;
    }

    /// <summary>
    /// A player that dies mid-move is left with no order, no path and no movement.
    /// </summary>
    [UnityTest]
    public IEnumerator Death_ClearsTheOrders()
    {
        yield return Boot();
        DisableMobs();
        commands.Click((Vector2)player.transform.position + Vector2.right * 50f, default);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));

        Health health = player.GetComponent<Health>();
        health.ApplyDamage(health.MaxHealth);
        yield return null;

        PlayerMotor2D motor = player.GetComponent<PlayerMotor2D>();
        Assert.That(player.CurrentOrder, Is.EqualTo(PlayerOrderKind.Idle));
        Assert.That(motor.HasPath, Is.False);
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// Boots into Testlands, caches the gameplay container, local player, its world's navigation, the scene and grid of
    /// the chunk it stands in and its movement profile, and switches the player to scripted commands.
    /// </summary>
    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(gameplay.Resolve<Worlds>().TryGet(world, out LoadedWorld loaded), Is.True);
        navigation = loaded.Navigation;
        startChunk = loaded.Chunks[ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(player.transform.position)))];
        chunkGrid = SceneQuery.FindFirst<NavigationGrid2D>(startChunk);
        profile = player.ControlSettings.MovementProfile;
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
    }

    /// <summary>
    /// Waits until the player has no order, failing the test after the walk timeout.
    /// </summary>
    private IEnumerator WaitUntilIdle()
    {
        float deadline = Time.realtimeSinceStartup + WalkTimeoutSeconds;
        while (player.CurrentOrder != PlayerOrderKind.Idle)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The move order should finish.");
            yield return null;
        }
    }

    /// <summary>
    /// The first mob whose home is the chunk the player started in, which lives in that chunk's scene.
    /// </summary>
    private MobController MobOfTheStartChunk()
    {
        return SceneQuery.FindFirst<MobController>(startChunk);
    }

    /// <summary>
    /// Keeps only the first mob of the start chunk active, with its AI off, and stands it just over half a unit from the
    /// player toward open ground, within the sword's range; <paramref name="along"/> is that direction.
    /// </summary>
    private MobController PlaceAMobBesideThePlayer(out Vector2 along)
    {
        MobController mob = MobOfTheStartChunk();
        Assert.That(mob, Is.Not.Null);
        foreach (MobController other in Object.FindObjectsByType<MobController>())
        {
            other.gameObject.SetActive(other == mob);
        }

        mob.enabled = false;
        Vector2 start = player.transform.position;
        along = (FindClearGoal(start, 4) - start).normalized;
        MoveMob(mob, start + along * 0.55f);
        return mob;
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
    /// Puts <paramref name="mob"/>'s body and transform at <paramref name="position"/>.
    /// </summary>
    private static void MoveMob(MobController mob, Vector2 position)
    {
        mob.GetComponent<Rigidbody2D>().position = position;
        mob.transform.position = position;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// A runtime copy of <paramref name="sword"/>'s hit with a windup slow enough to watch frame by frame (0.6 s, then
    /// 0.1 s of contact and 0.2 s of recovery, a 1 s cooldown), for the test to destroy.
    /// </summary>
    private static PlayerWeapon SlowSword(PlayerWeapon sword)
    {
        Vector2[] offsets = { sword.HitOffset(Vector2.down), sword.HitOffset(Vector2.up), sword.HitOffset(Vector2.left), sword.HitOffset(Vector2.right) };
        return PlayerWeapon.Create(
            "Slow Sword",
            sword.Damage,
            1f,
            sword.HitDistance,
            offsets,
            range: sword.AttackRange,
            size: sword.HitSize,
            windup: 0.6f,
            contact: 0.1f,
            recovery: 0.2f,
            attackClipName: sword.AttackClip);
    }

    /// <summary>
    /// The first weapon of <paramref name="catalog"/> that fires projectiles (the longbow).
    /// </summary>
    private static PlayerWeapon RangedWeaponOf(WeaponCatalog catalog)
    {
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.Get(i).FiresProjectile)
            {
                return catalog.Get(i);
            }
        }

        Assert.Fail("The weapon catalog should hold a ranged weapon.");
        return null;
    }

    /// <summary>
    /// Whether a collider on the Obstacles layer, a wall, lies on the line from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    private static bool WallBetween(Vector2 from, Vector2 to)
    {
        return Physics2D.Linecast(from, to, LayerMask.GetMask("Obstacles")).collider != null;
    }

    /// <summary>
    /// Deactivates every mob so none attacks or blocks the player during the test.
    /// </summary>
    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Builds a seven-cell collision wall two cells to the right of the player through its chunk's
    /// <see cref="AreaTerrain"/> and returns a point straight behind it. The chunk's grid picks the new tiles up by
    /// itself, and the wall's render tilemap draws over the blended ground.
    /// </summary>
    private Vector2 BuildWallInFrontOfThePlayer()
    {
        Tilemap collision = chunkGrid.BlockingTilemaps.Count > 0 ? chunkGrid.BlockingTilemaps[0] : null;
        Assert.That(collision, Is.Not.Null, "The start chunk should have a collision tilemap.");
        AreaTerrain terrain = SceneQuery.FindFirst<AreaTerrain>(startChunk);
        Vector3Int start = navigation.WorldToCell(player.transform.position);
        Vector3Int[] wall = new Vector3Int[7];
        for (int dy = -3; dy <= 3; dy++)
        {
            wall[dy + 3] = new Vector3Int(start.x + 2, start.y + dy, start.z);
        }

        Assert.That(terrain.TrySetCells(collision, wall, filled: true), Is.True);
        TilemapCollider2D wallCollider = collision.GetComponent<TilemapCollider2D>();
        wallCollider.ProcessTilemapChanges();
        Assert.That(wallCollider.shapeCount, Is.GreaterThanOrEqualTo(wall.Length), "Every wall cell gets a grid collider.");
        DualGridTilemapModule wallArt = collision.GetComponent<DualGridTilemapModule>();
        Assert.That(wallArt, Is.Not.Null, "The collision tilemap is a dual-grid wall tilemap.");
        Assert.That(wallArt.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(wall[0], 3)), Is.True, "Its render tilemap draws the new wall.");
        Assert.That(wallArt.RenderTilemap.gameObject.layer, Is.Not.EqualTo(collision.gameObject.layer), "The wall art is on a layer the camera draws.");
        TilemapRenderer wallRenderer = wallArt.RenderTilemap.GetComponent<TilemapRenderer>();
        TerrainBlendRenderer ground = chunkGrid.GetComponent<TerrainBlendRenderer>();
        Assert.That(ground, Is.Not.Null, "The chunk's ground is the blended terrain.");
        Assert.That(ground.Chunks, Is.Not.Empty);
        foreach (MeshRenderer chunk in ground.Chunks)
        {
            Assert.That(chunk.sortingLayerID, Is.EqualTo(wallRenderer.sortingLayerID), "The walls and the ground share the Terrain sorting layer.");
            Assert.That(chunk.sortingOrder, Is.LessThan(wallRenderer.sortingOrder), "The walls draw over the ground.");
        }

        Physics2D.SyncTransforms();

        Vector3Int goal = new(start.x + 4, start.y, start.z);
        Assert.That(navigation.IsCellWalkable(goal, profile), Is.True);
        Assert.That(navigation.TryGetLineCost(start, goal, profile, out _), Is.False, "The wall should block the straight line.");
        return navigation.CellToWorldCenter(goal);
    }

    /// <summary>
    /// The center of the first cell <paramref name="distance"/> cells right, left, up or down of
    /// <paramref name="from"/> that has a walkable straight line with walkable cells on both flanks; fails the test
    /// when there is none.
    /// </summary>
    private Vector2 FindClearGoal(Vector2 from, int distance)
    {
        Vector3Int start = navigation.WorldToCell(from);
        Vector3Int[] directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
        foreach (Vector3Int direction in directions)
        {
            Vector3Int cell = start + direction * distance;
            if (navigation.TryGetLineCost(start, cell, profile, out _) && ClearOfWalls(start, direction, distance))
            {
                return navigation.CellToWorldCenter(cell);
            }
        }

        Assert.Fail("The spawn point should have open ground on one side.");
        return from;
    }

    /// <summary>
    /// Whether the cells on both sides of the straight run from <paramref name="start"/> along
    /// <paramref name="direction"/> are walkable for its full <paramref name="distance"/>.
    /// </summary>
    private bool ClearOfWalls(Vector3Int start, Vector3Int direction, int distance)
    {
        Vector3Int side = new(direction.y, direction.x, 0);
        for (int step = 0; step <= distance; step++)
        {
            Vector3Int cell = start + direction * step;
            if (!navigation.IsCellWalkable(cell + side, profile) || !navigation.IsCellWalkable(cell - side, profile))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// <see cref="IPlayerCommandSource"/> that the test feeds one click or attack move at a time.
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
        /// Queues a single attack-move press at <paramref name="point"/> for the next read.
        /// </summary>
        public void AttackMove(Vector2 point)
        {
            next = new PlayerCommand(point, default, movePressed: false, moveHeld: false, stopPressed: false, attackMovePressed: true);
        }

        /// <summary>
        /// Returns the queued command and clears it, so each click is read once; an empty command otherwise.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
