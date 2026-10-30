using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector of every kind of <see cref="PropRecipe"/>: its settings, then every variant baked on a strip of each
/// preview terrain (its albedo tiled at one texel a pixel, the variants and the rogue's idle frame facing south for
/// scale, all at the worlds' 0.85 light, without the sun's shadows), each footprint centred on its cells as the placement tool
/// snaps it; at 1x, and at 3x (shrunk to fit) with the cells drawn faintly, the cells each footprint blocks and the
/// footprint itself. The strips are baked again when the recipe changes (its serialized settings are compared on each
/// layout). Below them: each variant's size and footprint, the bake time, why the recipe cannot be written if it cannot,
/// and Generate, which writes the sheet and prefabs through <see cref="PropWriter"/> once the inspector is drawn.
/// </summary>
[CustomEditor(typeof(PropRecipe), true)]
public sealed class PropRecipeEditor : Editor
{
    private const float WorldLight = 0.85f;
    private const int Cell = PropBaker.PixelsPerCell;
    private const int Zoom = 3;
    private const int RogueCells = 2;
    private const int RogueFeet = 8;
    private const string RogueSheet = "Assets/Sprites/Characters/Rogue/Rogue_Idle.png";
    private const string RogueSlice = "Rogue_Idle_S_0";
    private static readonly Color32 BareGround = new(70, 104, 52, 255);
    private static readonly Color GridColour = new(1f, 1f, 1f, 0.2f);
    private static readonly Color CellsColour = new(1f, 0.82f, 0.24f, 0.9f);
    private static readonly Color FootprintColour = new(1f, 0.35f, 0.24f, 0.9f);

    private readonly Dictionary<string, Picture> pictures = new();
    private readonly List<Strip> strips = new();
    private List<PropVariant> variants;
    private string previewKey;
    private string previewProblem;
    private string writeProblem;
    private double bakeMilliseconds;

    /// <summary>
    /// Destroys the strips' textures.
    /// </summary>
    private void OnDisable()
    {
        ReleaseStrips();
    }

