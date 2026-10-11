# Scene generation

An editor tool that fills an area scene with generated content: dual-grid terrain layers, props and mob spawn points, plus the party's start spawn. The result is saved in the scene like hand-made content. There are two generators: the forest Clearing (`Area_Clearing.unity` was made with it) and the Village Forest, which joins a village, a clearing and a forest in one larger area (`Area_VillageForest.unity`).

## Using it

1. Open the area scene and make it the active scene.
2. Open **Tools > TopDownRPG > World > Scene Generator** and pick a generator asset: [Generator_Clearing.asset](../Assets/Settings/Generation/Generator_Clearing.asset) or [Generator_VillageForest.asset](../Assets/Settings/Generation/Generator_VillageForest.asset).
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
| [AreaGenerator](../Assets/Editor/World/Generation/AreaGenerator.cs) | Abstract `ScriptableObject`, one subclass per kind of environment. Holds the `Seed` the window uses, lists the `TerrainLayers` it paints (each one has a tilemap name, a `DualGridRuleTile`, a `TerrainType2D`, a navigation priority and a sorting order), and `Generate(seed)` returns an `AreaLayout`. `PropScatter` is the base of the generators' scatter layers: a label, a prefab list, a count and a spacing; each generator's subclass adds which part of its area the layer fills. |
| [AreaLayout](../Assets/Editor/World/Generation/AreaLayout.cs) | Plain data in one-unit cells: the filled cells of each terrain layer, `Props` (prefab plus cell: the bottom-left cell of a blocking prop's footprint, or the cell and an offset inside it for a walk-through prop), `Mobs` (prefab plus cell), `SpawnCell`, and `Warnings`. |
| [AreaLayoutBuilder](../Assets/Editor/World/Generation/AreaLayoutBuilder.cs) | Internal. The state and steps both generators share during one generation: the seeded random source, the layout with its grass, gravel and stone cells, how deep each grass cell lies inside the edge, and which cells are reserved (kept free of blocking props), covered by a footprint or hold a walk-through prop. Its steps: `LayGrass` (a wobbly superellipse, its largest 4-connected region and the edge distances), `WobblyShape`, `SampleSpline` and `Paint`, `PlaceSpawn`, `ReservePathClearance`, `ReserveSquare` and `ReserveDisc`, `ScatterProps` and `TryPlaceFootprint`, `MobCells` and `PlaceMobs`, `ReachableFromSpawn` and `FloodFill`. |
| [ClearingGenerator](../Assets/Editor/World/Generation/ClearingGenerator.cs) | The forest clearing, described below. Create menu: `TopDownRPG/World/Clearing Generator`. |
| [VillageForestGenerator](../Assets/Editor/World/Generation/VillageForestGenerator.cs) | A village, a clearing and a forest in one area, described below. `GenerateWithRegions(seed)` also returns the region of every grass cell, which the tests read. Create menu: `TopDownRPG/World/Village Forest Generator`. |
| [AreaLayoutWriter](../Assets/Editor/World/Generation/AreaLayoutWriter.cs) | `Write(grid, generator, layout)` writes a layout into the grid's scene as described above. Blocking props go to `PropGridSnapping.RootOnCells`, so their footprints cover exactly their cells, and walk-through props are snapped to the pixel grid. Mob spawns and the party spawn go to cell centres. |
| [SceneGeneratorWindow](../Assets/Editor/World/Generation/SceneGeneratorWindow.cs) | The window. It targets the active scene's grid (the one with the `NavigationGrid2D`, else the first `Grid`), draws the generator's own inspector and the preview, and runs Generate after the GUI pass. |

The generators work in cells and know footprints only through `PropGridSnapping.CellsCovered`, the same rule the writer and `NavigationGrid2D` follow. `AreaGenerationTests` checks that a written layout is blocked by the navigation grid on exactly its footprint cells.

## The Clearing generator

One generation, in order:

1. **Grass**: the cells whose centres lie inside a superellipse of `size` (`squareness` 2 is an ellipse, higher is squarer). Its radius wobbles by up to `edgeWobble` with four random harmonics around it. Only the largest 4-connected region is kept. Every grass cell records its distance in steps (8 neighbours) to the nearest cell without grass.
2. **Stone road**: a Catmull-Rom spline through five points from beyond the west edge to beyond the east edge, each at a random height within `roadMeander` of the middle. Every grass cell whose centre lies within half of `roadWidth` of the curve is painted. A road cell outside the grass is dropped, so paths always lie on grass.
3. **Gravel paths**: `gravelPathCount` branches leave the middle half of the road, alternating north and south, and run out past the edge through a point that bends them sideways.
4. **Spawn**: on the road where it passes `spawnAlongRoad` (from -1 for the west edge to 1 for the east edge), or at the nearest road cell that has grass all around it for `spawnClearRadius` cells. If no road cell has that room, the spawn goes to the deepest grass cell, with a warning. Both generators share this rule.
5. **Open ground**: blocking props are kept off the path cells and `pathClearance` cells beside them, which leaves a gap in the forest edge where a path leaves, and off the disc of `spawnClearRadius` around the spawn.
6. **Scatter layers**, in list order, so earlier layers take the room first. Each one tries the cells of its zone in random order: `Border` is the band `borderDepth` cells deep along the edge, `Interior` is everything inside it. Each try takes a random prefab from the layer, and stops once `count` props are placed. A prefab with a footprint collider is a blocking prop. Its footprint stands on the tried cell, as centred as its width allows, and every cell it covers must be free grass. A walk-through prop goes at a random point in its cell, which must be free and not next to a path. Every prop also keeps `spacing` from the other props of its layer.
7. **Mobs**: `mobCount` spawns of `mobPrefab` on cells inside the border band that the spawn can walk to (4-connected around the footprints, like the navigation grid's regions). Each one is away from props, at least `mobDistanceFromSpawn` from the spawn and `mobSpacing` from the other mobs. A warning reports any that did not fit.

All randomness comes from a `System.Random` seeded with the seed, and every loop walks ordered lists rather than hash sets, so a seed always gives the same layout. A generation takes about 15 ms.

The Clearing's asset has four scatter layers: dense trees along the edge, a dozen trees inside, bushes, rocks and a stump, and walk-through plants. `Plant_Fern` is not in the plant list: it was left out while its sprite still showed a sliver of the neighbouring tile, and that slice is now fixed. Adding it back changes what every seed generates, so regenerate the Clearing afterwards if you want it there.

### What the PlayMode tests rely on

The Clearing tests boot into the generated scene. `PlayerControlsPlayModeTests` builds a seven-cell wall two cells right of the spawn and walks around it, and also needs five clear cells in a straight line beside it: `spawnClearRadius` 6 gives that room. Mobs spawn at least 14 cells from the spawn, more than twice their detection radius, and the tests that watch one mob deactivate the others. Keep these margins when retuning the Clearing, or adjust the tests.

## The Village Forest generator

One larger area, ringed by forest, that joins three environments: a village of cottages along a stone street that enters from the map edge, an open clearing, and a dense forest crossed by winding gravel trails. One generation, in order:

1. **Grass**: as in the Clearing, from `size`, `squareness` and `edgeWobble`. The asset's 92 x 62 with squareness 4 is nearly rectangular.
2. **Regions**: the village is a blob of `villageSize` against the west or east edge (picked at random), moved inwards by up to three cells; the clearing is a blob of `clearingSize` on the other side of the middle, off to the north or south. Both are superellipses whose radius wobbles by `regionWobble`. Every grass cell in the band `borderDepth` deep along the edge, and every cell outside both blobs, is forest, so the forest rings the whole area and fills the rest.
3. **Street**: stone, `streetWidth` wide: a spline through four points from beyond the village's edge of the map, through the village's middle row, to the village's inner end, the junction. Each point is within `streetMeander` cells of that row. Keep the meander small: the cottages line up along the street.
4. **Spawn**: on the street at `spawnAlongStreet` (0 at the map edge, 1 at the junction), or the nearest point along it with grass all around for `spawnClearRadius` cells. The disc around it is kept free of blocking props from here on.
5. **Square**: a disc of stone of `squareRadius` opening south off the street at `squareAlongStreet`, around a bed of grass that holds the fountain's footprint with `pathClearance` cells around it, so the fountain keeps its clearance from the paving like any blocking prop.
6. **Buildings**: walking the street from the map edge towards the junction, a lot is tried at every column, and after one fits the next starts `lotGap` columns further on. A lot is the building's width plus `sideYard` columns on each side; from the street up it holds a row of clearance, the fence row, `yardDepth` rows of yard, the building, then `backYard` rows. The Cottage's sprite shows its front wall and porch, so its front is its south side and it faces the street. Every column of a lot must border the street, the lot must be grass off the paths, the edge band and the clearing, the building must stay out of the spawn's disc, and the lot must end `LotMargin` columns (half the trail width plus the clearance plus one) before the junction, where the trails start. A one-cell stone path runs from the street up to the doorstep, the cell in front of the door: `doorColumn` counts from the footprint's left, and 3 puts the Cottage's doorstep under its porch. The doorstep touches the building, so it is the one path cell allowed within a building's clearance. Every cell of a built lot joins the village, even where the village's wobbly outline fell short of it. A warning reports fewer than `buildingCount` buildings.
7. **Trails**: gravel, `trailWidth` wide. The first runs from the junction to the clearing's middle and on across it, away from the village. Then come `forestTrailCount` forest trails. Each aims at a forest cell at least `borderDepth + gladeRadius + 1` from the edge, picked at random among the eighth of them furthest from every path laid so far, and starts from the nearest point of the trails already laid. A trail bends about every nine cells, each bend pushed sideways by up to `trailWinding` of the distance between bends, unless that would take it into the edge band. A trail that would enter the village, come within `LotMargin` of a lot or leave the grass is dropped, the cells around its target are ruled out, and another target is tried, up to 24 times. Each forest trail ends in a glade, a disc of `gladeRadius` kept free of blocking props. Every trail starts on a path, so all the paths form one network. A path cell that touches that network only at a corner, left where the edge of the grass clipped a path, is then dropped.
8. **Open ground**: blocking props are kept off every path and the `pathClearance` cells beside it, and off the glades.
9. **Village furniture**: the fountain in the middle of the square; lamp posts about every `lampSpacing` cells along the street through the village, alternately north and south of it just beyond its clearance and outside the lots, so the northern ones stand in the gaps between yards rather than in their fences; a fence along each lot's front row, laid from either side of the gate outwards; `townPropsPerBuilding` town props in each yard, in front of and beside its building, and `townPropsAroundSquare` in a ring south of the square, each set `townPropSpacing` apart. The lots are then reserved, so the scatter layers put no blocking prop in a yard.
10. **Scatter layers**, in list order, each over the grass cells of its region, placed as in the Clearing.
11. **Mobs**: each region's count of `mobPrefab` (the clearing's, then the forest's, then the village's) on cells of that region the spawn can walk to, deeper than the edge band, not next to a blocking prop, at least `mobDistanceFromSpawn` from the spawn and `mobSpacing` from every other mob. A warning reports a region where fewer fit.
12. **Reachability**: once every prop stands, a warning names any path cell, doorstep or region the spawn cannot walk to (4-connected around the footprints, like the navigation grid's regions). The dense forest can seal off a few stray cells between trees, which matters to nothing; the paths, doors, mobs and regions are what is checked.

A generation takes about 120 ms in the editor, eight times the Clearing's: the area is four times larger and holds over 500 blocking props.

The asset ([Generator_VillageForest.asset](../Assets/Settings/Generation/Generator_VillageForest.asset), seed 1 in `Area_VillageForest.unity`) builds three Cottages with `Fence_Iron`, `LampPost_Vine` and the `Fountain`, and furnishes yards and square from barrels, pots, an urn, a table, stools, a bench and a cauldron. Its scatter layers are village trees, bushes and flowers; clearing trees, bushes and rocks, and plants; then forest undergrowth (bushes, dark rocks, a large stump), forest trees and forest-floor plants. The forest trees layer asks for 2000 at a spacing of 2.2, more than ever fit, so it fills every spot it can. A prefab listed twice in a layer is picked twice as often: the forest trees list the oak, the round tree and the tall pine twice, so the twisted trees, which read as dead wood from a distance, stay a minority. Mobs: three in the clearing, six in the forest, none in the village. Seed 1 places 803 props (568 blocking), which makes the scene file about 5 MB.

Like the Clearing, it keeps `spawnClearRadius` 6 and mobs at least 14 cells from the spawn, so it would satisfy the PlayMode tests' margins if it became the starting area.

`AreaGenerationTests` checks over 20 seeds that there are no warnings; that every region has at least 300 cells, the street crosses the village and trails reach the clearing and the forest; that the paths lie on grass, free of footprints, as one 4-connected network through the spawn; that the forest is more than three times as dense with footprints as the clearing; that at least 99% of the open ground of the village and the clearing, and 97% of the forest's, can be walked to from the spawn; that every configured building stands in the village with a stone doorstep the spawn can reach and no other path within its clearance; that there is one fountain, at least two lamp posts and a fence per building; that the spawn is on the street with open grass around it; that footprints are on grass, apart, and (buildings aside) clear of the paths' clearance; and that every region has its configured mobs, most of them in the forest, all reachable, far enough from the spawn and from each other.

## Terrain layers and drawing order

| Layer | Tilemap | Rule tile | Terrain type | Navigation priority | Sorting order (Terrain layer) |
|---|---|---|---|---|---|
| Grass | `GrassTilemap` | `grass_DualGridRuleTile` | `Terrain_Grass` | 0 | 0 |
| Gravel | `GravelTilemap` | `gravel_path_DualGridRuleTile` | `Terrain_Gravel` | 1 | 1 |
| Stone | `StoneTilemap` | `stone_path_DualGridRuleTile` | `Terrain_Stone` | 2 | 2 |
| Walls | `CollisionTilemap` (not generated) | `walls_DualGridRuleTile` | blocking | n/a | 10 |

Grass is painted under the paths too, so the grass shows through a path's ragged edge. Stone is drawn over gravel where they meet. Walls sit at order 10 in every area so they draw over any ground layer, with room for more of them.

`TerrainMovement_Default` has no rules for stone or gravel, so both are walkable at the default cost of 10, the same as grass. Making roads cheaper would make units prefer them, but it would also lower the profile's minimum cost, which the A* heuristic uses as its step cost, and it would stop click-to-move from taking its straight-line shortcut across grass. Clicks would then bend toward the road. The terrain types are there for later uses such as footsteps or speed.

## Multiplayer

Generation happens in the editor, and its result is saved in the scene. Every machine loads the same terrain and props, and nothing is generated or replicated at runtime. Mobs are only spawn markers. The host spawns them as network objects through `AreaMobSpawner`, as in any area.

## Adding a generator

1. Subclass `AreaGenerator` with `[CreateAssetMenu(menuName = "TopDownRPG/World/...")]`. List the terrain layers it paints in `TerrainLayers` (bottom first) and fill one `AreaLayout` per call to `Generate(seed)`. Build it on an `AreaLayoutBuilder` for the grass, painting, reservations, scatter, mobs and reachability, and derive its scatter layers from `PropScatter`.
2. Keep it deterministic: one `System.Random` from the seed (the builder's), and no iteration over hash sets where order matters.
3. Keep paths on terrain, the spawn open, footprints apart and every mob reachable from the spawn, and test those properties over many seeds as `AreaGenerationTests` does for both generators. Add its asset path to the determinism and writer test cases.
4. Create its asset under `Assets/Settings/Generation`. The window lists every `AreaGenerator` asset.
