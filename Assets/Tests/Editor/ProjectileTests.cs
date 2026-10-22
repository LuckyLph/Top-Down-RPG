using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for projectiles (Docs/Animation.md, Phase 4): <see cref="ProjectileService"/> and
/// <see cref="ProjectileFlight"/> over fake queries (the first unit only, walls, the maximum range, the faction rules,
/// homing on a target and flying on straight once it is gone, nothing landing without authority, the launch distance),
/// pooled flights and visuals allocating nothing once warm, <see cref="PhysicsProjectileQueries"/> against real
/// colliders, and <see cref="DirectionalSprites"/>.
/// </summary>
public class ProjectileTests
{
    private const float Speed = 10f;
    private const float Range = 6.5f;
    private const float LaunchDistance = 0.5f;

    private readonly List<Object> created = new();
    private FakeQueries queries;
    private GameObject shooter;

    /// <summary>
    /// Builds empty fake queries and a shooter of the players' faction at the origin.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        queries = new FakeQueries();
        shooter = CreateUnit("Shooter", Vector2.zero, Faction.Players, 0.3f).gameObject;
    }

    /// <summary>
    /// Destroys everything the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            if (made != null)
            {
                Object.DestroyImmediate(made is Component component ? component.gameObject : made);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A straight projectile stops at the first enemy on its line and hits it once; the one behind it takes nothing,
    /// and the shooter, whose body it starts in, is never hit.
    /// </summary>
    [Test]
    public void AStraightProjectile_HitsTheFirstUnitOnItsLine_Only()
    {
        DamageReceiver first = CreateUnit("First", new Vector2(2f, 0f), Faction.Mobs);
        DamageReceiver second = CreateUnit("Second", new Vector2(3f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);
        int struck = 0;
        service.UnitStruck += (_, _, _) => struck++;

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);
        Assert.That(service.FlyingCount, Is.EqualTo(1));
        Assert.That(service.Flying(0).Position.x, Is.EqualTo(LaunchDistance).Within(0.0001f), "It leaves its launch distance ahead.");
        FlyFor(service, 1f);

        Assert.That(service.FlyingCount, Is.Zero);
        Assert.That(struck, Is.EqualTo(1));
        Assert.That(first.Health.CurrentHealth, Is.EqualTo(first.Health.MaxHealth - 5));
        Assert.That(second.Health.CurrentHealth, Is.EqualTo(second.Health.MaxHealth), "Only the first unit is hit.");
        Assert.That(shooter.GetComponent<Health>().CurrentHealth, Is.EqualTo(shooter.GetComponent<Health>().MaxHealth));
    }

    /// <summary>
    /// A wall between the shooter and an enemy stops the projectile there: nothing is hit.
    /// </summary>
    [Test]
    public void AWall_StopsTheProjectile()
    {
        DamageReceiver enemy = CreateUnit("Enemy", new Vector2(3f, 0f), Faction.Mobs);
        queries.AddWall(new Vector2(1.5f, 0f), 0.3f);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);
        FlyFor(service, 0.1f);
        Assert.That(service.FlyingCount, Is.Zero, "It stopped at the wall.");
        FlyFor(service, 1f);
        Assert.That(enemy.Health.CurrentHealth, Is.EqualTo(enemy.Health.MaxHealth));
    }

    /// <summary>
    /// A projectile flies its maximum range from the shooter's feet and no further: an enemy past it is not hit.
    /// </summary>
    [Test]
    public void AProjectile_FliesItsRange_AndNoFurther()
    {
        DamageReceiver beyond = CreateUnit("Beyond", new Vector2(Range + 0.6f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);
        FlyFor(service, (Range - LaunchDistance) / Speed - 0.05f);
        Assert.That(service.FlyingCount, Is.EqualTo(1));
        FlyFor(service, 0.1f);
        Assert.That(service.FlyingCount, Is.Zero, "It is gone once it has flown its range.");
        FlyFor(service, 1f);
        Assert.That(beyond.Health.CurrentHealth, Is.EqualTo(beyond.Health.MaxHealth));
    }

    /// <summary>
    /// Under the faction rules a projectile flies through the shooter's teammates and past dead units, and stops at
    /// the first enemy.
    /// </summary>
    [Test]
    public void AProjectile_FliesThroughTeammatesAndTheDead_ToTheFirstEnemy()
    {
        DamageReceiver teammate = CreateUnit("Teammate", new Vector2(1.5f, 0f), Faction.Players);
        DamageReceiver dead = CreateUnit("Dead", new Vector2(2.2f, 0f), Faction.Mobs);
        dead.Health.ApplyDamage(dead.Health.MaxHealth);
        DamageReceiver enemy = CreateUnit("Enemy", new Vector2(3f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);
        FlyFor(service, 1f);

        Assert.That(teammate.Health.CurrentHealth, Is.EqualTo(teammate.Health.MaxHealth));
        Assert.That(enemy.Health.CurrentHealth, Is.EqualTo(enemy.Health.MaxHealth - 5));
    }

    /// <summary>
    /// A homing projectile turns toward its target as it moves and hits only it, flying through another enemy in its
    /// way.
    /// </summary>
    [Test]
    public void AHomingProjectile_TurnsTowardItsTarget_AndHitsOnlyIt()
    {
        DamageReceiver target = CreateUnit("Target", new Vector2(4f, 0f), Faction.Mobs);
        DamageReceiver inTheWay = CreateUnit("InTheWay", new Vector2(2f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(true), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, Target(target));
        FlyFor(service, 0.1f);
        MoveUnit(target, new Vector2(3f, 2f));
        FlyFor(service, 0.02f);
        Vector2 heading = service.Flying(0).Direction;
        Vector2 toTarget = (new Vector2(3f, 2f) - service.Flying(0).Position).normalized;
        Assert.That(Vector2.Dot(heading, toTarget), Is.GreaterThan(0.99f), "It turns toward where its target is now.");
        FlyFor(service, 1f);

        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - 5));
        Assert.That(inTheWay.Health.CurrentHealth, Is.EqualTo(inTheWay.Health.MaxHealth), "A homing projectile only hits its target.");
    }

    /// <summary>
    /// A homing projectile whose target dies flies on straight and hits the first enemy it meets.
    /// </summary>
    [Test]
    public void AHomingProjectile_WhoseTargetDies_FliesOnStraight()
    {
        DamageReceiver target = CreateUnit("Target", new Vector2(5f, 0f), Faction.Mobs);
        DamageReceiver ahead = CreateUnit("Ahead", new Vector2(3f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(true), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, Target(target));
        FlyFor(service, 0.1f);
        Assert.That(service.Flying(0).IsHoming, Is.True);
        target.Health.ApplyDamage(target.Health.MaxHealth);
        FlyFor(service, 1f);

        Assert.That(ahead.Health.CurrentHealth, Is.EqualTo(ahead.Health.MaxHealth - 5));
    }

    /// <summary>
    /// Without authority a projectile still stops at the unit it meets, reporting it unresolved, but hits nothing.
    /// </summary>
    [Test]
    public void WithoutAuthority_AProjectileStopsAtTheUnit_ButHitsNothing()
    {
        DamageReceiver enemy = CreateUnit("Enemy", new Vector2(2f, 0f), Faction.Mobs);
        ProjectileService service = CreateService(new FixedGameAuthority(false));
        bool? resolved = null;
        service.UnitStruck += (unit, wasResolved, _) => resolved = unit == enemy ? wasResolved : null;

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);
        FlyFor(service, 1f);

        Assert.That(service.FlyingCount, Is.Zero);
        Assert.That(resolved, Is.EqualTo(false));
        Assert.That(enemy.Health.CurrentHealth, Is.EqualTo(enemy.Health.MaxHealth));
    }

    /// <summary>
    /// An enemy within the launch distance is hit as the projectile leaves, before any frame passes.
    /// </summary>
    [Test]
    public void AnEnemyWithinTheLaunchDistance_IsHitAsTheProjectileLeaves()
    {
        DamageReceiver close = CreateUnit("Close", new Vector2(0.4f, 0f), Faction.Mobs, 0.1f);
        ProjectileService service = CreateService(FixedGameAuthority.Authoritative);

        service.Launch(shooter, Spec(false), new Hit(5, DamageType.Physical, null), Vector2.zero, Vector2.right, default);

        Assert.That(service.FlyingCount, Is.Zero);
        Assert.That(close.Health.CurrentHealth, Is.EqualTo(close.Health.MaxHealth - 5));
    }

    /// <summary>
    /// Each projectile shows a pooled visual with the sprite of the direction nearest its flight, moved with it and
    /// turned with a homing projectile, and returned to the pool when it lands; launching, flying and landing allocate
    /// nothing once warm.
    /// </summary>
    [Test]
    public void Projectiles_ShowPooledVisuals_AndAllocateNothingOnceWarm()
    {
        DirectionalSprites sprites = Track(DirectionalSprites.Create(CreateSprites(32)));
        ProjectileVisual prefab = CreateVisualPrefab(sprites);
        ProjectileVisualPool pool = new(default(Scene));
        DamageReceiver enemy = CreateUnit("Enemy", new Vector2(3f, 1f), Faction.Mobs);
        queries.AddWall(new Vector2(-3f, 0f), 0.3f);
        ProjectileService service = CreateService(new FixedGameAuthority(false), pool);
        ProjectileSpec homing = new(Speed, Range, 0.1f, LaunchDistance, true, prefab);
        ProjectileSpec straight = new(Speed, Range, 0.1f, LaunchDistance, false, prefab);
        Hit hit = new(5, DamageType.Physical, null);
        UnitTarget target = Target(enemy);

        try
        {
            service.Launch(shooter, homing, hit, Vector2.zero, Vector2.right, target);
            ProjectileVisual shown = service.Flying(0).Visual;
            Assert.That(shown, Is.Not.Null);
            Assert.That(shown.gameObject.activeSelf, Is.True);
            Assert.That(shown.transform.position.x, Is.EqualTo(service.Flying(0).Position.x).Within(0.0001f));
            Assert.That(shown.GetComponentInChildren<SpriteRenderer>().sprite, Is.SameAs(sprites.Facing(service.Flying(0).Direction)));
            FlyFor(service, 2f);
            Assert.That(shown.gameObject.activeSelf, Is.False, "A landed projectile's visual goes back to the pool.");

            System.Action volley = () =>
            {
                service.Launch(shooter, homing, hit, Vector2.zero, Vector2.right, target);
                service.Launch(shooter, straight, hit, Vector2.zero, Vector2.left, default);
                service.Launch(shooter, straight, hit, Vector2.zero, Vector2.down, default);
                for (int i = 0; i < 40; i++)
                {
                    service.Advance(0.05f);
                }
            };
            volley();
            volley();
            Assert.That(service.FlyingCount, Is.Zero);
            Assert.That(pool.CreatedCount, Is.EqualTo(3), "Visuals are reused.");
            Assert.That(() => volley(), Is.Not.AllocatingGCMemory());
        }
        finally
        {
            foreach (ProjectileVisual visual in Object.FindObjectsByType<ProjectileVisual>(FindObjectsInactive.Include))
            {
                if (visual != prefab)
                {
                    Object.DestroyImmediate(visual.gameObject);
                }
            }
        }
    }

    /// <summary>
    /// The physics queries report a wall on a wall layer and a unit by its receiver, each at its distance along the
    /// step, and leave out a collider on a unit layer with no receiver.
    /// </summary>
    [Test]
    public void PhysicsQueries_ReportWallsAndUnits_AlongTheStep()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        int wallLayer = LayerMask.NameToLayer("Obstacles");
        DamageReceiver unit = CreateUnit("Unit", new Vector2(2f, 0f), Faction.Mobs);
        BoxCollider2D unitBody = unit.gameObject.AddComponent<BoxCollider2D>();
        unitBody.size = new Vector2(0.4f, 0.4f);
        unit.gameObject.layer = enemyLayer;
        GameObject wall = Track(new GameObject("Wall") { layer = wallLayer });
        wall.transform.position = new Vector2(4f, 0f);
        wall.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 2f);
        GameObject stray = Track(new GameObject("Stray") { layer = enemyLayer });
        stray.transform.position = new Vector2(3f, 0f);
        stray.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.4f);
        Physics2D.SyncTransforms();
        PhysicsProjectileQueries physics = new(1 << enemyLayer, 1 << wallLayer);
        List<ProjectileContact> contacts = new();

        physics.Cast(Vector2.zero, Vector2.right, 6f, 0.1f, contacts);

        Assert.That(contacts.Count, Is.EqualTo(2));
        ProjectileContact unitContact = contacts.Find(c => !c.IsWall);
        ProjectileContact wallContact = contacts.Find(c => c.IsWall);
        Assert.That(unitContact.Unit, Is.SameAs(unit));
        Assert.That(unitContact.Distance, Is.EqualTo(1.7f).Within(0.02f));
        Assert.That(wallContact.Distance, Is.EqualTo(3.7f).Within(0.02f));
    }

    /// <summary>
    /// <see cref="DirectionalSprites"/> picks the direction nearest a heading, counterclockwise from east, wrapping
    /// round, east for a zero heading.
    /// </summary>
    [Test]
    public void DirectionalSprites_PickTheNearestDirection()
    {
        DirectionalSprites sprites = Track(DirectionalSprites.Create(CreateSprites(32)));

        Assert.That(sprites.IndexOf(Vector2.right), Is.Zero);
        Assert.That(sprites.IndexOf(Vector2.up), Is.EqualTo(8));
        Assert.That(sprites.IndexOf(Vector2.left), Is.EqualTo(16));
        Assert.That(sprites.IndexOf(Vector2.down), Is.EqualTo(24));
        Assert.That(sprites.IndexOf(new Vector2(1f, -0.05f)), Is.Zero, "Just below east wraps round to east.");
        Assert.That(sprites.IndexOf(new Vector2(Mathf.Cos(0.2f), Mathf.Sin(0.2f))), Is.EqualTo(1));
        Assert.That(sprites.IndexOf(Vector2.zero), Is.Zero);
        Assert.That(sprites.Facing(Vector2.up), Is.SameAs(sprites.Get(8)));
    }

    /// <summary>
    /// A projectile flying at the test speed for the test range, leaving at the launch distance, with no visual.
    /// </summary>
    private static ProjectileSpec Spec(bool homing)
    {
        return new ProjectileSpec(Speed, Range, 0.1f, LaunchDistance, homing, null);
    }

    /// <summary>
    /// A service over the fake queries under <paramref name="authority"/>, its hits going through a hit service under
    /// the same authority, showing visuals from <paramref name="pool"/> when given.
    /// </summary>
    private ProjectileService CreateService(IGameAuthority authority, ProjectileVisualPool pool = null)
    {
        return new ProjectileService(authority, TestCombat.CreateHitService(authority), queries, new ManualClock(), pool);
    }

    /// <summary>
    /// Flies the service's projectiles for <paramref name="seconds"/> in 20 ms steps.
    /// </summary>
    private static void FlyFor(ProjectileService service, float seconds)
    {
        for (float flown = 0f; flown < seconds - 1e-5f; flown += 0.02f)
        {
            service.Advance(Mathf.Min(0.02f, seconds - flown));
        }
    }

    /// <summary>
    /// A unit with health and a receiver of <paramref name="faction"/> at <paramref name="position"/>, a circle of
    /// <paramref name="radius"/> to the fake queries.
    /// </summary>
    private DamageReceiver CreateUnit(string name, Vector2 position, Faction faction, float radius = 0.3f)
    {
        GameObject unit = Track(new GameObject(name));
        unit.transform.position = position;
        unit.AddComponent<Health>();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        receiver.SetProfile(Track(CombatProfile.Create(faction)));
        queries.AddUnit(receiver, radius);
        return receiver;
    }

    /// <summary>
    /// <paramref name="unit"/> as an attack's target, with a collider the projectile homes on.
    /// </summary>
    private static UnitTarget Target(DamageReceiver unit)
    {
        CircleCollider2D body = unit.GetComponent<CircleCollider2D>();
        if (body == null)
        {
            body = unit.gameObject.AddComponent<CircleCollider2D>();
            body.radius = 0.3f;
        }

        Physics2D.SyncTransforms();
        return new UnitTarget(unit.Health, body, UnitTeam.Enemy);
    }

    /// <summary>
    /// Moves <paramref name="unit"/> to <paramref name="position"/>, its collider too.
    /// </summary>
    private static void MoveUnit(DamageReceiver unit, Vector2 position)
    {
        unit.transform.position = position;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// <paramref name="count"/> distinct sprites over one small texture.
    /// </summary>
    private Sprite[] CreateSprites(int count)
    {
        Texture2D texture = Track(new Texture2D(count, 1));
        Sprite[] sprites = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            sprites[i] = Track(Sprite.Create(texture, new Rect(i, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 32f));
        }

        return sprites;
    }

    /// <summary>
    /// A hidden visual "prefab" (a scene object the pool copies) showing <paramref name="sprites"/> on a sprite child.
    /// </summary>
    private ProjectileVisual CreateVisualPrefab(DirectionalSprites sprites)
    {
        GameObject root = Track(new GameObject("ArrowPrefab"));
        GameObject child = new("Sprite");
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = new Vector3(0f, 0.76f, 0f);
        SpriteRenderer renderer = child.AddComponent<SpriteRenderer>();
        ProjectileVisual visual = root.AddComponent<ProjectileVisual>();
        visual.Configure(sprites, renderer);
        root.SetActive(false);
        return visual;
    }

    /// <summary>
    /// Keeps <paramref name="made"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T made) where T : Object
    {
        created.Add(made);
        return made;
    }

    /// <summary>
    /// <see cref="IProjectileQueries"/> over circles the test places: units at their transform's position and walls,
    /// each met where a circle of the projectile's radius moving along the step first touches it (at once when it
    /// starts inside).
    /// </summary>
    private sealed class FakeQueries : IProjectileQueries
    {
        private readonly List<DamageReceiver> units = new();
        private readonly List<float> unitRadii = new();
        private readonly List<Vector2> walls = new();
        private readonly List<float> wallRadii = new();

        /// <summary>
        /// Places <paramref name="unit"/> as a circle of <paramref name="radius"/> around its position.
        /// </summary>
        public void AddUnit(DamageReceiver unit, float radius)
        {
            units.Add(unit);
            unitRadii.Add(radius);
        }

        /// <summary>
        /// Places a wall as a circle of <paramref name="radius"/> around <paramref name="centre"/>.
        /// </summary>
        public void AddWall(Vector2 centre, float radius)
        {
            walls.Add(centre);
            wallRadii.Add(radius);
        }

        /// <summary>
        /// Every unit and wall the moving circle meets within the step.
        /// </summary>
        public void Cast(Vector2 origin, Vector2 direction, float distance, float radius, List<ProjectileContact> results)
        {
            results.Clear();
            for (int i = 0; i < units.Count; i++)
            {
                if (TryMeet(origin, direction, distance, units[i].transform.position, unitRadii[i] + radius, out float at))
                {
                    results.Add(new ProjectileContact(at, units[i]));
                }
            }

            for (int i = 0; i < walls.Count; i++)
            {
                if (TryMeet(origin, direction, distance, walls[i], wallRadii[i] + radius, out float at))
                {
                    results.Add(new ProjectileContact(at, null));
                }
            }
        }

        /// <summary>
        /// Where along the step a point moving from <paramref name="origin"/> first comes within
        /// <paramref name="reach"/> of <paramref name="centre"/>.
        /// </summary>
        private static bool TryMeet(Vector2 origin, Vector2 direction, float distance, Vector2 centre, float reach, out float at)
        {
            Vector2 toCentre = centre - origin;
            float along = Vector2.Dot(toCentre, direction);
            float aside = (toCentre - direction * along).sqrMagnitude;
            at = 0f;
            if (aside > reach * reach)
            {
                return false;
            }

            at = Mathf.Max(0f, along - Mathf.Sqrt(reach * reach - aside));
            return toCentre.sqrMagnitude <= reach * reach || (along >= 0f && at <= distance);
        }
    }
}