    /// <summary>
    /// Draws the settings, the strips, the lines under them and Generate.
    /// </summary>
    public override void OnInspectorGUI()
    {
        PropRecipe recipe = (PropRecipe)target;
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (Event.current.type == EventType.Layout)
        {
            RebuildIfChanged(recipe);
        }

        if (previewProblem != null)
        {
            EditorGUILayout.HelpBox(previewProblem, MessageType.Warning);
        }
        else
        {
            foreach (Strip strip in strips)
            {
                EditorGUILayout.LabelField(strip.Label, EditorStyles.boldLabel);
                DrawStrip(strip, 1);
                DrawStrip(strip, Zoom);
            }

            DrawSummary(recipe);
        }

        if (writeProblem != null)
        {
            EditorGUILayout.HelpBox(writeProblem, MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(previewProblem != null || writeProblem != null))
        {
            if (GUILayout.Button("Generate"))
            {
                EditorApplication.delayCall += () => GenerateNow(recipe);
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="recipe"/>'s sheet and prefabs, and logs what changed.
    /// </summary>
    private static void GenerateNow(PropRecipe recipe)
    {
        if (recipe == null)
        {
            return;
        }

        IReadOnlyList<string> written = PropWriter.Generate(recipe);
        if (written != null)
        {
            UnityEngine.Debug.Log(written.Count > 0 ? $"Generated {recipe.name}: {string.Join(", ", written)}." : $"Generated {recipe.name}: nothing changed.", recipe);
        }
    }

    /// <summary>
    /// Bakes the variants and the strips again when the recipe changed since the last bake.
    /// </summary>
    internal void RebuildIfChanged(PropRecipe recipe)
    {
        string key = EditorJsonUtility.ToJson(recipe);
        if (key == previewKey)
        {
            return;
        }

        previewKey = key;
        ReleaseStrips();
        variants = null;
        writeProblem = null;
        if (!recipe.CanGenerate(out previewProblem))
        {
            return;
        }

        Stopwatch watch = Stopwatch.StartNew();
        variants = PropBaker.BakeAll(recipe);
        bakeMilliseconds = watch.Elapsed.TotalMilliseconds;
        writeProblem = PropWriter.Refusal(recipe, variants, PropWriter.PrefabsFolder);
        TerrainMaterial[] terrains = recipe.PreviewTerrains;
        if (terrains == null || terrains.Length == 0)
        {
            strips.Add(Compose(recipe, null));
            return;
        }

        foreach (TerrainMaterial terrain in terrains)
        {
            strips.Add(Compose(recipe, terrain));
        }
    }

    /// <summary>
    /// Lays the variants and the rogue on a strip of <paramref name="terrain"/>'s albedo (bare ground without one).
    /// </summary>
    private Strip Compose(PropRecipe recipe, TerrainMaterial terrain)
    {
        Vector2Int frame = recipe.FrameSize;
        Vector2Int basePixel = recipe.BasePixel;
        int slot = Mathf.CeilToInt(frame.x / (float)Cell);
        List<Vector2Int> bases = new();
        Strip strip = new() { Label = terrain != null ? terrain.name : "No preview terrain" };
        int top = Cell * 3;
        for (int i = 0; i < variants.Count; i++)
        {
            PropVariant variant = variants[i];
            int firstColumn = 1 + i * slot;
            Vector2Int at;
            if (variant.HasFootprint)
            {
                Vector2Int cells = PropGridSnapping.CellsCovered(FootprintInCells(variant.Footprint, basePixel), Vector2.one);
                int left = firstColumn + (slot - cells.x) / 2;
                at = new Vector2Int(left * Cell + cells.x * Cell / 2, Cell + basePixel.y - variant.Footprint.y);
                strip.Cells.Add(new RectInt(left * Cell, Cell, cells.x * Cell, cells.y * Cell));
                strip.Footprints.Add(new RectInt(at.x - basePixel.x + variant.Footprint.x, at.y - basePixel.y + variant.Footprint.y, variant.Footprint.width, variant.Footprint.height));
            }
            else
            {
                at = new Vector2Int(firstColumn * Cell + slot * Cell / 2, Cell + Cell / 2);
            }

            bases.Add(at);
            top = Mathf.Max(top, at.y - basePixel.y + frame.y);
        }

        int width = (2 + variants.Count * slot + RogueCells) * Cell;
        int height = (top + Cell - 1) / Cell * Cell;
        Color32[] pixels = Ground(terrain, width, height);
        for (int i = 0; i < variants.Count; i++)
        {
            Blend(pixels, width, height, variants[i].Pixels, frame, bases[i] - basePixel);
        }

        DrawRogue(pixels, width, height, new Vector2Int((1 + variants.Count * slot) * Cell + RogueCells * Cell / 2, Cell + RogueFeet));
        strip.Texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        strip.Texture.SetPixels32(pixels);
        strip.Texture.Apply();
        return strip;
    }

    /// <summary>
    /// A strip of <paramref name="terrain"/>'s albedo tiled at one texel a pixel, at the worlds' light.
    /// </summary>
    private Color32[] Ground(TerrainMaterial terrain, int width, int height)
    {
        Color32[] pixels = new Color32[width * height];
        Picture albedo = terrain != null && terrain.Albedo != null ? PictureAt(AssetDatabase.GetAssetPath(terrain.Albedo)) : null;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = Lit(albedo != null ? albedo.Pixels[y % albedo.Height * albedo.Width + x % albedo.Width] : BareGround);
            }
        }

        return pixels;
    }

    /// <summary>
    /// Draws the rogue's idle frame facing south with its feet on <paramref name="feet"/>.
    /// </summary>
    private void DrawRogue(Color32[] pixels, int width, int height, Vector2Int feet)
    {
        if (!SpriteSheetFiles.SpritesByName(RogueSheet).TryGetValue(RogueSlice, out Sprite rogue))
        {
            return;
        }

        Picture sheet = PictureAt(RogueSheet);
        RectInt rect = new((int)rogue.rect.x, (int)rogue.rect.y, (int)rogue.rect.width, (int)rogue.rect.height);
        Color32[] frame = new Color32[rect.width * rect.height];
        for (int y = 0; y < rect.height; y++)
        {
            System.Array.Copy(sheet.Pixels, (rect.y + y) * sheet.Width + rect.x, frame, y * rect.width, rect.width);
        }

        Vector2Int pivot = new(Mathf.RoundToInt(rogue.pivot.x), Mathf.RoundToInt(rogue.pivot.y));
        Blend(pixels, width, height, frame, new Vector2Int(rect.width, rect.height), feet - pivot);
    }

    /// <summary>
    /// Blends <paramref name="source"/> of <paramref name="size"/> over the strip with its bottom left at
    /// <paramref name="corner"/>, its colours at the worlds' light.
    /// </summary>
    private static void Blend(Color32[] pixels, int width, int height, Color32[] source, Vector2Int size, Vector2Int corner)
    {
        for (int y = 0; y < size.y; y++)
        {
            int ty = corner.y + y;
            if (ty < 0 || ty >= height)
            {
                continue;
            }

            for (int x = 0; x < size.x; x++)
            {
                int tx = corner.x + x;
                Color32 over = source[y * size.x + x];
                if (tx < 0 || tx >= width || over.a == 0)
                {
                    continue;
                }

                Color32 colour = Lit(over);
                pixels[ty * width + tx] = Color32.Lerp(pixels[ty * width + tx], new Color32(colour.r, colour.g, colour.b, 255), over.a / 255f);
            }
        }
    }

    /// <summary>
    /// <paramref name="colour"/> under the worlds' light.
    /// </summary>
    private static Color32 Lit(Color32 colour)
    {
        return new Color32((byte)Mathf.RoundToInt(colour.r * WorldLight), (byte)Mathf.RoundToInt(colour.g * WorldLight), (byte)Mathf.RoundToInt(colour.b * WorldLight), 255);
    }

    /// <summary>
    /// <paramref name="footprint"/>, in frame pixels, in cells from the base.
    /// </summary>
    private static Rect FootprintInCells(RectInt footprint, Vector2Int basePixel)
    {
        return new Rect((footprint.x - basePixel.x) / (float)Cell, (footprint.y - basePixel.y) / (float)Cell, footprint.width / (float)Cell, footprint.height / (float)Cell);
    }

    /// <summary>
    /// Draws <paramref name="strip"/> at <paramref name="zoom"/>, shrunk to fit the inspector, with the cells, the
    /// blocked cells and the footprints over it when zoomed.
    /// </summary>
    private static void DrawStrip(Strip strip, int zoom)
    {
        Texture2D texture = strip.Texture;
        float scale = Mathf.Min(zoom, (EditorGUIUtility.currentViewWidth - 40f) / texture.width);
        Rect area = GUILayoutUtility.GetRect(texture.width * scale, texture.height * scale, GUILayout.Width(texture.width * scale), GUILayout.Height(texture.height * scale));
        GUI.DrawTexture(area, texture);
        if (zoom == 1)
        {
            return;
        }

        for (int x = Cell; x < texture.width; x += Cell)
        {
            EditorGUI.DrawRect(new Rect(area.x + x * scale, area.y, 1f, area.height), GridColour);
        }

        for (int y = Cell; y < texture.height; y += Cell)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.yMax - y * scale, area.width, 1f), GridColour);
        }

