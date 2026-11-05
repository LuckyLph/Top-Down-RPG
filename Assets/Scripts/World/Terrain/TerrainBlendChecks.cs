using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// The checks a <see cref="TerrainBlendRenderer"/> runs before it builds: whether its style, layers and materials can be
/// drawn, with an error logged against the renderer (or the material or texture at fault) for each problem, and the
/// warnings about what it draws anyway.
/// </summary>
internal static class TerrainBlendChecks
{
    /// <summary>
    /// Returns false without a word for a renderer not configured at all (no style, no layers) outside play mode, as
    /// right after it is added. Otherwise logs an error for each thing that stops the renderer from drawing (no style
    /// or shader, no layers or more than <see cref="TerrainControlMap.MaxLayers"/>, a graphics device that cannot sample
    /// the format of the control map it builds, narrow or <paramref name="wide"/>, a wide one for a shader without
    /// the wide keyword, a layer without a tilemap, a tilemap listed twice, a layer without a material, a
    /// material's textures missing or of the wrong size or format, an unknown sorting layer) and returns whether there
    /// was none; a material several layers draw is checked once.
    /// Also logs an error for each layer whose navigation source's priority is not its index, which does not stop the
    /// drawing (the drawing never reads the priority), warns about each layer whose tilemap has an enabled
    /// <see cref="TilemapRenderer"/>, which would draw its tiles' sprites as well, and warns when
    /// <paramref name="grid"/> is not made of unrotated one-unit cells, which the shader assumes, and when the layers leave
    /// the style's layer order (<see cref="WarnOffLayerOrder"/>).
    /// </summary>
    public static bool IsUsable(TerrainBlendRenderer renderer, Grid grid, string sortingLayerName, bool wide)
    {
        string name = renderer.name;
        TerrainStyle style = renderer.Style;
        IReadOnlyList<TerrainBlendRenderer.Layer> layers = renderer.Layers;
        if (style == null && (layers == null || layers.Count == 0) && !Application.isPlaying)
        {
            return false;
        }

        if (style == null || style.Shader == null)
        {
            Debug.LogError($"{name}: {nameof(TerrainBlendRenderer)} needs a terrain style with a shader.", renderer);
            return false;
        }

        if (layers == null || layers.Count == 0 || layers.Count > TerrainControlMap.MaxLayers)
        {
            Debug.LogError($"{name}: {nameof(TerrainBlendRenderer)} needs 1 to {TerrainControlMap.MaxLayers} ground layers.", renderer);
            return false;
        }

        if (!TerrainControlMap.IsFormatSupported(wide))
        {
            Debug.LogError($"{name}: this graphics device ({SystemInfo.graphicsDeviceType}) cannot sample the {TerrainControlMap.FormatOf(wide)} control map {nameof(TerrainBlendRenderer)} draws from, so the ground is not drawn.", renderer);
            return false;
        }

        if (wide && !new LocalKeyword(style.Shader, TerrainBlendRenderer.WideKeywordName).isValid)
        {
            Debug.LogError($"{name}: the terrain shader {style.Shader.name} has no {TerrainBlendRenderer.WideKeywordName} keyword to read the wide control map {nameof(TerrainBlendRenderer)} builds for more than {TerrainControlMap.NarrowLayers} layers, so the ground is not drawn.", renderer);
            return false;
        }

        bool usable = true;
        List<TerrainMaterial> checkedMaterials = new();
        for (int i = 0; i < layers.Count; i++)
        {
            TerrainBlendRenderer.Layer layer = layers[i];
            int first = renderer.LayerIndexOf(layer?.Tilemap);
            int earlier = first >= 0 && first < i ? first : -1;
            if (layer == null || layer.Tilemap == null)
            {
                Debug.LogError($"{name}: ground layer {i} has no tilemap.", renderer);
                usable = false;
            }
            else if (earlier >= 0)
            {
                Debug.LogError($"{name}: ground layers {earlier} and {i} both list {layer.Tilemap.name}; each tilemap is one layer.", renderer);
                usable = false;
            }
            else if (layer.Material == null)
            {
                Debug.LogError($"{name}: ground layer {i} ({layer.Tilemap.name}) has no material.", renderer);
                usable = false;
            }
            else if (!checkedMaterials.Contains(layer.Material))
            {
                checkedMaterials.Add(layer.Material);
                usable &= HasUsableTextures(layer.Material);
            }

            NavigationTerrainSource2D source = renderer.NavigationSourceOf(i);
            if (source != null && source.Priority != TerrainBlendRenderer.NavigationPriorityOf(i))
            {
                Debug.LogError($"{name}: ground layer {i} ({layer.Tilemap.name}) has navigation priority {source.Priority}, but a ground layer's navigation priority is its place in the layers, its draw order: {TerrainBlendRenderer.NavigationPriorityOf(i)}. Set it from the draw order in the {nameof(TerrainBlendRenderer)} inspector.", renderer);
            }

            if (layer != null && layer.Tilemap != null && layer.Tilemap.TryGetComponent(out TilemapRenderer tilemapRenderer) && tilemapRenderer.enabled)
            {
                Debug.LogWarning($"{name}: ground layer {i} ({layer.Tilemap.name}) has an enabled Tilemap Renderer, which draws its tiles' sprites (their palette swatches) beside the blended ground. A ground layer is a data tilemap: remove its Tilemap Renderer.", renderer);
            }
        }

        WarnOffLayerOrder(renderer, style, layers);
        if (!SortingLayer.IsValid(SortingLayer.NameToID(sortingLayerName)))
        {
            Debug.LogError($"{name}: there is no sorting layer named {sortingLayerName}.", renderer);
            usable = false;
        }

        if (grid.cellSize.x != 1f || grid.cellSize.y != 1f || grid.cellLayout != GridLayout.CellLayout.Rectangle || grid.transform.rotation != Quaternion.identity)
        {
            Debug.LogWarning($"{name}: {nameof(TerrainBlendRenderer)} assumes an unrotated rectangular grid of one-unit cells.", renderer);
        }

        return usable;
    }

