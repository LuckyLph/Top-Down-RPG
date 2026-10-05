using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for picking the unit under the pointer with <see cref="PointerTargetPicker"/> and for turning raw input
/// into commands with <see cref="LocalPlayerCommandSource"/>.
/// </summary>
public class PlayerTargetingTests
{
    private readonly List<Object> createdObjects = new();
    private PlayerControlSettings settings;
    private PlayerRegistry players;
    private PointerTargetPicker picker;

    /// <summary>
    /// Builds a picker with a 0.35 pick radius over an empty player registry.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        settings = Track(TestPlayerControlSettings.Create(pickRadius: 0.35f));
        players = new PlayerRegistry();
        picker = new PointerTargetPicker(settings, players);
    }

    /// <summary>
    /// Destroys the settings, units, cameras and render textures the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// With two enemies inside the pick radius, the picker returns the closer one with its collider and the
    /// enemy team.
    /// </summary>
    [Test]
    public void Picker_ReturnsTheClosestLivingEnemy()
    {
        Health far = CreateMob(new Vector2(0.3f, 0f));
        Health near = CreateMob(new Vector2(-0.1f, 0f));
        Physics2D.SyncTransforms();

        UnitTarget picked = picker.Pick(Vector2.zero);

        Assert.That(picked.Health, Is.SameAs(near));
        Assert.That(picked.Team, Is.EqualTo(UnitTeam.Enemy));
        Assert.That(picked.Collider, Is.SameAs(near.GetComponent<Collider2D>()));
        Assert.That(far, Is.Not.Null);
    }

    /// <summary>
    /// The picker skips dead enemies and colliders on the enemy layer that have no <see cref="DamageReceiver"/>,
    /// even when they are closer than a living enemy.
    /// </summary>
    [Test]
    public void Picker_IgnoresDeadUnits_AndEnemyLayerCollidersWithoutAReceiver()
    {
        Health dead = CreateMob(new Vector2(0.05f, 0f));
        dead.ApplyDamage(dead.MaxHealth);
        GameObject prop = Track(new GameObject("EnemyLayerProp"));
        prop.layer = LayerMask.NameToLayer("Enemy");
        prop.AddComponent<BoxCollider2D>().size = Vector2.one * 0.3f;
        Health alive = CreateMob(new Vector2(0.3f, 0f));
        Physics2D.SyncTransforms();

        Assert.That(picker.Pick(Vector2.zero).Health, Is.SameAs(alive));
    }

    /// <summary>
    /// The picker returns a player body as an ally only when it is registered in the
    /// <see cref="PlayerRegistry"/> and alive.
    /// </summary>
    [Test]
    public void Picker_FindsRegisteredLivingAlliesOnly()
    {
        Health stranger = CreatePlayerBody(new Vector2(0.05f, 0f), register: false);
        Health ally = CreatePlayerBody(new Vector2(0.2f, 0f), register: true);
        Physics2D.SyncTransforms();

        UnitTarget picked = picker.Pick(Vector2.zero);
        Assert.That(picked.Health, Is.SameAs(ally));
        Assert.That(picked.Team, Is.EqualTo(UnitTeam.Ally));

        ally.ApplyDamage(ally.MaxHealth);
        Assert.That(picker.Pick(Vector2.zero).Exists, Is.False, "Dead allies are not targetable.");
        Assert.That(stranger, Is.Not.Null);
    }

    /// <summary>
    /// The picker ignores units beyond the pick radius and still picks a unit behind an obstacle collider.
    /// </summary>
    [Test]
    public void Picker_IgnoresUnitsOutsideTheRadius_AndNeedsNoLineOfSight()
    {
        CreateMob(new Vector2(2f, 0f));
        Health behindWall = CreateMob(new Vector2(0f, 0.2f));
        GameObject wall = Track(new GameObject("Wall"));
        wall.layer = LayerMask.NameToLayer("Obstacles");
        wall.AddComponent<BoxCollider2D>().size = new Vector2(3f, 0.1f);
        Physics2D.SyncTransforms();

        Assert.That(picker.Pick(new Vector2(0f, 0.15f)).Health, Is.SameAs(behindWall));
        Assert.That(picker.Pick(new Vector2(1f, -1f)).Exists, Is.False);
    }

    /// <summary>
    /// Picking over an enemy and a registered ally allocates no managed memory once warm.
    /// </summary>
    [Test]
    public void Picker_DoesNotAllocate()
    {
        CreateMob(Vector2.zero);
        CreatePlayerBody(new Vector2(0.1f, 0f), register: true);
        Physics2D.SyncTransforms();
        picker.Pick(Vector2.zero);

        TestDelegate pick = () => picker.Pick(Vector2.zero);

        Assert.That(pick, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// A move press converts the pointer's screen position to world space through the camera and carries the
    /// enemy picked under it.
    /// </summary>
    [Test]
    public void CommandSource_ConvertsThePointerToTheWorld_AndPicksTheUnitUnderIt()
    {
        Camera camera = CreateCamera(new Vector2(10f, 20f));
        Health mob = CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();
        FakePlayerInput input = new() { PointerScreenPosition = new Vector2(100f, 50f), MovePressedThisFrame = true, MoveHeld = true };
        LocalPlayerCommandSource source = new(input, camera, picker);

        PlayerCommand command = source.ReadCommand();

        Assert.That(command.PointerWorld.x, Is.EqualTo(10f).Within(0.001f));
        Assert.That(command.PointerWorld.y, Is.EqualTo(20f).Within(0.001f));
        Assert.That(command.MovePressed, Is.True);
        Assert.That(command.Target.Health, Is.SameAs(mob));
        Assert.That(command.Target.Team, Is.EqualTo(UnitTeam.Enemy));
    }

    /// <summary>
    /// A move press over UI is dropped along with the hold that follows it, even once the pointer leaves the UI,
    /// and the next press outside UI counts again.
    /// </summary>
    [Test]
    public void CommandSource_DropsAPressOverUI_AndTheHoldThatFollowsIt()
    {
        FakePlayerInput input = new() { PointerScreenPosition = new Vector2(100f, 50f), MovePressedThisFrame = true, MoveHeld = true, IsPointerOverUI = true };
        LocalPlayerCommandSource source = new(input, CreateCamera(Vector2.zero), picker);

        PlayerCommand pressed = source.ReadCommand();
        input.MovePressedThisFrame = false;
        input.IsPointerOverUI = false;
        PlayerCommand held = source.ReadCommand();

        Assert.That(pressed.MovePressed, Is.False);
        Assert.That(pressed.MoveHeld, Is.False);
        Assert.That(held.MoveHeld, Is.False, "A hold that started over UI never reaches gameplay.");

        input.MoveHeld = false;
        source.ReadCommand();
        input.MovePressedThisFrame = true;
        input.MoveHeld = true;
        Assert.That(source.ReadCommand().MovePressed, Is.True, "The next press outside UI counts again.");
    }

    /// <summary>
    /// A hold that started outside UI keeps holding when the pointer moves over UI.
    /// </summary>
    [Test]
    public void CommandSource_KeepsAnAcceptedHold_WhenThePointerMovesOverUI()
    {
        FakePlayerInput input = new() { PointerScreenPosition = new Vector2(100f, 50f), MovePressedThisFrame = true, MoveHeld = true };
        LocalPlayerCommandSource source = new(input, CreateCamera(Vector2.zero), picker);
        source.ReadCommand();

        input.MovePressedThisFrame = false;
        input.IsPointerOverUI = true;

        Assert.That(source.ReadCommand().MoveHeld, Is.True);
    }

    /// <summary>
    /// The command forwards a stop press and the pressed ability slot with no move press, and carries no target
    /// when no unit is under the pointer.
    /// </summary>
    [Test]
    public void CommandSource_ForwardsStopAndTheFirstAbilityPressed()
    {
        FakePlayerInput input = new() { StopPressedThisFrame = true, AbilityPressed = 4 };
        LocalPlayerCommandSource source = new(input, CreateCamera(Vector2.zero), picker);

        PlayerCommand command = source.ReadCommand();

        Assert.That(command.StopPressed, Is.True);
        Assert.That(command.HasAbility, Is.True);
        Assert.That(command.AbilitySlot, Is.EqualTo(4));
        Assert.That(command.MovePressed, Is.False);
        Assert.That(command.Target.Exists, Is.False, "Nothing is picked when no unit is under the pointer.");
    }

    /// <summary>
    /// An ability press without the move button still picks the unit under the cursor as the command's target.
    /// </summary>
    [Test]
    public void CommandSource_PicksTheUnitUnderTheCursorForAnAbilityPress()
    {
        Camera camera = CreateCamera(new Vector2(10f, 20f));
        Health mob = CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();
        FakePlayerInput input = new() { PointerScreenPosition = new Vector2(100f, 50f), AbilityPressed = 2 };
        LocalPlayerCommandSource source = new(input, camera, picker);

        PlayerCommand command = source.ReadCommand();

        Assert.That(command.AbilitySlot, Is.EqualTo(2));
        Assert.That(command.Target.Health, Is.SameAs(mob));
    }

    /// <summary>
    /// While gameplay input is disabled the command carries no move, stop or ability press.
    /// </summary>
    [Test]
    public void CommandSource_GivesNothingWhileGameplayInputIsDisabled()
    {
        FakePlayerInput input = new() { GameplayEnabled = false, MovePressedThisFrame = true, MoveHeld = true, StopPressedThisFrame = true, AbilityPressed = 0 };
        LocalPlayerCommandSource source = new(input, CreateCamera(Vector2.zero), picker);

        PlayerCommand command = source.ReadCommand();

        Assert.That(command.MovePressed, Is.False);
        Assert.That(command.StopPressed, Is.False);
        Assert.That(command.HasAbility, Is.False);
    }

    /// <summary>
    /// A default <see cref="PlayerCommand"/> reports <see cref="PlayerCommand.NoAbility"/> rather than slot 0,
    /// while slot 0 passed explicitly is kept.
    /// </summary>
    [Test]
    public void DefaultCommand_HasNoAbility()
    {
        Assert.That(default(PlayerCommand).HasAbility, Is.False);
        Assert.That(default(PlayerCommand).AbilitySlot, Is.EqualTo(PlayerCommand.NoAbility));
        Assert.That(new PlayerCommand(Vector2.zero, default, false, false, false, 0).AbilitySlot, Is.EqualTo(0));
    }

    /// <summary>
    /// Creates a tracked orthographic camera centred on <paramref name="position"/> that renders to a 200x100
    /// texture, so screen-to-world conversion does not depend on the editor window.
    /// </summary>
    private Camera CreateCamera(Vector2 position)
    {
        GameObject cameraObject = Track(new GameObject("Camera"));
        cameraObject.transform.position = new Vector3(position.x, position.y, -10f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.targetTexture = Track(new RenderTexture(200, 100, 0));
        return camera;
    }

    /// <summary>
    /// Creates a tracked enemy-layer mob at <paramref name="position"/> with a box collider, a
    /// <see cref="Health"/> and a <see cref="DamageReceiver"/>, and returns its health.
    /// </summary>
    private Health CreateMob(Vector2 position)
    {
        GameObject mob = Track(new GameObject("Mob"));
        mob.layer = LayerMask.NameToLayer("Enemy");
        mob.transform.position = position;
        mob.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 0.2f);
        Health health = mob.AddComponent<Health>();
        mob.AddComponent<DamageReceiver>();
        return health;
    }

    /// <summary>
    /// Creates a tracked player-layer body at <paramref name="position"/> with a box collider and a
    /// <see cref="Health"/>, adds it to the player registry when <paramref name="register"/> is set, and returns
    /// its health.
    /// </summary>
    private Health CreatePlayerBody(Vector2 position, bool register)
    {
        GameObject player = Track(new GameObject("PlayerBody"));
        player.layer = LayerMask.NameToLayer("Player");
        player.transform.position = position;
        player.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 0.2f);
        Health health = player.AddComponent<Health>();
        if (register)
        {
            players.Add(new PlayerHandle(player.transform, health));
        }

        return health;
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
