# Scene generation

An editor tool that fills an area scene with generated content: dual-grid terrain layers, props and mob spawn points, plus the party's start spawn. The result is saved in the scene like hand-made content. There is one generator so far, for the forest Clearing (`Area_Clearing.unity` was made with it).

## Using it

1. Open the area scene and make it the active scene.
2. Open **Tools > TopDownRPG > World > Scene Generator** and pick a generator asset. The Clearing's is [Generator_Clearing.asset](../Assets/Settings/Generation/Generator_Clearing.asset).
3. Tune its settings in the window. A map of the layout redraws on every change, one pixel per cell: grass green, gravel tan, stone grey, blocking footprints dark green, walk-through props light green, mob spawns red, the party spawn cyan. The line under it counts cells, props and mobs, and warnings say what did not fit.
4. **Random Seed** picks a new seed. The seed is stored on the generator asset, and the same seed and settings always give the same layout.
5. **Generate** asks first, then replaces the scene's content as a single undo step. Review the scene and save it.

What Generate replaces:
- Every terrain layer the generator lists: the data tilemap child of the area's `Grid` with that name is cleared and repainted. A layer the scene does not have yet is created with a dual-grid module, render tilemap and navigation source. Each layer's rule tile, terrain type, navigation priority and draw order are set from the generator, and the layers are put first among the grid's children, in the generator's order.
- Every child of the `Props` root, so props placed by hand there go too.
- Every `MobSpawnPoint` in the scene. The new ones go under a `MobSpawns` root, named `MobSpawn_<prefab>_<n>`.
- The position of the `start` spawn point, or of the first spawn point when none has that id. One is created when the scene has none.

The collision tilemap (walls), `AreaTerrain`, the lifetime scope, lights and any other objects are left alone. Generated layouts assume cells of one unit, like every area; the writer refuses any other grid.

## Pieces

All of it is editor-only code in `TopDownRPG.Editor`, under [Assets/Editor/World/Generation](../Assets/Editor/World/Generation).

