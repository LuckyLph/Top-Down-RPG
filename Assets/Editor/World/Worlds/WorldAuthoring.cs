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
/// <see cref="WorldChunk"/>, a terrain renderer over one grass layer and a <see cref="NavigationGrid2D"/> for its
/// navigation page. Every scene gets a
/// <see cref="SceneDefinition"/> and a place in the build list. Painting ground, walls and raised ground into the open
/// chunks, keeping each chunk's layer list covering its neighbours' border materials, and baking the coast maps
/// (<see cref="WorldCoastBake"/>) work across a whole world. The World Editor window (Phase 5) builds on these steps.
/// </summary>
public static class WorldAuthoring
{
    public const string WorldsFolder = "Assets/Worlds";
    public const string StylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    public const string AreaTemplatePath = "Assets/Settings/Scenes/AreaTemplate.unity";
    public const string WallsName = "CollisionTilemap";
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
    /// <see cref="ChunkLifetimeScope"/> and a grid at the world's origin with its <see cref="WorldChunk"/> and a terrain
    /// renderer drawing one empty grass layer, its definition, its entry in the world and its place in the build list. Returns the scene path. Throws
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
            gridObject.AddComponent<WorldChunk>().Configure(world, coords, null);
            TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
            renderer.Configure(AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath));
            GroundLayerMigration.AddLayer(gridObject.GetComponent<Grid>(), MaterialNamed("Grass"));
            SetUpNavigation(gridObject.GetComponent<Grid>());
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
    /// Gives <paramref name="grid"/> the walls of the area template: a copy of its empty <see cref="WallsName"/>, a
    /// dual-grid data tilemap on the Obstacles layer with its collider, its wall module and its half-cell render
    /// tilemap, placed after the ground layers. Returns its module.
    /// </summary>
    public static DualGridTilemapModule AddWalls(Grid grid)
    {
        Scene template = EditorSceneManager.OpenScene(AreaTemplatePath, OpenSceneMode.Additive);
        try
        {
            Grid templateGrid = SceneQuery.FindFirst<Grid>(template);
            Transform source = templateGrid != null ? templateGrid.transform.Find(WallsName) : null;
            if (source == null)
            {
                throw new System.InvalidOperationException($"{AreaTemplatePath} has no {WallsName} under its grid.");
            }

            GameObject copy = Object.Instantiate(source.gameObject, grid.transform, false);
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

            RefreshLinks(grid);
            return module;
        }
        finally
        {
            EditorSceneManager.CloseScene(template, true);
        }
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
                    SetMarker(marker, world, chunk.Key, null);
                    chunk.Value.gameObject.SetActive(true);
                }
                else
                {
                    SetMarker(marker, world, chunk.Key, marker.CoastMap);
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
    /// (<see cref="WorldCoastBake"/>), opening the scenes that are not open: a 16-bit PNG beside each chunk scene
    /// showing waves (<c>World_name_x_y_Coast.png</c>), set on its <see cref="WorldChunk"/>; a chunk showing none loses
    /// its map. Saves the scenes it changed. Returns a line per chunk with what it did and how long it took.
    /// </summary>
    public static List<string> BakeCoastMaps(WorldDefinition world)
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
                ushort[] encoded = WorldCoastBake.BakeChunk(cells, style, renderer.Layers.Select(l => l != null ? l.Material : null), ChunkCoords.Bounds(chunk.Key), out int width, out int height);
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

                if (marker.CoastMap != baked)
                {
                    SetMarker(marker, world, chunk.Key, baked);
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
    /// Sets <paramref name="marker"/>'s world, coordinates and coast map through its serialized object, so the scene
    /// records the change.
    /// </summary>
    private static void SetMarker(WorldChunk marker, WorldDefinition world, Vector2Int coords, Texture2D coastMap)
    {
        SerializedObject data = new(marker);
        data.FindProperty("world").objectReferenceValue = world;
        data.FindProperty("coords").vector2IntValue = coords;
        data.FindProperty("coastMap").objectReferenceValue = coastMap;
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

            Grid grid = SceneQuery.FindFirst<Grid>(scene);
            if (grid != null)
            {
                grids[chunk.Coords] = grid;
            }
        }

        return grids;
    }

    /// <summary>
    /// Saves and closes the scenes <see cref="OpenChunks"/> opened.
    /// </summary>
    private static void CloseScenes(List<Scene> opened)
    {
        foreach (Scene scene in opened)
        {
            if (scene.isDirty)
            {
                EditorSceneManager.SaveScene(scene);
            }

            EditorSceneManager.CloseScene(scene, true);
        }
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
