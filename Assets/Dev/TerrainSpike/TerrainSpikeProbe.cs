#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-only measurements for the terrain blending spike: renders the spike through a temporary camera at a whole
/// number of screen pixels per art pixel, checks the topmost visible layer for detached blobs and holes, and times
/// renders with and without the spike.
/// </summary>
public static class TerrainSpikeProbe
{
    private const int ArtPixelsPerUnit = 32;
    private const string TopSortingLayer = "Entities";
    private const int TopSortingOrder = 32767;

    /// <summary>
    /// Renders a world rectangle at <paramref name="scale"/> screen pixels per art pixel and returns the pixels (the
    /// caller destroys the texture). With <paramref name="onTop"/>, the spike's chunks sort above every prop for the
    /// render, on a sorting layer the area's light still reaches. With <paramref name="hideEntities"/>, sprites on
    /// the Entities sorting layer are left out through <see cref="Renderer.forceRenderingOff"/>, which is not saved.
    /// </summary>
    public static Texture2D Render(TerrainSpikeRenderer spike, Rect world, int scale, bool onTop, bool linear, bool hideEntities = false)
    {
        var hidden = new List<SpriteRenderer>();
        if (hideEntities)
        {
            foreach (SpriteRenderer sprite in Object.FindObjectsByType<SpriteRenderer>())
            {
                if (sprite.sortingLayerName == TopSortingLayer && !sprite.forceRenderingOff)
                {
                    sprite.forceRenderingOff = true;
                    hidden.Add(sprite);
                }
            }
        }
        int width = Mathf.RoundToInt(world.width * ArtPixelsPerUnit * scale);
        int height = Mathf.RoundToInt(world.height * ArtPixelsPerUnit * scale);
        if (spike != null && spike.isActiveAndEnabled && spike.RuntimeMaterial != null)
        {
            CompileNow(spike.RuntimeMaterial);
        }
        var restore = new List<(MeshRenderer renderer, int layer, int order)>();
        if (onTop && spike != null)
        {
            foreach (GameObject chunk in spike.Chunks)
            {
                var meshRenderer = chunk.GetComponent<MeshRenderer>();
                restore.Add((meshRenderer, meshRenderer.sortingLayerID, meshRenderer.sortingOrder));
                meshRenderer.sortingLayerName = TopSortingLayer;
                meshRenderer.sortingOrder = TopSortingOrder;
            }
        }

        var cameraObject = new GameObject("TerrainSpikeProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var probe = cameraObject.AddComponent<Camera>();
        probe.enabled = false;
        probe.orthographic = true;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
        target.Create();
        probe.targetTexture = target;
        probe.orthographicSize = world.height * 0.5f;
        probe.transform.position = new Vector3(world.center.x, world.center.y, -10f);
        RenderPipeline.SubmitRenderRequest(probe, new RenderPipeline.StandardRequest { destination = target });

        var pixels = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        pixels.Apply();
        RenderTexture.active = previous;

        probe.targetTexture = null;
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
        foreach (var (meshRenderer, layer, order) in restore)
        {
            meshRenderer.sortingLayerID = layer;
            meshRenderer.sortingOrder = order;
        }
        foreach (SpriteRenderer sprite in hidden)
        {
            sprite.forceRenderingOff = false;
        }
        return pixels;
    }

    /// <summary>
    /// Renders a world rectangle without props and writes it as a PNG.
    /// </summary>
    public static void Capture(TerrainSpikeRenderer spike, Rect world, int scale, string path)
    {
        Texture2D pixels = Render(spike, world, scale, false, false, true);
        System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
        Object.DestroyImmediate(pixels);
    }

    /// <summary>
    /// Writes three captures of a world rectangle without props: today's render tilemaps (the spike's chunks switched
    /// off for the render; renderers created or reactivated in the same editor update are not drawn yet), then the
    /// spike in crisp and in soft, as <paramref name="pathPrefix"/>_old.png, _crisp.png and _soft.png.
    /// </summary>
    public static void CaptureComparison(TerrainSpikeRenderer spike, Rect world, int scale, string pathPrefix)
    {
        TerrainSpikeRenderer.Look look = spike.CurrentLook;
        foreach (GameObject chunk in spike.Chunks)
        {
            chunk.GetComponent<MeshRenderer>().forceRenderingOff = true;
        }
        Capture(null, world, scale, pathPrefix + "_old.png");
        foreach (GameObject chunk in spike.Chunks)
        {
            chunk.GetComponent<MeshRenderer>().forceRenderingOff = false;
        }
        spike.SetLook(TerrainSpikeRenderer.Look.Crisp);
        Capture(spike, world, scale, pathPrefix + "_crisp.png");
        spike.SetLook(TerrainSpikeRenderer.Look.Soft);
        Capture(spike, world, scale, pathPrefix + "_soft.png");
        spike.SetLook(look);
    }

    /// <summary>
    /// Renders the spike's debug view over its whole bounds at one screen pixel per art pixel, in both looks, and
    /// reports per layer the connected regions of pixels where that layer is the topmost one drawn, listing those
    /// under <paramref name="minPixels"/> (detached blobs), and the regions with no ground that do not reach the edge
    /// of the bounds (holes).
    /// </summary>
    public static string CheckBlobs(TerrainSpikeRenderer spike, int minPixels)
    {
        var report = new StringBuilder();
        TerrainSpikeRenderer.Look look = spike.CurrentLook;
        BoundsInt bounds = spike.Bounds;
        var world = new Rect(bounds.xMin, bounds.yMin, bounds.size.x, bounds.size.y);
        foreach (TerrainSpikeRenderer.Look each in new[] { TerrainSpikeRenderer.Look.Crisp, TerrainSpikeRenderer.Look.Soft })
        {
            spike.SetLook(each);
            spike.SetDebugLayers(true);
            Texture2D pixels = Render(spike, world, 1, true, true);
            spike.SetDebugLayers(false);
            report.AppendLine(each + ":");
            report.Append(Analyse(pixels, bounds, minPixels));
            Object.DestroyImmediate(pixels);
        }
        spike.SetLook(look);
        return report.ToString();
    }

    /// <summary>
    /// Times <paramref name="frames"/> renders of a camera of the given size centred on <paramref name="centre"/>,
    /// with the spike shown and hidden, waiting for the GPU after each batch. Returns milliseconds per frame.
    /// </summary>
    public static string MeasureCost(TerrainSpikeRenderer spike, Vector2 centre, int width, int height, int zoom, int frames)
    {
        var report = new StringBuilder();
        var cameraObject = new GameObject("TerrainSpikeProbeCamera") { hideFlags = HideFlags.HideAndDontSave };
        var probe = cameraObject.AddComponent<Camera>();
        probe.enabled = false;
        probe.orthographic = true;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = Color.black;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        target.Create();
        probe.targetTexture = target;
        probe.orthographicSize = height / (2f * ArtPixelsPerUnit * zoom);
        probe.transform.position = new Vector3(centre.x, centre.y, -10f);
        var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        foreach (TerrainSpikeRenderer.Look look in new[] { TerrainSpikeRenderer.Look.Crisp, TerrainSpikeRenderer.Look.Soft })
        {
            spike.SetLook(look);
            foreach (bool shown in new[] { false, true, false, true })
            {
                foreach (GameObject chunk in spike.Chunks)
                {
                    chunk.GetComponent<MeshRenderer>().forceRenderingOff = !shown;
                }
                CompileNow(spike.RuntimeMaterial);
                RenderFrames(probe, target, readback, 3);
                var watch = Stopwatch.StartNew();
                RenderFrames(probe, target, readback, frames);
                watch.Stop();
                report.AppendLine($"{look} spike {(shown ? "on " : "off")}: {watch.Elapsed.TotalMilliseconds / frames:F3} ms per frame ({width}x{height}, zoom {zoom})");
            }
        }
        foreach (GameObject chunk in spike.Chunks)
        {
            chunk.GetComponent<MeshRenderer>().forceRenderingOff = false;
        }
        spike.SetLook(TerrainSpikeRenderer.Look.Crisp);
        probe.targetTexture = null;
        Object.DestroyImmediate(readback);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(cameraObject);
        return report.ToString();
    }

    /// <summary>
    /// Renders a camera of the given size at <paramref name="zoom"/> screen pixels per art pixel, moved by each of
    /// <paramref name="offsets"/> (in screen pixels, along x and y), and compares each frame with the unmoved frame
    /// shifted by the best whole number of screen pixels. Point-sampled art that only jumps by whole pixels matches
    /// exactly; any other difference is shimmer. Run with the spike shown (it covers the render tilemaps) and hidden.
    /// </summary>
    public static string MeasureShimmer(TerrainSpikeRenderer spike, Vector2 centre, int width, int height, int zoom, float[] offsets)
    {
        var report = new StringBuilder();
        foreach (bool shown in new[] { false, true })
        {
            foreach (GameObject chunk in spike.Chunks)
            {
                chunk.GetComponent<MeshRenderer>().forceRenderingOff = !shown;
            }
            Color32[] reference = RenderAt(spike, centre, width, height, zoom);
            report.Append(shown ? $"spike {spike.CurrentLook}:" : "render tilemaps:");
            foreach (float offset in offsets)
            {
                float unit = offset / (ArtPixelsPerUnit * zoom);
                Color32[] moved = RenderAt(spike, centre + new Vector2(unit, unit), width, height, zoom);
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
            report.AppendLine();
        }
        foreach (GameObject chunk in spike.Chunks)
        {
            chunk.GetComponent<MeshRenderer>().forceRenderingOff = false;
        }
        return report.ToString();
    }

    /// <summary>
    /// One frame of a camera centred on a world position, without props, as pixels.
    /// </summary>
    private static Color32[] RenderAt(TerrainSpikeRenderer spike, Vector2 centre, int width, int height, int zoom)
    {
        float worldWidth = width / (float)(ArtPixelsPerUnit * zoom);
        float worldHeight = height / (float)(ArtPixelsPerUnit * zoom);
        var world = new Rect(centre.x - worldWidth * 0.5f, centre.y - worldHeight * 0.5f, worldWidth, worldHeight);
        Texture2D pixels = Render(spike, world, zoom, false, false, true);
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
    /// Labels the debug view's classes (0 nothing, 1 grass, 2 gravel, 3 stone) into 4-connected regions and describes
    /// the small ones and the holes.
    /// </summary>
    private static string Analyse(Texture2D pixels, BoundsInt bounds, int minPixels)
    {
        int width = pixels.width;
        int height = pixels.height;
        Color32[] colours = pixels.GetPixels32();
        var classes = new byte[colours.Length];
        for (int i = 0; i < colours.Length; i++)
        {
            int r = colours[i].r;
            classes[i] = (byte)(r < 27 ? 0 : r < 81 ? 1 : r < 135 ? 2 : 3);
        }
        var seen = new bool[classes.Length];
        var regions = new int[4];
        var small = new StringBuilder();
        var holes = new StringBuilder();
        int smallCount = 0;
        int holeCount = 0;
        var stack = new Stack<int>();
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
                if (x > 0) Visit(index - 1);
                if (x < width - 1) Visit(index + 1);
                if (y > 0) Visit(index - width);
                if (y < height - 1) Visit(index + width);
            }
            regions[cls]++;
            float cx = bounds.xMin + (sumX / (float)size + 0.5f) / ArtPixelsPerUnit;
            float cy = bounds.yMin + (sumY / (float)size + 0.5f) / ArtPixelsPerUnit;
            if (cls == 0 && !touchesEdge && holeCount++ < 20)
            {
                holes.AppendLine($"  hole: {size} px around ({cx:F2}, {cy:F2})");
            }
            else if (cls != 0 && size < minPixels && smallCount++ < 20)
            {
                small.AppendLine($"  {Name(cls)} blob: {size} px around ({cx:F2}, {cy:F2})");
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
        return $"  regions: none {regions[0]}, grass {regions[1]}, gravel {regions[2]}, stone {regions[3]}; blobs under {minPixels} px: {smallCount}; holes: {holeCount}\n{small}{holes}";
    }

    /// <summary>
    /// The layer name for a debug class.
    /// </summary>
    private static string Name(byte cls)
    {
        return cls == 1 ? "grass" : cls == 2 ? "gravel" : "stone";
    }
}
#endif
