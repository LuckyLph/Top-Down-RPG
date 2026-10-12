# Dual-grid terrain

The terrain tilemaps use a dual grid: you paint a **data tilemap** and a **render tilemap**, offset by half a cell, draws it. Each render cell sits on the corner shared by four data cells and picks its sprite from which of those four are filled. A 16-piece tileset therefore covers every corner combination, and painted shapes get smooth edges without hand-placed edge tiles.

The ground is moving to one blending shader ([TerrainBlending.md](TerrainBlending.md)). In the Village Forest a `TerrainBlendRenderer` already draws grass, gravel and stone from their data tilemaps and the three ground render tilemaps are switched off; their modules and rule tiles still run (painting, the generator and `AreaTerrain` work as before) until Phase 4 removes them. The Clearing and the stress test draw their ground as described here. Walls stay dual-grid.

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
- Collider type is set once on the rule tile, not per tiling rule. With `EnableTilemapCollider` on, the module's editor adds a `TilemapCollider2D` to the data tilemap for `Grid` colliders, or to the render tilemap for `Sprite` colliders (useful for rounded tilesets). The grass module leaves this off. The walls module has it on with `Grid` colliders, so the collider sits on its data tilemap (the area's `CollisionTilemap`) and covers exactly the navigation cells; `Sprite` colliders on the offset render tilemap would make physics and A* disagree.

## Authoring

1. Import a 16-piece dual-grid tilesheet. Set the Sprite Mode to Multiple, then slice it in the Sprite Editor with Grid By Cell Count 4x4 and Keep Empty Rects ticked. The automatic rules rely on the slice names ending in `_<index>`, so do not rename the slices.
2. Right-click the texture and choose **Create > 2D > Tiles > Dual Grid Rule Tile**, then accept the prompt to fill the tiling rules automatically. The inspector's preview shows a fixed sample layout so you can check the rules. **Apply Automatic Rule Tiling** rebuilds the rules if they are wrong. Exactly 16 rules are required.
3. Create a tilemap with **GameObject > 2D Object > Tilemap > Dual Grid Tilemap**. If a `Grid` is selected the tilemap goes under it, otherwise a new grid is created. Assign the rule tile to the module.
4. Paint the data tilemap with any brush, or use the **Dual Grid Brush**, which also previews paints and flood fills on the render tilemap. The render tilemap updates in edit mode through `DualGridTilemapPersistentListener` and in play mode through the module itself.
5. Give the layer a tile palette: create one in `Assets/Tiles/Palettes` (manual cell size 1x1) and put the rule tile's data tile in it. That is the tile the data tilemap holds; the rule tile itself goes only on the render tilemap. Set the data tile's sprite to the full-fill slice (`_6`) so the palette shows a swatch. Data tilemaps have no renderer, so the sprite is never drawn in the scene.

### Palettes

Each terrain layer has a palette in `Assets/Tiles/Palettes` holding only its data tile: `Grass_Palette`, `Gravel_Palette`, `Stone_Palette` and `Walls_Palette` (the walls go on the `CollisionTilemap`). In the Tile Palette window, pick the layer's palette, select its tile, set the active tilemap to that layer's data tilemap (`GrassTilemap`, `GravelTilemap`, `StoneTilemap` or `CollisionTilemap`, never a `RenderTilemap`), and paint with the Dual Grid Brush. The Dual Grid Brush always writes the target module's own data tile, so the palette mostly tells you which layer you are on; the default brush writes the tile you picked, which is why each palette holds exactly its layer's data tile.

Rules: one rule tile per data tilemap, and never share a data tilemap between modules. Several modules can share a `Grid`.

Navigation reads the data tilemap, never the render tilemap: put the `NavigationTerrainSource2D` on the data tilemap (the one with the module). A source on a render tilemap would shift every path by half a cell, so `NavigationGrid2D` skips it with an error. Walls go on the collision tilemap listed in the grid's `blockingTilemaps`, which blocks those cells for every terrain layer. That tilemap is itself a dual-grid data tilemap: see Walls below. Since a module holds a single tileset, several terrains mean stacked modules; give a layer drawn over another (a road, a bridge) a higher `Priority` on its terrain source so it decides those cells. Painting a data or collision tilemap at runtime updates the grid by itself. Gameplay that changes terrain goes through the area's `AreaTerrain` so the host's edits reach every client (see [Architecture.md](Architecture.md#world-and-areas)). Navigation cells match the data cells, but the art does not always fill them: rounded tilesets cut the outer corner of corner cells. The movement profile's `EdgeCost` makes A* prefer cells away from the ground's edge, so units do not walk along that visible gap.

## Walls

