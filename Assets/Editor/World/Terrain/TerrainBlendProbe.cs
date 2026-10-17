using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor checks for <see cref="TerrainBlendRenderer"/>: renders a world rectangle through a temporary camera at a
/// whole number of screen pixels per art pixel and writes it as a PNG; checks the topmost visible layer for detached
/// blobs and holes; measures shimmer at sub-pixel camera offsets, the cost of a frame, of a rebuild and of switching
/// the look; builds the stress scene those measurements share. Any check can run in either look through
/// <see cref="InMode{T}"/>, which puts the renderer's mode back afterwards. Shaders compile synchronously during its
/// renders, since a render skips variants still compiling in the background. It hides renderers only through
/// <see cref="Renderer.forceRenderingOff"/>, which takes effect at once and is not saved, and puts back what a render
/// hid even when the render fails, clearing the dirty mark that setting it leaves: otherwise the next save would write
/// every prop's sorting order (set in edit mode by its <see cref="YPositionSorter"/>) into the scene as a prefab
/// override.
/// </summary>
public static class TerrainBlendProbe
{
    private const int ArtPixelsPerUnit = 32;
    private const string EntitiesSortingLayer = "Entities";
    private const string MenuRoot = "Tools/TopDownRPG/World/Terrain/";
    private const int DebugLayers = TerrainControlMap.MaxLayers;
    private const int UnknownClass = DebugLayers + 1;
    private const string StylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    private const int StressWidth = 72;
    private const int StressHeight = 48;
    private const int StressDiscs = 4;
    private const int StressMinRadius = 2;
    private const int StressMaxRadius = 5;
    private const float StressLightIntensity = 0.85f;

    private static readonly Vector3Int[] DebugColours =
    {
        new(2, 0, 0), new(0, 2, 0), new(2, 2, 0), new(0, 0, 2), new(2, 0, 2), new(0, 2, 2), new(2, 2, 2),
        new(2, 1, 0), new(2, 0, 1), new(1, 2, 0), new(0, 2, 1), new(1, 0, 2), new(0, 1, 2),
        new(2, 2, 1), new(2, 1, 2), new(1, 2, 2)
    };

    /// <summary>
    /// Logs the blob check of every terrain renderer in the open scenes, in the crisp look and in the soft one, leaving
    /// each renderer in the look it had.
    /// </summary>
    [MenuItem(MenuRoot + "Check Blobs In Open Areas")]
    private static void CheckBlobsInOpenAreas()
    {
        int found = 0;
        foreach (TerrainBlendRenderer renderer in Object.FindObjectsByType<TerrainBlendRenderer>())
        {
            found++;
            string crisp = InMode(renderer, TerrainBlendRenderer.BlendMode.Crisp, () => CheckBlobs(renderer, 200));
            string soft = InMode(renderer, TerrainBlendRenderer.BlendMode.Soft, () => CheckBlobs(renderer, 200));
            UnityEngine.Debug.Log($"Terrain blob check, {renderer.gameObject.scene.name}, crisp:\n{crisp}soft:\n{soft}", renderer);
        }

        if (found == 0)
        {
            UnityEngine.Debug.LogWarning("No TerrainBlendRenderer in the open scenes.");
        }
    }

    /// <summary>
    /// Runs <paramref name="check"/> with <paramref name="terrain"/> drawing in <paramref name="mode"/> and puts its
    /// mode back afterwards, even when the check throws. The mode is set through
    /// <see cref="TerrainBlendRenderer.Mode"/>, which marks nothing changed, so a check in the soft look leaves no
    /// scene diff.
    /// </summary>
    public static T InMode<T>(TerrainBlendRenderer terrain, TerrainBlendRenderer.BlendMode mode, System.Func<T> check)
    {
        TerrainBlendRenderer.BlendMode previous = terrain.Mode;
        try
        {
            terrain.Mode = mode;
            return check();
        }
        finally
        {
            terrain.Mode = previous;
        }
    }