    /// <summary>
    /// Warns once when <paramref name="layers"/> leave <paramref name="style"/>'s shared draw order
    /// (<see cref="TerrainStyle.LayerOrder"/>, Docs/KnownGaps.md, Phase 6): a material the order does not list, or a layer
    /// listed above one whose material the order puts above its own. The renderer draws its layers as listed, so such a
    /// pair draws the other way up from other areas. A style with no order checks nothing.
    /// </summary>
    private static void WarnOffLayerOrder(TerrainBlendRenderer renderer, TerrainStyle style, IReadOnlyList<TerrainBlendRenderer.Layer> layers)
    {
        if (style.LayerOrder == null || style.LayerOrder.Count == 0)
        {
            return;
        }

        List<string> problems = null;
        int highestRank = -1;
        string highestName = null;
        for (int i = 0; i < layers.Count; i++)
        {
            TerrainMaterial material = layers[i]?.Material;
            if (material == null)
            {
                continue;
            }

            int rank = style.LayerOrderIndexOf(material);
            if (rank < 0)
            {
                (problems ??= new List<string>()).Add($"{material.name} (layer {i}) is not in it");
            }
            else if (rank < highestRank)
            {
                (problems ??= new List<string>()).Add($"{material.name} (layer {i}) is listed above {highestName}, which it puts above {material.name}");
            }
            else
            {
                highestRank = rank;
                highestName = material.name;
            }
        }

        if (problems != null)
        {
            Debug.LogWarning($"{renderer.name}: its ground layers leave {style.name}'s layer order, so they draw differently from other areas: {string.Join("; ", problems)}. Reorder the layers in the {nameof(TerrainBlendRenderer)} inspector, or run Convert Ground.", renderer);
        }
    }

    /// <summary>
    /// Logs an error and returns false when <paramref name="terrainMaterial"/> has no albedo, lacks the cell field its
    /// cell mode needs, or has a texture (the height included) that is not repeat x 32 px square RGBA32 with sRGB off.
    /// A material without a height texture is usable: the soft look moves its edges by nothing.
    /// </summary>
    private static bool HasUsableTextures(TerrainMaterial terrainMaterial)
    {
        if (terrainMaterial.Albedo == null)
        {
            Debug.LogError($"Terrain material {terrainMaterial.name} has no albedo texture.", terrainMaterial);
            return false;
        }

        if (terrainMaterial.Cells != TerrainMaterial.CellMode.None && terrainMaterial.CellField == null)
        {
            Debug.LogError($"Terrain material {terrainMaterial.name} has chips or stones but no cell field texture.", terrainMaterial);
            return false;
        }

        return HasUsableFormat(terrainMaterial, terrainMaterial.Albedo)
            && (terrainMaterial.CellField == null || HasUsableFormat(terrainMaterial, terrainMaterial.CellField))
            && (terrainMaterial.Height == null || HasUsableFormat(terrainMaterial, terrainMaterial.Height));
    }

    /// <summary>
    /// Logs an error and returns false unless <paramref name="texture"/> is the material's repeat x 32 px square and
    /// RGBA32 with sRGB off, so it can be copied into the arrays as it is.
    /// </summary>
    private static bool HasUsableFormat(TerrainMaterial terrainMaterial, Texture2D texture)
    {
        int size = terrainMaterial.RepeatPixels;
        if (texture.width == size && texture.height == size && texture.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm)
        {
            return true;
        }

        Debug.LogError($"{texture.name} of terrain material {terrainMaterial.name} must be {size} px square (its repeat of {terrainMaterial.RepeatCells} cells) and RGBA32 with sRGB off.", texture);
        return false;
    }
}
