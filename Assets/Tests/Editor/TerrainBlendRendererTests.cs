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
/// cell changes; editing the style or a material it draws rebuilds it, a layer swapped to another material follows only
/// the new one, and fixing a material rebuilds a renderer that could not build; the mode switches the soft keyword on
/// the runtime material, across rebuilds, without scheduling a rebuild or allocating; the height array has a slice per
/// layer, and a material without a height still builds; an animated layer gives the renderer its rate, which the Scene
/// view follows; a fixed time for animated materials reaches the runtime material and clears without a rebuild; a
/// brush's preview shows only in the shown cells, replaces the last one, needs the renderer drawing the tilemap, follows the built layers until a rebuild, clears back to the real
/// cells, grows the map and the chunks with one upload and shrinks them back once cleared, and leaves nothing running
/// or allocating per frame;
/// the style, materials and textures are never written; a layer without a material, a texture of the wrong size and a
/// tilemap listed twice stop the build, a broken material drawn by two layers is reported once, and a layer with a
/// Tilemap Renderer is warned about; a layer whose navigation priority is not its draw order is reported while the
/// ground is still drawn, a change to a layer's navigation source (one added after the build included, which starts
/// with its layer's priority) rebuilds, the inspector's sync gives each layer's source its draw order with undo, and
/// only a renderer above a tilemap sets its priority. Each test builds its own style and materials with
/// <see cref="TestTerrainStyle"/>.
/// </summary>
public class TerrainBlendRendererTests
{
    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tilemap[] layers;
    private Tile tile;
    private TerrainStyle style;
    private TerrainMaterial[] materials;

