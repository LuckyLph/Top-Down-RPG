using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Creates seamless worlds and their chunks as assets and scenes (Docs/WorldStreaming.md): a world's definition, its
/// root scene with its scope and lighting, and chunk scenes whose grid sits at the world's origin with a terrain
/// renderer over one grass layer. Every scene gets a <see cref="SceneDefinition"/> and a place in the build list. The
/// World Editor window (Phase 5) builds on these steps.
/// </summary>
public static class WorldAuthoring
{
    public const string WorldsFolder = "Assets/Worlds";
    public const string StylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    private const float GlobalLightIntensity = 0.85f;

    /// <summary>
    /// The folder of the world named <paramref name="worldName"/>.
    /// </summary>
    public static string FolderOf(string worldName)
    {
        return $"{WorldsFolder}/{worldName}";
    }

    /// <summary>
    /// Creates the world <paramref name="worldName"/> at <paramref name="origin"/>, <paramref name="sizeInChunks"/>
    /// chunks across, with no chunks yet: its folder, root scene (a <see cref="WorldLifetimeScope"/>, and a
    /// <see cref="WorldLighting"/> holding a white Global Light 2D at 0.85 as in the areas), the scene's definition and
    /// the world's definition. Leaves the scenes open as they were. Throws when the folder already exists.
    /// </summary>
    public static WorldDefinition CreateWorld(string worldName, Vector2Int origin, Vector2Int sizeInChunks)
    {
        string folder = FolderOf(worldName);
        if (AssetDatabase.IsValidFolder(folder))
        {
            throw new System.InvalidOperationException($"{folder} already exists.");
        }

        EnsureFolder(folder);
        string rootPath = $"{folder}/World_{worldName}.unity";
        Scene scene = NewWorkScene();
        try
        {
            new GameObject("WorldLifetimeScope", typeof(WorldLifetimeScope));
            GameObject lighting = new("Lighting", typeof(WorldLighting));
            GameObject lightObject = new("Global Light 2D");
            lightObject.transform.SetParent(lighting.transform, false);
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = GlobalLightIntensity;
            EditorSceneManager.SaveScene(scene, rootPath);
        }
        finally
        {
            CloseWorkScene(scene);
        }

        SceneDefinition rootDefinition = CreateSceneDefinition(rootPath);
        WorldDefinition world = ScriptableObject.CreateInstance<WorldDefinition>();
        AssetDatabase.CreateAsset(world, $"{folder}/{worldName}.asset");
        world.Configure(rootDefinition, origin, sizeInChunks);
        AddToBuild(rootPath);
        AssetDatabase.SaveAssets();
        return world;
    }

