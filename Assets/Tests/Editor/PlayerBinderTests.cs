using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PlayerBinder"/> binding local and remote players into the registry and the local player
/// tracker, plus the damage service's authority check.
/// </summary>
public class PlayerBinderTests
{
    private readonly List<GameObject> createdObjects = new();
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private PlayerBinder binder;

    /// <summary>
    /// Creates an empty player registry and local player tracker, and a binder over them with a local command source
    /// on fake input.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        binder = new PlayerBinder(players, localPlayer, new LocalPlayerCommandSource(new FakePlayerInput(), null, null));
    }

    /// <summary>
    /// Destroys every game object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Binding the local player registers it, assigns and announces it as the local player and lets it simulate its
    /// own movement, leaving it where it stands: placing it is the world session's.
    /// </summary>
    [Test]
    public void BindLocal_RegistersTheLocalPlayer_WithoutMovingIt()
    {
        PlayerController controller = CreatePlayer();
        controller.transform.position = new Vector3(4f, -2f, 0f);
        LocalPlayer assigned = null;
        localPlayer.Changed += player => assigned = player;

        PlayerHandle handle = binder.BindLocal(controller);

        Assert.That(players.Contains(handle), Is.True);
        Assert.That(assigned, Is.SameAs(localPlayer.Current));
        Assert.That(localPlayer.Current.Controller, Is.SameAs(controller));
        Assert.That(controller.SimulatesMovement, Is.True);
        Assert.That((Vector2)controller.transform.position, Is.EqualTo(new Vector2(4f, -2f)));
    }

    /// <summary>
    /// Binding a remote player registers it without making it the local player, simulating its movement or
    /// moving it.
    /// </summary>
    [Test]
    public void BindRemote_RegistersWithoutTakingOverTheLocalPlayerOrItsMovement()
    {
        PlayerController controller = CreatePlayer();
        Vector3 position = controller.transform.position;

        PlayerHandle handle = binder.BindRemote(controller);

        Assert.That(players.Contains(handle), Is.True);
        Assert.That(localPlayer.Current, Is.Null);
        Assert.That(controller.SimulatesMovement, Is.False);
        Assert.That(controller.transform.position, Is.EqualTo(position));
    }

    /// <summary>
    /// Unbinding removes a player from the registry and clears the local player only when it is the one removed.
    /// </summary>
    [Test]
    public void Unbind_RemovesThePlayer_AndClearsItIfItWasLocal()
    {
        PlayerHandle local = binder.BindLocal(CreatePlayer());
        PlayerHandle remote = binder.BindRemote(CreatePlayer());

        binder.Unbind(remote);
        Assert.That(players.Contains(remote), Is.False);
        Assert.That(localPlayer.Current, Is.Not.Null, "Removing a remote player keeps the local one.");

        binder.Unbind(local);
        Assert.That(players.Players, Is.Empty);
        Assert.That(localPlayer.Current, Is.Null);
    }

    /// <summary>
    /// A damage service on a machine without authority leaves health untouched, reports the hit as unresolved and
    /// publishes no damage event.
    /// </summary>
    [Test]
    public void DamageService_AppliesNothingWithoutAuthority()
    {
        GameObject target = Create("Target");
        Health health = target.AddComponent<Health>();
        DamageReceiver receiver = target.AddComponent<DamageReceiver>();
        CombatEvents combatEvents = new();
        int published = 0;
        combatEvents.DamageApplied += _ => published++;
        DamageService damageService = TestCombat.CreateDamageService(new FixedGameAuthority(false), combatEvents);

        Assert.That(damageService.ApplyDamage(receiver, 3).Resolved, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(published, Is.EqualTo(0));
    }

    /// <summary>
    /// Creates a player with a controller and health, placed away from the origin and any spawn point.
    /// </summary>
    private PlayerController CreatePlayer()
    {
        GameObject playerObject = Create("Player");
        playerObject.transform.position = new Vector3(20f, 20f, 0f);
        PlayerController controller = playerObject.AddComponent<PlayerController>();
        playerObject.AddComponent<Health>();
        return controller;
    }

    /// <summary>
    /// Creates a named game object that is destroyed on teardown.
    /// </summary>
    private GameObject Create(string name)
    {
        GameObject created = new(name);
        createdObjects.Add(created);
        return created;
    }
}
