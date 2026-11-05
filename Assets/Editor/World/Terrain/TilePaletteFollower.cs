using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Switches the Tile Palette window to the palette that paints the tilemap picked as its paint target, so picking the
/// sand layer brings up the sand palette (Docs/TerrainBlending.md, Phase 5 notes). The tilemap's tiles are looked for in
/// the window's palettes, in this order: a dual-grid layer's data tile, a ground layer's tile (the tile of the material
/// its <see cref="TerrainBlendRenderer"/> draws it with, so an empty layer finds its palette too), then the tiles
/// painted on it. The first palette holding one of them is selected; with none, the palette stays as it is. It can be
/// turned off from its menu item, an editor preference that is on by default.
/// </summary>
[InitializeOnLoad]
public static class TilePaletteFollower
{
    private const string MenuPath = "Tools/TopDownRPG/World/Terrain/Match Palette To Tilemap";
    private const string EnabledPreference = "TopDownRPG.Terrain.MatchPaletteToTilemap";

    /// <summary>
    /// Follows the Tile Palette's paint target for the whole session, on every domain load, and stops before the next
    /// reload.
    /// </summary>
    static TilePaletteFollower()
    {
        GridPaintingState.scenePaintTargetChanged += HandlePaintTargetChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }

    /// <summary>
    /// Whether the palette follows the paint target, for this user of the editor.
    /// </summary>
    public static bool Enabled
    {
        get => EditorPrefs.GetBool(EnabledPreference, true);
        set => EditorPrefs.SetBool(EnabledPreference, value);
    }

    /// <summary>
    /// The first of <paramref name="palettes"/> holding a tile <paramref name="target"/> paints (see the class
    /// summary), or null when <paramref name="target"/> is no tilemap or no palette holds one.
    /// </summary>
    internal static GameObject PaletteFor(GameObject target, IEnumerable<GameObject> palettes)
    {
        if (target == null || palettes == null || !target.TryGetComponent(out Tilemap tilemap))
        {
            return null;
        }

        foreach (TileBase tile in TilesOf(tilemap))
        {
            foreach (GameObject palette in palettes)
            {
                if (palette != null && Holds(palette, tile))
                {
                    return palette;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The tiles <paramref name="tilemap"/> paints, most telling first: its dual-grid module's data tile, the tile of
    /// the material a terrain renderer draws it with, then the tiles painted on it.
    /// </summary>
    private static List<TileBase> TilesOf(Tilemap tilemap)
    {
        List<TileBase> tiles = new();
        if (tilemap.TryGetComponent(out DualGridTilemapModule module) && module.RenderTile != null && module.DataTile != null)
        {
            tiles.Add(module.DataTile);
        }

        TerrainBlendRenderer renderer = TerrainBlendRenderer.FindLayerDrawing(tilemap, out int layer);
        if (renderer != null && renderer.Layers[layer].Material != null)
        {
            Tile tile = TerrainTileWriter.TileOf(renderer.Layers[layer].Material);
            if (tile != null)
            {
                tiles.Add(tile);
            }
        }

        int count = tilemap.GetUsedTilesCount();
        TileBase[] used = new TileBase[count];
        tilemap.GetUsedTilesNonAlloc(used);
        foreach (TileBase tile in used)
        {
            if (tile != null && !tiles.Contains(tile))
            {
                tiles.Add(tile);
            }
        }

        return tiles;
    }

    /// <summary>
    /// Whether one of <paramref name="palette"/>'s tilemaps holds <paramref name="tile"/>.
    /// </summary>
    private static bool Holds(GameObject palette, TileBase tile)
    {
        foreach (Tilemap tilemap in palette.GetComponentsInChildren<Tilemap>(true))
        {
            if (tilemap.ContainsTile(tile))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Selects the palette for the new paint target when following is on and it differs from the selected one.
    /// </summary>
    private static void HandlePaintTargetChanged(GameObject target)
    {
        if (!Enabled)
        {
            return;
        }

        GameObject palette = PaletteFor(target, GridPaintingState.palettes);
        if (palette != null && palette != GridPaintingState.palette)
        {
            GridPaintingState.palette = palette;
        }
    }

    /// <summary>
    /// Stops following the paint target before an assembly reload, which subscribes again.
    /// </summary>
    private static void Stop()
    {
        GridPaintingState.scenePaintTargetChanged -= HandlePaintTargetChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= Stop;
    }

    /// <summary>
    /// Turns following on or off.
    /// </summary>
    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
    }

    /// <summary>
    /// Shows whether following is on.
    /// </summary>
    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
