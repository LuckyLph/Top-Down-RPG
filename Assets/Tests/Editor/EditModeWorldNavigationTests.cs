using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tests for <see cref="EditModeWorldNavigation"/> on Testlands' chunk scenes, opened beside the test's scene and closed
/// unchanged: an open chunk's page joins its world's navigation, so once its eastern neighbour is open too the movement
/// blocker gizmo no longer outlines the border between them, and outlines it again once the neighbour closes. A chunk
/// built in memory, in no saved scene of its world, is left alone.
/// </summary>
public class EditModeWorldNavigationTests
{
    private const string WorldPath = "Assets/Worlds/Testlands/Testlands.asset";

    private readonly List<Scene> opened = new();

    /// <summary>
    /// Closes the chunk scenes the test opened, discarding anything opening them changed, and joins what is left.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Scene scene in opened)
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        opened.Clear();
        EditModeWorldNavigation.Sync();
    }

    /// <summary>
    /// Chunk (0, 0) alone outlines its eastern border at x = 64; with chunk (1, 0) open beside it, both join their
    /// world's navigation and the border is not outlined; with chunk (1, 0) closed again, it is.
    /// </summary>
    [Test]
    public void OpenNeighbours_JoinTheirPages_SoTheGizmoDrawsNoBorderBetweenThem()
    {
        WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath);
        NavigationGrid2D west = Open(world, new Vector2Int(0, 0));
        EditModeWorldNavigation.Sync();
        Assert.That(EditModeWorldNavigation.IsJoined(west), Is.True);
        float border = world.Origin.x + ChunkCoords.Size;
        MovementBlockerGizmo.BlockerMap map = new();
        map.Refresh(west, null);
        Assert.That(SegmentsAlong(map.Edges, border), Is.GreaterThan(0), "Alone, the chunk outlines its eastern border.");

        NavigationGrid2D east = Open(world, new Vector2Int(1, 0));
        EditModeWorldNavigation.Sync();
        Assert.That(EditModeWorldNavigation.IsJoined(east), Is.True);
        map.Refresh(west, null);
        Assert.That(SegmentsAlong(map.Edges, border), Is.Zero, "With its neighbour open, the border is not outlined.");

        EditorSceneManager.CloseScene(east.gameObject.scene, true);
        EditModeWorldNavigation.Sync();
        map.Refresh(west, null);
        Assert.That(SegmentsAlong(map.Edges, border), Is.GreaterThan(0), "With its neighbour closed, it is outlined again.");
    }

    /// <summary>
    /// A chunk built in memory, outside its world's scenes, is not joined.
    /// </summary>
    [Test]
    public void AChunkInNoSavedSceneOfItsWorld_IsLeftAlone()
    {
        WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath);
        GameObject gridObject = new("Chunk", typeof(Grid));
        try
        {
            gridObject.SetActive(false);
            gridObject.AddComponent<WorldChunk>().Configure(world, new Vector2Int(1, 1), null);
            NavigationGrid2D grid = gridObject.AddComponent<NavigationGrid2D>();
            gridObject.SetActive(true);

            EditModeWorldNavigation.Sync();

            Assert.That(EditModeWorldNavigation.IsJoined(grid), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(gridObject);
        }
    }

    /// <summary>
    /// Opens the scene of the chunk at <paramref name="coords"/> beside the open scenes and returns its navigation grid.
    /// </summary>
    private NavigationGrid2D Open(WorldDefinition world, Vector2Int coords)
    {
        Assert.That(world.TryGetChunkScene(coords, out SceneDefinition definition), Is.True);
        Scene scene = EditorSceneManager.OpenScene(definition.ScenePath, OpenSceneMode.Additive);
        opened.Add(scene);
        return SceneQuery.FindFirst<NavigationGrid2D>(scene);
    }

    /// <summary>
    /// How many of <paramref name="lines"/>' segments run along the vertical line at world x = <paramref name="x"/>.
    /// </summary>
    private static int SegmentsAlong(MovementBlockerGizmo.LineList lines, float x)
    {
        int count = 0;
        System.ReadOnlySpan<Vector3> points = lines.Points;
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            if (Mathf.Abs(points[i].x - x) < 0.01f && Mathf.Abs(points[i + 1].x - x) < 0.01f)
            {
                count++;
            }
        }

        return count;
    }
}
