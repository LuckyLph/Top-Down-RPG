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
/// root scene with its scope and lighting, and chunk scenes whose grid sits at the world's origin with a
/// <see cref="WorldChunk"/>, a terrain renderer over one grass layer, a <see cref="NavigationGrid2D"/> for its
/// navigation page and an <see cref="AreaTerrain"/> for its runtime edits. Every scene gets a
/// <see cref="SceneDefinition"/> and a place in the build list. Painting ground, walls and raised ground into the open
/// chunks, keeping each chunk's layer list covering its neighbours' border materials, and baking the coast maps
/// (<see cref="WorldCoastBake"/>, only where the sea changed when asked) work across a whole world. Chunks open with
/// their neighbours and close again, and a world is listed in <see cref="GameScenes"/>. World exits are placed in the
/// open chunks and entries set on a world. The World Editor window
/// (<see cref="WorldEditorWindow"/>) runs these steps.
/// </summary>
public static class WorldAuthoring
{
    public const string WorldsFolder = "Assets/Worlds";
    public const string StylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    public const string WallsPrefabPath = "Assets/Prefabs/World/Walls.prefab";
    public const string WallsName = "CollisionTilemap";
    public const string ExitsRootName = "Exits";
    public const string GrassLayerName = "GrassTilemap";
    public const string GrassTilePath = "Assets/Tiles/Ground/Tile_Grass.asset";
    public const string TerrainSyncPrefabPath = "Assets/Prefabs/Network/AreaTerrainSync.prefab";
    private const float GlobalLightIntensity = 0.85f;

    /// <summary>
    /// The folder of the world named <paramref name="worldName"/>.
    /// </summary>
    public static string FolderOf(string worldName)
    {
        return $"{WorldsFolder}/{worldName}";
    }

    /// <summary>
    /// Why <paramref name="worldName"/> cannot name a new world, or null when it can: a name is letters and digits,
    /// starting with a letter, since it names the world's folder and scenes, and no world's folder may have it yet.
    /// </summary>
    public static string NameProblem(string worldName)
    {
        if (string.IsNullOrEmpty(worldName) || !char.IsLetter(worldName[0]) || !worldName.All(char.IsLetterOrDigit))
        {
            return "A world's name is letters and digits, starting with a letter.";
        }

        return AssetDatabase.IsValidFolder(FolderOf(worldName)) ? $"{FolderOf(worldName)} already exists." : null;
    }

    /// <summary>
    /// Creates the world <paramref name="worldName"/> at <paramref name="origin"/>, <paramref name="sizeInChunks"/>
    /// chunks across, with no chunks yet: its folder, root scene (a <see cref="WorldLifetimeScope"/>, and a
    /// <see cref="WorldLighting"/> holding a white Global Light 2D at 0.85), the scene's definition and the world's
    /// definition, in <see cref="WorldsFolder"/> or another <paramref name="parentFolder"/> (a dev world's, under
    /// <see cref="SceneBuildValidator.DevFolder"/>, stays out of the build list). Leaves the scenes open as they were.
    /// Throws when the folder already exists.
    /// </summary>
    public static WorldDefinition CreateWorld(string worldName, Vector2Int origin, Vector2Int sizeInChunks, string parentFolder = WorldsFolder)
    {
        string folder = $"{parentFolder}/{worldName}";
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
            AddGlobalLight(lighting.transform);
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
    /// <see cref="ChunkLifetimeScope"/> and a grid at the world's origin with its <see cref="WorldChunk"/> and a terrain
    /// renderer drawing one empty grass layer (<see cref="SetUpGround"/>), its definition, its entry in the world and its
    /// place in the build list (none for a dev world). Returns the scene path. Throws when the chunk lies outside the
    /// world or already has a scene.
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
            gridObject.AddComponent<WorldChunk>().Configure(world, coords, null);
            SetUpGround(gridObject.GetComponent<Grid>());
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
    /// Replaces the open scenes, after offering to save them, with a new unsaved scratch scene for the Scene Generator:
    /// a Global Light 2D and a standalone grid at the origin set up as a chunk's (<see cref="SetUpGround"/>, then
    /// <see cref="AddWalls"/>) with no <see cref="WorldChunk"/>, so no world streams it. Returns the grid, or null when
    /// the save was declined.
    /// </summary>
    public static Grid CreateScratchScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return null;
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AddGlobalLight(null);
        GameObject gridObject = new("Grid", typeof(Grid));
        gridObject.SetActive(false);
        Grid grid = gridObject.GetComponent<Grid>();
        SetUpGround(grid);
        gridObject.SetActive(true);
        AddWalls(grid);
        return grid;
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
    /// lacks it, and to each open neighbour whose skirt the painted cells reach, so every layer list keeps covering its
    /// neighbours' border materials. Cells of chunks that are not open are skipped and counted in the result.
    /// </summary>
    public static int Paint(WorldDefinition world, TerrainMaterial material, IEnumerable<Vector2Int> cells)
    {
        Dictionary<Vector2Int, List<Vector3Int>> byChunk = SplitByChunk(cells);
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
                RefreshLinks(grid);
            }

            TileBase[] tiles = new TileBase[part.Value.Count];
            System.Array.Fill(tiles, tile);
            tilemap.SetTiles(part.Value.ToArray(), tiles);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            CoverInNeighbours(world, part.Key, part.Value, material);
        }

