using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An area's content in grid cells, as an <see cref="AreaGenerator"/> lays it out: the filled cells of each of the
/// generator's terrain layers (the layers it lists, so a layer's index depends on the generator), the props, the mob
/// spawns, the party's spawn cell, and notes on what the generator could not fit. <see cref="AreaLayoutWriter"/> maps
/// it onto an area's grid.
/// </summary>
public sealed class AreaLayout
{
    /// <summary>
    /// A prop to place. A blocking prop's footprint fills the cells it needs from <see cref="Cell"/>, its bottom-left
    /// cell (<see cref="PropGridSnapping.CellsCovered"/>); a walk-through prop stands at <see cref="Offset"/>, a fraction
    /// of a cell, inside <see cref="Cell"/>.
    /// </summary>
    public readonly struct Prop
    {
        /// <summary>
        /// Creates a placement of <paramref name="prefab"/> at <paramref name="cell"/>, offset by
        /// <paramref name="offset"/> when it is a walk-through prop.
        /// </summary>
        public Prop(GameObject prefab, Vector3Int cell, Vector2 offset)
        {
            Prefab = prefab;
            Cell = cell;
            Offset = offset;
        }

        public GameObject Prefab { get; }
        public Vector3Int Cell { get; }
        public Vector2 Offset { get; }
    }

    /// <summary>
    /// A mob spawn point for <see cref="Prefab"/> at the centre of <see cref="Cell"/>.
    /// </summary>
    public readonly struct Mob
    {
        /// <summary>
        /// Creates a spawn of <paramref name="prefab"/> at the centre of <paramref name="cell"/>.
        /// </summary>
        public Mob(MobController prefab, Vector3Int cell)
        {
            Prefab = prefab;
            Cell = cell;
        }

        public MobController Prefab { get; }
        public Vector3Int Cell { get; }
    }

    private readonly AreaGenerator.TerrainLayer[] terrainLayers;
    private readonly HashSet<Vector3Int>[] terrain;

    /// <summary>
    /// Creates an empty layout of <paramref name="layers"/>, a generator's terrain layers, bottom first.
    /// </summary>
    public AreaLayout(IReadOnlyList<AreaGenerator.TerrainLayer> layers)
    {
        terrainLayers = new AreaGenerator.TerrainLayer[layers.Count];
        terrain = new HashSet<Vector3Int>[layers.Count];
        for (int i = 0; i < terrain.Length; i++)
        {
            terrainLayers[i] = layers[i];
            terrain[i] = new HashSet<Vector3Int>();
        }
    }

    /// <summary>
    /// The terrain layers the layout was laid out with, bottom first, as the generator listed them.
    /// </summary>
    public IReadOnlyList<AreaGenerator.TerrainLayer> TerrainLayers => terrainLayers;

    public int TerrainLayerCount => terrain.Length;
    public List<Prop> Props { get; } = new();
    public List<Mob> Mobs { get; } = new();
    public List<string> Warnings { get; } = new();
    public Vector3Int SpawnCell { get; set; }

    /// <summary>
    /// The cells of raised ground, ramps included, which the writer puts on the area's raised tilemap.
    /// </summary>
    public HashSet<Vector3Int> Raised { get; } = new();

    /// <summary>
    /// The cells of <see cref="Raised"/> that are ramps.
    /// </summary>
    public HashSet<Vector3Int> Ramps { get; } = new();

    /// <summary>
    /// The filled cells of terrain layer <paramref name="layer"/>, indexed like <see cref="TerrainLayers"/>.
    /// </summary>
    public HashSet<Vector3Int> TerrainCells(int layer)
    {
        return terrain[layer];
    }

    /// <summary>
    /// The filled cells of <paramref name="layer"/>, one of <see cref="TerrainLayers"/>.
    /// </summary>
    public HashSet<Vector3Int> TerrainCells(AreaGenerator.TerrainLayer layer)
    {
        int index = Array.IndexOf(terrainLayers, layer);
        if (layer == null || index < 0)
        {
            throw new ArgumentException("The layer is not one of the layout's terrain layers.", nameof(layer));
        }

        return terrain[index];
    }

    /// <summary>
    /// The index of the first terrain layer drawn with <paramref name="material"/>, or -1 when none is.
    /// </summary>
    public int IndexOf(TerrainMaterial material)
    {
        for (int i = 0; i < terrainLayers.Length; i++)
        {
            if (material != null && terrainLayers[i].Material == material)
            {
                return i;
            }
        }

        return -1;
    }
}
