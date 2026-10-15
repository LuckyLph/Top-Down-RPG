#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor only: when a <see cref="TerrainBlendRenderer"/> rebuilds after an inspector change. Remembers what its last
/// build read (the style, the layers' tilemaps and materials, the chunk size and the sorting), so a change to anything
/// else (the look) rebuilds nothing; follows the inspector changes of its style and of its layers' materials (their
/// <c>Validated</c> events), once per material however many layers draw it; and schedules the rebuild after the
/// editor's current update (<see cref="UnityEditor.EditorApplication.delayCall"/>, since objects cannot be destroyed
/// during validation), once however often asked.
/// </summary>
internal sealed class TerrainBlendRebuilds
{
    private readonly Action rebuild;
    private readonly List<TerrainMaterial> subscribedMaterials = new();
    private readonly List<Tilemap> builtTilemaps = new();
    private readonly List<TerrainMaterial> builtMaterials = new();
    private TerrainStyle subscribedStyle;
    private TerrainStyle builtStyle;
    private Tilemap builtRaisedTilemap;
    private int builtChunkSize;
    private string builtSortingLayerName;
    private int builtSortingOrder;

    /// <summary>
    /// Creates a tracker that runs <paramref name="rebuild"/> when a scheduled rebuild comes due.
    /// </summary>
    public TerrainBlendRebuilds(Action rebuild)
    {
        this.rebuild = rebuild;
    }

    public bool IsScheduled { get; private set; }

    /// <summary>
    /// Remembers what a build read, for <see cref="Changed"/>.
    /// </summary>
    public void Remember(TerrainStyle style, IReadOnlyList<TerrainBlendRenderer.Layer> layers, Tilemap raisedTilemap, int chunkSize, string sortingLayerName, int sortingOrder)
    {
        builtStyle = style;
        builtRaisedTilemap = raisedTilemap;
        builtTilemaps.Clear();
        builtMaterials.Clear();
        if (layers != null)
        {
            foreach (TerrainBlendRenderer.Layer layer in layers)
            {
                builtTilemaps.Add(layer?.Tilemap);
                builtMaterials.Add(layer?.Material);
            }
        }

        builtChunkSize = chunkSize;
        builtSortingLayerName = sortingLayerName;
        builtSortingOrder = sortingOrder;
    }

    /// <summary>
    /// Whether anything the last build read differs from these.
    /// </summary>
    public bool Changed(TerrainStyle style, IReadOnlyList<TerrainBlendRenderer.Layer> layers, Tilemap raisedTilemap, int chunkSize, string sortingLayerName, int sortingOrder)
    {
        int count = layers?.Count ?? 0;
        if (!ReferenceEquals(style, builtStyle) || raisedTilemap != builtRaisedTilemap || count != builtTilemaps.Count || chunkSize != builtChunkSize
            || sortingLayerName != builtSortingLayerName || sortingOrder != builtSortingOrder)
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(layers[i]?.Tilemap, builtTilemaps[i]) || !ReferenceEquals(layers[i]?.Material, builtMaterials[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Has inspector changes to <paramref name="style"/> and to each layer's material (once per material, however many
    /// layers draw it) schedule a rebuild.
    /// </summary>
    public void Follow(TerrainStyle style, IReadOnlyList<TerrainBlendRenderer.Layer> layers)
    {
        if (style != null)
        {
            subscribedStyle = style;
            subscribedStyle.Validated += Schedule;
        }

        if (layers == null)
        {
            return;
        }

        foreach (TerrainBlendRenderer.Layer layer in layers)
        {
            TerrainMaterial layerMaterial = layer?.Material;
            if (layerMaterial != null && !subscribedMaterials.Contains(layerMaterial))
            {
                subscribedMaterials.Add(layerMaterial);
                layerMaterial.Validated += Schedule;
            }
        }
    }

    /// <summary>
    /// Stops following the style and the materials.
    /// </summary>
    public void StopFollowing()
    {
        if (subscribedStyle != null)
        {
            subscribedStyle.Validated -= Schedule;
        }

        subscribedStyle = null;
        foreach (TerrainMaterial subscribed in subscribedMaterials)
        {
            if (subscribed != null)
            {
                subscribed.Validated -= Schedule;
            }
        }

        subscribedMaterials.Clear();
    }

    /// <summary>
    /// Rebuilds after the editor finishes its current work, once however often asked.
    /// </summary>
    public void Schedule()
    {
        if (IsScheduled)
        {
            return;
        }

        IsScheduled = true;
        UnityEditor.EditorApplication.delayCall += RunScheduled;
    }

    /// <summary>
    /// Runs the scheduled rebuild, if one is still waiting. The editor calls it once it is done with the change that
    /// scheduled it; tests call it through the renderer.
    /// </summary>
    public void RunScheduled()
    {
        if (!IsScheduled)
        {
            return;
        }

        IsScheduled = false;
        rebuild();
    }
}
#endif
