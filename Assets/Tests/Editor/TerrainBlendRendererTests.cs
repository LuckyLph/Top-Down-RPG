using System.Collections;
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
/// Tests for <see cref="TerrainBlendRenderer"/>: 16 layers build, each drawn on top in its own debug colour, and a
/// preview on the top layer shows over the others; tile changes from painting, from <see cref="AreaTerrain"/> on the
/// host and from replicated edits on a client reach the control map's texture, and so do undos; a render uploads pending
/// changes once and stops listening; the chunks cover the layers and follow the bounds as they grow and shrink;
/// disabling unsubscribes from everything and destroys what was built; nothing runs or allocates per frame while no
/// cell changes; editing the style or a material it draws rebuilds it, a layer swapped to another material follows only
/// the new one, and fixing a material rebuilds a renderer that could not build; the mode switches the soft keyword on
/// the runtime material, across rebuilds, without scheduling a rebuild or allocating; the height array has a slice per
/// layer, and a material without a height still builds; a sunken layer shows its bank over an ordinary pair and none
/// over a seamless one; a bridge leaves the layers under it their own edges beside it and hides the edge of a layer
/// painted under it where both end together, and a layer that turns to its runs turns its textures where it runs
/// across; an animated layer whose height holds only sparkle phases lights only its sparkles; an animated layer gives
/// the renderer its rate, which the Scene view follows; raised ground draws its cliff face, its shadow and its ramp,
/// and the elevation map follows the raised tilemap (TerrainTypes.md's Phase 10); a fixed time for animated
/// materials reaches the runtime material and clears without a rebuild, and the layer count the shader reads is the
/// built one until a rebuild; a
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
    /// A renderer of <see cref="TerrainControlMap.MaxLayers"/> layers (16), each its own material, builds a slice per
    /// layer and the lookup texture's rows for 16 layers. Over a strip of the bottom layer, with each other layer
    /// filling its own 3 x 3 block, its debug view shows every layer on top in its block, each in a colour the probe
    /// tells apart from the others; a preview on the top layer shows that layer over a block, and clearing it shows the
    /// block's own layer again. Renders from a later editor update, until anything is drawn (at most 30 updates): chunks
    /// built in one update are drawn from the next, and right after the terrain shader is imported the first render
    /// draws nothing either.
    /// </summary>
    [UnityTest]
    public IEnumerator SixteenLayers_BuildAndDrawEveryLayer_AndPreviewOnTheTopOne()
    {
        const int count = TerrainControlMap.MaxLayers;
        GameObject gridObject = Track(new GameObject("SixteenLayerGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap[] tilemaps = new Tilemap[count];
        TerrainBlendRenderer.Layer[] drawn = new TerrainBlendRenderer.Layer[count];
        for (int i = 0; i < count; i++)
        {
            GameObject layerObject = new($"Layer{i}", typeof(Tilemap));
            layerObject.transform.SetParent(gridObject.transform);
            tilemaps[i] = layerObject.GetComponent<Tilemap>();
            drawn[i] = new TerrainBlendRenderer.Layer(tilemaps[i], TestTerrainStyle.PlainMaterial($"Material{i}", TerrainMaterial.CellMode.None, createdObjects));
        }

        for (int x = 0; x < 4 * count; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                tilemaps[0].SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        for (int i = 1; i < count; i++)
        {
            foreach (Vector3Int cell in BlockCells(i))
            {
                tilemaps[i].SetTile(cell, tile);
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, drawn);
        gridObject.SetActive(true);

        Assert.That(renderer.ControlMap, Is.Not.Null);
        Assert.That(renderer.ControlMap.LayerCount, Is.EqualTo(count));
        Assert.That(renderer.AlbedoArray.depth, Is.EqualTo(count));
        Assert.That(renderer.HeightArray.depth, Is.EqualTo(count));
        Assert.That(renderer.Lut.height, Is.EqualTo(TerrainBlendLut.Height(count)));
        Assert.That(renderer.ControlMap.GetShownCell(new Vector3Int(4 * (count - 1) + 1, 2, 0)), Is.EqualTo(0x8001));
        Rect world = new(0f, 0f, 4 * count, 5f);
        byte[] shown = new byte[count];
        for (int frame = 0; frame < 30 && shown[0] == 0; frame++)
        {
            yield return null;
            shown = DebugClasses(renderer, world, count);
        }

        for (int i = 0; i < count; i++)
        {
            Assert.That(shown[i], Is.EqualTo(i + 1), $"block {i}");
        }

        Assert.That(renderer.SetPreview(tilemaps[count - 1], BlockCells(0), true), Is.True);
        Assert.That(renderer.ControlMap.GetShownCell(new Vector3Int(1, 2, 0)), Is.EqualTo(0x8001));
        Assert.That(DebugClasses(renderer, world, count)[0], Is.EqualTo(count), "The preview shows the top layer.");

        renderer.ClearPreview();
        Assert.That(DebugClasses(renderer, world, count)[0], Is.EqualTo(1), "Clearing the preview shows the bottom layer again.");
    }

    /// <summary>
    /// A swaying layer's weed is drawn only where its foot and its top stand on the layer: under a weed rooted beneath a
    /// layer drawn over it, one rooted below the layer's own edge, and one growing up under a layer drawn over it, the
    /// pixels of its box show the ground from the cell field. The swaying material's textures hold a red weed in every
    /// cell, its foot 4 px below the cell's top and its box rising 11 rows above it into the cell above (the height's
    /// green channel its row from under the foot, plus one, the field's alpha 11), over green ground; a cover layer
    /// fills part of the bottom row and of the third. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ASwayingLayersWeed_ShowsOnlyWhereItsFootStandsOnTheLayer()
    {
        const int size = 128;
        Color32 green = new(40, 160, 40, 255);
        Color32 red = new(220, 30, 30, 255);
        Color32[] albedo = new Color32[size * size];
        Color32[] field = new Color32[size * size];
        Color32[] height = new Color32[size * size];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = green;
            field[i] = green;
            height[i] = new Color32(128, 0, 0, 255);
        }

        for (int column = 0; column < size / 32; column++)
        {
            for (int cellRow = 0; cellRow < size / 32; cellRow++)
            {
                for (int dy = -1; dy < 12; dy++)
                {
                    int i = ((cellRow * 32 + 28 + dy) % size) * size + column * 32 + 16;
                    height[i].g = (byte)(dy + 2);
                    field[i].a = 11;
                    albedo[i] = dy >= 0 ? red : green;
                }
            }
        }

        TerrainMaterial swaying = TerrainMaterial.Create("Swaying", Texture("Swaying albedo", albedo), Texture("Swaying field", field), 4, TerrainMaterial.CellMode.None, green, new TerrainStyle.EdgeSettings(), Texture("Swaying height", height));
        createdObjects.Add(swaying);
        SerializedObject sway = new(swaying);
        sway.FindProperty("framesPerSecond").floatValue = 8f;
        sway.FindProperty("swayAmplitude").floatValue = 2f;
        sway.ApplyModifiedPropertiesWithoutUndo();
        TerrainMaterial cover = TestTerrainStyle.PlainMaterial("Cover", TerrainMaterial.CellMode.None, createdObjects);

        GameObject gridObject = Track(new GameObject("SwayingGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(gridObject.transform);
        Tilemap over = new GameObject("Over", typeof(Tilemap)).GetComponent<Tilemap>();
        over.transform.SetParent(gridObject.transform);
        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        for (int x = 2; x < 6; x++)
        {
            over.SetTile(new Vector3Int(x, 0, 0), tile);
            over.SetTile(new Vector3Int(x, 2, 0), tile);
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(ground, swaying), new TerrainBlendRenderer.Layer(over, cover));
        gridObject.SetActive(true);
        renderer.SetFixedTime(0f);
        Rect world = new(0f, 0f, 8f, 4f);
        Color32 rooted = default;
        Color32 underCover = default;
        Color32 belowEdge = default;
        Color32 intoCover = default;
        for (int frame = 0; frame < 30 && !IsDrawn(rooted); frame++)
        {
            yield return null;
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            rooted = pixels.GetPixel(240, 68);
            underCover = pixels.GetPixel(112, 36);
            belowEdge = pixels.GetPixel(240, 4);
            intoCover = pixels.GetPixel(112, 62);
            Object.DestroyImmediate(pixels);
        }

        renderer.SetFixedTime(null);
        Assert.That(rooted.r, Is.GreaterThan(rooted.g), "A weed standing on the layer is drawn.");
        Assert.That(underCover.g, Is.GreaterThan(underCover.r), "A weed rooted under the cover shows the ground.");
        Assert.That(belowEdge.g, Is.GreaterThan(belowEdge.r), "A weed rooted below the layer's edge shows the ground.");
        Assert.That(intoCover.g, Is.GreaterThan(intoCover.r), "A weed growing up under the cover shows the ground.");
    }

    /// <summary>
    /// A swaying layer whose clumps lean whole moves only its weeds' own pixels, each weed as one: over the frames of a
    /// gust, a weed whose upper half crosses a cell row shows the same lean in every row of that half (all at the full
    /// weight), and a mark of the ground inside its box stays where it is, while the weed does lean in some frame. The
    /// same textures without whole clumps lean the rows on either side of the cell row by different amounts in some
    /// frame, and slide the ground mark with them. Each of the textures' 16 cells holds a red weed whose base lies 8 px
    /// under the top of its cell, so its box crosses into the cell above, its rows from the fifth up at the full weight,
    /// and a blue ground mark beside its top, in the albedo and the cell field alike, over green ground. Renders from a
    /// later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALayerWhoseClumpsLeanWhole_MovesEachWeedAsOne_OverStillGround()
    {
        const int size = 128;
        const int baseRow = 24;
        Color32 green = new(40, 160, 40, 255);
        Color32 red = new(220, 30, 30, 255);
        Color32 blue = new(30, 30, 220, 255);
        Color32[] albedo = new Color32[size * size];
        Color32[] field = new Color32[size * size];
        Color32[] height = new Color32[size * size];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = green;
            field[i] = green;
            height[i] = new Color32(128, 0, 0, 255);
        }

        for (int column = 0; column < size / 32; column++)
        {
            for (int cellRow = 0; cellRow < size / 32; cellRow++)
            {
                for (int dy = -1; dy <= 10; dy++)
                {
                    int y = (cellRow * 32 + baseRow + dy) % size;
                    for (int x = column * 32 + 10; x <= column * 32 + 20; x++)
                    {
                        int i = y * size + x;
                        height[i].g = (byte)(dy + 2);
                        height[i].b = (byte)(dy <= 0 ? 0 : 255 * Mathf.Min(dy, 5) / 5);
                        field[i].a = 10;
                    }

                    if (dy >= 0)
                    {
                        albedo[y * size + column * 32 + 12] = red;
                    }
                }

                int mark = ((cellRow * 32 + baseRow + 10) % size) * size + column * 32 + 18;
                albedo[mark] = blue;
                field[mark] = new Color32(blue.r, blue.g, blue.b, 10);
            }
        }

        Texture2D albedoTexture = Texture("Whole albedo", albedo);
        Texture2D fieldTexture = Texture("Whole field", field);
        Texture2D heightTexture = Texture("Whole height", height);
        TerrainBlendRenderer[] renderers = new TerrainBlendRenderer[2];
        for (int r = 0; r < 2; r++)
        {
            TerrainMaterial swaying = TerrainMaterial.Create(r == 0 ? "Whole" : "Sheared", albedoTexture, fieldTexture, 4, TerrainMaterial.CellMode.None, green, new TerrainStyle.EdgeSettings(), heightTexture);
            createdObjects.Add(swaying);
            SerializedObject sway = new(swaying);
            sway.FindProperty("framesPerSecond").floatValue = 8f;
            sway.FindProperty("swayAmplitude").floatValue = 2f;
            sway.FindProperty("wholeClumps").boolValue = r == 0;
            sway.ApplyModifiedPropertiesWithoutUndo();
            GameObject gridObject = Track(new GameObject($"WholeGrid{r}", typeof(Grid)));
            gridObject.transform.position = new Vector3(r * 40f, 0f, 0f);
            gridObject.SetActive(false);
            Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
            ground.transform.SetParent(gridObject.transform, false);
            for (int x = 0; x < 6; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    ground.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }

            renderers[r] = gridObject.AddComponent<TerrainBlendRenderer>();
            renderers[r].Configure(style, new TerrainBlendRenderer.Layer(ground, swaying));
            gridObject.SetActive(true);
        }

        int drawn = 0;
        for (int frame = 0; frame < 30 && drawn == 0; frame++)
        {
            yield return null;
            renderers[0].SetFixedTime(0f);
            Texture2D pixels = TerrainBlendProbe.Render(renderers[0], new Rect(0f, 0f, 6f, 4f), 1, false, true, true);
            drawn = pixels.GetPixel(32 + 12, 32 + baseRow + 2).a > 0f ? 1 : 0;
            Object.DestroyImmediate(pixels);
        }

        Assert.That(drawn, Is.EqualTo(1), "The layer is drawn.");
        int leaned = 0;
        int sheared = 0;
        int slid = 0;
        for (int step = 0; step <= 20; step++)
        {
            for (int r = 0; r < 2; r++)
            {
                renderers[r].SetFixedTime(step * 0.125f);
                Texture2D pixels = TerrainBlendProbe.Render(renderers[r], new Rect(r * 40f, 0f, 6f, 4f), 1, false, true, true);
                for (int cell = 1; cell <= 4; cell++)
                {
                    int left = cell * 32;
                    int top = 32 + baseRow + 10;
                    int below = Lean(pixels, left, 32 + baseRow + 6);
                    int above = Lean(pixels, left, top);
                    bool markStays = IsBlue(pixels.GetPixel(left + 18, top));
                    if (r == 0)
                    {
                        Assert.That(above, Is.EqualTo(below), $"t {step * 0.125f}, cell {cell}: the weed leans as one across the cell row");
                        Assert.That(markStays, Is.True, $"t {step * 0.125f}, cell {cell}: the ground stays");
                        leaned += above > 0 ? 1 : 0;
                    }
                    else
                    {
                        sheared += above != below ? 1 : 0;
                        slid += markStays ? 0 : 1;
                    }
                }

                Object.DestroyImmediate(pixels);
                renderers[r].SetFixedTime(null);
            }
        }

        Assert.That(leaned, Is.GreaterThan(0), "The weeds lean in some frame.");
        Assert.That(sheared, Is.GreaterThan(0), "Without whole clumps a weed shears across the cell row in some frame.");
        Assert.That(slid, Is.GreaterThan(0), "Without whole clumps the ground slides with the weeds.");
    }

    /// <summary>
    /// How many pixels right of its column (12 px into the cell from <paramref name="left"/>) the red weed shows in row
    /// <paramref name="row"/> of <paramref name="pixels"/>, or -1 when it shows nowhere within 4 px.
    /// </summary>
    private static int Lean(Texture2D pixels, int left, int row)
    {
        for (int offset = 0; offset <= 4; offset++)
        {
            Color pixel = pixels.GetPixel(left + 12 + offset, row);
            if (pixel.r > pixel.g && pixel.r > pixel.b)
            {
                return offset;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether <paramref name="pixel"/> is mostly blue.
    /// </summary>
    private static bool IsBlue(Color pixel)
    {
        return pixel.b > pixel.r && pixel.b > pixel.g;
    }

    /// <summary>
    /// An animated layer whose height holds only sparkle phases lights only its sparkles, never its glint colour, even
    /// with glints set to stay lit all the time: snow sparkles without glints by its recipe writing no glint phase. The
    /// material is one dark blue with sparkle phases of 0 at a few interior texels, glinting red every second for all of
    /// it and sparkling green every second for half of it. At 0.25 s the sparkle texels show green and every other pixel
    /// keeps its colour; at 0.75 s the sparkles are out and they show the blue again; no pixel is ever red. Renders from
    /// a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALayerWithOnlySparklePhases_LightsOnlyItsSparkles()
    {
        const int size = 128;
        Color32 blue = new(30, 60, 140, 255);
        Color32[] albedo = new Color32[size * size];
        Color32[] height = new Color32[size * size];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = blue;
            height[i] = new Color32(128, 128, 0, 255);
        }

        Vector2Int[] sparkles = { new(40, 40), new(80, 56), new(60, 90) };
        foreach (Vector2Int sparkle in sparkles)
        {
            height[sparkle.y * size + sparkle.x].b = 128;
        }

        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial sparkling = TerrainMaterial.Create("Sparkling", Texture("Sparkling albedo", albedo), null, 4, TerrainMaterial.CellMode.None, blue, bare, Texture("Sparkling height", height));
        createdObjects.Add(sparkling);
        SerializedObject animation = new(sparkling);
        animation.FindProperty("framesPerSecond").floatValue = 8f;
        animation.FindProperty("glintColour").colorValue = Color.red;
        animation.FindProperty("glintPeriod").floatValue = 1f;
        animation.FindProperty("glintDuty").floatValue = 1f;
        animation.FindProperty("sparkleColour").colorValue = Color.green;
        animation.FindProperty("sparklePeriod").floatValue = 1f;
        animation.FindProperty("sparkleDuty").floatValue = 0.5f;
        animation.ApplyModifiedPropertiesWithoutUndo();

        GameObject gridObject = Track(new GameObject("SparklingGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap ground = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        ground.transform.SetParent(gridObject.transform);
        for (int x = 0; x < 4; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(ground, sparkling));
        gridObject.SetActive(true);
        Rect world = new(0f, 0f, 4f, 4f);
        Color32 plain = default;
        for (int frame = 0; frame < 30 && !IsDrawn(plain); frame++)
        {
            yield return null;
            renderer.SetFixedTime(0.25f);
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            plain = pixels.GetPixel(20, 20);
            Object.DestroyImmediate(pixels);
        }

        Assert.That(IsDrawn(plain), Is.True, "The layer is drawn.");
        foreach (float time in new[] { 0.25f, 0.75f })
        {
            renderer.SetFixedTime(time);
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            int lit = 0;
            for (int y = 16; y < size - 16; y++)
            {
                for (int x = 16; x < size - 16; x++)
                {
                    Color32 pixel = pixels.GetPixel(x, y);
                    Assert.That(pixel.r > pixel.g && pixel.r > pixel.b, Is.False, $"t {time}: no pixel glints, at ({x}, {y})");
                    bool sparkle = System.Array.IndexOf(sparkles, new Vector2Int(x, y)) >= 0;
                    if (sparkle && time < 0.5f)
                    {
                        Assert.That(pixel.g, Is.GreaterThan(pixel.b), $"t {time}: the sparkle at ({x}, {y}) is lit");
                        lit++;
                    }
                    else
                    {
                        Assert.That(pixel, Is.EqualTo(plain), $"t {time}: ({x}, {y}) keeps its colour");
                    }
                }
            }

            Object.DestroyImmediate(pixels);
            Assert.That(lit, Is.EqualTo(time < 0.5f ? sparkles.Length : 0), $"t {time}: lit sparkles");
        }

        renderer.SetFixedTime(null);
    }

    /// <summary>
    /// A layer with waves rolls them in toward its coast: green ground fills a 12 x 14 block and a blue sea its bottom six
    /// rows, its waves 32 px apart on a 2 s period, whole everywhere, in red with a yellow crest. Down a column in the
    /// middle of the sea, the waves' bands lie a spacing apart, and a quarter of a second later each that stays in view
    /// has moved 3 to 5 px toward the coast. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALayerWithWaves_RollsThemInTowardItsCoast()
    {
        TerrainBlendRenderer renderer = WavesRenderer(14, 6, 0f, out Color32 blue);
        Rect world = new(0f, 0f, 12f, 14f);
        yield return RenderUntilDrawn(renderer, world, new Vector2Int(192, 300));

        List<int> before = BandFronts(renderer, world, 0f, blue);
        List<int> after = BandFronts(renderer, world, 0.25f, blue);

        Assert.That(before.Count, Is.GreaterThanOrEqualTo(2), "waves cross the column");
        for (int i = 0; i < before.Count; i++)
        {
            int front = before[i];
            if (front + 5 <= 168)
            {
                Assert.That(after.Exists(moved => moved >= front + 3 && moved <= front + 5), Is.True, $"the band at {front} moves 4 px toward the coast");
                Assert.That(after.Contains(front), Is.False, $"the band at {front} moves");
            }

            if (i > 0)
            {
                Assert.That(front - before[i - 1], Is.InRange(30, 34), $"the band at {front} lies a spacing from the last");
            }
        }

        renderer.SetFixedTime(null);
    }

    /// <summary>
    /// A layer with waves and swash washes up over the layer it meets as each wave arrives, never further than its
    /// swash, and without swash draws nothing past its edge. Green ground fills an 8 x 4 block and a blue sea its bottom
    /// two rows, with no rim, outline or shadow; its waves arrive every second and its swash, 6 px, draws every colour
    /// white. Over the eight frames of a period, pixels of the band just above the sea's top edge show white, none more
    /// than the swash past it. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALayerWithWaves_WashesUpWhatItMeets_NeverPastItsSwash()
    {
        TerrainBlendRenderer renderer = WavesRenderer(4, 2, 6f, out _);
        Rect world = new(0f, 0f, 8f, 4f);
        yield return RenderUntilDrawn(renderer, world, new Vector2Int(128, 110));

        Color32 inland = Pixel(renderer, world, 0f, new Vector2Int(128, 110));
        int washed = 0;
        for (int step = 0; step < 8; step++)
        {
            renderer.SetFixedTime(step * 0.125f);
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            for (int x = 32; x < 224; x++)
            {
                for (int y = 66; y < 80; y++)
                {
                    Color32 pixel = pixels.GetPixel(x, y);
                    bool white = pixel.r > 200 && pixel.g > 200 && pixel.b > 200;
                    washed += white ? 1 : 0;
                    Assert.That(white && y > 64 + 6 + 2, Is.False, $"t {step * 0.125f}: ({x}, {y}) is washed past the swash");
                }
            }

            Object.DestroyImmediate(pixels);
        }

        Assert.That(washed, Is.GreaterThan(200), "The wash reaches past the edge.");
        TerrainMaterial sea = renderer.Layers[1].Material;
        SerializedObject settings = new(sea);
        settings.FindProperty("swash").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        renderer.Rebuild();
        yield return null;
        for (int step = 0; step < 8; step++)
        {
            renderer.SetFixedTime(step * 0.125f);
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            for (int x = 32; x < 224; x++)
            {
                for (int y = 67; y < 80; y++)
                {
                    Assert.That((Color32)pixels.GetPixel(x, y), Is.EqualTo(inland), $"t {step * 0.125f}: ({x}, {y}) without swash");
                }
            }

            Object.DestroyImmediate(pixels);
        }

        renderer.SetFixedTime(null);
    }

    /// <summary>
    /// A sunken layer (deep water) shows its bank along a top edge over a lower layer it meets with an ordinary pair,
    /// in that layer's bank colour, and none along a top edge over one it meets seamlessly (shallows, the same water's
    /// surface), where its own interior runs up to the edge. A blue sunken layer fills the bottom two rows, a green
    /// layer lies over the left half of the row above it and green ground under everything, each with a red bank colour;
    /// no edge draws an outline, rim or shadow, and the sunken layer meets the left layer seamlessly. Renders from a later
    /// editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ASunkenLayer_ShowsNoBank_OverALayerItMeetsSeamlessly()
    {
        Color32 green = new(40, 160, 40, 255);
        Color32 blue = new(30, 60, 200, 255);
        Color32 red = new(220, 30, 30, 255);
        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial ground = SolidMaterial("Ground", green, bare);
        TerrainMaterial seamlessBelow = SolidMaterial("Shallow", green, bare);
        TerrainMaterial sunken = SolidMaterial("Sunken", blue, bare);
        foreach (TerrainMaterial material in new[] { ground, seamlessBelow })
        {
            SerializedObject settings = new(material);
            settings.FindProperty("bankColour").colorValue = red;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        SerializedObject sinking = new(sunken);
        sinking.FindProperty("sinkDepth").floatValue = 4f;
        sinking.FindProperty("bankShadow").floatValue = 0f;
        sinking.ApplyModifiedPropertiesWithoutUndo();
        TerrainStyle seamlessStyle = TestTerrainStyle.Create(createdObjects, new TerrainStyle.PairOverride(sunken, seamlessBelow, new TerrainStyle.EdgeSettings(0f, null, false, Color.black, Color.black, 0f, 0f, seamless: true)));

        GameObject gridObject = Track(new GameObject("SunkenGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap[] tilemaps = new Tilemap[3];
        for (int i = 0; i < tilemaps.Length; i++)
        {
            tilemaps[i] = new GameObject($"Layer{i}", typeof(Tilemap)).GetComponent<Tilemap>();
            tilemaps[i].transform.SetParent(gridObject.transform);
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                tilemaps[0].SetTile(new Vector3Int(x, y, 0), tile);
            }

            tilemaps[2].SetTile(new Vector3Int(x, 0, 0), tile);
            tilemaps[2].SetTile(new Vector3Int(x, 1, 0), tile);
            if (x < 4)
            {
                tilemaps[1].SetTile(new Vector3Int(x, 2, 0), tile);
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(seamlessStyle, new TerrainBlendRenderer.Layer(tilemaps[0], ground), new TerrainBlendRenderer.Layer(tilemaps[1], seamlessBelow), new TerrainBlendRenderer.Layer(tilemaps[2], sunken));
        gridObject.SetActive(true);
        Rect world = new(0f, 0f, 8f, 4f);
        Color32 underSeamless = default;
        Color32 underGround = default;
        for (int frame = 0; frame < 30 && !IsDrawn(underGround); frame++)
        {
            yield return null;
            Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
            underSeamless = pixels.GetPixel(48, 61);
            underGround = pixels.GetPixel(208, 61);
            Object.DestroyImmediate(pixels);
        }

        Assert.That(underGround.r, Is.GreaterThan(underGround.b), "The bank shows over the ground.");
        Assert.That(underSeamless.b, Is.GreaterThan(underSeamless.r), "No bank shows over the seamless layer.");
    }

    /// <summary>
    /// A bridge (a dock) lies on what is painted under it: where it crosses a shore from water onto the ground, the
    /// water keeps its own straight edge beside it, while an ordinary layer in its place carries the water on under its
    /// ground end, so the water's rounded inner corner reaches onto the ground beside it. Green ground fills an 8 x 4
    /// block, blue water its top two rows, and a red layer crosses the shore in a two-cell column; no edge draws an
    /// outline, rim or shadow, and the edges have no noise. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ABridge_LeavesTheLayersUnderItTheirOwnEdges()
    {
        SetEdgeNoise(0f);
        Color32 green = new(40, 160, 40, 255);
        Color32 blue = new(30, 60, 200, 255);
        Color32 red = new(220, 30, 30, 255);
        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial ground = SolidMaterial("Ground", green, bare);
        TerrainMaterial water = SolidMaterial("Water", blue, bare);
        TerrainMaterial dock = SolidMaterial("Dock", red, bare);
        GameObject gridObject = Track(new GameObject("BridgeGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap[] tilemaps = new Tilemap[3];
        for (int i = 0; i < tilemaps.Length; i++)
        {
            tilemaps[i] = new GameObject($"Layer{i}", typeof(Tilemap)).GetComponent<Tilemap>();
            tilemaps[i].transform.SetParent(gridObject.transform);
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                tilemaps[0].SetTile(new Vector3Int(x, y, 0), tile);
                if (y >= 2)
                {
                    tilemaps[1].SetTile(new Vector3Int(x, y, 0), tile);
                }

                if (x == 3 || x == 4)
                {
                    tilemaps[2].SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(tilemaps[0], ground), new TerrainBlendRenderer.Layer(tilemaps[1], water), new TerrainBlendRenderer.Layer(tilemaps[2], dock));
        gridObject.SetActive(true);
        Rect world = new(0f, 0f, 8f, 4f);
        Color32 beside = default;
        for (int frame = 0; frame < 30 && !IsDrawn(beside); frame++)
        {
            yield return null;
            beside = RenderedPixel(renderer, world, 94, 62);
        }

        Assert.That(beside.b, Is.GreaterThan(beside.g), "An ordinary layer carries the water on under its ground end.");

        SerializedObject settings = new(dock);
        settings.FindProperty("bridge").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        renderer.RunScheduledRebuild();
        Color32 bridged = default;
        for (int frame = 0; frame < 30 && !IsDrawn(bridged); frame++)
        {
            yield return null;
            bridged = RenderedPixel(renderer, world, 94, 62);
        }

        Assert.That(bridged.g, Is.GreaterThan(bridged.b), "Beside a bridge the water keeps its straight shore.");
        Assert.That(RenderedPixel(renderer, world, 112, 62).r, Is.GreaterThan(200), "The bridge is drawn over the ground.");
        Assert.That(RenderedPixel(renderer, world, 112, 100).r, Is.GreaterThan(200), "The bridge is drawn over the water.");
    }

    /// <summary>
    /// Where a bridge ends on the edge of a layer painted under it, with another layer between the two in the draw
    /// order, the lower layer's edge is not drawn beneath the bridge's: the ground (an 8 x 4 block casting a yellow
    /// shadow over nothing) and a red bridge column across it both end at the map's bottom edge, and below the bridge
    /// nothing is drawn, while below the ground beside it the ground's shadow shows. A blue layer between them fills a
    /// corner away from the bridge. No edge has noise. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ABridgeEndingOnALowerLayersEdge_HidesThatEdgeBeneathIt()
    {
        SetEdgeNoise(0f);
        Color32 green = new(40, 160, 40, 255);
        Color32 blue = new(30, 60, 200, 255);
        Color32 red = new(220, 30, 30, 255);
        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainStyle.EdgeSettings shadowed = new(0f, null, false, Color.black, new Color(1f, 1f, 0f), 1f, 3f);
        TerrainMaterial ground = SolidMaterial("Ground", green, shadowed);
        TerrainMaterial water = SolidMaterial("Water", blue, bare);
        TerrainMaterial dock = SolidMaterial("Dock", red, bare);
        SerializedObject settings = new(dock);
        settings.FindProperty("bridge").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GameObject gridObject = Track(new GameObject("BridgeEdgeGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap[] tilemaps = new Tilemap[3];
        for (int i = 0; i < tilemaps.Length; i++)
        {
            tilemaps[i] = new GameObject($"Layer{i}", typeof(Tilemap)).GetComponent<Tilemap>();
            tilemaps[i].transform.SetParent(gridObject.transform);
        }

        for (int x = 0; x < 8; x++)
        {
            for (int y = 0; y < 4; y++)
            {
                tilemaps[0].SetTile(new Vector3Int(x, y, 0), tile);
                if (x >= 6 && y >= 2)
                {
                    tilemaps[1].SetTile(new Vector3Int(x, y, 0), tile);
                }

                if (x == 3 || x == 4)
                {
                    tilemaps[2].SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(tilemaps[0], ground), new TerrainBlendRenderer.Layer(tilemaps[1], water), new TerrainBlendRenderer.Layer(tilemaps[2], dock));
        gridObject.SetActive(true);
        Rect world = new(0f, -1f, 8f, 5f);
        Color32 drawn = default;
        for (int frame = 0; frame < 30 && !IsDrawn(drawn); frame++)
        {
            yield return null;
            drawn = RenderedPixel(renderer, world, 128, 64);
        }

        Assert.That(drawn.r, Is.GreaterThan(200), "The bridge is drawn over the ground.");
        Color32 belowGround = RenderedPixel(renderer, world, 40, 30);
        Assert.That(belowGround.r, Is.GreaterThan(100), "The ground casts its shadow below its edge.");
        Assert.That(belowGround.g, Is.GreaterThan(100));
        Color32 belowBridge = RenderedPixel(renderer, world, 128, 30);
        Assert.That(belowBridge.r + belowBridge.g + belowBridge.b, Is.Zero, "Below the bridge the ground's edge is not drawn.");
    }

    /// <summary>
    /// A layer that turns to its runs reads its textures turned a quarter in cells where it runs further left and right
    /// than up and down, and as they are elsewhere: an albedo striped across its x (red then blue every 16 px) shows its
    /// stripes along x in a run going up and along y in a run going across. Renders from a later editor update, until
    /// anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator ALayerThatTurnsToItsRuns_TurnsItsTexturesWhereItRunsAcross()
    {
        SetEdgeNoise(0f);
        Color32 red = new(220, 30, 30, 255);
        Color32 blue = new(30, 60, 200, 255);
        Color32[] stripes = new Color32[128 * 128];
        Color32[] height = new Color32[128 * 128];
        for (int i = 0; i < stripes.Length; i++)
        {
            stripes[i] = (i % 128) % 32 < 16 ? red : blue;
            height[i] = new Color32(128, 128, 0, 255);
        }

        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial boards = TerrainMaterial.Create("Boards", Texture("Boards albedo", stripes), null, 4, TerrainMaterial.CellMode.None, red, bare, Texture("Boards height", height));
        createdObjects.Add(boards);
        SerializedObject settings = new(boards);
        settings.FindProperty("turnToRuns").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GameObject gridObject = Track(new GameObject("TurnGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap tilemap = new GameObject("Boards", typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(gridObject.transform);
        for (int x = 0; x < 6; x++)
        {
            tilemap.SetTile(new Vector3Int(x, 0, 0), tile);
        }

        for (int y = 2; y < 8; y++)
        {
            tilemap.SetTile(new Vector3Int(8, y, 0), tile);
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(tilemap, boards));
        gridObject.SetActive(true);
        Rect world = new(0f, 0f, 10f, 8f);
        Color32 first = default;
        for (int frame = 0; frame < 30 && !IsDrawn(first); frame++)
        {
            yield return null;
            first = RenderedPixel(renderer, world, 2 * 32 + 16, 4);
        }

        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        Color32 acrossLow = pixels.GetPixel(2 * 32 + 4, 8);
        Color32 acrossHigh = pixels.GetPixel(2 * 32 + 4, 24);
        Color32 acrossRight = pixels.GetPixel(2 * 32 + 20, 8);
        Color32 upLeft = pixels.GetPixel(8 * 32 + 4, 4 * 32 + 8);
        Color32 upRight = pixels.GetPixel(8 * 32 + 20, 4 * 32 + 8);
        Color32 upHigher = pixels.GetPixel(8 * 32 + 4, 4 * 32 + 24);
        Object.DestroyImmediate(pixels);

        Assert.That(SameStripe(acrossLow, acrossRight), Is.True, "Across a turned run the stripes run along x.");
        Assert.That(SameStripe(acrossLow, acrossHigh), Is.False, "A turned run changes stripe going up.");
        Assert.That(SameStripe(upLeft, upHigher), Is.True, "Up an unturned run the stripes run along y.");
        Assert.That(SameStripe(upLeft, upRight), Is.False, "An unturned run changes stripe going across.");
    }

    /// <summary>
    /// Raised ground draws a cliff face along its bottom edge, inside its lowest cells, in the bank colour of the ground
    /// on top; casts a shadow on the ground below the face; slopes down a ramp in the ground's own colour, darkening
    /// towards its foot; and leaves the rest of its top and the ground beside it the ground's own colour; without a raised
    /// tilemap the same pixels show plain ground. Green ground with a red bank colour fills an 8 x 6 block, raised ground
    /// covers columns 1 to 6 of rows 2 to 4, and columns 3 and 4 of row 2 are a ramp; no edge draws an outline, rim or
    /// shadow, and the cliff has no fringe or pebbles. Renders from a later editor update, until anything is drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator RaisedGround_DrawsItsCliffFace_ItsShadow_AndItsRamp()
    {
        Color32 green = new(40, 160, 40, 255);
        Color32 red = new(220, 30, 30, 255);
        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial ground = SolidMaterial("Ground", green, bare);
        SerializedObject groundSettings = new(ground);
        groundSettings.FindProperty("bankColour").colorValue = red;
        groundSettings.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject cliff = new(style);
        cliff.FindProperty("cliffFringe").floatValue = 0f;
        cliff.FindProperty("cliffPebbleChance").floatValue = 0f;
        cliff.ApplyModifiedPropertiesWithoutUndo();
        GameObject gridObject = Track(new GameObject("RaisedGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap groundMap = new GameObject("Ground", typeof(Tilemap)).GetComponent<Tilemap>();
        groundMap.transform.SetParent(gridObject.transform);
        Tilemap raisedMap = new GameObject("Raised", typeof(Tilemap)).GetComponent<Tilemap>();
        raisedMap.transform.SetParent(gridObject.transform);
        RaisedGroundTile raised = Track(RaisedGroundTile.Create(false));
        RaisedGroundTile ramp = Track(RaisedGroundTile.Create(true));
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Vector3Int cell = new(x, y, 0);
                groundMap.SetTile(cell, tile);
                if (x >= 1 && x <= 6 && y >= 2 && y <= 4)
                {
                    raisedMap.SetTile(cell, (x == 3 || x == 4) && y == 2 ? ramp : raised);
                }
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        TerrainBlendRenderer.Layer layer = new(groundMap, ground);
        renderer.Configure(style, raisedMap, layer);
        gridObject.SetActive(true);
        Rect world = new(0f, 0f, 8f, 6f);
        yield return RenderUntilDrawn(renderer, world, new Vector2Int(48, 70));
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        Color32 face = pixels.GetPixel(48, 70);
        Color32 top = pixels.GetPixel(48, 120);
        Color32 slope = pixels.GetPixel(112, 70);
        Color32 shadowed = pixels.GetPixel(48, 62);
        Color32 open = pixels.GetPixel(48, 30);
        Color32 beside = pixels.GetPixel(240, 70);
        Object.DestroyImmediate(pixels);

        Assert.That(face.r, Is.GreaterThan(face.g), "The cliff face shows the bank colour.");
        Assert.That(top, Is.EqualTo(open), "The plateau's top shows the ground.");
        Assert.That(beside, Is.EqualTo(open), "The ground beside the plateau shows no cliff.");
        Assert.That(shadowed.g, Is.LessThan(open.g), "The cliff casts a shadow below it.");
        Assert.That(slope.g, Is.GreaterThan(slope.r), "The ramp shows the ground.");
        Assert.That(slope.g, Is.LessThan(open.g), "The ramp's ground darkens towards its foot.");
        Assert.That(slope.r, Is.LessThanOrEqualTo(open.r), "The ramp's ground is not drawn towards the bank colour.");

        renderer.Configure(style, (Tilemap)null, layer);
        Color32 plain = default;
        for (int frame = 0; frame < 30 && !IsDrawn(plain); frame++)
        {
            yield return null;
            plain = RenderedPixel(renderer, world, 48, 70);
        }

        Assert.That(plain, Is.EqualTo(RenderedPixel(renderer, world, 48, 30)), "Without a raised tilemap the ground is level.");
    }

    /// <summary>
    /// The renderer's elevation map holds what each cell of its raised tilemap is (raised ground, a ramp, or level
    /// ground) and follows tiles painted or erased there once the changes are uploaded, its lookup texture holds the
    /// style's cliff height and its runtime material has the raised ground keyword; without a raised tilemap it has
    /// neither, so the shader draws no cliffs.
    /// </summary>
    [Test]
    public void TheElevationMap_FollowsTheRaisedTilemap()
    {
        GameObject raisedObject = new("Raised", typeof(Tilemap));
        raisedObject.transform.SetParent(root.transform);
        Tilemap raisedMap = raisedObject.GetComponent<Tilemap>();
        RaisedGroundTile raised = Track(RaisedGroundTile.Create(false));
        RaisedGroundTile ramp = Track(RaisedGroundTile.Create(true));
        raisedMap.SetTile(new Vector3Int(3, 3, 0), raised);
        raisedMap.SetTile(new Vector3Int(4, 3, 0), ramp);
        TerrainBlendRenderer renderer = root.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, raisedMap, new TerrainBlendRenderer.Layer(layers[0], materials[0]));
        root.SetActive(true);

        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(3, 3, 0)), Is.EqualTo(RaisedGround.Raised));
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(4, 3, 0)), Is.EqualTo(RaisedGround.Ramp));
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(5, 3, 0)), Is.EqualTo(RaisedGround.Level));
        Assert.That(renderer.Lut.GetPixel(TerrainBlendLut.SharedCliffColumn, TerrainBlendLut.SharedRow).r, Is.EqualTo(style.CliffHeight));
        LocalKeyword raisedKeyword = new(renderer.RuntimeMaterial.shader, "_TERRAIN_RAISED");
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(raisedKeyword), Is.True, "The material draws raised ground.");

        raisedMap.SetTile(new Vector3Int(5, 3, 0), raised);
        raisedMap.SetTile(new Vector3Int(3, 3, 0), null);
        Assert.That(renderer.IsFlushScheduled, Is.True);
        renderer.FlushPendingChanges();
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(5, 3, 0)), Is.EqualTo(RaisedGround.Raised));
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(3, 3, 0)), Is.EqualTo(RaisedGround.Level));

        renderer.Configure(style, (Tilemap)null, new TerrainBlendRenderer.Layer(layers[0], materials[0]));
        Assert.That(renderer.Lut.GetPixel(TerrainBlendLut.SharedCliffColumn, TerrainBlendLut.SharedRow), Is.EqualTo(Color.clear));
        Assert.That(renderer.RuntimeMaterial.IsKeywordEnabled(new LocalKeyword(renderer.RuntimeMaterial.shader, "_TERRAIN_RAISED")), Is.False, "Without raised ground the material has no raised keyword.");
        Assert.That(renderer.ElevationMap.GetCell(new Vector3Int(5, 3, 0)), Is.EqualTo(RaisedGround.Level));
    }

    /// <summary>
    /// The colour <paramref name="renderer"/> draws at pixel (<paramref name="x"/>, <paramref name="y"/>) of
    /// <paramref name="world"/> rendered at one screen pixel per art pixel, with every other renderer left out.
    /// </summary>
    private static Color32 RenderedPixel(TerrainBlendRenderer renderer, Rect world, int x, int y)
    {
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        Color32 colour = pixels.GetPixel(x, y);
        Object.DestroyImmediate(pixels);
        return colour;
    }

    /// <summary>
    /// Whether a rendered pixel shows anything: the probe's camera clears to opaque black, so a render that drew nothing,
    /// as the first one after the terrain shader is imported, reads black there.
    /// </summary>
    private static bool IsDrawn(Color32 pixel)
    {
        return pixel.r + pixel.g + pixel.b > 0;
    }

    /// <summary>
    /// Whether two pixels show the same stripe, red or blue.
    /// </summary>
    private static bool SameStripe(Color32 a, Color32 b)
    {
        return a.r > a.b == b.r > b.b;
    }

    /// <summary>
    /// A renderer over green ground filling 12 x <paramref name="rows"/> cells (8 wide when only 4 rows) and a blue sea
    /// filling its bottom <paramref name="seaRows"/> rows with waves: 32 px apart on a 2 s period at 8 frames per second
    /// for the ground's 14 rows, or 16 px apart on a 1 s period for the 4 rows, 6 px wide, square to the coast, whole
    /// everywhere, red with a yellow crest and a black trough, and <paramref name="swash"/> pixels of swash, every colour
    /// of which is white. Neither layer draws a rim, outline or shadow.
    /// </summary>
    private TerrainBlendRenderer WavesRenderer(int rows, int seaRows, float swash, out Color32 blue)
    {
        Color32 green = new(40, 160, 40, 255);
        blue = new Color32(30, 60, 200, 255);
        TerrainStyle.EdgeSettings bare = new(0f, null, false, Color.black, Color.black, 0f, 0f);
        TerrainMaterial ground = SolidMaterial("Ground", green, bare);
        TerrainMaterial sea = SolidMaterial("Sea", blue, bare);
        bool tall = rows > 4;
        SerializedObject settings = new(sea);
        settings.FindProperty("framesPerSecond").floatValue = 8f;
        settings.FindProperty("glintDuty").floatValue = 0f;
        settings.FindProperty("sparkleDuty").floatValue = 0f;
        settings.FindProperty("swash").floatValue = swash;
        settings.FindProperty("waveSpacing").floatValue = tall ? 32f : 16f;
        settings.FindProperty("wavePeriod").floatValue = tall ? 2f : 1f;
        settings.FindProperty("waveWidth").floatValue = 6f;
        settings.FindProperty("waveAngle").floatValue = 0f;
        settings.FindProperty("waveWholeWithin").floatValue = 1000f;
        settings.FindProperty("waveColour").colorValue = new Color(1f, 0.1f, 0.1f);
        settings.FindProperty("waveCrestColour").colorValue = new Color(1f, 1f, 0f);
        settings.FindProperty("waveTroughColour").colorValue = Color.black;
        foreach (string colour in new[] { "washFoamColour", "washFoamShadeColour", "washFilmColour", "washFilmBackColour" })
        {
            settings.FindProperty(colour).colorValue = Color.white;
        }

        settings.FindProperty("washWet").floatValue = 0f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        GameObject gridObject = Track(new GameObject("WavesGrid", typeof(Grid)));
        gridObject.SetActive(false);
        Tilemap land = new GameObject("Land", typeof(Tilemap)).GetComponent<Tilemap>();
        Tilemap water = new GameObject("Water", typeof(Tilemap)).GetComponent<Tilemap>();
        land.transform.SetParent(gridObject.transform);
        water.transform.SetParent(gridObject.transform);
        int columns = tall ? 12 : 8;
        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                land.SetTile(new Vector3Int(x, y, 0), tile);
                if (y < seaRows)
                {
                    water.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, new TerrainBlendRenderer.Layer(land, ground), new TerrainBlendRenderer.Layer(water, sea));
        gridObject.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// Renders <paramref name="world"/> at time 0 on later editor updates until the pixel at <paramref name="probe"/>
    /// is drawn, at most 30 updates, and asserts it was.
    /// </summary>
    private static IEnumerator RenderUntilDrawn(TerrainBlendRenderer renderer, Rect world, Vector2Int probe)
    {
        Color32 pixel = default;
        for (int frame = 0; frame < 30 && !IsDrawn(pixel); frame++)
        {
            yield return null;
            pixel = Pixel(renderer, world, 0f, probe);
        }

        Assert.That(IsDrawn(pixel), Is.True, "The layers are drawn.");
    }

    /// <summary>
    /// The pixel at <paramref name="at"/> of <paramref name="world"/> rendered at <paramref name="time"/>.
    /// </summary>
    private static Color32 Pixel(TerrainBlendRenderer renderer, Rect world, float time, Vector2Int at)
    {
        renderer.SetFixedTime(time);
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        Color32 pixel = pixels.GetPixel(at.x, at.y);
        Object.DestroyImmediate(pixels);
        return pixel;
    }

    /// <summary>
    /// The rows, bottom up, of the shoreward fronts of the waves' bands down the middle column of the sea, from 96 px
    /// to 168 px up (well inside the sea and clear of the coast's last pixels and of the map's edge), rendered at
    /// <paramref name="time"/>: the top row of each run of rows not showing the sea's own colour.
    /// </summary>
    private static List<int> BandFronts(TerrainBlendRenderer renderer, Rect world, float time, Color32 sea)
    {
        renderer.SetFixedTime(time);
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, false, true, true);
        List<int> fronts = new();
        bool inBand = false;
        for (int y = 96; y <= 168; y++)
        {
            Color32 pixel = pixels.GetPixel(192, y);
            bool wave = pixel.r != sea.r || pixel.g != sea.g || pixel.b != sea.b;
            if (inBand && !wave)
            {
                fronts.Add(y - 1);
            }

            inBand = wave;
        }

        Object.DestroyImmediate(pixels);
        return fronts;
    }

    /// <summary>
    /// A material named <paramref name="name"/> of one colour, with no cell field and a flat height, and
    /// <paramref name="edge"/> as its default edge.
    /// </summary>
    private TerrainMaterial SolidMaterial(string name, Color32 colour, TerrainStyle.EdgeSettings edge)
    {
        Color32[] albedo = new Color32[128 * 128];
        Color32[] height = new Color32[128 * 128];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = colour;
            height[i] = new Color32(128, 128, 0, 255);
        }

        TerrainMaterial material = TerrainMaterial.Create(name, Texture($"{name} albedo", albedo), null, 4, TerrainMaterial.CellMode.None, colour, edge, Texture($"{name} height", height));
        createdObjects.Add(material);
        return material;
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
    /// The layer count the runtime material receives is the one the renderer was built with: after an inspector change
    /// adds a layer, a fixed time or the debug view set before the rebuild still hands the shader the built count, which
    /// the lookup texture's rows follow, and the rebuild hands it the new one.
    /// </summary>
    [Test]
    public void TheLayerCountTheShaderReads_IsTheBuiltOne_UntilTheRebuild()
    {
        TerrainBlendRenderer renderer = CreateRenderer();
        int infoId = Shader.PropertyToID("_TerrainInfo");
        Tilemap water = new GameObject("Water", typeof(Tilemap)).GetComponent<Tilemap>();
        water.transform.SetParent(root.transform);
        SerializedObject serialized = new(renderer);
        SerializedProperty layerList = serialized.FindProperty("layers");
        layerList.arraySize++;
        SerializedProperty added = layerList.GetArrayElementAtIndex(3);
        added.FindPropertyRelative("tilemap").objectReferenceValue = water;
        added.FindPropertyRelative("material").objectReferenceValue = materials[0];
        serialized.ApplyModifiedPropertiesWithoutUndo();

        renderer.SetFixedTime(2.5f);
        renderer.SetDebugView(true);

        Assert.That(renderer.IsRebuildScheduled, Is.True);
        Assert.That(renderer.RuntimeMaterial.GetVector(infoId), Is.EqualTo(new Vector4(3f, 1f, 1f, 2.5f)));
        Assert.That(renderer.Lut.height, Is.EqualTo(TerrainBlendLut.Height(3)));

        renderer.RunScheduledRebuild();

        Assert.That(renderer.RuntimeMaterial.GetVector(infoId), Is.EqualTo(new Vector4(4f, 1f, 1f, 2.5f)));
        Assert.That(renderer.Lut.height, Is.EqualTo(TerrainBlendLut.Height(4)));
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
    /// A readable 128 px RGBA32 texture with sRGB off holding <paramref name="pixels"/>, destroyed after the test.
    /// </summary>
    private Texture2D Texture(string name, Color32[] pixels)
    {
        Texture2D texture = Track(new Texture2D(128, 128, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point });
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
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
    /// The 3 x 3 cells of block <paramref name="index"/> of a strip 5 cells high: cells 1 to 3 across and up, from 4
    /// cells per block along x.
    /// </summary>
    private static Vector3Int[] BlockCells(int index)
    {
        List<Vector3Int> cells = new();
        for (int y = 1; y < 4; y++)
        {
            for (int x = 1; x < 4; x++)
            {
                cells.Add(new Vector3Int(4 * index + x, y, 0));
            }
        }

        return cells.ToArray();
    }

    /// <summary>
    /// Renders <paramref name="renderer"/>'s debug view over <paramref name="world"/> at one screen pixel per art pixel,
    /// at a fixed time and with every other renderer left out, and returns the probe's class (the layer on top plus
    /// one) at the centre of each of the first <paramref name="blocks"/> blocks (<see cref="BlockCells"/>).
    /// </summary>
    private static byte[] DebugClasses(TerrainBlendRenderer renderer, Rect world, int blocks)
    {
        renderer.SetDebugView(true);
        renderer.SetFixedTime(0f);
        Texture2D pixels = TerrainBlendProbe.Render(renderer, world, 1, true, true, true);
        try
        {
            byte[] classes = new byte[blocks];
            for (int i = 0; i < blocks; i++)
            {
                Color32 colour = pixels.GetPixel(Mathf.RoundToInt((4 * i + 2.5f - world.xMin) * 32f), Mathf.RoundToInt((2.5f - world.yMin) * 32f));
                classes[i] = TerrainBlendProbe.DebugClass(colour);
            }

            return classes;
        }
        finally
        {
            Object.DestroyImmediate(pixels);
            renderer.SetDebugView(false);
            renderer.SetFixedTime(null);
        }
    }

    /// <summary>
    /// The control map texture's 16-bit texel for <paramref name="cell"/>.
    /// </summary>
    private static ushort TexelOf(TerrainBlendRenderer renderer, Vector3Int cell)
    {
        BoundsInt bounds = renderer.ControlMap.Bounds;
        return renderer.ControlMap.Texture.GetPixelData<ushort>(0)[(cell.y - bounds.yMin) * bounds.size.x + cell.x - bounds.xMin];
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
