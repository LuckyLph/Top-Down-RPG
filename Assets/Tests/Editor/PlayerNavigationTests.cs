using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PlayerNavigation"/>: a player in a world finding its world's navigation, and a player in no
/// world finding none.
/// </summary>
public class PlayerNavigationTests
{
    private readonly List<GameObject> sceneObjects = new();
    private PlayerRegistry players;
    private PlayerWorlds playerWorlds;
    private Worlds worlds;
    private PlayerNavigation playerNavigation;

    /// <summary>
    /// Builds the shared player registry, world records and players' navigation.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        players = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(players);
        worlds = new Worlds();
        playerNavigation = new PlayerNavigation(players, playerWorlds, worlds);
    }

    /// <summary>
    /// Destroys the scene objects the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        playerWorlds.Dispose();
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
    /// A player in no world paths on nothing; once in a world it paths on that world's navigation while the world is
    /// loaded, and on nothing while it is not.
    /// </summary>
    [Test]
    public void Of_APlayerInAWorld_PathsOnItsWorldsNavigation_AndOnNothingOtherwise()
    {
        GameObject playerObject = new("Player");
        sceneObjects.Add(playerObject);
        PlayerHandle player = new(playerObject.transform, null);
        players.Add(player);
        WorldDefinition world = WorldDefinition.Create("PlayerNavigation", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero });
        WorldNavigation navigation = new(new NavigationPagePool());
        try
        {
            Assert.That(playerNavigation.Of(playerObject.transform), Is.Null, "A player in no world has no navigation.");

            playerWorlds.Set(player, world);
            Assert.That(playerNavigation.Of(playerObject.transform), Is.Null, "Its world is not loaded yet.");

            worlds.Add(new LoadedWorld(world, default, null, null, navigation));
            Assert.That(playerNavigation.Of(playerObject.transform), Is.SameAs(navigation));

            playerWorlds.Set(player, null);
            Assert.That(playerNavigation.Of(playerObject.transform), Is.Null);
            Assert.That(playerNavigation.Of(null), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(world);
        }
    }
}