        foreach (RectInt cells in strip.Cells)
        {
            Outline(area, scale, cells, CellsColour);
        }

        foreach (RectInt footprint in strip.Footprints)
        {
            Outline(area, scale, footprint, FootprintColour);
        }
    }

    /// <summary>
    /// Draws the outline of <paramref name="rect"/>, in strip pixels from the bottom left, over <paramref name="area"/>.
    /// </summary>
    private static void Outline(Rect area, float scale, RectInt rect, Color colour)
    {
        float left = area.x + rect.x * scale;
        float right = area.x + rect.xMax * scale;
        float bottom = area.yMax - rect.y * scale;
        float top = area.yMax - rect.yMax * scale;
        EditorGUI.DrawRect(new Rect(left, top, right - left, 1f), colour);
        EditorGUI.DrawRect(new Rect(left, bottom - 1f, right - left, 1f), colour);
        EditorGUI.DrawRect(new Rect(left, top, 1f, bottom - top), colour);
        EditorGUI.DrawRect(new Rect(right - 1f, top, 1f, bottom - top), colour);
    }

    /// <summary>
    /// Draws a line per variant (its height from the base and its width, in cells, and its footprint and the cells it
    /// blocks), the bake time and a warning for each drawing that reaches its frame's edge.
    /// </summary>
    private void DrawSummary(PropRecipe recipe)
    {
        if (variants == null)
        {
            return;
        }

        for (int i = 0; i < variants.Count; i++)
        {
            PropVariant variant = variants[i];
            string size = $"{(variant.Drawn.yMax - recipe.BasePixel.y) / (float)Cell:0.00} cells tall, {variant.Drawn.width / (float)Cell:0.00} wide";
            string footprint = "no footprint";
            if (variant.HasFootprint)
            {
                Vector2Int cells = PropGridSnapping.CellsCovered(FootprintInCells(variant.Footprint, recipe.BasePixel), Vector2.one);
                footprint = $"footprint {variant.Footprint.width} x {variant.Footprint.height} px on {cells.x} x {cells.y} cells";
            }

            EditorGUILayout.LabelField($"{PropWriter.SliceName(recipe.PropName, i)}: {size}, {footprint}", EditorStyles.miniLabel);
            if (variant.AtEdge)
            {
                EditorGUILayout.HelpBox($"Variant {i}'s drawing reaches its frame's edge: make the frame larger or move the base pixel.", MessageType.Warning);
            }
        }

        EditorGUILayout.LabelField($"{variants.Count} variants of {recipe.FrameSize.x} x {recipe.FrameSize.y} px baked in {bakeMilliseconds:0} ms", EditorStyles.miniLabel);
    }

    /// <summary>
    /// The pixels of the PNG at <paramref name="path"/>, read from its file once and kept.
    /// </summary>
    private Picture PictureAt(string path)
    {
        if (pictures.TryGetValue(path, out Picture picture))
        {
            return picture;
        }

        Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false);
        decoded.LoadImage(File.ReadAllBytes(path));
        picture = new Picture(decoded.width, decoded.height, decoded.GetPixels32());
        DestroyImmediate(decoded);
        pictures[path] = picture;
        return picture;
    }

    /// <summary>
    /// Destroys the strips' textures and forgets them.
    /// </summary>
    private void ReleaseStrips()
    {
        foreach (Strip strip in strips)
        {
            if (strip.Texture != null)
            {
                DestroyImmediate(strip.Texture);
            }
        }

        strips.Clear();
    }

    /// <summary>
    /// A decoded image, rows from the bottom.
    /// </summary>
    private sealed class Picture
    {
        public Picture(int width, int height, Color32[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public int Width { get; }
        public int Height { get; }
        public Color32[] Pixels { get; }
    }

    /// <summary>
    /// One preview strip: its texture, its label, and the cells and footprints drawn over it when zoomed, in strip
    /// pixels from the bottom left.
    /// </summary>
    private sealed class Strip
    {
        public Texture2D Texture { get; set; }
        public string Label { get; set; }
        public List<RectInt> Cells { get; } = new();
        public List<RectInt> Footprints { get; } = new();
    }
}