    /// <summary>
    /// Builds an inactive grid with grass, gravel and stone layer tilemaps (grass painted over a 20 x 12 block, gravel
    /// along a row of it), a style and three plain materials.
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
        materials = TestTerrainStyle.PlainMaterials(createdObjects);
    }

    /// <summary>
    /// Clears the undo history of the layers and destroys the grid, the tile, the style, the materials and the textures
    /// the test created.
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
    /// Enabling builds the control map, the albedo, cell field and height arrays (a slice per layer, keeping no copy in
    /// memory), the lookup texture, the material and the chunks, in the crisp look, and waits for no render.
    /// </summary>
    [Test]
    public void Enabling_BuildsEverything()
    {
        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.ControlMap.Texture, Is.Not.Null);
        Assert.That(renderer.AlbedoArray.depth, Is.EqualTo(3));
        Assert.That(renderer.FieldArray.depth, Is.EqualTo(3));
        Assert.That(renderer.HeightArray.depth, Is.EqualTo(3));
        Assert.That(renderer.AlbedoArray.isReadable, Is.False);
        Assert.That(renderer.FieldArray.isReadable, Is.False);
        Assert.That(renderer.HeightArray.isReadable, Is.False);
        Assert.That(renderer.RuntimeMaterial.GetTexture("_HeightArray"), Is.SameAs(renderer.HeightArray));
        Assert.That(renderer.Mode, Is.EqualTo(TerrainBlendRenderer.BlendMode.Crisp));
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(SoftKeyword(renderer)), Is.False);
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
    /// Disabling stops following tile changes, render callbacks (even with an upload pending), undos and style and
    /// material changes, and destroys the control map's texture, the albedo, cell field and height arrays, the lookup
    /// texture, the material, the chunk meshes and the chunk objects.
    /// </summary>
    [Test]
    public void Disabling_UnsubscribesFromEverything_AndDestroysWhatWasBuilt()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        PaintWithUndo(layers[2], new Vector3Int(1, 1, 0));
        Assert.That(renderer.IsFlushScheduled, Is.True);
        List<Object> built = new() { renderer.ControlMap.Texture, renderer.AlbedoArray, renderer.FieldArray, renderer.HeightArray, renderer.Lut, renderer.RuntimeMaterial };
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
        SetEdgeNoise(2f);
        SetCellField(materials[1], TestTerrainStyle.CreateTexture("Other field", new Color32(128, 128, 128, 0), createdObjects));

        Assert.That(renderer.HandledChangeCount, Is.EqualTo(changes), "A disabled renderer no longer follows tile changes.");
        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(renders), "A disabled renderer no longer waits for a render.");
        Assert.That(renderer.HandledUndoCount, Is.EqualTo(undos), "A disabled renderer no longer follows undos.");
        Assert.That(renderer.IsRebuildScheduled, Is.False, "A disabled renderer no longer follows its style or materials.");
        Assert.That(renderer.IsFlushScheduled, Is.False);
        Assert.That(renderer.ControlMap, Is.Null);
        Assert.That(renderer.HeightArray, Is.Null);
        Assert.That(renderer.Chunks, Is.Empty);
        foreach (Object created in built)
        {
            Assert.That(created == null, Is.True, $"{created} was not destroyed.");
        }
    }

    /// <summary>
    /// Setting the mode to soft enables the soft keyword on the runtime material at once, in edit mode; a rebuild
    /// keeps it on the new material, and setting it back to crisp clears it again, also across a rebuild.
    /// </summary>
    [Test]
    public void TheMode_SetsAndClearsTheSoftKeyword_AcrossRebuilds()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        LocalKeyword soft = SoftKeyword(renderer);
        Assert.That(soft.isValid, Is.True);

        renderer.Mode = TerrainBlendRenderer.BlendMode.Soft;

        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(soft), Is.True);
        Material before = renderer.RuntimeMaterial;
        renderer.Rebuild();
        Assert.That(renderer.RuntimeMaterial, Is.Not.SameAs(before));
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(soft), Is.True, "A rebuild keeps the soft look.");

        renderer.Mode = TerrainBlendRenderer.BlendMode.Crisp;

        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(soft), Is.False);
        renderer.Rebuild();
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(soft), Is.False);
    }

    /// <summary>
    /// Flipping the mode, from code or through a serialized object as the inspector does, only switches the keyword:
    /// it schedules no rebuild, keeps the material, arrays and chunks, and allocates nothing. A change to a field the
    /// build reads still schedules one.
    /// </summary>
    [Test]
    public void FlippingTheMode_SchedulesNoRebuild_AndAllocatesNothing()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        LocalKeyword soft = SoftKeyword(renderer);
        Material material = renderer.RuntimeMaterial;
        Texture2DArray heights = renderer.HeightArray;
        MeshRenderer chunk = renderer.Chunks[0];
        SerializedObject serialized = new(renderer);

        serialized.FindProperty("mode").enumValueIndex = (int)TerrainBlendRenderer.BlendMode.Soft;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(renderer.Mode, Is.EqualTo(TerrainBlendRenderer.BlendMode.Soft));
        Assert.That(renderer.IsRebuildScheduled, Is.False);
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(soft), Is.True);
        renderer.Mode = TerrainBlendRenderer.BlendMode.Crisp;
        renderer.Mode = TerrainBlendRenderer.BlendMode.Soft;
        Assert.That(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                renderer.Mode = TerrainBlendRenderer.BlendMode.Crisp;
                renderer.Mode = TerrainBlendRenderer.BlendMode.Soft;
            }
        }, Is.Not.AllocatingGCMemory());
        Assert.That(renderer.IsRebuildScheduled, Is.False);
        Assert.That(renderer.RuntimeMaterial, Is.SameAs(material));
        Assert.That(renderer.HeightArray, Is.SameAs(heights));
        Assert.That(renderer.Chunks[0], Is.SameAs(chunk));

        serialized.Update();
        serialized.FindProperty("sortingOrder").intValue = 3;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(renderer.IsRebuildScheduled, Is.True, "A change the build reads rebuilds.");
    }

    /// <summary>
    /// A material without a height texture builds: its slice of the height array is left empty, and the lookup
    /// texture gives it a height scale of 0, so the soft look moves its edges by nothing.
    /// </summary>
    [Test]
    public void AMaterialWithoutAHeight_BuildsWithAZeroHeightScale()
    {
        materials[0] = TerrainMaterial.Create("Grass", materials[0].Albedo, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings(), null, 2f);
        createdObjects.Add(materials[0]);

        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.HeightArray.depth, Is.EqualTo(3));
        Assert.That(renderer.Lut.GetPixel(3, 1).r, Is.EqualTo(0f));
        Assert.That(renderer.Lut.GetPixel(3, 2).r, Is.EqualTo(TerrainMaterial.DefaultHeightScale));
    }

    /// <summary>
    /// A renderer drawing only still materials has no animation rate; one drawing an animated material has its frames
    /// per second, which the Scene view follows for the fastest enabled renderer, and none once disabled.
    /// </summary>
    [Test]
    public void AnAnimatedLayer_GivesTheRendererItsRate_WhichTheSceneViewFollows()
    {
        TerrainBlendRenderer still = CreateRenderer();
        Assert.That(still.AnimationFramesPerSecond, Is.EqualTo(0f));
        Assert.That(TerrainSceneViewAnimation.FramesPerSecond(new[] { still }), Is.EqualTo(0f));
        Object.DestroyImmediate(still);
        root.SetActive(false);
        SerializedObject animation = new(materials[2]);
        animation.FindProperty("framesPerSecond").floatValue = 8f;
        animation.ApplyModifiedPropertiesWithoutUndo();

        TerrainBlendRenderer animated = CreateRenderer();

        Assert.That(animated.AnimationFramesPerSecond, Is.EqualTo(8f));
        Assert.That(TerrainSceneViewAnimation.FramesPerSecond(new[] { animated }), Is.EqualTo(8f));
        animated.enabled = false;
        Assert.That(animated.AnimationFramesPerSecond, Is.EqualTo(0f));
        Assert.That(TerrainSceneViewAnimation.FramesPerSecond(new[] { animated }), Is.EqualTo(0f));
    }

    /// <summary>
    /// A fixed time reaches the runtime material (its info's third and fourth values), so animated materials draw at
    /// that time, and clearing it goes back to the frame's time; neither schedules a rebuild or an upload.
    /// </summary>
    [Test]
    public void AFixedTime_ReachesTheMaterial_AndClearingItGoesBackToTheFramesTime()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        int infoId = Shader.PropertyToID("_TerrainInfo");
        Assert.That(renderer.RuntimeMaterial.GetVector(infoId), Is.EqualTo(new Vector4(3f, 0f, 0f, 0f)));

        renderer.SetFixedTime(2.5f);

        Assert.That(renderer.RuntimeMaterial.GetVector(infoId), Is.EqualTo(new Vector4(3f, 0f, 1f, 2.5f)));
        renderer.SetFixedTime(null);
        Assert.That(renderer.RuntimeMaterial.GetVector(infoId), Is.EqualTo(new Vector4(3f, 0f, 0f, 0f)));
        Assert.That(renderer.IsRebuildScheduled, Is.False);
        Assert.That(renderer.IsFlushScheduled, Is.False);
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
    /// A preview shows in the shown cells, filled on an empty cell and empty on a filled one, but not in the real cells
    /// or the tilemaps, and reaches the texture with the next upload; a preview on another layer replaces it, and
    /// clearing restores the texels.
    /// </summary>
    [Test]
    public void APreview_ShowsInTheShownCellsOnly_ReplacesTheLast_AndClearingRestoresThem()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Vector3Int stone = new(7, 8, 0);
        Vector3Int grass = new(3, 3, 0);

        Assert.That(renderer.SetPreview(layers[2], new[] { stone }, true), Is.True);

        Assert.That(renderer.IsFlushScheduled, Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(stone), Is.EqualTo(5));
        Assert.That(renderer.ControlMap.GetCell(stone), Is.EqualTo(1));
        renderer.FlushPendingChanges();
        Assert.That(TexelOf(renderer, stone), Is.EqualTo(5));
        Assert.That(layers[2].HasTile(stone), Is.False);

        renderer.SetPreview(layers[0], new[] { grass }, false);

        Assert.That(renderer.ControlMap.GetShownCell(stone), Is.EqualTo(1), "The grass preview replaced the stone one.");
        Assert.That(renderer.ControlMap.GetShownCell(grass), Is.EqualTo(0));
        Assert.That(renderer.ControlMap.GetCell(grass), Is.EqualTo(1));
        renderer.FlushPendingChanges();
        Assert.That(TexelOf(renderer, stone), Is.EqualTo(1));
        Assert.That(TexelOf(renderer, grass), Is.EqualTo(0));
        Assert.That(layers[0].HasTile(grass), Is.True);

        renderer.ClearPreview();
        renderer.FlushPendingChanges();

        Assert.That(renderer.ControlMap.HasPreview, Is.False);
        Assert.That(TexelOf(renderer, grass), Is.EqualTo(1));
    }

    /// <summary>
    /// Only a renderer that is built and drawing a tilemap shows a preview of it: one that is off, or a tilemap it
    /// does not draw, changes nothing.
    /// </summary>
    [Test]
    public void APreview_NeedsTheRendererDrawingTheTilemap()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Tilemap other = new GameObject("Other", typeof(Tilemap)).GetComponent<Tilemap>();
        other.transform.SetParent(root.transform);
        Vector3Int cell = new(7, 8, 0);

        Assert.That(renderer.IsDrawing(other), Is.False);
        Assert.That(renderer.SetPreview(other, new[] { cell }, true), Is.False);
        Assert.That(renderer.ControlMap.HasPreview, Is.False);

        renderer.enabled = false;

        Assert.That(renderer.IsDrawing(layers[2]), Is.False);
        Assert.That(renderer.SetPreview(layers[2], new[] { cell }, true), Is.False);
    }

    /// <summary>
    /// After an inspector change to the layers and before the rebuild it schedules, a preview follows the layers the
    /// renderer built: a reordered tilemap still shows its built layer's bit and a tilemap just added shows nothing.
    /// Once rebuilt, the preview follows the new layers.
    /// </summary>
    [Test]
    public void AfterAnInspectorChangeToTheLayers_APreviewFollowsTheBuiltLayersUntilTheRebuild()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        Tilemap water = new GameObject("Water", typeof(Tilemap)).GetComponent<Tilemap>();
        water.transform.SetParent(root.transform);
        Vector3Int cell = new(7, 8, 0);
        SerializedObject serialized = new(renderer);
        SerializedProperty layerList = serialized.FindProperty("layers");
        layerList.MoveArrayElement(2, 1);
        layerList.arraySize++;
        SerializedProperty added = layerList.GetArrayElementAtIndex(3);
        added.FindPropertyRelative("tilemap").objectReferenceValue = water;
        added.FindPropertyRelative("material").objectReferenceValue = materials[0];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        Assert.That(renderer.LayerIndexOf(layers[2]), Is.EqualTo(1));
        Assert.That(renderer.SetPreview(layers[2], new[] { cell }, true), Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(5), "Stone is still built as layer 2.");
        Assert.That(renderer.SetPreview(water, new[] { cell }, true), Is.False);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(5), "A refused preview leaves the last one.");

        renderer.RunScheduledRebuild();

        Assert.That(renderer.SetPreview(layers[2], new[] { cell }, true), Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(3), "Stone is now built as layer 1.");
        Assert.That(renderer.SetPreview(water, new[] { cell }, true), Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(cell), Is.EqualTo(9));
    }

    /// <summary>
    /// A filled preview outside the bounds grows the control map, adds chunks over it and moves the control rectangle,
    /// without touching the tilemap; the next render uploads it, once.
    /// </summary>
    [Test]
    public void APreviewOutsideTheBounds_GrowsTheMapAndTheChunks_AndUploadsOnce()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        int chunks = renderer.Chunks.Count;
        int uploads = renderer.ControlMap.UploadCount;
        Vector3Int far = new(60, -30, 0);

        renderer.SetPreview(layers[1], new[] { far }, true);

        Assert.That(renderer.ControlMap.Bounds.Contains(far), Is.True);
        Assert.That(renderer.Chunks.Count, Is.GreaterThan(chunks));
        Assert.That(Covered(renderer, far), Is.True);
        Assert.That(renderer.IsFlushScheduled, Is.True);
        Assert.That(renderer.ControlMap.UploadCount, Is.EqualTo(uploads), "Nothing is uploaded before the next render.");

        RenderOneFrame();

        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(1));
        Assert.That(renderer.ControlMap.UploadCount, Is.EqualTo(uploads + 1));
        Assert.That(TexelOf(renderer, far), Is.EqualTo(2));
        BoundsInt bounds = renderer.ControlMap.Bounds;
        Assert.That(renderer.RuntimeMaterial.GetVector("_ControlRect"), Is.EqualTo(new Vector4(bounds.xMin, bounds.yMin, bounds.size.x, bounds.size.y)));
        Assert.That(layers[1].HasTile(far), Is.False);
    }

    /// <summary>
    /// The bounds and chunks a preview grew stay while previews replace it, and clearing shrinks them back to the
    /// filled cells, destroying the chunks they no longer reach and moving the control rectangle back.
    /// </summary>
    [Test]
    public void ClearingAPreviewThatGrewTheMap_ShrinksItBack()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        BoundsInt bounds = renderer.ControlMap.Bounds;
        int chunks = renderer.Chunks.Count;
        Vector3Int far = new(60, -30, 0);
        renderer.SetPreview(layers[1], new[] { far }, true);
        List<Object> farChunks = new();
        foreach (MeshRenderer chunk in renderer.Chunks)
        {
            if (Covers(chunk, far))
            {
                farChunks.Add(chunk.gameObject);
                farChunks.Add(chunk.GetComponent<MeshFilter>().sharedMesh);
            }
        }

        renderer.SetPreview(layers[1], new[] { new Vector3Int(4, 4, 0) }, true);

        Assert.That(renderer.ControlMap.Bounds.Contains(far), Is.True, "Replacing the preview keeps the grown bounds.");
        Assert.That(farChunks, Is.Not.Empty);

        renderer.ClearPreview();
        renderer.FlushPendingChanges();

        Assert.That(renderer.ControlMap.Bounds, Is.EqualTo(bounds));
        Assert.That(renderer.Chunks.Count, Is.EqualTo(chunks));
        foreach (Object removed in farChunks)
        {
            Assert.That(removed == null, Is.True, $"{removed} was not destroyed.");
        }

        Assert.That(renderer.RuntimeMaterial.GetVector("_ControlRect"), Is.EqualTo(new Vector4(bounds.xMin, bounds.yMin, bounds.size.x, bounds.size.y)));
        Assert.That(TexelOf(renderer, new Vector3Int(4, 4, 0)), Is.EqualTo(1));
    }

    /// <summary>
    /// Once a preview is uploaded, nothing waits for a render, a later frame runs nothing and allocates nothing, and
    /// once a few moves have run, moving or clearing a preview inside the bounds allocates nothing either.
    /// </summary>
    [Test]
    public void APreview_SchedulesAndAllocatesNothingPerFrameAfterwards()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        List<Vector3Int> cells = new() { new Vector3Int(4, 4, 0), new Vector3Int(5, 4, 0) };
        renderer.SetPreview(layers[2], cells, true);
        RenderOneFrame();
        int callbacks = renderer.RenderCallbackCount;
        int uploads = renderer.ControlMap.UploadCount;

        Assert.That(renderer.IsFlushScheduled, Is.False);
        RenderOneFrame();
        Assert.That(renderer.RenderCallbackCount, Is.EqualTo(callbacks), "Nothing runs on a frame without changes.");
        Assert.That(() =>
        {
            for (int frame = 0; frame < 10; frame++)
            {
                renderer.FlushPendingChanges();
            }
        }, Is.Not.AllocatingGCMemory());
        Assert.That(renderer.ControlMap.UploadCount, Is.EqualTo(uploads));

        for (int x = 0; x < 3; x++)
        {
            MovePreview(x);
        }

        Assert.That(() =>
        {
            for (int x = 0; x < 10; x++)
            {
                MovePreview(x);
            }
        }, Is.Not.AllocatingGCMemory());
        Assert.That(renderer.ControlMap.UploadCount, Is.EqualTo(uploads + 13));

        void MovePreview(int x)
        {
            cells[0] = new Vector3Int(x, 4, 0);
            renderer.ClearPreview();
            renderer.SetPreview(layers[2], cells, true);
            renderer.FlushPendingChanges();
        }
    }

    /// <summary>
    /// A layer whose tilemap has an enabled Tilemap Renderer, which would draw its tiles' sprites as well, is warned
    /// about, and the ground is still drawn.
    /// </summary>
    [Test]
    public void ALayerWithATilemapRenderer_IsWarnedAbout_AndStillDrawn()
    {
        layers[1].gameObject.AddComponent<TilemapRenderer>();
        LogAssert.Expect(LogType.Warning, "TerrainBlendGrid: ground layer 1 (Gravel) has an enabled Tilemap Renderer, which draws its tiles' sprites (their palette swatches) beside the blended ground. A ground layer is a data tilemap: remove its Tilemap Renderer.");

        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.Chunks, Is.Not.Empty);
    }

    /// <summary>
    /// An inspector change to the style or to a material one of its layers draws schedules a rebuild, which builds the
    /// lookup texture again from the new settings; a change to a material no layer draws schedules nothing.
    /// </summary>
    [Test]
    public void EditingTheStyleOrAMaterialItDraws_RebuildsTheRenderer()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        TerrainMaterial unused = TestTerrainStyle.PlainMaterial("Sand", TerrainMaterial.CellMode.None, createdObjects);

        SetBaseColour(unused, Color.yellow);

        Assert.That(renderer.IsRebuildScheduled, Is.False, "No layer draws Sand.");

        SetBaseColour(materials[1], new Color(0.5f, 0.25f, 0.125f));

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.Lut.GetPixel(2, 2), Is.EqualTo(new Color(0.5f, 0.25f, 0.125f)), "Gravel's layer row holds its new base colour.");

        SetEdgeNoise(2f);

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.Lut.GetPixel(0, 0).r, Is.EqualTo(2f));
    }

    /// <summary>
    /// Changing a layer's material in the inspector rebuilds the renderer, which then follows the new material and no
    /// longer the old one.
    /// </summary>
    [Test]
    public void SwappingALayersMaterial_FollowsTheNewMaterialOnly()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        TerrainMaterial previous = materials[1];
        TerrainMaterial replacement = TestTerrainStyle.PlainMaterial("Other gravel", TerrainMaterial.CellMode.WholeChips, createdObjects);
        SerializedObject serialized = new(renderer);
        serialized.FindProperty("layers.Array.data[1].material").objectReferenceValue = replacement;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();

        SetBaseColour(previous, Color.yellow);

        Assert.That(renderer.IsRebuildScheduled, Is.False, "The old material no longer rebuilds the renderer.");

        SetBaseColour(replacement, Color.yellow);

        Assert.That(renderer.IsRebuildScheduled, Is.True);
    }

    /// <summary>
    /// A broken material drawn by two layers logs its error once (any second error would fail the test).
    /// </summary>
    [Test]
    public void ABrokenMaterialDrawnByTwoLayers_LogsItsErrorOnce()
    {
        Texture2D albedo = TestTerrainStyle.CreateTexture("Gravel albedo", new Color32(150, 120, 90, 255), createdObjects);
        TerrainMaterial broken = TerrainMaterial.Create("Gravel", albedo, null, 4, TerrainMaterial.CellMode.WholeChips, Color.gray, new TerrainStyle.EdgeSettings());
        createdObjects.Add(broken);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[1], broken), new TerrainBlendRenderer.Layer(layers[2], broken));

        LogAssert.Expect(LogType.Error, "Terrain material Gravel has chips or stones but no cell field texture.");
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Null);
    }

    /// <summary>
    /// A renderer that could not build because a material it draws lacked a texture rebuilds once the material is
    /// fixed in the inspector: the change schedules a rebuild, which then succeeds.
    /// </summary>
    [Test]
    public void FixingAMaterial_RebuildsARendererThatCouldNotBuild()
    {
        Texture2D albedo = TestTerrainStyle.CreateTexture("Gravel albedo", new Color32(150, 120, 90, 255), createdObjects);
        materials[1] = TerrainMaterial.Create("Gravel", albedo, null, 4, TerrainMaterial.CellMode.WholeChips, Color.gray, new TerrainStyle.EdgeSettings());
        createdObjects.Add(materials[1]);
        LogAssert.Expect(LogType.Error, "Terrain material Gravel has chips or stones but no cell field texture.");
        TerrainBlendRenderer renderer = CreateRenderer();
        Assert.That(renderer.ControlMap, Is.Null);

        SetCellField(materials[1], TestTerrainStyle.CreateTexture("Gravel field", new Color32(128, 128, 128, 0), createdObjects));

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.Chunks, Is.Not.Empty);
    }

    /// <summary>
    /// Building, patching, switching the look, rebuilding and disabling leave the style, the materials and their
    /// textures exactly as they were.
    /// </summary>
    [Test]
    public void TheStyleAndMaterials_AreNeverModified()
    {
        List<Object> assets = new() { style };
        assets.AddRange(materials);
        List<string> before = new();
        List<int> dirtyCounts = new();
        foreach (Object asset in assets)
        {
            before.Add(EditorJsonUtility.ToJson(asset));
            dirtyCounts.Add(EditorUtility.GetDirtyCount(asset));
        }

        Color32[] albedoBefore = materials[1].Albedo.GetPixels32();
        Color32[] fieldBefore = materials[1].CellField.GetPixels32();
        Color32[] heightBefore = materials[1].Height.GetPixels32();

        TerrainBlendRenderer renderer = CreateRenderer();
        layers[1].SetTile(new Vector3Int(3, 3, 0), tile);
        renderer.FlushPendingChanges();
        renderer.SetDebugView(true);
        renderer.Mode = TerrainBlendRenderer.BlendMode.Soft;
        renderer.Rebuild();
        renderer.Mode = TerrainBlendRenderer.BlendMode.Crisp;
        renderer.enabled = false;

        for (int i = 0; i < assets.Count; i++)
        {
            Assert.That(EditorJsonUtility.ToJson(assets[i]), Is.EqualTo(before[i]), assets[i].name);
            Assert.That(EditorUtility.GetDirtyCount(assets[i]), Is.EqualTo(dirtyCounts[i]), assets[i].name);
        }

        Assert.That(materials[1].Albedo.GetPixels32(), Is.EqualTo(albedoBefore));
        Assert.That(materials[1].CellField.GetPixels32(), Is.EqualTo(fieldBefore));
        Assert.That(materials[1].Height.GetPixels32(), Is.EqualTo(heightBefore));
    }

    /// <summary>
    /// A layer without a material logs an error and builds nothing.
    /// </summary>
    [Test]
    public void ALayerWithoutAMaterial_LogsAnErrorAndBuildsNothing()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[1], null));

        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layer 1 (Gravel) has no material.");
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Null);
        Assert.That(renderer.Chunks, Is.Empty);
    }

    /// <summary>
    /// A material whose texture is not its repeat x 32 px square logs an error and builds nothing, since the texture
    /// could not be copied into the arrays.
    /// </summary>
    [Test]
    public void AMaterialWithATextureOfTheWrongSize_LogsAnErrorAndBuildsNothing()
    {
        Texture2D small = new(64, 64, TextureFormat.RGBA32, false, true) { name = "Small albedo" };
        createdObjects.Add(small);
        materials[0] = TerrainMaterial.Create("Grass", small, null, 4, TerrainMaterial.CellMode.None, Color.gray, new TerrainStyle.EdgeSettings());
        createdObjects.Add(materials[0]);

        LogAssert.Expect(LogType.Error, "Small albedo of terrain material Grass must be 128 px square (its repeat of 4 cells) and RGBA32 with sRGB off.");
        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.ControlMap, Is.Null);
    }

    /// <summary>
    /// A tilemap listed as two layers logs an error and builds nothing, since an edit could reach only one of them.
    /// </summary>
    [Test]
    public void ATilemapListedTwice_LogsAnErrorAndBuildsNothing()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[0], materials[1]));

        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layers 0 and 1 both list Grass; each tilemap is one layer.");
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Null);
    }

    /// <summary>
    /// A layer whose tilemap's navigation source has another priority than its index, its draw order, logs an error,
    /// and the ground is still drawn, since the drawing never reads the priority.
    /// </summary>
    [Test]
    public void ALayerWhoseNavigationPriorityIsNotItsDrawOrder_IsReported_AndStillDrawn()
    {
        AddSources(0, 1, 5);

        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layer 2 (Stone) has navigation priority 5, but a ground layer's navigation priority is its place in the layers, its draw order: 2. Set it from the draw order in the TerrainBlendRenderer inspector.");
        TerrainBlendRenderer renderer = CreateRenderer();

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.Chunks, Is.Not.Empty);
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.False);
    }

    /// <summary>
    /// A change to a layer's navigation source through a serialized object schedules a rebuild, which reports a
    /// priority that is no longer the layer's draw order and still draws.
    /// </summary>
    [Test]
    public void ChangingALayersNavigationPriority_RebuildsAndReportsAMismatch()
    {
        NavigationTerrainSource2D[] sources = AddSources(0, 1, 2);
        TerrainBlendRenderer renderer = CreateRenderer();
        Assert.That(renderer.ControlMap, Is.Not.Null);

        SerializedObject serialized = new(sources[2]);
        serialized.FindProperty("priority").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layer 2 (Stone) has navigation priority 0, but a ground layer's navigation priority is its place in the layers, its draw order: 2. Set it from the draw order in the TerrainBlendRenderer inspector.");
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
    }

    /// <summary>
    /// A navigation source added to a layer's tilemap after the renderer was built starts with that layer's priority,
    /// and a later change to it reaches the renderer, which rebuilds and reports the mismatch.
    /// </summary>
    [Test]
    public void ANavigationSourceAddedLater_StartsInStep_AndItsChangesReachTheRenderer()
    {
        TerrainBlendRenderer renderer = CreateRenderer();

        NavigationTerrainSource2D stone = layers[2].gameObject.AddComponent<NavigationTerrainSource2D>();

        Assert.That(stone.Priority, Is.EqualTo(2));
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        renderer.RunScheduledRebuild();
        SerializedObject serialized = new(stone);
        serialized.FindProperty("priority").intValue = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        LogAssert.Expect(LogType.Error, "TerrainBlendGrid: ground layer 2 (Stone) has navigation priority 0, but a ground layer's navigation priority is its place in the layers, its draw order: 2. Set it from the draw order in the TerrainBlendRenderer inspector.");
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.False);
    }

    /// <summary>
    /// After the layers are reordered (stone moved under gravel), the inspector's sync gives each layer's navigation
    /// source its new index, as one undo step, and the renderer builds with gravel drawn on top; the source's inspector
    /// finds the renderer and the layer that set its priority. The renderer is disabled before the undo, which would
    /// otherwise rebuild it and report the restored priorities as out of step.
    /// </summary>
    [Test]
    public void TheInspectorsSync_GivesEachLayerItsDrawOrderAsPriority()
    {
        NavigationTerrainSource2D[] sources = AddSources(0, 1, 2);
        TerrainBlendRenderer renderer = CreateRenderer();
        SerializedObject serialized = new(renderer);
        serialized.FindProperty("layers").MoveArrayElement(2, 1);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.False);

        Undo.IncrementCurrentGroup();
        int changed = TerrainBlendRendererEditor.SyncNavigationPriorities(renderer);

        Assert.That(changed, Is.EqualTo(2));
        Assert.That(sources[0].Priority, Is.EqualTo(0));
        Assert.That(sources[2].Priority, Is.EqualTo(1), "Stone is now layer 1.");
        Assert.That(sources[1].Priority, Is.EqualTo(2), "Gravel is now layer 2, drawn on top.");
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.Layers[2].Tilemap, Is.SameAs(layers[1]));
        Assert.That(TerrainBlendRenderer.FindLayerDrawing(layers[1], out int layer), Is.SameAs(renderer));
        Assert.That(layer, Is.EqualTo(2));

        renderer.enabled = false;
        Undo.PerformUndo();

        Assert.That(sources[2].Priority, Is.EqualTo(2));
        Assert.That(sources[1].Priority, Is.EqualTo(1));
    }

    /// <summary>
    /// Only a renderer above a tilemap that lists it sets its priority: a tilemap the renderer does not list, and one it
    /// lists from outside its own hierarchy, are not drawn by it as far as navigation goes, so the renderer does not
    /// check their sources, the sync leaves them alone and their inspectors keep the priority editable.
    /// </summary>
    [Test]
    public void OnlyARendererAboveATilemap_SetsItsPriority()
    {
        GameObject outside = Track(new GameObject("Outside", typeof(Grid)));
        GameObject outsideLayer = new("Sand", typeof(Tilemap));
        outsideLayer.transform.SetParent(outside.transform);
        Tilemap sand = outsideLayer.GetComponent<Tilemap>();
        sand.SetTile(new Vector3Int(3, 3, 0), tile);
        NavigationTerrainSource2D sandSource = outsideLayer.AddComponent<NavigationTerrainSource2D>();
        sandSource.Configure(null, 7);
        NavigationTerrainSource2D stone = layers[2].gameObject.AddComponent<NavigationTerrainSource2D>();
        stone.Configure(null, 7);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[1], materials[1]), new TerrainBlendRenderer.Layer(sand, materials[2]));
        root.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(TerrainBlendRenderer.FindLayerDrawing(layers[2], out int layer), Is.Null);
        Assert.That(layer, Is.EqualTo(-1));
        Assert.That(TerrainBlendRenderer.FindLayerDrawing(sand, out _), Is.Null);
        Assert.That(renderer.NavigationSourceOf(2), Is.Null);
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        Assert.That(TerrainBlendRendererEditor.SyncNavigationPriorities(renderer), Is.EqualTo(0));
        Assert.That(sandSource.Priority, Is.EqualTo(7));
    }

    /// <summary>
    /// Adds a navigation source to each layer tilemap, in order, with the given priorities.
    /// </summary>
    private NavigationTerrainSource2D[] AddSources(params int[] priorities)
    {
        NavigationTerrainSource2D[] sources = new NavigationTerrainSource2D[priorities.Length];
        for (int i = 0; i < priorities.Length; i++)
        {
            sources[i] = layers[i].gameObject.AddComponent<NavigationTerrainSource2D>();
            sources[i].Configure(null, priorities[i]);
        }

        return sources;
    }

    /// <summary>
    /// Adds a renderer over the three layers, drawing the three materials in order, to the grid and activates it.
    /// </summary>
    private TerrainBlendRenderer CreateRenderer()
    {
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(layers[0], materials[0]), new TerrainBlendRenderer.Layer(layers[1], materials[1]), new TerrainBlendRenderer.Layer(layers[2], materials[2]));
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
    /// Sets <paramref name="material"/>'s cell field through a serialized object, as the inspector does, which
    /// validates the material.
    /// </summary>
    private static void SetCellField(TerrainMaterial material, Texture2D field)
    {
        SerializedObject serialized = new(material);
        serialized.FindProperty("cellField").objectReferenceValue = field;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Sets <paramref name="material"/>'s base colour through a serialized object, as the inspector does.
    /// </summary>
    private static void SetBaseColour(TerrainMaterial material, Color colour)
    {
        SerializedObject serialized = new(material);
        serialized.FindProperty("baseColour").colorValue = colour;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Sets the style's edge noise through a serialized object, as the inspector does, which validates the style.
    /// </summary>
    private void SetEdgeNoise(float noise)
    {
        SerializedObject serialized = new(style);
        serialized.FindProperty("edgeNoise").floatValue = noise;
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
    /// The soft look's keyword on the renderer's shader.
    /// </summary>
    private static LocalKeyword SoftKeyword(TerrainBlendRenderer renderer)
    {
        return new LocalKeyword(renderer.RuntimeMaterial.shader, "_TERRAIN_SOFT");
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