        return skipped;
    }

    /// <summary>
    /// Fills (or with <paramref name="filled"/> false, empties) the walls of <paramref name="cells"/> (world-local
    /// cells) in the open chunk scenes of <paramref name="world"/>, each cell through the wall module of the chunk that
    /// owns it, adding the walls (<see cref="AddWalls"/>) to a chunk that has none. Cells of chunks that are not open
    /// are skipped and counted in the result.
    /// </summary>
    public static int PaintWalls(WorldDefinition world, IEnumerable<Vector2Int> cells, bool filled = true)
    {
        int skipped = 0;
        foreach (KeyValuePair<Vector2Int, List<Vector3Int>> part in SplitByChunk(cells))
        {
            Grid grid = OpenChunkGrid(world, part.Key);
            if (grid == null)
            {
                skipped += part.Value.Count;
                continue;
            }

            DualGridTilemapModule walls = WallsOf(grid);
            if (walls == null)
            {
                walls = AddWalls(grid);
            }

            if (filled)
            {
                walls.SetDataTiles(part.Value);
            }
            else
            {
                walls.ClearDataTiles(part.Value);
            }

            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return skipped;
    }

    /// <summary>
    /// Writes <paramref name="tile"/> (a <see cref="RaisedGroundTile"/>, or null to level them) into the raised
    /// tilemaps of <paramref name="cells"/> (world-local cells) in the open chunk scenes of <paramref name="world"/>,
    /// each cell into the chunk that owns it, setting up the raised tilemap of a chunk that has none. Cells of chunks
    /// that are not open are skipped and counted in the result.
    /// </summary>
    public static int PaintRaised(WorldDefinition world, IEnumerable<Vector2Int> cells, TileBase tile)
    {
        int skipped = 0;
        foreach (KeyValuePair<Vector2Int, List<Vector3Int>> part in SplitByChunk(cells))
        {
            Grid grid = OpenChunkGrid(world, part.Key);
            if (grid == null)
            {
                skipped += part.Value.Count;
                continue;
            }

            TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
            Tilemap raised = renderer != null ? renderer.RaisedTilemap : null;
            if (raised == null)
            {
                raised = AreaGroundSetup.SetUpRaised(grid, new List<string>());
                RefreshLinks(grid);
            }

            TileBase[] tiles = new TileBase[part.Value.Count];
            System.Array.Fill(tiles, tile);
            raised.SetTiles(part.Value.ToArray(), tiles);
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return skipped;
    }

    /// <summary>
    /// Gives <paramref name="grid"/> the walls of the walls prefab (<see cref="WallsPrefabPath"/>): an unlinked copy of
    /// its empty <see cref="WallsName"/>, a dual-grid data tilemap on the Obstacles layer with its collider, its wall
    /// module and its half-cell render tilemap, placed after the ground layers. Returns its module.
    /// </summary>
    public static DualGridTilemapModule AddWalls(Grid grid)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallsPrefabPath);
        if (prefab == null || !prefab.TryGetComponent(out DualGridTilemapModule _))
        {
            throw new System.InvalidOperationException($"{WallsPrefabPath} is missing or has no {nameof(DualGridTilemapModule)}.");
        }

        GameObject copy = Object.Instantiate(prefab, grid.transform, false);
        copy.name = WallsName;
        Undo.RegisterCreatedObjectUndo(copy, $"Add {WallsName}");
        DualGridTilemapModule module = copy.GetComponent<DualGridTilemapModule>();
        module.DataTilemap.ClearAllTiles();
        module.RenderTilemap.ClearAllTiles();
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        if (grid.TryGetComponent(out NavigationGrid2D _))
        {
            SetUpNavigation(grid);
        }

        SetUpTerrainEdits(grid);
        RefreshLinks(grid);
        return module;
    }

    /// <summary>
    /// Gives <paramref name="grid"/> a raised tilemap its terrain renderer draws and its navigation reads the cliff of,
    /// as one undo step, or returns the one it has.
    /// </summary>
    public static Tilemap AddRaised(Grid grid)
    {
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        if (renderer != null && renderer.RaisedTilemap != null)
        {
            return renderer.RaisedTilemap;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Add raised ground");
        int undoGroup = Undo.GetCurrentGroup();
        Tilemap raised = AreaGroundSetup.SetUpRaised(grid, new List<string>());
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        RefreshLinks(grid);
        return raised;
    }

    /// <summary>
    /// Gives <paramref name="grid"/>, a chunk's grid, the <see cref="NavigationGrid2D"/> its navigation page is built
    /// from, or brings the one it has in line: it reads the ground layers under the grid, is blocked by the walls
    /// (<see cref="WallsName"/>) and by colliders on the Obstacles layer, and reads the cliff of the raised tilemap the
    /// terrain renderer draws. Returns whether it changed anything.
    /// </summary>
    public static bool SetUpNavigation(Grid grid)
    {
        bool changed = false;
        if (!grid.TryGetComponent(out NavigationGrid2D navigation))
        {
            navigation = Undo.AddComponent<NavigationGrid2D>(grid.gameObject);
            changed = true;
        }

        SerializedObject data = new(navigation);
        Transform walls = grid.transform.Find(WallsName);
        Tilemap wallsTilemap = walls != null ? walls.GetComponent<Tilemap>() : null;
        if (wallsTilemap != null && !navigation.BlockingTilemaps.Contains(wallsTilemap))
        {
            SerializedProperty blocking = data.FindProperty("blockingTilemaps");
            blocking.arraySize++;
            blocking.GetArrayElementAtIndex(blocking.arraySize - 1).objectReferenceValue = wallsTilemap;
            changed = true;
        }

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Tilemap raised = renderer != null ? renderer.RaisedTilemap : null;
        if (navigation.RaisedTilemap != raised)
        {
            data.FindProperty("raisedTilemap").objectReferenceValue = raised;
            changed = true;
        }

        int obstacles = LayerMask.GetMask("Obstacles");
        if (navigation.ObstacleLayers.value != obstacles)
        {
            data.FindProperty("obstacleLayers").intValue = obstacles;
            changed = true;
        }

        data.ApplyModifiedProperties();
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return changed;
    }

    /// <summary>
    /// Gives <paramref name="grid"/>, a chunk's grid, the <see cref="AreaTerrain"/> its runtime edits go through
    /// (Docs/WorldStreaming.md: Networking), or brings the one it has in line: it lists the grass layer
    /// (<see cref="GrassLayerName"/>, filled with <see cref="GrassTilePath"/>'s tile) and the walls
    /// (<see cref="WallsName"/>) when the chunk has them, adding a missing one after those it lists so a layer keeps its
    /// index, and replicates through the terrain sync prefab. Returns whether it changed anything.
    /// </summary>
    public static bool SetUpTerrainEdits(Grid grid)
    {
        bool changed = false;
        if (!grid.TryGetComponent(out AreaTerrain terrain))
        {
            terrain = Undo.AddComponent<AreaTerrain>(grid.gameObject);
            changed = true;
        }

        SerializedObject data = new(terrain);
        SerializedProperty layers = data.FindProperty("layers");
        Transform grass = grid.transform.Find(GrassLayerName);
        Transform walls = grid.transform.Find(WallsName);
        changed |= AddTerrainLayer(layers, grass != null ? grass.GetComponent<Tilemap>() : null, AssetDatabase.LoadAssetAtPath<TileBase>(GrassTilePath));
        changed |= AddTerrainLayer(layers, walls != null ? walls.GetComponent<Tilemap>() : null, null);
        data.ApplyModifiedProperties();
        Undo.RecordObject(terrain, "Set the terrain sync prefab");
        changed |= terrain.AssignSyncPrefabIfMissing(AssetDatabase.LoadAssetAtPath<GameObject>(TerrainSyncPrefabPath));
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        }

        return changed;
    }

    /// <summary>
    /// Sets up the runtime terrain edits of every chunk scene of <paramref name="world"/>
    /// (<see cref="SetUpTerrainEdits(Grid)"/>), opening the scenes that are not open, and saves the ones it changed.
    /// Returns the names of the scenes it changed.
    /// </summary>
    public static List<string> SetUpTerrainEdits(WorldDefinition world)
    {
        List<string> changed = new();
        List<Scene> opened = new();
        try
        {
            foreach (KeyValuePair<Vector2Int, Grid> chunk in OpenChunks(world, opened))
            {
                if (SetUpTerrainEdits(chunk.Value))
                {
                    EditorSceneManager.SaveScene(chunk.Value.gameObject.scene);
                    changed.Add(chunk.Value.gameObject.scene.name);
                }
            }
        }
        finally
        {
            CloseScenes(opened);
        }

        return changed;
    }

    /// <summary>
    /// Appends <paramref name="tilemap"/> with <paramref name="fillTile"/> to an <see cref="AreaTerrain"/>'s
    /// <paramref name="layers"/> unless it is null or already listed. Returns whether it added it.
    /// </summary>
    private static bool AddTerrainLayer(SerializedProperty layers, Tilemap tilemap, TileBase fillTile)
    {
        if (tilemap == null)
        {
            return false;
        }

        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("tilemap").objectReferenceValue == tilemap)
            {
                return false;
            }
        }

        layers.arraySize++;
        SerializedProperty layer = layers.GetArrayElementAtIndex(layers.arraySize - 1);
        layer.FindPropertyRelative("tilemap").objectReferenceValue = tilemap;
        layer.FindPropertyRelative("fillTile").objectReferenceValue = fillTile;
        return true;
    }

    /// <summary>
    /// Sets up the navigation of every chunk scene of <paramref name="world"/> (<see cref="SetUpNavigation(Grid)"/>),
    /// opening the scenes that are not open, and saves the ones it changed. Returns the names of the scenes it changed.
    /// </summary>
    public static List<string> SetUpNavigation(WorldDefinition world)
    {
        List<string> changed = new();
        List<Scene> opened = new();
        try
        {
            foreach (KeyValuePair<Vector2Int, Grid> chunk in OpenChunks(world, opened))
            {
                if (SetUpNavigation(chunk.Value))
                {
                    EditorSceneManager.SaveScene(chunk.Value.gameObject.scene);
                    changed.Add(chunk.Value.gameObject.scene.name);
                }
            }
        }
        finally
        {
            CloseScenes(opened);
        }

        return changed;
    }

    /// <summary>
    /// Gives every chunk scene of <paramref name="world"/> its <see cref="WorldChunk"/> naming its world and
    /// coordinates, opening the scenes that are not open, and saves the ones it changed. Returns the names of the
    /// scenes it changed.
    /// </summary>
    public static List<string> MarkChunks(WorldDefinition world)
    {
        List<string> changed = new();
        List<Scene> opened = new();
        try
        {
            foreach (KeyValuePair<Vector2Int, Grid> chunk in OpenChunks(world, opened))
            {
                WorldChunk marker = chunk.Value.GetComponent<WorldChunk>();
                if (marker != null && marker.World == world && marker.Coords == chunk.Key)
                {
                    continue;
                }

                if (marker == null)
                {
                    chunk.Value.gameObject.SetActive(false);
                    marker = Undo.AddComponent<WorldChunk>(chunk.Value.gameObject);
                    SetMarker(marker, world, chunk.Key, null, null);
                    chunk.Value.gameObject.SetActive(true);
                }
                else
                {
                    SetMarker(marker, world, chunk.Key, marker.CoastMap, marker.CoastSource);
                }

                EditorSceneManager.SaveScene(chunk.Value.gameObject.scene);
                changed.Add(chunk.Value.gameObject.scene.name);
            }
        }
        finally
        {
            CloseScenes(opened);
        }

        return changed;
    }

    /// <summary>
    /// Adds to each chunk of <paramref name="world"/> a layer for every material its own cells or its neighbours'
    /// border strips (<see cref="WorldChunk.SkirtCells"/> wide) show that it does not draw yet, at its place in the
    /// style's order, opening the scenes that are not open, and saves the ones it changed. Returns what it added.
    /// </summary>
    public static List<string> SyncLayerLists(WorldDefinition world)
    {
        List<string> added = new();
        List<Scene> opened = new();
        try
        {
            Dictionary<Vector2Int, Grid> grids = OpenChunks(world, opened);
            WorldCoastBake.WorldCells cells = ReadWorld(world, grids);
            foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
            {
                RectInt reach = Grow(ChunkCoords.Bounds(chunk.Key), WorldChunk.SkirtCells);
                bool changed = false;
                foreach (TerrainMaterial material in SortedByOrder(cells.MaterialsIn(reach)))
                {
                    if (LayerOf(chunk.Value, material) == null && GroundLayerMigration.AddLayer(chunk.Value, material) != null)
                    {
                        added.Add($"{chunk.Value.gameObject.scene.name}: {material.name}");
                        changed = true;
                    }
                }

                if (changed)
                {
                    RefreshLinks(chunk.Value);
                    EditorSceneManager.SaveScene(chunk.Value.gameObject.scene);
                }
            }
        }
        finally
        {
            CloseScenes(opened);
        }

        return added;
    }

    /// <summary>
    /// Bakes the coast map of every chunk of <paramref name="world"/> from the whole world's ground
    /// (<see cref="WorldCoastBake"/>), or with <paramref name="onlyChanged"/> only of the chunks whose sea or shore
    /// within the bake's reach changed since their last bake (<see cref="IsCoastStale"/>), opening the scenes that are
    /// not open: a 16-bit PNG beside each chunk scene showing waves (<c>World_name_x_y_Coast.png</c>), set on its
    /// <see cref="WorldChunk"/> with what it was baked from; a chunk showing none loses its map. Saves the scenes it
    /// changed. Returns a line per chunk with what it did and how long it took.
    /// </summary>
    public static List<string> BakeCoastMaps(WorldDefinition world, bool onlyChanged = false)
    {
        List<string> report = new();
        List<Scene> opened = new();
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        try
        {
            Dictionary<Vector2Int, Grid> grids = OpenChunks(world, opened);
            System.Diagnostics.Stopwatch read = System.Diagnostics.Stopwatch.StartNew();
            WorldCoastBake.WorldCells cells = ReadWorld(world, grids);
            report.Add($"Read the world's ground in {read.ElapsedMilliseconds} ms.");
            foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
            {
                WorldChunk marker = chunk.Value.GetComponent<WorldChunk>();
                TerrainBlendRenderer renderer = chunk.Value.GetComponent<TerrainBlendRenderer>();
                if (marker == null || renderer == null)
                {
                    report.Add($"{chunk.Value.gameObject.scene.name}: no {nameof(WorldChunk)} or terrain renderer; skipped.");
                    continue;
                }

                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                List<TerrainMaterial> materials = renderer.Layers.Select(l => l != null ? l.Material : null).ToList();
                string source = WorldCoastBake.SourceOf(cells, style, materials, ChunkCoords.Bounds(chunk.Key));
                if (onlyChanged && !IsCoastStale(marker, source))
                {
                    report.Add($"{chunk.Value.gameObject.scene.name}: its sea has not changed; kept.");
                    continue;
                }

                ushort[] encoded = WorldCoastBake.BakeChunk(cells, style, materials, ChunkCoords.Bounds(chunk.Key), out int width, out int height);
                string path = CoastMapPath(world, chunk.Key);
                Texture2D baked = null;
                if (encoded != null)
                {
                    Texture2D texture = WorldCoastBake.ToTexture(encoded, width, height, Path.GetFileNameWithoutExtension(path));
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                    Object.DestroyImmediate(texture);
                    AssetDatabase.ImportAsset(path);
                    ConfigureCoastImport(path);
                    baked = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                else if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }

                string recorded = baked != null ? source : string.Empty;
                if (marker.CoastMap != baked || (marker.CoastSource ?? string.Empty) != recorded)
                {
                    SetMarker(marker, world, chunk.Key, baked, recorded);
                    EditorSceneManager.SaveScene(chunk.Value.gameObject.scene);
                }

                report.Add($"{chunk.Value.gameObject.scene.name}: {(baked != null ? $"baked {width} x {height}" : "no waves, nothing baked")} in {watch.ElapsedMilliseconds} ms.");
            }
        }
        finally
        {
            CloseScenes(opened);
        }

        return report;
    }

    /// <summary>
    /// The chunks of <paramref name="world"/> whose coast map is out of date (<see cref="IsCoastStale"/>), opening the
    /// scenes that are not open and closing them again unchanged. About a millisecond a chunk once open.
    /// </summary>
    public static List<Vector2Int> StaleCoastMaps(WorldDefinition world)
    {
        List<Vector2Int> stale = new();
        List<Scene> opened = new();
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        try
        {
            Dictionary<Vector2Int, Grid> grids = OpenChunks(world, opened);
            WorldCoastBake.WorldCells cells = ReadWorld(world, grids);
            foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
            {
                WorldChunk marker = chunk.Value.GetComponent<WorldChunk>();
                TerrainBlendRenderer renderer = chunk.Value.GetComponent<TerrainBlendRenderer>();
                if (marker == null || renderer == null)
                {
                    continue;
                }

                List<TerrainMaterial> materials = renderer.Layers.Select(l => l != null ? l.Material : null).ToList();
                if (IsCoastStale(marker, WorldCoastBake.SourceOf(cells, style, materials, ChunkCoords.Bounds(chunk.Key))))
                {
                    stale.Add(chunk.Key);
                }
            }
        }
        finally
        {
            CloseScenes(opened, false);
        }

        return stale;
    }

    /// <summary>
    /// Whether <paramref name="marker"/>'s coast map is out of date against <paramref name="source"/>, what it would be
    /// baked from now (<see cref="WorldCoastBake.SourceOf"/>): a chunk showing waves needs a map baked from that
    /// source, and one showing none needs no map.
    /// </summary>
    public static bool IsCoastStale(WorldChunk marker, string source)
    {
        if (source == WorldCoastBake.NoWaves)
        {
            return marker.CoastMap != null;
        }

        return marker.CoastMap == null || marker.CoastSource != source;
    }

    /// <summary>
    /// The path of the coast map baked for the chunk at <paramref name="coords"/> of <paramref name="world"/>, beside
    /// its scene.
    /// </summary>
    public static string CoastMapPath(WorldDefinition world, Vector2Int coords)
    {
        string folder = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(world)).Replace('\\', '/')}/Chunks";
        return $"{folder}/{ChunkSceneName(world, coords)}_Coast.png";
    }

    /// <summary>
    /// The import settings of a baked coast map: a linear, uncompressed R16 texture without mipmaps, bilinear and
    /// clamped, as the renderer builds its own, and not readable.
    /// </summary>
    private static void ConfigureCoastImport(string path)
    {
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
        settings.format = TextureImporterFormat.R16;
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(settings);
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Sets <paramref name="marker"/>'s world, coordinates, coast map and what that map was baked from through its
    /// serialized object, so the scene records the change.
    /// </summary>
    private static void SetMarker(WorldChunk marker, WorldDefinition world, Vector2Int coords, Texture2D coastMap, string coastSource)
    {
        SerializedObject data = new(marker);
        data.FindProperty("world").objectReferenceValue = world;
        data.FindProperty("coords").vector2IntValue = coords;
        data.FindProperty("coastMap").objectReferenceValue = coastMap;
        data.FindProperty("coastSource").stringValue = coastSource ?? string.Empty;
        data.ApplyModifiedProperties();
        if (marker.isActiveAndEnabled)
        {
            marker.enabled = false;
            marker.enabled = true;
        }
    }

    /// <summary>
    /// The world's ground over its cells, from the layers of <paramref name="grids"/>' terrain renderers, each read
    /// only inside its own chunk.
    /// </summary>
    private static WorldCoastBake.WorldCells ReadWorld(WorldDefinition world, Dictionary<Vector2Int, Grid> grids)
    {
        WorldCoastBake.WorldCells cells = new(world.LocalBounds);
        foreach (KeyValuePair<Vector2Int, Grid> chunk in grids)
        {
            if (!chunk.Value.TryGetComponent(out TerrainBlendRenderer renderer))
            {
                continue;
            }

            foreach (TerrainBlendRenderer.Layer layer in renderer.Layers)
            {
                if (layer != null)
                {
                    cells.Add(layer.Tilemap, layer.Material, ChunkCoords.Bounds(chunk.Key));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// Adds <paramref name="material"/>'s layer to each open neighbour of the chunk at <paramref name="chunk"/> whose
    /// skirt reaches one of <paramref name="cells"/> and that does not draw it yet.
    /// </summary>
    private static void CoverInNeighbours(WorldDefinition world, Vector2Int chunk, List<Vector3Int> cells, TerrainMaterial material)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2Int at = chunk + new Vector2Int(dx, dy);
                if (at == chunk)
                {
                    continue;
                }

                RectInt reach = Grow(ChunkCoords.Bounds(at), WorldChunk.SkirtCells);
                if (!cells.Any(c => reach.Contains(new Vector2Int(c.x, c.y))))
                {
                    continue;
                }

                Grid grid = OpenChunkGrid(world, at);
                if (grid != null && LayerOf(grid, material) == null && GroundLayerMigration.AddLayer(grid, material) != null)
                {
                    RefreshLinks(grid);
                }
            }
        }
    }

    /// <summary>
    /// Has the chunk marker on <paramref name="grid"/>, if any, give its neighbours its border cells again after its
    /// layers or walls changed.
    /// </summary>
    private static void RefreshLinks(Grid grid)
    {
        if (grid.TryGetComponent(out WorldChunk marker) && marker.isActiveAndEnabled)
        {
            marker.RefreshLinks();
        }
    }

    /// <summary>
    /// The wall module of <paramref name="grid"/>'s <see cref="WallsName"/>, or null.
    /// </summary>
    private static DualGridTilemapModule WallsOf(Grid grid)
    {
        Transform walls = grid.transform.Find(WallsName);
        return walls != null ? walls.GetComponent<DualGridTilemapModule>() : null;
    }

    /// <summary>
    /// <paramref name="cells"/> as cells at z = 0, grouped by the chunk owning them.
    /// </summary>
    private static Dictionary<Vector2Int, List<Vector3Int>> SplitByChunk(IEnumerable<Vector2Int> cells)
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

        return byChunk;
    }

    /// <summary>
    /// <paramref name="materials"/> in the style's layer order.
    /// </summary>
    private static List<TerrainMaterial> SortedByOrder(IEnumerable<TerrainMaterial> materials)
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        return materials.OrderBy(m => style.LayerOrderIndexOf(m)).ToList();
    }

    /// <summary>
    /// The grids of every chunk of <paramref name="world"/>, opening the scenes that are not open beside the others and
    /// adding them to <paramref name="opened"/>.
    /// </summary>
    private static Dictionary<Vector2Int, Grid> OpenChunks(WorldDefinition world, List<Scene> opened)
    {
        Dictionary<Vector2Int, Grid> grids = new();
        foreach (KeyValuePair<Vector2Int, Scene> chunk in OpenChunkScenes(world, opened))
        {
            Grid grid = SceneQuery.FindFirst<Grid>(chunk.Value);
            if (grid != null)
            {
                grids[chunk.Key] = grid;
            }
        }

        return grids;
    }

    /// <summary>
    /// The scenes of every chunk of <paramref name="world"/> by their coordinates, opening the ones that are not open
    /// beside the others and adding them to <paramref name="opened"/>.
    /// </summary>
    public static Dictionary<Vector2Int, Scene> OpenChunkScenes(WorldDefinition world, List<Scene> opened)
    {
        Dictionary<Vector2Int, Scene> scenes = new();
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            if (chunk == null || chunk.Scene == null || !chunk.Scene.IsValid)
            {
                continue;
            }

            Scene scene = SceneManager.GetSceneByPath(chunk.Scene.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(chunk.Scene.ScenePath, OpenSceneMode.Additive);
                opened.Add(scene);
            }

            scenes[chunk.Coords] = scene;
        }

        return scenes;
    }

    /// <summary>
    /// Closes the scenes <see cref="OpenChunkScenes"/> opened, saving the changed ones first unless
    /// <paramref name="save"/> is false.
    /// </summary>
    public static void CloseScenes(List<Scene> opened, bool save = true)
    {
        foreach (Scene scene in opened)
        {
            if (save && scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// Opens the scene of the chunk at <paramref name="coords"/> of <paramref name="world"/>, with the scenes of its
    /// eight neighbours when <paramref name="withNeighbours"/>, beside the open scenes (closing an untitled scene with no
    /// changes), and makes it the active scene. Scenes already open stay as they are. Returns the chunk's scene; throws
    /// when the world has no chunk there.
    /// </summary>
    public static Scene OpenChunk(WorldDefinition world, Vector2Int coords, bool withNeighbours = true)
    {
        if (!world.TryGetChunkScene(coords, out SceneDefinition definition) || !definition.IsValid)
        {
            throw new System.ArgumentException($"{world.name} has no chunk at {coords}.", nameof(coords));
        }

        Scene scene = OpenBeside(definition.ScenePath);
        if (withNeighbours)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2Int at = coords + new Vector2Int(dx, dy);
                    if (at != coords && world.TryGetChunkScene(at, out SceneDefinition neighbour) && neighbour.IsValid)
                    {
                        OpenBeside(neighbour.ScenePath);
                    }
                }
            }
        }

        SceneManager.SetActiveScene(scene);
        return scene;
    }

    /// <summary>
    /// Opens the root scene of <paramref name="world"/> beside the open scenes, like <see cref="OpenChunk"/>.
    /// </summary>
    public static Scene OpenRoot(WorldDefinition world)
    {
        if (world.RootScene == null || !world.RootScene.IsValid)
        {
            throw new System.InvalidOperationException($"{world.name} has no root scene.");
        }

        return OpenBeside(world.RootScene.ScenePath);
    }

    /// <summary>
    /// The coordinates of <paramref name="world"/>'s chunks whose scenes are open.
    /// </summary>
    public static List<Vector2Int> OpenChunkCoords(WorldDefinition world)
    {
        List<Vector2Int> open = new();
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            if (chunk != null && chunk.Scene != null && chunk.Scene.IsValid && IsOpen(chunk.Scene.ScenePath))
            {
                open.Add(chunk.Coords);
            }
        }

        return open;
    }

    /// <summary>
    /// The open scenes of <paramref name="world"/>'s chunks, its root scene included when <paramref name="root"/>,
    /// except the chunks in <paramref name="keep"/>: the scenes <see cref="CloseChunks"/> closes.
    /// </summary>
    public static List<Scene> OpenScenesExcept(WorldDefinition world, ICollection<Vector2Int> keep, bool root)
    {
        List<Scene> scenes = new();
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            if (chunk != null && chunk.Scene != null && chunk.Scene.IsValid && (keep == null || !keep.Contains(chunk.Coords)) && IsOpen(chunk.Scene.ScenePath))
            {
                scenes.Add(SceneManager.GetSceneByPath(chunk.Scene.ScenePath));
            }
        }

        if (root && world.RootScene != null && world.RootScene.IsValid && IsOpen(world.RootScene.ScenePath))
        {
            scenes.Add(SceneManager.GetSceneByPath(world.RootScene.ScenePath));
        }

        return scenes;
    }

    /// <summary>
    /// Closes the open scenes of <paramref name="world"/>'s chunks except those in <paramref name="keep"/> (and its root
    /// scene too when <paramref name="root"/>), discarding their unsaved changes: the caller asks to save them first.
    /// The last scene open is never closed. Returns how many it closed.
    /// </summary>
    public static int CloseChunks(WorldDefinition world, ICollection<Vector2Int> keep, bool root = false)
    {
        int closed = 0;
        foreach (Scene scene in OpenScenesExcept(world, keep, root))
        {
            if (SceneManager.sceneCount > 1 && EditorSceneManager.CloseScene(scene, true))
            {
                closed++;
            }
        }

        return closed;
    }

    /// <summary>
    /// Places a <see cref="WorldExit"/> on <paramref name="world"/>'s cell <paramref name="cell"/> (its lower-left
    /// cell, counted from the world's origin) in the open chunk owning that cell, under the scene's
    /// <see cref="ExitsRootName"/> root (made when missing), with a new <see cref="PersistentId"/>, leading to
    /// <paramref name="target"/>'s entry <paramref name="entryName"/> and covering <paramref name="size"/> cells, as
    /// one undo step. Throws when that chunk is not open.
    /// </summary>
    public static WorldExit AddExit(WorldDefinition world, Vector2Int cell, WorldDefinition target, string entryName, Vector2Int size)
    {
        Vector2Int coords = ChunkCoords.ChunkOf(cell);
        Grid grid = OpenChunkGrid(world, coords);
        if (grid == null)
        {
            throw new System.InvalidOperationException($"Chunk {coords} of {world.name}, which owns cell {cell}, is not open.");
        }

        Scene scene = grid.gameObject.scene;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Add world exit");
        int undoGroup = Undo.GetCurrentGroup();
        GameObject root = null;
        foreach (GameObject candidate in scene.GetRootGameObjects())
        {
            if (candidate.name == ExitsRootName)
            {
                root = candidate;
            }
        }

        if (root == null)
        {
            root = new GameObject(ExitsRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Add world exit");
        }

        GameObject exitObject = new($"Exit_{(target != null ? target.name : "Nowhere")}");
        exitObject.transform.SetParent(root.transform, false);
        exitObject.transform.position = world.ToWorld(cell + Vector2.one * 0.5f);
        WorldExit exit = exitObject.AddComponent<WorldExit>();
        PersistentIds.SetId(exitObject.GetComponent<PersistentId>(), PersistentIds.NewId());
        ConfigureExit(exit, target, entryName, size);
        Undo.RegisterCreatedObjectUndo(exitObject, "Add world exit");
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        return exit;
    }

    /// <summary>
    /// Sets where <paramref name="exit"/> leads and how many cells it covers, through its serialized object, recorded
    /// for undo.
    /// </summary>
    public static void ConfigureExit(WorldExit exit, WorldDefinition target, string entryName, Vector2Int size)
    {
        SerializedObject data = new(exit);
        data.FindProperty("targetWorld").objectReferenceValue = target;
        data.FindProperty("targetEntry").stringValue = entryName ?? string.Empty;
        data.FindProperty("sizeInCells").vector2IntValue = Vector2Int.Max(size, Vector2Int.one);
        data.ApplyModifiedProperties();
    }

    /// <summary>
    /// The exits in <paramref name="world"/>'s open chunk scenes, in chunk order.
    /// </summary>
    public static List<WorldExit> OpenExits(WorldDefinition world)
    {
        List<WorldExit> exits = new();
        foreach (Vector2Int coords in OpenChunkCoords(world))
        {
            Grid grid = OpenChunkGrid(world, coords);
            if (grid != null)
            {
                exits.AddRange(SceneQuery.FindAll<WorldExit>(grid.gameObject.scene));
            }
        }

        return exits;
    }

    /// <summary>
    /// Gives <paramref name="world"/> the entry <paramref name="entryName"/> at <paramref name="localPosition"/> from
    /// its origin, replacing the one of that name or adding it after the others (so the default stays first), and
    /// saves the world.
    /// </summary>
    public static void SetEntry(WorldDefinition world, string entryName, Vector2 localPosition, bool waystone)
    {
        List<WorldDefinition.Entry> entries = new();
        bool replaced = false;
        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (entry != null && entry.Name == entryName)
            {
                entries.Add(new WorldDefinition.Entry(entryName, localPosition, waystone));
                replaced = true;
            }
            else
            {
                entries.Add(entry);
            }
        }

        if (!replaced)
        {
            entries.Add(new WorldDefinition.Entry(entryName, localPosition, waystone));
        }

        Undo.RecordObject(world, "Set world entry");
        world.SetEntries(entries.ToArray());
        AssetDatabase.SaveAssetIfDirty(world);
    }

    /// <summary>
    /// Lists <paramref name="world"/> in the project's <see cref="GameScenes"/>, unless it is there already. Returns
    /// whether it added it.
    /// </summary>
    public static bool ListInGameScenes(WorldDefinition world)
    {
        GameScenes gameScenes = ProjectScenes.LoadGameScenes();
        if (gameScenes == null || gameScenes.Worlds.Contains(world))
        {
            return false;
        }

        SerializedObject data = new(gameScenes);
        SerializedProperty worlds = data.FindProperty("worlds");
        worlds.arraySize++;
        worlds.GetArrayElementAtIndex(worlds.arraySize - 1).objectReferenceValue = world;
        data.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>
    /// Whether the scene at <paramref name="path"/> is open and loaded.
    /// </summary>
    private static bool IsOpen(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        return scene.IsValid() && scene.isLoaded;
    }

    /// <summary>
    /// Opens the scene at <paramref name="path"/> beside the open scenes, or returns it when it is open, then closes an
    /// untitled scene with no changes, which would only stand in the way of saving. Opening beside the others keeps the
    /// assets already loaded alive, which opening a single scene would unload.
    /// </summary>
    private static Scene OpenBeside(string path)
    {
        if (IsOpen(path))
        {
            return SceneManager.GetSceneByPath(path);
        }

        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene open = SceneManager.GetSceneAt(i);
            if (string.IsNullOrEmpty(open.path) && !open.isDirty && SceneManager.sceneCount > 1)
            {
                EditorSceneManager.CloseScene(open, true);
            }
        }

        return scene;
    }

    /// <summary>
    /// <paramref name="cells"/> grown by <paramref name="by"/> cells on every side.
    /// </summary>
    private static RectInt Grow(RectInt cells, int by)
    {
        return new RectInt(cells.xMin - by, cells.yMin - by, cells.width + 2 * by, cells.height + 2 * by);
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
    /// Gives a grid, inactive while it is set up, a terrain renderer with the project's style drawing one empty grass
    /// layer, its <see cref="NavigationGrid2D"/> and its <see cref="AreaTerrain"/>.
    /// </summary>
    private static void SetUpGround(Grid grid)
    {
        TerrainBlendRenderer renderer = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath));
        GroundLayerMigration.AddLayer(grid, MaterialNamed("Grass"));
        SetUpNavigation(grid);
        SetUpTerrainEdits(grid);
    }

    /// <summary>
    /// Adds a white Global Light 2D at 0.85 under <paramref name="parent"/> (at the scene's root when null).
    /// </summary>
    private static void AddGlobalLight(Transform parent)
    {
        GameObject lightObject = new("Global Light 2D");
        if (parent != null)
        {
            lightObject.transform.SetParent(parent, false);
        }

        Light2D light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.color = Color.white;
        light.intensity = GlobalLightIntensity;
    }

    /// <summary>
    /// Adds the scene at <paramref name="scenePath"/> to the build list, enabled, unless it is there already or it is a
    /// dev scene (under <see cref="SceneBuildValidator.DevFolder"/>), played from the editor only.
    /// </summary>
    private static void AddToBuild(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenePath.StartsWith(SceneBuildValidator.DevFolder) || scenes.Any(s => s.path == scenePath))
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
