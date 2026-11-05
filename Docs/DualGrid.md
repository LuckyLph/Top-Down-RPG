# Dual-grid walls

The walls use a dual grid: you paint a **data tilemap** and a **render tilemap**, offset by half a cell, draws it. Each render cell sits on the corner shared by four data cells and picks its sprite from which of those four are filled. A 16-piece tileset therefore covers every corner combination, and painted shapes get smooth edges without hand-placed edge tiles.

The ground no longer does. Since Phase 4 of [TerrainBlending.md](TerrainBlending.md), grass, gravel and stone are plain data tilemaps holding one plain tile each, drawn by a `TerrainBlendRenderer` on the area's `Grid` (see [Architecture.md](Architecture.md#world)), and their rule tiles, tilesets and render tilemaps are gone. The walls stay dual-grid, because they are raised and carry colliders. The Terrain Brush, the Tile Palette's default, is the Dual Grid Brush with a preview drawn by that renderer on the ground layers; on the walls it is the Dual Grid Brush.

The code started as skner's DualGrid 2.1.0 ([skner-dev/DualGrid](https://github.com/skner-dev/DualGrid), upstream commit `493500b`) and is now maintained here. Upstream's MIT license is kept in [LICENSE.txt](../Assets/Scripts/World/DualGrid/LICENSE.txt) and covers both the runtime and editor code.

## Where the code lives

| Folder | Assembly | Contents |
|---|---|---|
| [Assets/Scripts/World/DualGrid](../Assets/Scripts/World/DualGrid) | `TopDownRPG.Gameplay` | `DualGridTilemapModule`, `DualGridRuleTile`, `DualGridDataTile`, `DualGridPreviewTile`, `DualGridUtils`, small extension classes |
| [Assets/Editor/DualGrid](../Assets/Editor/DualGrid) | `TopDownRPG.Editor` | Inspectors, the dual grid brush, the rule tile previewer, automatic tiling rules, menus, the edit-mode change listener |

The editor code sets `internal` members of the runtime types (for example `DualGridTilemapModule.RenderTile`), so Gameplay's `AssemblyInfo.cs` grants `InternalsVisibleTo` to `TopDownRPG.Editor`.

## Runtime types

- **`DualGridTilemapModule`** sits on the data tilemap's GameObject. Its render tilemap is its first child that has a `Tilemap`, placed at local `(-0.5, -0.5)`. `SetDataTile`/`ClearDataTile` paint one cell; `SetDataTiles`/`ClearDataTiles` (a list of cells, or a `BoundsInt`) paint a batch with one `SetTiles` call. Either way the render cells around the changed cells are collected into a set and each is updated once: new ones are written in bulk, emptied ones cleared in bulk, the rest refreshed. `RefreshRenderTilemap` clears the render tilemap and rebuilds it from every data cell. While the module is enabled it also listens to `Tilemap.tilemapPositionsChanged`, so painting the data tilemap directly (a brush, or code calling `Tilemap.SetTile`) keeps the render tilemap in sync; it ignores the callbacks for its own writes, so nothing is refreshed twice. A render cell is removed only when none of its four data cells is filled. Single-cell edits allocate nothing; `DualGridUtils.GetRenderTilePosition`/`GetDataTilePosition` give the corners without building arrays.
- **`DualGridRuleTile`** is a `RuleTile<DualGridNeighbor>` placed on the render tilemap. Its 16 tiling rules check only the four diagonal neighbours, and each one is read from the **data** tilemap: offsets `(0,0)`, `(-1,0)`, `(0,-1)`, `(-1,-1)` from the render cell, mapped to neighbour positions by `DualGridUtils.ConvertDataTileOffsetToNeighborOffset`. Neighbours with no rule match anything. It owns a `DualGridDataTile` sub-asset (`DataTile`) that carries the default collider type and GameObject. Reading `DataTile` when it is missing logs an error and throws.
- **`DualGridDataTile`** is the tile painted on the data tilemap. It drops its GameObject unless the module's `GameObjectOrigin` is `DataTilemap`.
- **`DualGridPreviewTile`** is a stand-in for filled or empty cells while the brush shows a preview. In the editor, rule matching looks at the data tilemap's editor preview tile before the real tile, so previews change the render tilemap before you commit the paint.

### Why the rule tile looks up its data tilemap on every call

