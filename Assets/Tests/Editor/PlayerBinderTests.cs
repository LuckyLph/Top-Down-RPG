using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PlayerBinderTests
{
    private readonly List<GameObject> createdObjects = new();
    private PlayerRegistry players;
    private LocalPlayerTracker localPlayer;
    private ActiveSpawnPoint activeSpawnPoint;
    private PlayerBinder binder;

    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        localPlayer = new LocalPlayerTracker();
        activeSpawnPoint = new ActiveSpawnPoint();
        binder = new PlayerBinder(players, localPlayer, new LocalPlayerCommandSource(new FakePlayerInput(), null, null), activeSpawnPoint);
    }

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

    [Test]
    public void BindLocal_RegistersTheLocalPlayer_AndPlacesItAtTheActiveSpawnPoint()
    {
        GameObject spawnObject = Create("Spawn");
        spawnObject.transform.position = new Vector3(4f, -2f, 0f);
        activeSpawnPoint.Set(spawnObject.AddComponent<SpawnPoint>());
        PlayerController controller = CreatePlayer();
        LocalPlayer assigned = null;
        localPlayer.Changed += player => assigned = player;

        PlayerHandle handle = binder.BindLocal(controller);

        Assert.That(players.Contains(handle), Is.True);
        Assert.That(assigned, Is.SameAs(localPlayer.Current));
        Assert.That(localPlayer.Current.Controller, Is.SameAs(controller));
        Assert.That(controller.SimulatesMovement, Is.True);
        Assert.That((Vector2)controller.transform.position, Is.EqualTo(new Vector2(4f, -2f)));
    }

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

    [Test]
    public void DamageService_AppliesNothingWithoutAuthority()
    {
        GameObject target = Create("Target");
        Health health = target.AddComponent<Health>();
        DamageReceiver receiver = target.AddComponent<DamageReceiver>();
        CombatEvents combatEvents = new();
        int published = 0;
        combatEvents.DamageApplied += _ => published++;
        DamageService damageService = new(combatEvents, new FixedGameAuthority(false));

        Assert.That(damageService.ApplyDamage(receiver, 3), Is.EqualTo(0));
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(published, Is.EqualTo(0));
    }

    private PlayerController CreatePlayer()
    {
        GameObject playerObject = Create("Player");
        playerObject.transform.position = new Vector3(20f, 20f, 0f);
        PlayerController controller = playerObject.AddComponent<PlayerController>();
        playerObject.AddComponent<Health>();
        return controller;
    }

    private GameObject Create(string name)
    {
        GameObject created = new(name);
        createdObjects.Add(created);
        return created;
    }
}