    /// <summary>
    /// Times a full rebuild of <paramref name="terrain"/> (control map, arrays, lookup texture, material and chunks)
    /// and a switch of its look, each the best of <paramref name="repeats"/> runs, and leaves it in the look it had.
    /// Chunks rebuilt here are drawn from the next editor update.
    /// </summary>
    public static string MeasureRebuildAndModeSwitch(TerrainBlendRenderer terrain, int repeats)
    {
        double rebuild = double.MaxValue;
        double modeSwitch = double.MaxValue;
        TerrainBlendRenderer.BlendMode previous = terrain.Mode;
        TerrainBlendRenderer.BlendMode other = previous == TerrainBlendRenderer.BlendMode.Crisp ? TerrainBlendRenderer.BlendMode.Soft : TerrainBlendRenderer.BlendMode.Crisp;
        for (int i = 0; i < repeats; i++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            terrain.Rebuild();
            watch.Stop();
            rebuild = System.Math.Min(rebuild, watch.Elapsed.TotalMilliseconds);
            watch.Restart();
            terrain.Mode = other;
            watch.Stop();
            modeSwitch = System.Math.Min(modeSwitch, watch.Elapsed.TotalMilliseconds);
            terrain.Mode = previous;
        }

        return $"rebuild: {rebuild:F3} ms; switching the look: {modeSwitch * 1000.0:F1} us (best of {repeats})";
    }

    /// <summary>
    /// Builds the scene the terrain's cost is measured on into the active scene and returns its renderer, so every
    /// session measures the same ground (TerrainLayers32.md's Decisions): a white Global Light 2D at 0.85, as in the
    /// areas, and a grid at the origin whose renderer draws <paramref name="layers"/> layers of the project's style in
    /// the crisp look. Layer 0 is the style's first material (grass) over 72 x 48 cells; layer i above it draws the
    /// materials after the first in the style's order in turn, starting over after the last, painted as four discs of
    /// radius 2 to 5 cells inside the grass at spots drawn from a <see cref="System.Random"/> seeded with i, so a
    /// scene of more layers is one of fewer with more layers on top. The caller opens an empty scene first and saves
    /// nothing. Chunks built here are drawn from the next editor update.
    /// </summary>
    public static TerrainBlendRenderer BuildStressScene(int layers)
    {
        if (layers < 1)
        {
            throw new System.ArgumentOutOfRangeException(nameof(layers), layers, "A stress scene has at least one layer.");
        }

        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        if (style == null || style.LayerOrder.Count < 2)
        {
            throw new System.InvalidOperationException($"The stress scene needs the terrain style at {StylePath}, with at least two materials in its layer order.");
        }

        GameObject lightObject = new("Global Light 2D");
        Light2D light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.color = Color.white;
        light.intensity = StressLightIntensity;

        GameObject gridObject = new("StressGrid", typeof(Grid));
        gridObject.SetActive(false);
        TerrainBlendRenderer.Layer[] drawn = new TerrainBlendRenderer.Layer[layers];
        int later = style.LayerOrder.Count - 1;
        for (int i = 0; i < layers; i++)
        {
            TerrainMaterial material = style.LayerOrder[i == 0 ? 0 : 1 + (i - 1) % later];
            TileBase tile = AssetDatabase.LoadAssetAtPath<TileBase>(TerrainTileWriter.TilePath(material));
            if (tile == null)
            {
                throw new System.InvalidOperationException($"The stress scene needs {material.name}'s tile at {TerrainTileWriter.TilePath(material)}.");
            }

            GameObject layerObject = new($"{i:00} {material.name}", typeof(Tilemap));
            layerObject.transform.SetParent(gridObject.transform, false);
            Tilemap tilemap = layerObject.GetComponent<Tilemap>();
            Vector3Int[] cells = StressCells(i).ToArray();
            TileBase[] tiles = new TileBase[cells.Length];
            System.Array.Fill(tiles, tile);
            tilemap.SetTiles(cells, tiles);
            drawn[i] = new TerrainBlendRenderer.Layer(tilemap, material);
        }

        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, drawn);
        gridObject.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// Renders a world rectangle at <paramref name="scale"/> screen pixels per art pixel and returns its pixels; the
    /// caller destroys the texture. With <paramref name="hideProps"/>, sprites on the Entities sorting layer are left
    /// out; with <paramref name="onlyTerrain"/>, every renderer in the open scenes but <paramref name="terrain"/>'s
    /// chunks is, other areas' chunks included (found through <see cref="Resources.FindObjectsOfTypeAll"/>, since
    /// <see cref="Object.FindObjectsByType"/> skips objects that are never saved).
    /// <paramref name="linear"/> reads the target without sRGB conversion, for the debug view.
    /// </summary>
    public static Texture2D Render(TerrainBlendRenderer terrain, Rect world, int scale, bool linear, bool hideProps, bool onlyTerrain = false)
    {
        List<Renderer> hidden = new();
        HashSet<Renderer> clean = new();
        GameObject cameraObject = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        bool finished = false;
        bool allowAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            HashSet<Renderer> chunks = new(terrain != null ? terrain.Chunks : new MeshRenderer[0]);
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (EditorUtility.IsPersistent(renderer) || !renderer.gameObject.scene.isLoaded)
                {
                    continue;
                }

                bool hide = onlyTerrain ? !chunks.Contains(renderer) : hideProps && renderer is SpriteRenderer && renderer.sortingLayerName == EntitiesSortingLayer;
                if (hide && !renderer.forceRenderingOff)
                {
                    if (!EditorUtility.IsDirty(renderer))
                    {
                        clean.Add(renderer);
                    }

                    renderer.forceRenderingOff = true;
                    hidden.Add(renderer);
                }
            }

