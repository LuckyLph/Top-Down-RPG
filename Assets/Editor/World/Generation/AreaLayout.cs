using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An area's content in grid cells, as an <see cref="AreaGenerator"/> lays it out: the filled cells of each terrain
/// layer, the props, the mob spawns, the party's spawn cell, and notes on what the generator could not fit.
/// <see cref="AreaLayoutWriter"/> maps it onto an area's grid.
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

    private readonly HashSet<Vector3Int>[] terrain;

    /// <summary>
    /// Creates an empty layout with <paramref name="terrainLayerCount"/> terrain layers.
    /// </summary>
    public AreaLayout(int terrainLayerCount)
    {
        terrain = new HashSet<Vector3Int>[terrainLayerCount];
        for (int i = 0; i < terrain.Length; i++)
        {
            terrain[i] = new HashSet<Vector3Int>();
        }
    }

    public int TerrainLayerCount => terrain.Length;
    public List<Prop> Props { get; } = new();
    public List<Mob> Mobs { get; } = new();
    public List<string> Warnings { get; } = new();
    public Vector3Int SpawnCell { get; set; }

    /// <summary>
    /// The filled cells of terrain layer <paramref name="layer"/>, indexed like
    /// <see cref="AreaGenerator.TerrainLayers"/>.
    /// </summary>
    public HashSet<Vector3Int> TerrainCells(int layer)
    {
        return terrain[layer];
    }
}