| Type | Role |
|---|---|
| [AreaGenerator](../Assets/Editor/World/Generation/AreaGenerator.cs) | Abstract `ScriptableObject`, one subclass per kind of environment. Holds the `Seed` the window uses, lists the `TerrainLayers` it paints (each one has a tilemap name, a `DualGridRuleTile`, a `TerrainType2D`, a navigation priority and a sorting order), and `Generate(seed)` returns an `AreaLayout`. |
| [AreaLayout](../Assets/Editor/World/Generation/AreaLayout.cs) | Plain data in one-unit cells: the filled cells of each terrain layer, `Props` (prefab plus cell: the bottom-left cell of a blocking prop's footprint, or the cell and an offset inside it for a walk-through prop), `Mobs` (prefab plus cell), `SpawnCell`, and `Warnings`. |
| [ClearingGenerator](../Assets/Editor/World/Generation/ClearingGenerator.cs) | The forest clearing, described below. Create menu: `TopDownRPG/World/Clearing Generator`. |
| [AreaLayoutWriter](../Assets/Editor/World/Generation/AreaLayoutWriter.cs) | `Write(grid, generator, layout)` writes a layout into the grid's scene as described above. Blocking props go to `PropGridSnapping.RootOnCells`, so their footprints cover exactly their cells, and walk-through props are snapped to the pixel grid. Mob spawns and the party spawn go to cell centres. |
| [SceneGeneratorWindow](../Assets/Editor/World/Generation/SceneGeneratorWindow.cs) | The window. It targets the active scene's grid (the one with the `NavigationGrid2D`, else the first `Grid`), draws the generator's own inspector and the preview, and runs Generate after the GUI pass. |

The generators work in cells and know footprints only through `PropGridSnapping.CellsCovered`, the same rule the writer and `NavigationGrid2D` follow. `AreaGenerationTests` checks that a written layout is blocked by the navigation grid on exactly its footprint cells.

## The Clearing generator

One generation, in order:

1. **Grass**: the cells whose centres lie inside a superellipse of `size` (`squareness` 2 is an ellipse, higher is squarer). Its radius wobbles by up to `edgeWobble` with four random harmonics around it. Only the largest 4-connected region is kept. Every grass cell records its distance in steps (8 neighbours) to the nearest cell without grass.
2. **Stone road**: a Catmull-Rom spline through five points from beyond the west edge to beyond the east edge, each at a random height within `roadMeander` of the middle. Every grass cell whose centre lies within half of `roadWidth` of the curve is painted. A road cell outside the grass is dropped, so paths always lie on grass.
3. **Gravel paths**: `gravelPathCount` branches leave the middle half of the road, alternating north and south, and run out past the edge through a point that bends them sideways.
4. **Spawn**: on the road where it passes `spawnAlongRoad` (from -1 for the west edge to 1 for the east edge), or at the nearest road cell that has grass all around it for `spawnClearRadius` cells. If no road cell has that room, the spawn goes to the deepest grass cell, with a warning.
5. **Open ground**: blocking props are kept off the path cells and `pathClearance` cells beside them, which leaves a gap in the forest edge where a path leaves, and off the disc of `spawnClearRadius` around the spawn.
6. **Scatter layers**, in list order, so earlier layers take the room first. Each one tries the cells of its zone in random order: `Border` is the band `borderDepth` cells deep along the edge, `Interior` is everything inside it. Each try takes a random prefab from the layer, and stops once `count` props are placed. A prefab with a footprint collider is a blocking prop. Its footprint stands on the tried cell, as centred as its width allows, and every cell it covers must be free grass. A walk-through prop goes at a random point in its cell, which must be free and not next to a path. Every prop also keeps `spacing` from the other props of its layer.
7. **Mobs**: `mobCount` spawns of `mobPrefab` on cells inside the border band that the spawn can walk to (4-connected around the footprints, like the navigation grid's regions). Each one is away from props, at least `mobDistanceFromSpawn` from the spawn and `mobSpacing` from the other mobs. A warning reports any that did not fit.

All randomness comes from a `System.Random` seeded with the seed, and every loop walks ordered lists rather than hash sets, so a seed always gives the same layout. A generation takes about 15 ms.

The Clearing's asset has four scatter layers: dense trees along the edge, a dozen trees inside, bushes, rocks and a stump, and walk-through plants. `Plant_Fern` is not in the plant list: it was left out while its sprite still showed a sliver of the neighbouring tile, and that slice is now fixed. Adding it back changes what every seed generates, so regenerate the Clearing afterwards if you want it there.

### What the PlayMode tests rely on

The Clearing tests boot into the generated scene. `PlayerControlsPlayModeTests` builds a seven-cell wall two cells right of the spawn and walks around it, and also needs five clear cells in a straight line beside it: `spawnClearRadius` 6 gives that room. Mobs spawn at least 14 cells from the spawn, more than twice their detection radius, and the tests that watch one mob deactivate the others. Keep these margins when retuning the Clearing, or adjust the tests.

## Terrain layers and drawing order

| Layer | Tilemap | Rule tile | Terrain type | Navigation priority | Sorting order (Terrain layer) |
|---|---|---|---|---|---|
| Grass | `GrassTilemap` | `grass_DualGridRuleTile` | `Terrain_Grass` | 0 | 0 |
| Gravel | `GravelTilemap` | `gravel_path_DualGridRuleTile` | `Terrain_Gravel` | 1 | 1 |
| Stone | `StoneTilemap` | `stone_path_DualGridRuleTile` | `Terrain_Stone` | 2 | 2 |
| Walls | `CollisionTilemap` (not generated) | `walls_DualGridRuleTile` | blocking | n/a | 10 |

Grass is painted under the paths too, so the grass shows through a path's ragged edge. Stone is drawn over gravel where they meet. Walls sit at order 10 in both areas so they draw over any ground layer, with room for more of them.

`TerrainMovement_Default` has no rules for stone or gravel, so both are walkable at the default cost of 10, the same as grass. Making roads cheaper would make units prefer them, but it would also lower the profile's minimum cost, which the A* heuristic uses as its step cost, and it would stop click-to-move from taking its straight-line shortcut across grass. Clicks would then bend toward the road. The terrain types are there for later uses such as footsteps or speed.

## Multiplayer

Generation happens in the editor, and its result is saved in the scene. Every machine loads the same terrain and props, and nothing is generated or replicated at runtime. Mobs are only spawn markers. The host spawns them as network objects through `AreaMobSpawner`, as in any area.

## Adding a generator

1. Subclass `AreaGenerator` with `[CreateAssetMenu(menuName = "TopDownRPG/World/...")]`. List the terrain layers it paints in `TerrainLayers` (bottom first) and fill one `AreaLayout` per call to `Generate(seed)`.
2. Keep it deterministic: one `System.Random` from the seed, and no iteration over hash sets where order matters.
3. Keep paths on terrain, the spawn open, footprints apart and every mob reachable from the spawn, and test those properties over many seeds as `AreaGenerationTests` does for the Clearing.
4. Create its asset under `Assets/Settings/Generation`. The window lists every `AreaGenerator` asset.