            if (terrain != null && terrain.RuntimeMaterial != null)
            {
                CompileNow(terrain.RuntimeMaterial);
                terrain.FlushPendingChanges();
            }

            int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * scale);
            int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * scale);
            cameraObject = new GameObject("TerrainBlendProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
            Camera probe = cameraObject.AddComponent<Camera>();
            probe.enabled = false;
            probe.orthographic = true;
            probe.clearFlags = CameraClearFlags.SolidColor;
            probe.backgroundColor = Color.black;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            target.Create();
            probe.targetTexture = target;
            probe.orthographicSize = world.height * 0.5f;
            probe.transform.position = new Vector3(world.center.x, world.center.y, -10f);
            RenderPipeline.SubmitRenderRequest(probe, new RenderPipeline.StandardRequest { destination = target });
            probe.targetTexture = null;

            pixels = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            RenderTexture.active = previous;
            finished = true;
            return pixels;
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = allowAsync;
            if (!finished && pixels != null)
            {
                Object.DestroyImmediate(pixels);
            }

            if (target != null)
            {
                Object.DestroyImmediate(target);
            }

            if (cameraObject != null)
            {
                Object.DestroyImmediate(cameraObject);
            }

            foreach (Renderer renderer in hidden)
            {
                renderer.forceRenderingOff = false;
                if (clean.Contains(renderer))
                {
                    EditorUtility.ClearDirty(renderer);
                }
            }
        }
    }

    /// <summary>
    /// Renders a world rectangle without props and writes it as a PNG at <paramref name="path"/>.
    /// </summary>
    public static void Capture(TerrainBlendRenderer terrain, Rect world, int scale, string path)
    {
        Texture2D pixels = Render(terrain, world, scale, false, true);
        System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels);
    }

    /// <summary>
    /// Renders the debug view over the control map's bounds at one screen pixel per art pixel, with every other
    /// renderer left out, and reports per layer the connected regions of pixels where that layer is the topmost one
    /// drawn, listing those under <paramref name="minPixels"/> (detached blobs), and the regions with no ground that
    /// do not reach the edge of the bounds (holes). Layers are told apart by their pure debug colours and named after
    /// their materials. Says so instead when the renderer has nothing built, or when nothing was drawn, as happens right
    /// after the chunks were built. Animated materials are drawn at a fixed time, though they never change which layer
    /// is on top.
    /// </summary>
    public static string CheckBlobs(TerrainBlendRenderer terrain, int minPixels)
    {
        if (terrain.ControlMap == null)
        {
            return "  nothing is built: the renderer is disabled or could not build (see its errors)\n";
        }

        terrain.FlushPendingChanges();
        BoundsInt bounds = terrain.ControlMap.Bounds;
        Vector3 origin = terrain.GetComponent<Grid>().CellToWorld(bounds.min);
        Rect world = new(origin.x, origin.y, bounds.size.x, bounds.size.y);
        try
        {
            terrain.SetDebugView(true);
            terrain.SetFixedTime(0f);
            Texture2D pixels = Render(terrain, world, 1, true, true, true);
            string report = Analyse(pixels, world, minPixels, LayerNames(terrain));
            Object.DestroyImmediate(pixels);
            return report;
        }
        finally
        {
            terrain.SetDebugView(false);
            terrain.SetFixedTime(null);
        }
    }

    /// <summary>
    /// Times <paramref name="frames"/> renders of a camera of the given size centred on <paramref name="centre"/>,
    /// with the chunks shown and hidden, waiting for the GPU after each batch. Returns milliseconds per frame.
    /// </summary>
    public static string MeasureCost(TerrainBlendRenderer terrain, Vector2 centre, int width, int height, int zoom, int frames)
    {
        StringBuilder report = new();
        GameObject cameraObject = new("TerrainBlendProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera probe = cameraObject.AddComponent<Camera>();
        probe.enabled = false;
        probe.orthographic = true;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        probe.targetTexture = target;
        probe.orthographicSize = height / (2f * ArtPixelsPerUnit * zoom);
        probe.transform.position = new Vector3(centre.x, centre.y, -10f);
        Texture2D readback = new(1, 1, TextureFormat.RGBA32, false);
        bool allowAsync = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            CompileNow(terrain.RuntimeMaterial);
            foreach (bool shown in new[] { false, true, false, true })
            {
                SetChunksHidden(terrain, !shown);
                RenderFrames(probe, target, readback, 3);
                Stopwatch watch = Stopwatch.StartNew();
                RenderFrames(probe, target, readback, frames);
                watch.Stop();
                report.AppendLine($"chunks {(shown ? "on " : "off")}: {watch.Elapsed.TotalMilliseconds / frames:F3} ms per frame ({width}x{height}, zoom {zoom})");
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = allowAsync;
            SetChunksHidden(terrain, false);
            probe.targetTexture = null;
            Object.DestroyImmediate(readback);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
        }

        return report.ToString();
    }

    /// <summary>
    /// Renders a camera of the given size at <paramref name="zoom"/> screen pixels per art pixel, moved by each of
    /// <paramref name="offsets"/> (in screen pixels, along x and y), and compares each frame with the unmoved frame
    /// shifted by the best whole number of screen pixels. Point-sampled art that only jumps by whole pixels matches
    /// exactly; any other difference is shimmer. Measures whatever is drawn but props.
    /// </summary>
    public static string MeasureShimmer(TerrainBlendRenderer terrain, Vector2 centre, int width, int height, int zoom, float[] offsets)
    {
        StringBuilder report = new();
        Color32[] reference = RenderAt(terrain, centre, width, height, zoom);
        foreach (float offset in offsets)
        {
            float unit = offset / (ArtPixelsPerUnit * zoom);
            Color32[] moved = RenderAt(terrain, centre + new Vector2(unit, unit), width, height, zoom);
            int best = int.MaxValue;
            int bestShift = 0;
            for (int shift = -2; shift <= 2; shift++)
            {
                int mismatches = CountMismatches(reference, moved, width, height, shift);
                if (mismatches < best)
                {
                    best = mismatches;
                    bestShift = shift;
                }
            }

            report.Append($" {offset:0.###}px: {best} off (shift {bestShift});");
        }

        return report.ToString();
    }

    /// <summary>
    /// The cells layer <paramref name="layer"/> of the stress scene fills: for layer 0 every cell of the 72 x 48
    /// ground, for the others four discs, each of a radius and a centre drawn in turn from a
    /// <see cref="System.Random"/> seeded with the layer, the centre at least the radius inside the ground, so a disc
    /// never leaves it. A cell two discs cover is listed twice.
    /// </summary>
    private static List<Vector3Int> StressCells(int layer)
    {
        List<Vector3Int> cells = new();
        if (layer == 0)
        {
            for (int y = 0; y < StressHeight; y++)
            {
                for (int x = 0; x < StressWidth; x++)
                {
                    cells.Add(new Vector3Int(x, y, 0));
                }
            }

            return cells;
        }

        System.Random random = new(layer);
        for (int disc = 0; disc < StressDiscs; disc++)
        {
            int radius = random.Next(StressMinRadius, StressMaxRadius + 1);
            int cx = random.Next(radius, StressWidth - radius);
            int cy = random.Next(radius, StressHeight - radius);
            for (int y = cy - radius; y <= cy + radius; y++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius)
                    {
                        cells.Add(new Vector3Int(x, y, 0));
                    }
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// Switches the chunks off or on for the next renders through <see cref="Renderer.forceRenderingOff"/>, which
    /// takes effect at once and is not saved.
    /// </summary>
    private static void SetChunksHidden(TerrainBlendRenderer terrain, bool hidden)
    {
        foreach (MeshRenderer chunk in terrain.Chunks)
        {
            if (chunk != null)
            {
                chunk.forceRenderingOff = hidden;
            }
        }
    }

    /// <summary>
    /// One frame of a camera centred on a world position, without props, as pixels.
    /// </summary>
    private static Color32[] RenderAt(TerrainBlendRenderer terrain, Vector2 centre, int width, int height, int zoom)
    {
        float worldWidth = width / (float)(ArtPixelsPerUnit * zoom);
        float worldHeight = height / (float)(ArtPixelsPerUnit * zoom);
        Rect world = new(centre.x - worldWidth * 0.5f, centre.y - worldHeight * 0.5f, worldWidth, worldHeight);
        Texture2D pixels = Render(terrain, world, zoom, false, true);
        Color32[] colours = pixels.GetPixels32();
        Object.DestroyImmediate(pixels);
        return colours;
    }

    /// <summary>
    /// Pixels that differ between a frame and the reference moved by <paramref name="shift"/> screen pixels along
    /// both axes (the camera moved up and right, so the picture moved down and left), ignoring a border.
    /// </summary>
    private static int CountMismatches(Color32[] reference, Color32[] moved, int width, int height, int shift)
    {
        const int border = 4;
        int count = 0;
        for (int y = border; y < height - border; y++)
        {
            for (int x = border; x < width - border; x++)
            {
                Color32 a = moved[y * width + x];
                Color32 b = reference[(y + shift) * width + x + shift];
                if (a.r != b.r || a.g != b.g || a.b != b.b)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Compiles the material's current variant of every pass before rendering, since the editor skips objects whose
    /// shader is still compiling in the background.
    /// </summary>
    private static void CompileNow(Material material)
    {
        for (int pass = 0; pass < material.passCount; pass++)
        {
            if (!ShaderUtil.IsPassCompiled(material, pass))
            {
                ShaderUtil.CompilePass(material, pass, true);
            }
        }
    }

    /// <summary>
    /// Renders several frames and reads one pixel back so the GPU has finished them.
    /// </summary>
    private static void RenderFrames(Camera probe, RenderTexture target, Texture2D readback, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            RenderPipeline.SubmitRenderRequest(probe, new RenderPipeline.StandardRequest { destination = target });
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        readback.Apply();
        RenderTexture.active = previous;
    }

    /// <summary>
    /// The name of each class a debug colour can show but nothing, for the report: the renderer's layer's material name,
    /// else its index, and last the colours that are no layer's.
    /// </summary>
    private static string[] LayerNames(TerrainBlendRenderer terrain)
    {
        string[] names = new string[UnknownClass];
        for (int i = 0; i < DebugLayers; i++)
        {
            TerrainMaterial material = i < terrain.Layers.Count ? terrain.Layers[i]?.Material : null;
            names[i] = material != null ? material.name.ToLowerInvariant() : $"layer {i}";
        }

        names[UnknownClass - 1] = "unknown colour";
        return names;
    }

    /// <summary>
    /// The layer a debug view colour shows, plus one: 0 for nothing, <see cref="UnknownClass"/> for a colour that is no
    /// layer's. Each channel reads as off, at half or full against the brightest one, and the three levels are looked
    /// up in <see cref="DebugColours"/> (TB_DEBUG_COLOURS in the terrain shader), so the lights' intensity does not
    /// matter.
    /// </summary>
    internal static byte DebugClass(Color32 c)
    {
        int brightest = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (brightest < 4)
        {
            return 0;
        }

        Vector3Int levels = new(Level(c.r, brightest), Level(c.g, brightest), Level(c.b, brightest));
        for (int i = 0; i < DebugColours.Length; i++)
        {
            if (DebugColours[i] == levels)
            {
                return (byte)(i + 1);
            }
        }

        return UnknownClass;

        static int Level(int channel, int max) => channel * 4 >= max * 3 ? 2 : channel * 4 >= max ? 1 : 0;
    }

    /// <summary>
    /// Labels the debug view's colours into classes by layer (<see cref="DebugClass"/>), groups them into 4-connected
    /// regions and describes the small ones and the holes at their world position in <paramref name="world"/>, naming
    /// the layers by <paramref name="names"/>.
    /// </summary>
    private static string Analyse(Texture2D pixels, Rect world, int minPixels, string[] names)
    {
        int width = pixels.width;
        int height = pixels.height;
        Color32[] colours = pixels.GetPixels32();
        byte[] classes = new byte[colours.Length];
        for (int i = 0; i < colours.Length; i++)
        {
            classes[i] = DebugClass(colours[i]);
        }

        bool[] seen = new bool[classes.Length];
        int[] regions = new int[UnknownClass + 1];
        StringBuilder small = new();
        StringBuilder holes = new();
        int smallCount = 0;
        int holeCount = 0;
        Stack<int> stack = new();
        for (int start = 0; start < classes.Length; start++)
        {
            if (seen[start])
            {
                continue;
            }

            byte cls = classes[start];
            int size = 0;
            long sumX = 0;
            long sumY = 0;
            bool touchesEdge = false;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int x = index % width;
                int y = index / width;
                size++;
                sumX += x;
                sumY += y;
                touchesEdge |= x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (x > 0)
                {
                    Visit(index - 1);
                }

                if (x < width - 1)
                {
                    Visit(index + 1);
                }

                if (y > 0)
                {
                    Visit(index - width);
                }

                if (y < height - 1)
                {
                    Visit(index + width);
                }
            }

            regions[cls]++;
            float cx = world.xMin + (sumX / (float)size + 0.5f) / ArtPixelsPerUnit;
            float cy = world.yMin + (sumY / (float)size + 0.5f) / ArtPixelsPerUnit;
            if (cls == 0 && !touchesEdge && holeCount++ < 20)
            {
                holes.AppendLine($"  hole: {size} px around ({cx:F2}, {cy:F2})");
            }
            else if (cls != 0 && size < minPixels && smallCount++ < 20)
            {
                small.AppendLine($"  {names[cls - 1]} blob: {size} px around ({cx:F2}, {cy:F2})");
            }

            void Visit(int next)
            {
                if (!seen[next] && classes[next] == cls)
                {
                    seen[next] = true;
                    stack.Push(next);
                }
            }
        }

        StringBuilder counts = new($"none {regions[0]}");
        int drawn = 0;
        for (int cls = 1; cls < regions.Length; cls++)
        {
            drawn += regions[cls];
            if (regions[cls] > 0)
            {
                counts.Append($", {names[cls - 1]} {regions[cls]}");
            }
        }

        if (drawn == 0)
        {
            return "  nothing was drawn: chunks built or rebuilt in this editor update are drawn from the next one, so check again\n";
        }

        return $"  regions: {counts}; blobs under {minPixels} px: {smallCount}; holes: {holeCount}\n{small}{holes}";
    }
}
