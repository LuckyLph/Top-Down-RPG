using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The plain tile a ground layer is painted with (Docs/TerrainBlending.md): a <see cref="Tile"/> whose sprite is a
/// swatch for the Tile Palette, never drawn in a scene, that keeps a reference to the <see cref="TerrainMaterial"/> it
/// paints, so the editor finds a material's tile and a tile's material however either is named or moved
/// (Docs/KnownGaps.md, Phase 6). A tile of a material that turns to its runs (<see cref="TerrainMaterial.TurnsToRuns"/>,
/// the planks) also says which way its textures lie in the cells it fills (<see cref="Facing"/>): turned by the layer's
/// runs, the default the generators keep, or fixed east to west or north to south. Written by the editor's
/// TerrainTileWriter; nothing at runtime writes it.
/// </summary>
public class TerrainTile : Tile
{
    /// <summary>
    /// Which way a material that turns to its runs lies in a cell: the way its runs decide, or with its textures' x
    /// (a board's length) running east to west, or north to south.
    /// </summary>
    public enum Facing
    {
        Automatic,
        EastWest,
        NorthSouth
    }

    [Tooltip("The ground material this tile paints.")]
    [SerializeField] private TerrainMaterial material;
    [Tooltip("Which way a material that turns to its runs lies in the cells this tile fills; automatic for any other.")]
    [SerializeField] private Facing facing;

    public TerrainMaterial Material => material;
    public Facing Direction => facing;

    /// <summary>
    /// The facing <paramref name="tile"/> gives its cell: its own for a <see cref="TerrainTile"/>, automatic for any
    /// other tile or none.
    /// </summary>
    public static Facing FacingOf(TileBase tile)
    {
        return tile is TerrainTile terrainTile ? terrainTile.facing : Facing.Automatic;
    }
}
