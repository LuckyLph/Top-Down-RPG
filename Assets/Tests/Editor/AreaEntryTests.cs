using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for placing the party on entering an area: <see cref="SpawnPoint"/> slots, <see cref="AreaEntry"/> and
/// <see cref="PlayerRespawner"/>.
/// </summary>
public class AreaEntryTests
{
    private readonly List<GameObject> sceneObjects = new();

    /// <summary>
    /// Destroys the scene objects the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (GameObject sceneObject in sceneObjects)
        {
            if (sceneObject != null)
            {
                Object.DestroyImmediate(sceneObject);
            }
        }

        sceneObjects.Clear();
    }

    /// <summary>
    /// Slot 0 of a spawn point is its own position, the first four slots are distinct, and slots 1 and 2 flank
    /// it symmetrically on x.
    /// </summary>
    [Test]
    public void SpawnPoint_FirstSlotIsTheSpawnPoint_AndOtherSlotsAreDistinct()
    {
        SpawnPoint spawnPoint = CreateSpawnPoint("start", new Vector2(2f, 3f));

        List<Vector2> slots = new();
        for (int i = 0; i < 4; i++)
        {
            slots.Add(spawnPoint.GetSlotPosition(i));
        }

        Assert.That(slots[0], Is.EqualTo(new Vector2(2f, 3f)));
        Assert.That(slots, Is.Unique);
        Assert.That(slots[1].x - 2f, Is.EqualTo(2f - slots[2].x).Within(0.0001f), "The first two extra slots should flank the spawn point.");
    }

    /// <summary>
    /// Entering an area makes the requested spawn point the active one and places each registered player in the
    /// slot matching its registration order.
    /// </summary>
    [Test]
    public void Enter_PlacesEveryRegisteredPlayerInItsOwnSlotAtTheRequestedSpawn()
    {
        SpawnPoint other = CreateSpawnPoint("other", new Vector2(-5f, -5f));
        SpawnPoint start = CreateSpawnPoint("start", new Vector2(2f, 3f));
        PlayerRegistry players = new();
        PlayerController first = CreatePlayer(players);
        PlayerController second = CreatePlayer(players);
        PlayerController third = CreatePlayer(players);

        ActiveSpawnPoint activeSpawnPoint = new();
        AreaEntry entry = new(
            new AreaEntryRequest(null, "start"),
            new[] { other, start },
            null,
            players,
            activeSpawnPoint,
            new ActiveNavigationGrid(),
            CreateCameraFollow());
        entry.Enter();

        Assert.That(activeSpawnPoint.Current, Is.SameAs(start), "Respawns should use the spawn point the party entered through.");

        Assert.That((Vector2)first.transform.position, Is.EqualTo(start.GetSlotPosition(0)));
        Assert.That((Vector2)second.transform.position, Is.EqualTo(start.GetSlotPosition(1)));
        Assert.That((Vector2)third.transform.position, Is.EqualTo(start.GetSlotPosition(2)));
    }

    /// <summary>
    /// The respawner refuses a living player, and brings a dead one back at full health in its own slot at the
    /// active spawn point.
    /// </summary>
    [Test]
    public void Respawner_RestoresADeadPlayerIntoItsSlotAtTheActiveSpawnPoint()
    {
        SpawnPoint start = CreateSpawnPoint("start", new Vector2(2f, 3f));
        PlayerRegistry players = new();
        CreatePlayer(players);
        PlayerController second = CreatePlayer(players);
        PlayerHandle secondHandle = players.Players[1];
        ActiveSpawnPoint activeSpawnPoint = new();
        activeSpawnPoint.Set(start);
        PlayerRespawner respawner = new(players, activeSpawnPoint);

        Assert.That(respawner.TryRespawn(secondHandle), Is.False, "A living player is not respawned.");

        secondHandle.Health.ApplyDamage(secondHandle.Health.MaxHealth);
        Assert.That(respawner.TryRespawn(secondHandle), Is.True);

        Assert.That(secondHandle.Health.IsDead, Is.False);
        Assert.That(secondHandle.Health.CurrentHealth, Is.EqualTo(secondHandle.Health.MaxHealth));
        Assert.That((Vector2)second.transform.position, Is.EqualTo(start.GetSlotPosition(1)));
    }

    /// <summary>
    /// The respawner leaves a dead player from another registry dead, and respawns a registered player where it
    /// died when no spawn point is active.
    /// </summary>
    [Test]
    public void Respawner_IgnoresPlayersOutsideTheRegistry_AndRespawnsInPlaceWithoutASpawnPoint()
    {
        PlayerRegistry players = new();
        PlayerController registered = CreatePlayer(players);
        PlayerHandle registeredHandle = players.Players[0];
        PlayerRegistry otherSession = new();
        CreatePlayer(otherSession);
        PlayerHandle stranger = otherSession.Players[0];
        PlayerRespawner respawner = new(players, new ActiveSpawnPoint());

        stranger.Health.ApplyDamage(stranger.Health.MaxHealth);
        Assert.That(respawner.TryRespawn(stranger), Is.False);
        Assert.That(stranger.Health.IsDead, Is.True);

        Vector3 deathPosition = registered.transform.position;
        registeredHandle.Health.ApplyDamage(registeredHandle.Health.MaxHealth);
        Assert.That(respawner.TryRespawn(registeredHandle), Is.True);
        Assert.That(registered.transform.position, Is.EqualTo(deathPosition));
    }

    /// <summary>
    /// Creates a tracked spawn point with id <paramref name="spawnId"/> at <paramref name="position"/>.
    /// </summary>
    private SpawnPoint CreateSpawnPoint(string spawnId, Vector2 position)
    {
        GameObject spawnObject = new($"SpawnPoint_{spawnId}");
        sceneObjects.Add(spawnObject);
        spawnObject.transform.position = position;
        SpawnPoint spawnPoint = spawnObject.AddComponent<SpawnPoint>();
        UnityEditor.SerializedObject serialized = new(spawnPoint);
        serialized.FindProperty("spawnId").stringValue = spawnId;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return spawnPoint;
    }

    /// <summary>
    /// Creates a tracked player away from the origin with a <see cref="PlayerController"/> and health, and adds
    /// its handle to <paramref name="players"/>.
    /// </summary>
    private PlayerController CreatePlayer(PlayerRegistry players)
    {
        GameObject playerObject = new("Player");
        sceneObjects.Add(playerObject);
        playerObject.transform.position = new Vector3(50f, 50f, 0f);
        PlayerController controller = playerObject.AddComponent<PlayerController>();
        players.Add(new PlayerHandle(playerObject.transform, playerObject.AddComponent<Health>()));
        return controller;
    }

    /// <summary>
    /// Creates a tracked camera object with a <see cref="CameraFollow2D"/> for the area entry to snap.
    /// </summary>
    private CameraFollow2D CreateCameraFollow()
    {
        GameObject cameraObject = new("Camera");
        sceneObjects.Add(cameraObject);
        return cameraObject.AddComponent<CameraFollow2D>();
    }
}
