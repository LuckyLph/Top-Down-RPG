using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PlayerRegistryTests
{
    private readonly List<GameObject> playerObjects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject playerObject in playerObjects)
        {
            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        playerObjects.Clear();
    }

    [Test]
    public void Add_IgnoresNullAndDuplicates_AndRaisesAddedOnce()
    {
        PlayerRegistry registry = new();
        PlayerHandle player = CreatePlayer();
        int addedCount = 0;
        registry.PlayerAdded += _ => addedCount++;

        Assert.That(registry.Add(player), Is.True);
        Assert.That(registry.Add(player), Is.False);
        Assert.That(registry.Add(null), Is.False);

        Assert.That(registry.Players, Is.EquivalentTo(new[] { player }));
        Assert.That(registry.Contains(player), Is.True);
        Assert.That(addedCount, Is.EqualTo(1));
    }

    [Test]
    public void Remove_RaisesRemovedOnlyForRegisteredPlayers()
    {
        PlayerRegistry registry = new();
        PlayerHandle player = CreatePlayer();
        registry.Add(player);
        PlayerHandle removed = null;
        registry.PlayerRemoved += handle => removed = handle;

        Assert.That(registry.Remove(CreatePlayer()), Is.False);
        Assert.That(removed, Is.Null);

        Assert.That(registry.Remove(player), Is.True);
        Assert.That(removed, Is.SameAs(player));
        Assert.That(registry.Contains(player), Is.False);
        Assert.That(registry.Players, Is.Empty);
    }

    [Test]
    public void AnyAlive_IsFalseOnlyOnceEveryPlayerIsDead()
    {
        PlayerRegistry registry = new();
        PlayerHandle first = CreatePlayer();
        PlayerHandle second = CreatePlayer();
        registry.Add(first);
        registry.Add(second);

        first.Health.ApplyDamage(first.Health.MaxHealth);
        Assert.That(registry.AnyAlive, Is.True);

        second.Health.ApplyDamage(second.Health.MaxHealth);
        Assert.That(registry.AnyAlive, Is.False);
    }

    private PlayerHandle CreatePlayer()
    {
        GameObject playerObject = new("Player");
        playerObjects.Add(playerObject);
        return new PlayerHandle(playerObject.transform, playerObject.AddComponent<Health>());
    }
}