    /// <summary>
    /// Creates the chunk at <paramref name="coords"/> of <paramref name="world"/>: a scene with a
    /// <see cref="ChunkLifetimeScope"/> and a grid at the world's origin whose terrain renderer draws one empty grass
    /// layer, its definition, its entry in the world and its place in the build list. Returns the scene path. Throws
    /// when the chunk lies outside the world or already has a scene.
    /// </summary>
    public static string CreateChunk(WorldDefinition world, Vector2Int coords)
    {
        if (coords.x < 0 || coords.y < 0 || coords.x >= world.SizeInChunks.x || coords.y >= world.SizeInChunks.y)
        {
            throw new System.ArgumentOutOfRangeException(nameof(coords), coords, $"{world.name} is {world.SizeInChunks} chunks across.");
        }

        if (world.HasChunk(coords))
        {
            throw new System.InvalidOperationException($"{world.name} already has a chunk at {coords}.");
        }

        string folder = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(world)).Replace('\\', '/')}/Chunks";
        EnsureFolder(folder);
        string path = $"{folder}/{ChunkSceneName(world, coords)}.unity";
        Scene scene = NewWorkScene();
        try
        {
            new GameObject("ChunkLifetimeScope", typeof(ChunkLifetimeScope));
            GameObject gridObject = new("Grid", typeof(Grid));
            gridObject.transform.position = new Vector3(world.Origin.x, world.Origin.y, 0f);
            gridObject.SetActive(false);
            TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
            renderer.Configure(AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath));
            GroundLayerMigration.AddLayer(gridObject.GetComponent<Grid>(), MaterialNamed("Grass"));
            gridObject.SetActive(true);
            EditorSceneManager.SaveScene(scene, path);
        }
        finally
        {
            CloseWorkScene(scene);
        }

        world.SetChunk(coords, CreateSceneDefinition(path));
        AddToBuild(path);
        AssetDatabase.SaveAssets();
        return path;
    }

    /// <summary>
    /// The name of the scene of the chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    public static string ChunkSceneName(WorldDefinition world, Vector2Int coords)
    {
        return $"World_{world.name}_{coords.x}_{coords.y}";
    }

    /// <summary>
    /// The project's terrain material named <paramref name="materialName"/> from the style's layer order.
    /// </summary>
    public static TerrainMaterial MaterialNamed(string materialName)
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        TerrainMaterial material = style.LayerOrder.FirstOrDefault(m => m != null && m.name == materialName);
        if (material == null)
        {
            throw new System.ArgumentException($"{style.name}'s layer order has no material named {materialName}.", nameof(materialName));
        }

        return material;
    }

    /// <summary>
    /// Paints <paramref name="cells"/> (world-local cells) of <paramref name="material"/> into the open chunk scenes of
    /// <paramref name="world"/>, each cell into the chunk that owns it, adding the material's layer to a chunk that
    /// lacks it. Cells of chunks that are not open are skipped and counted in the result.
    /// </summary>
    public static int Paint(WorldDefinition world, TerrainMaterial material, IEnumerable<Vector2Int> cells)
    {
        Dictionary<Vector2Int, List<Vector3Int>> byChunk = new();
        foreach (Vector2Int cell in cells)
        {
            Vector2Int chunk = ChunkCoords.ChunkOf(cell);
            if (!byChunk.TryGetValue(chunk, out List<Vector3Int> list))
            {
                byChunk[chunk] = list = new List<Vector3Int>();
            }

            list.Add(new Vector3Int(cell.x, cell.y, 0));
        }

        int skipped = 0;
        TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(material));
        foreach (KeyValuePair<Vector2Int, List<Vector3Int>> part in byChunk)
        {
            Grid grid = OpenChunkGrid(world, part.Key);
            if (grid == null)
            {
                skipped += part.Value.Count;
                continue;
            }

            Tilemap tilemap = LayerOf(grid, material);
            if (tilemap == null)
            {
                tilemap = GroundLayerMigration.AddLayer(grid, material);
            }

            TileBase[] tiles = new TileBase[part.Value.Count];
            System.Array.Fill(tiles, tile);
            tilemap.SetTiles(part.Value.ToArray(), tiles);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return skipped;
    }

    /// <summary>
    /// The grid of the chunk at <paramref name="coords"/> of <paramref name="world"/> when its scene is open, else null.
    /// </summary>
    public static Grid OpenChunkGrid(WorldDefinition world, Vector2Int coords)
    {
        if (!world.TryGetChunkScene(coords, out SceneDefinition definition))
        {
            return null;
        }

        Scene scene = SceneManager.GetSceneByPath(definition.ScenePath);
        return scene.IsValid() && scene.isLoaded ? SceneQuery.FindFirst<Grid>(scene) : null;
    }

    /// <summary>
    /// The tilemap of the layer drawing <paramref name="material"/> on <paramref name="grid"/>'s terrain renderer, or
    /// null.
    /// </summary>
    private static Tilemap LayerOf(Grid grid, TerrainMaterial material)
    {
        if (!grid.TryGetComponent(out TerrainBlendRenderer renderer))
        {
            return null;
        }

        foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
        {
            if (layer != null && layer.Material == material)
            {
                return layer.Tilemap;
            }
        }

        return null;
    }

    /// <summary>
    /// A new empty scene to build a world's or chunk's scene in: opened beside the open scenes, or in their place when
    /// one of them is an unsaved untitled scene, which Unity does not allow a new scene beside.
    /// </summary>
    private static Scene NewWorkScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
            {
                return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
    }

    /// <summary>
    /// Closes a scene made by <see cref="NewWorkScene"/>, unless it is the only scene open.
    /// </summary>
    private static void CloseWorkScene(Scene scene)
    {
        if (SceneManager.sceneCount > 1)
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Creates the <see cref="SceneDefinition"/> of the scene at <paramref name="scenePath"/> beside it.
    /// </summary>
    private static SceneDefinition CreateSceneDefinition(string scenePath)
    {
        SceneDefinition definition = ScriptableObject.CreateInstance<SceneDefinition>();
        AssetDatabase.CreateAsset(definition, Path.ChangeExtension(scenePath, ".asset"));
        definition.SetScene(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath));
        return definition;
    }

    /// <summary>
    /// Adds the scene at <paramref name="scenePath"/> to the build list, enabled, unless it is there already.
    /// </summary>
    private static void AddToBuild(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == scenePath))
        {
            return;
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>
    /// Creates <paramref name="folder"/> and its missing parents.
    /// </summary>
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
