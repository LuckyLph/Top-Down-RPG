using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.Tilemaps;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="TerrainBlendRenderer"/>: tile changes from painting, from <see cref="AreaTerrain"/> on the host
/// and from replicated edits on a client reach the control map's texture, and so do undos; a render uploads pending
/// changes once and stops listening; the chunks cover the layers and follow the bounds as they grow and shrink;
/// disabling unsubscribes from everything and destroys what was built; nothing runs or allocates per frame while no
/// cell changes; fixing the style rebuilds a renderer that could not build; the style is never written. Each test
/// builds its own style with <see cref="TestTerrainStyle"/>.
/// </summary>
public class TerrainBlendRendererTests
{
    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tilemap[] layers;
    private Tile tile;
    private TerrainStyle style;

    /// <summary>
    /// Builds an inactive grid with grass, gravel and stone layer tilemaps (grass painted over a 20 x 12 block, gravel
    /// along a row of it) and a style of three plain materials.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        root = Track(new GameObject("TerrainBlendGrid", typeof(Grid)));
        root.SetActive(false);
        tile = Track(ScriptableObject.CreateInstance<Tile>());
        layers = new Tilemap[3];
        string[] names = { "Grass", "Gravel", "Stone" };
        for (int i = 0; i < layers.Length; i++)
        {
            GameObject layerObject = new(names[i], typeof(Tilemap));
            layerObject.transform.SetParent(root.transform);
            layers[i] = layerObject.GetComponent<Tilemap>();
        }

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                layers[0].SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        for (int x = 2; x < 18; x++)
        {
            layers[1].SetTile(new Vector3Int(x, 5, 0), tile);
        }