`GetTileData` and `RuleMatches` find the module again (`GetComponentInParent<DualGridTilemapModule>` from the render tilemap) whenever the cached module or its data tilemap is missing. Unity can refresh a tile before `StartUp` has run, and it sometimes moves tiles between tilemaps, so a cached data tilemap cannot be trusted (upstream issue [#5](https://github.com/skner-dev/DualGrid/issues/5)). The last render tilemap and its module are kept, so repeated updates on the same tilemap only pay for one `GetComponent`. When no module is found, the tile matches no rules and shows its default sprite. That covers tile palettes, which are expected. It also covers a rule tile placed on a tilemap without a module, which is a mistake. The two cases look the same from inside the tile, so neither one logs anything.

### GameObjects and colliders

- `GameObjectOrigin.DataTilemap`: the rule tile's default GameObject is placed once per filled data cell.
- `GameObjectOrigin.RenderTilemap`: each tiling rule's GameObject is placed per render cell, so corners can differ.
- `GameObjectOrigin.None`: no GameObjects.
- Collider type is set once on the rule tile, not per tiling rule. With `EnableTilemapCollider` on, the module's editor adds a `TilemapCollider2D` to the data tilemap for `Grid` colliders, or to the render tilemap for `Sprite` colliders (useful for rounded tilesets). The walls module has it on with `Grid` colliders, so the collider sits on its data tilemap (the area's `CollisionTilemap`) and covers exactly the navigation cells; `Sprite` colliders on the offset render tilemap would make physics and A* disagree.

## Authoring

1. Import a 16-piece dual-grid tilesheet. Set the Sprite Mode to Multiple, then slice it in the Sprite Editor with Grid By Cell Count 4x4 and Keep Empty Rects ticked. The automatic rules rely on the slice names ending in `_<index>`, so do not rename the slices.
2. Right-click the texture and choose **Create > 2D > Tiles > Dual Grid Rule Tile**, then accept the prompt to fill the tiling rules automatically. The inspector's preview shows a fixed sample layout so you can check the rules. **Apply Automatic Rule Tiling** rebuilds the rules if they are wrong. Exactly 16 rules are required.
3. Create a tilemap with **GameObject > 2D Object > Tilemap > Dual Grid Tilemap**. If a `Grid` is selected the tilemap goes under it, otherwise a new grid is created. Assign the rule tile to the module.
4. Paint the data tilemap with the **Terrain Brush** (the Tile Palette's default) or the **Dual Grid Brush**. On a dual-grid tilemap both write the module's data tile whatever the palette holds, and preview paints and flood fills on the render tilemap. The render tilemap updates in edit mode through `DualGridTilemapPersistentListener` and in play mode through the module itself.
5. Give the layer a tile palette: create one in `Assets/Tiles/Palettes` (manual cell size 1x1) and put the rule tile's data tile in it. That is the tile the data tilemap holds; the rule tile itself goes only on the render tilemap. Set the data tile's sprite to the full-fill slice (`_6`) so the palette shows a swatch. Data tilemaps have no renderer, so the sprite is never drawn in the scene.

### Palettes

Each painted layer of an area has a palette in `Assets/Tiles/Palettes` holding its tile (the planks three: their automatic tile and one for each fixed way their boards lie, KnownGaps.md's Phase 6). `Walls_Palette` holds the walls' data tile. `Grass_Palette`, `Gravel_Palette` and `Stone_Palette` hold their material's plain tile (`Tile_Grass`, `Tile_Gravel`, `Tile_Stone` in `Assets/Tiles/Ground`, with a swatch of the material's texture; see [TerrainBlending.md](TerrainBlending.md)), the tile the plain ground layers hold.

To paint:
1. In the Tile Palette window, pick the layer's palette and select its tile: `Grass_Palette`, `Gravel_Palette` or `Stone_Palette` for the ground, `Walls_Palette` for walls.
2. Set the active tilemap to that layer's data tilemap under the area's `Grid`: `GrassTilemap`, `GravelTilemap` or `StoneTilemap` (plain ground layers), or `CollisionTilemap` (the walls), never the walls' `RenderTilemap`.
3. Paint with the Terrain Brush, the default. On a ground layer it writes the palette's tile into the stroke's cells and previews the stroke through the area's `TerrainBlendRenderer`. On the walls it is the Dual Grid Brush: it writes the module's data tile whatever the palette holds and previews on the render tilemap. The Dual Grid Brush is in the brush list too.

Rules: one rule tile per data tilemap, and never share a data tilemap between modules. Several modules can share a `Grid`.

Navigation reads data tilemaps, never a render tilemap. A `NavigationTerrainSource2D` on a render tilemap would shift every path by half a cell, so `NavigationGrid2D` skips it with an error. The ground layers are plain tilemaps, each with its own navigation source, whose priority is its place in the terrain renderer's layers (see [TerrainBlending.md](TerrainBlending.md)). Walls go on the collision tilemap listed in the grid's `blockingTilemaps`, which blocks those cells for every terrain layer. That tilemap is a dual-grid data tilemap: see Walls below. Painting a data or collision tilemap at runtime updates the grid by itself. Gameplay that changes terrain goes through the area's `AreaTerrain` so the host's edits reach every client (see [Architecture.md](Architecture.md#world)). Navigation cells match the data cells, but the art does not always fill them: rounded corners (the walls' pieces, the blended ground's crisp look) cut the outer corner of corner cells. The movement profile's `EdgeCost` makes A* prefer cells away from the ground's edge, so units do not walk along that visible gap.

## Walls

Each area's `CollisionTilemap` (on the `Obstacles` layer, which the camera culls) carries a `DualGridTilemapModule` with `walls_DualGridRuleTile`. Its `RenderTilemap` child is on the Default layer and the Terrain sorting layer at order 10, above the blended ground's chunks at order 0, and draws the walls; the data tiles are invisible and carry `Grid` colliders (the rule tile's default collider type), so the `TilemapCollider2D` on the collision tilemap, navigation's blocking cells and the art all use the same cells. Paint walls on the collision tilemap with the Terrain Brush or the Dual Grid Brush (the same brush there), through `AreaTerrain` at runtime, or with `DualGridTilemapModule.SetDataTiles` in code (the stress map does). Paint the ground under walls that stand beside ground: where the ground stops at a wall, its rounded outer corner (14 px) and the walls' fillet (6.2 px) leave pixels with no ground at each inner corner. The Terrain Brush and `WorldAuthoring.PaintWalls` paint it for you, the ground of the wall cell's neighbours (`WallGround`), and Validate reports a world's wall cells beside ground with none under them (KnownGaps.md's Phase 6). On a chunk, the Terrain Brush's preview of walls uses the walls of every open chunk the stroke covers, so the corners along a border come out right.

The wall art, `Assets/Tiles/tilesets/walls.png`, is a generated placeholder: a 128 px sheet of sixteen 32 px pieces at 32 pixels per unit, in the standard 4x4 layout, so a piece covers one cell like the props and the blended ground. Each piece fills the quadrants its tiling rule marks as filled, with straight edges on the data-cell borders, rounded outer corners (6 px), filleted inner corners (6.2 px), a dark outline and a darker front face along edges that face down, over a top of 2 px stone blocks in three shades. It is generated in the editor by `WallSheetRecipe` (`Assets/Settings/Terrain/Recipes/Recipe_Walls.asset`, its inspector's Generate), which reads each piece's quadrants from `walls_DualGridRuleTile`'s rules and slices the sheet keeping each slice's IDs, so the rule tile and `Walls_Palette` keep pointing at them (see [Architecture.md](Architecture.md#editor-tools)). To replace it with drawn art, keep the layout, the 32 PPU and the slice names `walls_0` to `walls_15`, and stop using the recipe, whose Generate would overwrite the file.

## Editor behaviour to know about

- `RestrictedTransformEditor` and `RestrictedTilemapEditor` replace the inspector for **every** `Transform` and `Tilemap` in the project. Both draw Unity's default inspector, but turn editing off on a render tilemap (one whose parent has a `DualGridTilemapModule`), because the module manages its position.
- `DualGridRuleTileEditor` copies two methods from Unity's `RuleTileEditor`, because the originals cannot be extended:
  - `RuleMatrixOnGUI` loops over a 2x2 matrix and remaps each cell to its corner neighbour (`0` becomes `1`), keeping the drawing rect unchanged.
  - `OnDrawElement` calls the local `DualGridRuleInspectorOnGUI` instead of the non-virtual `RuleInspectorOnGUI`. In that inspector the per-rule collider field is greyed out, because colliders are set per rule tile.

  Compare against these two methods when updating `com.unity.2d.tilemap.extras`.
- Changing a rule tile's default sprite, GameObject or collider refreshes its data tile and every module in the open scenes that uses it.
- The 16-entry neighbour table in `AutoDualGridRuleTileProvider` is written out by hand. No simple formula produces that slice order.