Each area's `CollisionTilemap` (on the `Obstacles` layer, which the camera culls) carries a `DualGridTilemapModule` with `walls_DualGridRuleTile`. Its `RenderTilemap` child is on the Default layer and the Terrain sorting layer at order 10, above every ground layer (grass at 0, gravel and stone paths at 1 and 2, or the blended terrain's chunks at 0), and draws the walls; the data tiles are invisible and carry `Grid` colliders (the rule tile's default collider type), so the `TilemapCollider2D` on the collision tilemap, navigation's blocking cells and the art all use the same cells. Paint walls with the Dual Grid Brush on the collision tilemap, through `AreaTerrain` at runtime, or with `DualGridTilemapModule.SetDataTiles` in code (the stress map does).

The wall art, `Assets/Tiles/tilesets/walls.png`, is a generated placeholder in the same 4x4 layout as the grass: each of the 16 pieces fills the quadrants its tiling rule marks as filled, with straight edges on the data-cell borders, rounded outer corners, filleted inner corners, a dark outline and a darker front face. Replace the texture with drawn art in the same layout and keep the slices named `walls_0` to `walls_15`.

## Paths

`Assets/Tiles/tilesets/stone_path.png` (cobblestones) and `gravel_path.png` (gravel chips on packed dirt) are path tilesets in the same 4x4 layout, with rule tiles `stone_path_DualGridRuleTile` and `gravel_path_DualGridRuleTile` whose tiling rules match the grass tile's. They have no colliders. Paths are their own dual-grid layers stacked over the grass, which stays painted under them, so grass shows through a path's soft edge. Each layer's render tilemap draws one order higher than the layer below it, and its navigation source has a higher priority, so the top layer decides a cell's terrain type. The scene generator sets these up (see [SceneGeneration.md](SceneGeneration.md)), and the Dual Grid Brush paints them like any other layer.

They draw the paths of the areas not yet converted to the blended terrain. The Village Forest's paths are drawn by its `TerrainBlendRenderer`, whose shader does the maths below per pixel in its crisp look (exact rounded distances, smooth edge noise with a slope under 1, shadow, outline and rim, chips and stones decided at their centre; its soft look, a developer toggle, blends the shapes from the cells and the materials' heights instead) over the 32 px material textures that the terrain material recipes generate in the editor (see [Architecture.md](Architecture.md#world-and-areas)).

Both textures are generated placeholders, like the walls, by [tools/terrain-art/pathtiles.py](../tools/terrain-art/pathtiles.py) (standard-library Python: `python pathtiles.py <output folder>`, then copy the two PNGs over the ones in `Assets/Tiles/tilesets`; `compose.py` in the same folder previews them on a test map and reports detached blobs). Each 64 px piece is drawn from an exact signed distance to the data cells around its corner: an outer corner is rounded with a 29 px radius and an inner one filleted with 22 px, so a path that steps diagonally from cell to cell reads as a wavy band rather than a staircase. Both radii stay under 32 px, half a piece, so every boundary is straight where it crosses a piece's edge, and any two pieces that meet agree there. Smooth noise made of three long waves that repeat every 64 px moves the edge in and out by about a pixel, enough to keep a long edge from looking ruled without making it bumpy. Its slope stays below 1, which keeps the distance rising away from the path everywhere, so the edge can never pinch off a separate blob: nothing is drawn outside the shape. The stones and the gravel chips come from jittered cells that also repeat every 64 px.

From the outside in, an edge is a translucent shadow two pixels wide that darkens the grass, an antialiased outline pixel, then a soft rim (darker dirt for gravel, earthy mortar for stone) before the paving or the chips. A gravel chip is drawn whole or not at all: only when the centre of its cell lies clear of the rim. A cobble whose cell centre lies near the rim is left out, so no thin slivers line it; the others are cut along the rim's smooth line and shaded to that cut like to their own edges. Every decision that depends on the shape is taken from the distance at a cell's centre, which two meeting pieces also agree on. Replace either texture with drawn art in the same layout and keep the slices named `stone_path_0` to `stone_path_15` and `gravel_path_0` to `gravel_path_15`.

## Editor behaviour to know about

- `RestrictedTransformEditor` and `RestrictedTilemapEditor` replace the inspector for **every** `Transform` and `Tilemap` in the project. Both draw Unity's default inspector, but turn editing off on a render tilemap (one whose parent has a `DualGridTilemapModule`), because the module manages its position.
- `DualGridRuleTileEditor` copies two methods from Unity's `RuleTileEditor`, because the originals cannot be extended:
  - `RuleMatrixOnGUI` loops over a 2x2 matrix and remaps each cell to its corner neighbour (`0` becomes `1`), keeping the drawing rect unchanged.
  - `OnDrawElement` calls the local `DualGridRuleInspectorOnGUI` instead of the non-virtual `RuleInspectorOnGUI`. In that inspector the per-rule collider field is greyed out, because colliders are set per rule tile.

  Compare against these two methods when updating `com.unity.2d.tilemap.extras`.
- Changing a rule tile's default sprite, GameObject or collider refreshes its data tile and every module in the open scenes that uses it.
- The 16-entry neighbour table in `AutoDualGridRuleTileProvider` is written out by hand. No simple formula produces that slice order.