        style = TestTerrainStyle.Create(createdObjects);
    }

    /// <summary>
    /// Clears the undo history of the layers and destroys the grid, the tile, the style and the textures the test
    /// created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Tilemap layer in layers)
        {
            if (layer != null)
            {
                Undo.ClearUndo(layer);
            }
        }

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
    /// Enabling builds the control map, the arrays (keeping no copy in memory), the lookup texture, the material and
    /// the chunks, and waits for no render.
    /// </summary>
    [Test]
    public void Enabling_BuildsEverything()
    {
        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.ControlMap.Texture, Is.Not.Null);
        Assert.That(renderer.AlbedoArray.depth, Is.EqualTo(3));
        Assert.That(renderer.FieldArray.depth, Is.EqualTo(3));
        Assert.That(renderer.AlbedoArray.isReadable, Is.False);
        Assert.That(renderer.FieldArray.isReadable, Is.False);
        Assert.That(renderer.Lut, Is.Not.Null);
        Assert.That(renderer.RuntimeMaterial.shader, Is.SameAs(style.Shader));
        Assert.That(renderer.RuntimeMaterial.GetTexture("_ControlMap"), Is.SameAs(renderer.ControlMap.Texture));
        Assert.That(renderer.Chunks, Is.Not.Empty);
        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(TexelOf(renderer, new Vector3Int(4, 5, 0)), Is.EqualTo(3));
    }

    /// <summary>
    /// Painting a cell on a layer's tilemap updates its texel once the pending changes are uploaded, which waits for
    /// the next render.
    /// </summary>
    [Test]
    public void PaintingADataTilemap_UpdatesItsTexel()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Vector3Int cell = new(7, 8, 0);

        layers[2].SetTile(cell, tile);

        Assert.That(renderer.IsFlushScheduled, Is.True);
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(1), "The texture changes only when the changes are uploaded.");
        renderer.FlushPendingChanges();
        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(5));
    }

    /// <summary>
    /// The next render after a change uploads it, once: the renderer then stops listening to render callbacks, so a
    /// later frame runs nothing.
    /// </summary>
    [Test]
    public void ARender_UploadsPendingChangesOnce_ThenStopsListening()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Vector3Int cell = new(7, 8, 0);
        layers[2].SetTile(cell, tile);

        RenderOneFrame();

        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(1));
        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(5));

        RenderOneFrame();

        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(1), "Nothing runs on a frame without changes.");
    }

    /// <summary>
    /// Undoing a paint restores the cell's texel: the renderer reads its layers again on the undo, since an undo
    /// restores the tilemap without reporting the cells it changed.
    /// </summary>
    [Test]
    public void UndoingAPaint_RestoresItsTexel()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Vector3Int cell = new(7, 8, 0);
        PaintWithUndo(layers[2], cell);
        renderer.FlushPendingChanges();
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(5));
        int undos = renderer.HandledUndoCount;

        Undo.PerformUndo();
        renderer.FlushPendingChanges();

        Assert.That(renderer.HandledUndoCount, Is.EqualTo(undos + 1));
        Assert.That(layers[2].HasTile(cell), Is.False);
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(1));
    }

    /// <summary>
    /// Painting outside the bounds grows the control map, adds chunks over the new cells and moves the material's
    /// control rectangle.
    /// </summary>
    [Test]
    public void PaintingOutsideTheBounds_GrowsTheMapAndAddsChunks()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        int chunks = renderer.Chunks.Count;
        Vector3Int far = new(60, -30, 0);

        layers[0].SetTile(far, tile);
        renderer.FlushPendingChanges();

        Assert.That(renderer.ControlMap.Bounds.Contains(far), Is.True);
        Assert.That(renderer.Chunks.Count, Is.GreaterThan(chunks));
        Assert.That(TexelOf(renderer, far), Is.EqualTo(1));
        Assert.That(Covered(renderer, far), Is.True);
        BoundsInt bounds = renderer.ControlMap.Bounds;
        Assert.That(renderer.RuntimeMaterial.GetVector("_ControlRect"), Is.EqualTo(new Vector4(bounds.xMin, bounds.yMin, bounds.size.x, bounds.size.y)));
    }

    /// <summary>
    /// An undo that shrinks the control map removes the chunks its bounds no longer reach, with their objects and
    /// meshes.
    /// </summary>
    [Test]
    public void AnUndoThatShrinksTheMap_RemovesTheChunksItNoLongerReaches()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        int chunks = renderer.Chunks.Count;
        Vector3Int far = new(60, -30, 0);
        PaintWithUndo(layers[0], far);
        renderer.FlushPendingChanges();
        List<Object> farChunks = new();
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            if (Covers(chunk, far))
            {
                farChunks.Add(chunk.gameObject);
                farChunks.Add(chunk.GetComponent<MeshFilter>().sharedMesh);
            }
        }

        Assert.That(farChunks, Is.Not.Empty);

        Undo.PerformUndo();
        renderer.FlushPendingChanges();

        Assert.That(renderer.ControlMap.Bounds.Contains(far), Is.False);
        Assert.That(renderer.Chunks.Count, Is.EqualTo(chunks));
        Assert.That(Covered(renderer, far), Is.False);
        foreach (Object removed in farChunks)
        {
            Assert.That(removed == null, Is.True, $"{removed} was not destroyed.");
        }
    }

    /// <summary>
    /// A renderer over layers with no filled cell builds no chunks, and painting a first cell creates them.
    /// </summary>
    [Test]
    public void AnEmptyMap_GetsChunksOnceACellIsPainted()
    {
        foreach (Tilemap layer in layers)
        {
            layer.ClearAllTiles();
        }

        TerrainBlendRenderer renderer = CreateRenderer();
        Assert.That(renderer.ControlMap.IsEmpty, Is.True);
        Assert.That(renderer.Chunks, Is.Empty);
        Vector3Int cell = new(3, 3, 0);

        layers[0].SetTile(cell, tile);
        renderer.FlushPendingChanges();

        Assert.That(renderer.Chunks, Is.Not.Empty);
        Assert.That(Covered(renderer, cell), Is.True);
        Assert.That(TexelOf(renderer, cell), Is.EqualTo(1));
    }

    /// <summary>
    /// An edit through <see cref="AreaTerrain"/> on the authority updates the texel of every cell it changes.
    /// </summary>
    [Test]
    public void AreaTerrainEdit_OnTheHost_UpdatesTheTexel()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        AreaTerrain terrain = CreateTerrain(FixedGameAuthority.Authoritative);
        Vector3Int[] cells = { new(3, 2, 0), new(4, 2, 0) };

        Assert.That(terrain.TrySetCells(layers[2], cells, filled: true), Is.True);
        Assert.That(terrain.TrySetCells(layers[0], new[] { new Vector3Int(10, 10, 0) }, filled: false), Is.True);
        renderer.FlushPendingChanges();

        Assert.That(TexelOf(renderer, cells[0]), Is.EqualTo(5));
        Assert.That(TexelOf(renderer, cells[1]), Is.EqualTo(5));
        Assert.That(TexelOf(renderer, new Vector3Int(10, 10, 0)), Is.EqualTo(0));
    }

    /// <summary>
    /// The host's edits applied on a client with <see cref="AreaTerrain.ApplyReplicated"/> update the texels too.
    /// </summary>
    [Test]
    public void ReplicatedEdit_OnAClient_UpdatesTheTexel()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        AreaTerrain terrain = CreateTerrain(new FixedGameAuthority(false));

        terrain.ApplyReplicated(new[]
        {
            new TerrainEdit(2, new Vector3Int(6, 6, 0), true),
            new TerrainEdit(1, new Vector3Int(4, 5, 0), false)
        });
        renderer.FlushPendingChanges();

        Assert.That(TexelOf(renderer, new Vector3Int(6, 6, 0)), Is.EqualTo(5));
        Assert.That(TexelOf(renderer, new Vector3Int(4, 5, 0)), Is.EqualTo(1));
    }

    /// <summary>
    /// Disabling stops following tile changes, render callbacks (even with an upload pending), undos and style
    /// changes, and destroys the control map's texture, the arrays, the lookup texture, the material, the chunk meshes
    /// and the chunk objects.
    /// </summary>
    [Test]
    public void Disabling_UnsubscribesFromEverything_AndDestroysWhatWasBuilt()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        PaintWithUndo(layers[2], new Vector3Int(1, 1, 0));
        Assert.That(renderer.IsFlushScheduled, Is.True);
        List<Object> built = new() { renderer.ControlMap.Texture, renderer.AlbedoArray, renderer.FieldArray, renderer.Lut, renderer.RuntimeMaterial };
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            built.Add(chunk.gameObject);
            built.Add(chunk.GetComponent<MeshFilter>().sharedMesh);
        }

        renderer.enabled = false;
        int changes = renderer.HandledChangeCount;
        int renders = renderer.RenderCallbackCount;
        int undos = renderer.HandledUndoCount;
        layers[0].SetTile(new Vector3Int(2, 2, 0), null);
        RenderOneFrame();
        Undo.PerformUndo();
        SetGravelField(TestTerrainStyle.CreateTexture("Other field", new Color32(128, 128, 128, 0), createdObjects));

        Assert.That(renderer.HandledChangeCount, Is.EqualTo(changes), "A disabled renderer no longer follows tile changes.");
        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(renders), "A disabled renderer no longer waits for a render.");
        Assert.That(renderer.HandledUndoCount, Is.EqualTo(undos), "A disabled renderer no longer follows undos.");
        Assert.That(renderer.IsRebuildScheduled, Is.False, "A disabled renderer no longer follows its style.");
        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(renderer.ControlMap, Is.Null);
        Assert.That(renderer.Chunks, Is.Empty);
        foreach (Object created in built)
        {
            Assert.That(created == null, Is.True, $"{created} was not destroyed.");
        }
    }

    /// <summary>
    /// The chunk quads cover every cell of the layers' bounds, sit on the Terrain sorting layer at order 0 and draw
    /// with the runtime material, and none of them is saved.
    /// </summary>
    [Test]
    public void ChunkMeshes_CoverTheLayersBounds()
    {
        TerrainBlendRenderer renderer = CreateRenderer();

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Assert.That(Covered(renderer, new Vector3Int(x, y, 0)), Is.True, $"cell ({x}, {y})");
            }
        }

        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            Assert.That(chunk.sortingLayerName, Is.EqualTo("Terrain"));
            Assert.That(chunk.sortingOrder, Is.EqualTo(0));
            Assert.That(chunk.sharedMaterial, Is.SameAs(renderer.RuntimeMaterial));
            Assert.That((chunk.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave, Is.True);
        }
    }

    /// <summary>
    /// While no cell changes, nothing waits for the next render, and the upload a render would run does nothing and
    /// allocates nothing.
    /// </summary>
    [Test]
    public void NoCellChanged_NothingRunsOrAllocatesPerFrame()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        layers[2].SetTile(new Vector3Int(1, 1, 0), tile);
        renderer.FlushPendingChanges();
        int uploads = renderer.ControlMap.UploadCount;

        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(() =>
        {
            for (int frame = 0; frame < 10; frame++)
            {
                renderer.FlushPendingChanges();
            }
        }, Is.Not.AllocatingGCMemory());
        Assert.That(renderer.ControlMap.UploadCount, Is.EqualTo(uploads));
    }

    /// <summary>
    /// A renderer that could not build because its style lacked a texture rebuilds once the style is fixed in the
    /// inspector: the change schedules a rebuild, which then succeeds.
    /// </summary>
    [Test]
    public void FixingTheStyle_RebuildsARendererThatCouldNotBuild()
    {
        style = TestTerrainStyle.Create(createdObjects,
            TestTerrainStyle.PlainMaterial("Grass", TerrainStyle.CellMode.None, createdObjects),
            new TerrainStyle.MaterialSettings("Gravel", TestTerrainStyle.CreateTexture("Gravel albedo", new Color32(150, 120, 90, 255), createdObjects), null, 4, TerrainStyle.CellMode.WholeChips, Color.gray, new TerrainStyle.EdgeSettings()),
            TestTerrainStyle.PlainMaterial("Stone", TerrainStyle.CellMode.CutStones, createdObjects));
        LogAssert.Expect(LogType.Error, "TerrainStyle: material Gravel has chips or stones but no cell field texture.");
        TerrainBlendRenderer renderer = CreateRenderer();
        Assert.That(renderer.ControlMap, Is.Null);

        SetGravelField(TestTerrainStyle.CreateTexture("Gravel field", new Color32(128, 128, 128, 0), createdObjects));

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.Chunks, Is.Not.Empty);
    }

    /// <summary>
    /// Building, patching, rebuilding and disabling leave the style and its textures exactly as they were.
    /// </summary>
    [Test]
    public void TheStyle_IsNeverModified()
    {
        string before = EditorJsonUtility.ToJson(style);
        int dirtyCount = EditorUtility.GetDirtyCount(style);
        Color32[] albedoBefore = style.Materials[1].Albedo.GetPixels32();

        TerrainBlendRenderer renderer = CreateRenderer();
        layers[1].SetTile(new Vector3Int(3, 3, 0), tile);
        renderer.FlushPendingChanges();
        renderer.SetDebugView(true);
        renderer.Rebuild();
        renderer.enabled = false;

        Assert.That(EditorJsonUtility.ToJson(style), Is.EqualTo(before));
        Assert.That(style.Materials[1].Albedo.GetPixels32(), Is.EqualTo(albedoBefore));
        Assert.That(EditorUtility.GetDirtyCount(style), Is.EqualTo(dirtyCount));
    }

    /// <summary>
    /// A layer drawing a material the style does not have logs an error and builds nothing.
    /// </summary>
    [Test]
    public void ALayerWithAnUnknownMaterial_LogsAnErrorAndBuildsNothing()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], 0), new TerrainBlendRenderer.Layer(layers[1], 5));

        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layer 1 (Gravel) draws material 5, but TerrainStyle has 3.");
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Null);
        Assert.That(renderer.Chunks, Is.Empty);
    }

    /// <summary>
    /// A tilemap listed as two layers logs an error and builds nothing, since an edit could reach only one of them.
    /// </summary>
    [Test]
    public void ATilemapListedTwice_LogsAnErrorAndBuildsNothing()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], 0), new TerrainBlendRenderer.Layer(layers[0], 1));

        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layers 0 and 1 both list Grass; each tilemap is one layer.");
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Null);
    }

    /// <summary>
    /// Adds a renderer over the three layers (materials 0, 1, 2) to the grid and activates it.
    /// </summary>
    private TerrainBlendRenderer CreateRenderer()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], 0), new TerrainBlendRenderer.Layer(layers[1], 1), new TerrainBlendRenderer.Layer(layers[2], 2));
        root.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// Adds an <see cref="AreaTerrain"/> whose layers are the three ground tilemaps filled with the plain tile.
    /// </summary>
    private AreaTerrain CreateTerrain(IGameAuthority authority)
    {
        GameObject terrainObject = new("Terrain");
        terrainObject.transform.SetParent(root.transform);
        AreaTerrain terrain = terrainObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(layers[0], tile), new AreaTerrain.Layer(layers[1], tile), new AreaTerrain.Layer(layers[2], tile));
        terrain.Construct(authority);
        return terrain;
    }

    /// <summary>
    /// Paints <paramref name="cell"/> of <paramref name="layer"/> as an undo step of its own.
    /// </summary>
    private void PaintWithUndo(Tilemap layer, Vector3Int cell)
    {
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(layer, "Paint");
        layer.SetTile(cell, tile);
        Undo.IncrementCurrentGroup();
    }

    /// <summary>
    /// Sets the style's gravel cell field through a serialized object, as the inspector does, which validates the
    /// style.
    /// </summary>
    private void SetGravelField(Texture2D field)
    {
        SerializedObject serialized = new(style);
        serialized.FindProperty("materials.Array.data[1].cellField").objectReferenceValue = field;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Renders one frame through a temporary camera, which raises the render pipeline's callbacks.
    /// </summary>
    private static void RenderOneFrame()
    {
        GameObject cameraObject = new("TerrainBlendTestCamera") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture target = new(64, 64, 24);
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            target.Create();
            camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            camera.targetTexture = null;
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
        }
    }

    /// <summary>
    /// The control map texture's byte for <paramref name="cell"/>.
    /// </summary>
    private static byte TexelOf(TerrainBlendRenderer renderer, Vector3Int cell)
    {
        BoundsInt bounds = renderer.ControlMap.Bounds;
        return renderer.ControlMap.Texture.GetPixelData<byte>(0)[(cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin];
    }

    /// <summary>
    /// Whether the centre of <paramref name="cell"/> lies inside one of the chunk quads.
    /// </summary>
    private static bool Covered(TerrainBlendRenderer renderer, Vector3Int cell)
    {
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            if (Covers(chunk, cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the centre of <paramref name="cell"/> lies inside <paramref name="chunk"/>'s quad.
    /// </summary>
    private static bool Covers(MeshRenderer chunk, Vector3Int cell)
    {
        Bounds bounds = chunk.GetComponent<MeshFilter>().sharedMesh.bounds;
        Vector3 local = chunk.transform.InverseTransformPoint(new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f));
        return local.x >= bounds.min.x && local.x <= bounds.max.x && local.y >= bounds.min.y && local.y <= bounds.max.y;
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
