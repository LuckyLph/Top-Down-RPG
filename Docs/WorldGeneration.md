# World generation

Plan for generating seamless worlds in the editor, starting with the first world, the Overworld: the six generated areas brought back as its regions, on an island, joined by roads through wild grassland.

- A world generator composes a whole world from region generators (the six `AreaGenerator`s, unchanged) and world-level steps (the ground between the regions, the roads joining them, the sea around them), the same settings always giving the same world, and writes it into the world's chunk scenes.
- Generation runs in the editor only. The chunk scenes hold the result like hand-painted ones, so streaming, drawing, navigation, mobs, saves and networking work on it as they work on Testlands.
- Regenerating keeps saves meaningful and hand work safe: ids derived from what they name, edit layers that are only appended to, locked chunks, and chunks painted by hand since their last write left alone.

[Architecture.md](Architecture.md) describes what exists, [WorldStreaming.md](WorldStreaming.md) the worlds this writes into, and [SceneGeneration.md](SceneGeneration.md) the six generators. This file describes where world generation is going. Tick off the phase checklist as steps land.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Generation happens in the editor and its result is scene content that every machine loads the same way; nothing is generated, seeded or replicated at runtime (see Behaviour: Multiplayer).

## Where we start

- **Six area generators** (`TopDownRPG.Editor`, `Assets/Editor/World/Generation`): the Clearing, the Village Forest, the Lakeside, the Snowfield, the Coast and the Lava Fields. Each `Generate(seed)` returns an `AreaLayout` in cells around the origin, laid on an `AreaLayoutBuilder`:
  - the ground is a wobbly superellipse (`LayGround`), its largest 4-connected region, every cell knowing its distance to the missing ground beyond its edge, with a band of trees `borderDepth` (3) deep along that edge;
  - the roads run from beyond the west edge to beyond the east edge and are clipped to the ground, so they end at the outline; the Clearing's gravel paths also leave north and south, and the Village Forest's street enters from its west or east edge only;
  - the Coast's ocean runs north out to its map's edge, so its beach faces down the screen and the sunken ocean shows no bank (a sunken layer shows its bank only along edges that face up the screen);
  - the mobs stand inside the band, at least 14 cells from the spawn cell, which only shapes the layout and is written nowhere; the Clearing keeps 6 cells around it open, the margins the PlayMode tests rely on;
  - patches keep two cells inside the map's edge, and a layer beside a lower one of another look keeps a cell from it, since the lower one shows under the upper one's rounded corners where the two meet a third material (Architecture.md's Known gaps).
- **Their assets** (`Assets/Settings/Generation`) hold seed 1 and set no props, so they lay terrain, the Coast's plateau and mobs only, and no campsite, building or square. Their sizes run from 46 x 32 (the Clearing) to 92 x 62 (the Village Forest), about 19 000 cells in all, under five chunks' worth. One generation takes 8 to 80 ms; the six together about 0.3 s. Their seed 1 layouts lay seventeen of the eighteen materials: plaza comes only with the Village Forest's square, laid only with a fountain. The Lakeside lays twelve on its own.
- **The writer** (`AreaLayoutWriter`) writes a layout into a standalone grid only (a scratch scene) and refuses a world chunk's. It replaces every child of `Props` and every `MobSpawnPoint` in the scene, gives no `PersistentId`, and is one undo step. The six area scenes are gone (WorldStreaming.md's Phase 9).
- **Worlds are painted by hand** with the World Editor and the Terrain Brush. `WorldAuthoring.Paint`, `PaintWalls` and `PaintRaised` write world-local cells into the open chunk scenes that own them, adding a material's layer where it is missing and to each open neighbour whose 4-cell skirt the cells reach. The whole-world steps (`SyncLayerLists`, `BakeCoastMaps`, `PersistentIds.Repair`, `WorldValidation`) open every chunk scene of the world at once.
- **Chunk scenes** hold tilemaps as YAML, about 270 bytes per tile, so a layer filling a 64 x 64 chunk is about 1.1 MB; Testlands' chunks are 1.2 to 1.3 MB. A chunk loads in 35 to 46 ms in the editor, mostly the scene's activation; a player build is unmeasured.
- **Layers.** A renderer draws up to 16 layers narrow and up to 32 in its wide variant, which costs about twice the narrow one in crisp and 1.35 times in soft at every pixel ([TerrainLayers32.md](TerrainLayers32.md)). A chunk lists the materials of its own cells and of its neighbours' 4-cell border strips, in the style's order.
- **The coast map** is baked per chunk from the whole world (`WorldCoastBake`, reading 40 cells around the chunk): about 0.4 s for a chunk inside a large world, under two minutes for 16 x 16, and only for the chunks whose sea changed.
- **What saves name.** Killed mobs by their spawn point's `PersistentId`, a GUID the editor makes at random; terrain edits by the index of the chunk's `AreaTerrain` layer, so a chunk's layers may only be appended to; discovered waystones by entry name; players' places by world id and position, checked only against the world's chunks.
- **Worlds.** Testlands (the starting world, entry `start`), the Sandbox and its interior, and the dev stress world hold the global layout's slots at (6144, 0), (2048, 0), (4096, 0) and (0, 0). The PlayMode tests boot into Testlands by its scene path, or start from the menu into GameScenes' starting world.
- **Tests.** `AreaGenerationTests` checks the six generators over 20 seeds and the writer on a standalone grid; `WorldBrushStrokeTests`, `WorldCoastBakeTests` and `ChunkedGroundRenderTests` cover painting across borders, the coast bake and drawing per chunk against drawing whole.

## Goals

- A world generator in the editor composes a world of up to 16 x 16 chunks from region generators and world-level steps, deterministically, previews it, and writes it into the world's chunk scenes.
- The Overworld: the six regions at their seed 1 layouts, cell for cell, on an island, joined by roads, with every one of the eighteen materials laid, every chunk within 16 layers, a waystone in each region, and the Overworld as the starting world.
- Ground, raised ground, navigation and mobs work across region and chunk borders: every chunk draws the same as the world drawn whole, and every waystone and mob can be walked to from the default entry.
- Regenerating is safe: a save from before still means the same mobs, edits, waystones and places, hand-placed objects survive, locked chunks and chunks painted by hand since their last write are never overwritten unasked, and only the chunks whose content changed are written.
- Fast enough to iterate on (Decisions: Targets), measured in Phase 0 and checked again once the tool exists.

## Non-goals

- Generating at runtime, or a world per save from a seed. Every world ships as chunk scenes.
- Props, buildings and interiors: there is no prop art yet, and only art generated in this project is allowed. The writer is designed for props, but places none.
- Region generators that fill a shape the world gives them (see Open questions). The six keep laying their own islands.
- Regions placed by rules. The Overworld's are placed by hand.
- Transition materials, rivers or cliffs between regions.
- Changes to the six generators' layouts or to `AreaGenerationTests`.
- New terrain materials, and exits from the Overworld.
- A world map, or region names shown in game.

## Decisions

| Topic | Decision |
|---|---|
| When generation runs | In the editor, written into chunk scenes, like everything so far. Runtime generation would need identical floating-point results on every machine (`Mathf.Sin`, `Pow` and `Atan2` can differ between Mono and IL2CPP or across CPUs, and one flipped cell desyncs navigation), the seed and the generator's version in the handshake, generation and a coast map on every chunk load (up to 80 ms and 115 ms), and the generators moved out of the Editor assembly, and would rule out hand painting. Baked, nothing changes at runtime and the build version already refuses a mismatched build. A hybrid (baked compact data the chunk writes into its tilemaps as it loads, as `StressTestMap` does) is kept for later, only if scene sizes or player-build loads call for it; in Phase 0's player build neither did (a chunk's scene is 150 to 180 KB there and activates in under a millisecond). |
| The first world | `Overworld`, at the first free slot of the global layout ((8192, 0) today), 5 x 4 chunks (320 x 256 cells), as Phase 0 settled it: the six regions fit with their gaps, a road round the island's west and east sides and at most 14 materials in a chunk. 4 x 4 also fits (13 at most), but leaves no room for the west road and runs the east road 6 cells from the beach. The generator is built and timed for 16 x 16. |
| Regions | Each region is one of the six generators' layouts from its seed (seed 1, the generator asset's), laid around the origin as in a scratch scene and moved to its place, cell for cell. The world owns everything outside the regions' outlines and the one thing it adds inside them, a waystone's plaza. A region's shape is its generator's outline, not one the world gives it. |
| Placement | By hand, in the world generator asset: each region's generator, key, seed and cell. The Overworld's placement and road links are in Behaviour: The Overworld, as Phase 0 settled them against the layer cap and the gaps. |
| Between regions | The wilds: grass laid by the world on every cell of the land outside the regions, with patches of weeds, meadow and tall grass, a few mobs and no paths of its own. Forest props come with prop art. |
| How regions meet | Each region keeps its outline and its band of trees and meets the wilds' grass there, with at least `regionGap` (12) cells of wilds between two regions and `regionShoreGap` (6) between a region and the beach. A region whose ground is not grass (the Snowfield's snow, the Lava Fields' dirt) meets the grass with no grass laid under its edge: the renderer already takes the lower layer to run on under the upper one where they meet, and Phase 0's captures with and without grass under the outer 3 cells, where a road crosses the edge, were the same pixel for pixel. One road network joins them: links between the regions' ports. |
| The sea | Around the whole land: the Overworld is an island. At least 16 cells of sea lie between any walkable cell and the world's edge, so the void beyond never shows (the view is 20 x 11.25 cells). The Coast region gives the north shore, as it was built to. Elsewhere the world lays a beach of sand between the land and the ocean, so the ocean meets only sand; along the south shore the land's bank shows above the water, as a pond's far bank does. The coast lies 20 cells from the world's edge, give or take 3 (so 17 cells of sea or more), wobbling as the Coast generator's lines do (sums of sine waves 12 to 36 cells long); along the side the Coast region lies on, it is held at the rows the region's coast holds at its sides (43 cells from the edge in the Overworld) and eases back to 20 over 40 cells beyond them, so the two beaches meet level. The beach is 4 to 7 cells wide, the Coast generator's widths. Grass lies under the beach, as under all land, and none under the ocean: the captures with grass under its first 2 cells and with none were the same, and grass under all of it doubles an open-sea chunk's scene (1.05 to 2.09 MB). |
| Layers per chunk | At most 16: a chunk's materials, with its neighbours' 4-cell strips, are counted in the composition and shown in the preview, and a chunk past 16 is refused. Regions that would push a chunk past it are kept apart by placement. A chunk may be allowed the wide variant only by an explicit entry in the generator asset. |
| The eighteen materials | All of them, in the style's order. The regions lay seventeen; the waystones' plaza discs lay the eighteenth. |
| Entries and waystones | A waystone per region at its spawn cell, named after the region's key, with a disc of plaza of radius 3 under the road there, which also gives waystones a visible place (at radius 2 only a row of slabs shows on either side of a 3-wide road; at 3 it reads as a small square). The Clearing's is first, so it is the default entry and the start of a new game; it keeps the PlayMode margins (6 open cells, mobs 14 or more away). A plain entry, a gate, on the world road just outside each linked port. The generator owns the entries it makes; entries made by hand stay. |
| Starting world | The Overworld, at `clearing`, from Phase 5. The PlayMode tests stay on Testlands: those that boot by path already do, and those that start from the menu get a test copy of GameScenes naming Testlands. |
| Exits and interiors | None in the Overworld yet. The Village Forest lays no buildings without building art; once it does, each doorstep's exit to an interior world is Later. |
| Persistent ids | Derived, not random: the first 32 hex digits of a SHA-256 of the world's id, the scope (a region's key, or the wilds and a chunk), the kind (`mob`) and the ordinal within that scope, the format of `PersistentIds.NewId`. A spawn point keeps its id through re-seeding and retuning, so a mob killed before stays dead at its new place. |
| Layer lists | Only `AreaTerrain`'s list, which saved edits index, is append-only: the writer appends a chunk's new ground layers to it and never removes or reorders an entry. A layer it lists stays in the renderer, even empty. The renderer's own list follows the style's order and may gain and lose layers. |
| Hand work | The writer never opens a locked chunk for writing, and refuses, unless forced, a chunk whose content differs from what it last wrote there (painted by hand since). It replaces only objects under its own roots (`GeneratedMobSpawns`, later `GeneratedProps`), so hand-placed exits, spawn points and props stay. Regenerating one region needs nothing more: the world is composed again and only the chunks whose content changed are written. |
| Determinism | One `System.Random` per stream, iteration over ordered lists only, as in the area generators: each region from its own seed, the roads from the world's seed, and the wilds per chunk from the world's seed and the chunk's coordinates, so a change in one place moves the wilds of at most the chunks around it. |
| Props | None: the regions lay terrain, raised ground and mobs. A layout's props are skipped with a warning (the project's generator assets lay none). |
| Tooling | A World Generator window (`Tools/TopDownRPG/World/World Generator`) over the world generator asset: the preview, and writing changed chunks followed by the World Editor's whole-world steps. The World Editor and the Scene Generator stay as they are; the Scene Generator is where a region's generator is tuned on its own. |
| Whole-world steps | They open every chunk scene of the world at once, as now, at every size up to 16 x 16, with no windows of chunks: Phase 0 ran them on 256 chunks (Sync layer lists 12 s, Bake every coast 48 s, Repair ids 12 s, Validate 12 s) with every chunk open in 16.6 GB of the editor's private memory, 31 to 34 MB a chunk, about half of it graphics memory, mostly each chunk renderer's own texture arrays. Windows come only if worlds grow past 16 x 16 or the editor has to run in 16 GB. The writer opens one chunk at a time. |
| Targets | Composing the Overworld in memory under 1 s (Phase 0: 0.4 to 0.7 s) and a 16 x 16 world under 10 s (4.6 s with 88 regions); writing a chunk that exists under 0.5 s (open, write, save, close: 0.14 s with its coast map baked, 0.43 to 0.48 s without) and under 1 s when it is created (0.35 to 0.53 s); the whole Overworld written, synced, baked, repaired and validated under 30 s once its chunks exist (15 to 19 s) and under a minute the first time (29 s); a 16 x 16 world under 4 minutes from nothing, its first bake included (3 to 3.8 minutes); scenes of 1.5 MB or less per chunk (1.05 to 1.42 MB). Phase 3 records what the tool reaches. |
| Branch | `feature/world-generation`, from `feature/world-streaming`, moved onto `main` once that branch merges. |

## Terms

- **World generator**: a `WorldGenerator` asset, a world's generation settings: its regions, road links, land and sea, wilds and chunk records.
- **Region**: one placed area generator's layout in a world. **Region key**: its name in the world, `clearing`, `villageForest`, `lakeside`, `snowfield`, `coast`, `lavaFields`.
- **Outline**: a region's edge, the cells of its ground (its generator's first layer) with a cell outside the ground among their 8 neighbours.
- **Land**: the world's cells inside the coast; **sea ring**: the ocean between the land and the world's edge.
- **Wilds**: the land outside every region.
- **Port**: a run of a region's path cells (gravel, plaza, stone) on its outline, where one of its roads runs out, with the side it faces. **Road link**: a world road from a port to another port or to a road already laid. **Gate**: the plain entry on a link just outside its port, named the region's key and the side (`clearingEast`).
- **Composition**: a world composed in memory (`WorldComposition`): its cells by material, raised ground, mobs, entries, each chunk's material list and the warnings.
- **Written hash**: a hash of what the writer last wrote into a chunk, kept in the world generator asset.
- **Locked chunk**: a chunk the writer never writes.
- **Derived id**: a persistent id computed from what it names, not drawn at random.

## Behaviour

### The Overworld

```
 ~~~~~~~~~~~~~~~~~ sea ~~~~~~~~~~~~~~~~~
 ~  Snowfield              Coast       ~
 ~  Clearing          Village Forest   ~
 ~  Lakeside            Lava Fields    ~
 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
```

North up, as Phase 0 placed it on 5 x 4 chunks (320 x 256 cells). The Coast lies on the north shore, its beach facing down the screen; snow and lava are on opposite corners; the Clearing, where a new game starts, lies beside the village. Each region's cell is where its layout's origin goes, in world-local cells:

| Region | Cell | Covers (x, y) |
|---|---|---|
| `snowfield` | (91, 175) | 58 to 120, 153 to 197 |
| `coast` | (200, 200) | 167 to 231, 177 to 222 |
| `clearing` | (86, 124) | 61 to 108, 108 to 140 |
| `villageForest` | (200, 126) | 152 to 245, 95 to 157 |
| `lakeside` | (91, 59) | 56 to 122, 36 to 82 |
| `lavaFields` | (186, 59) | 152 to 217, 36 to 82 |

The Lakeside, with the most materials (twelve, thirteen with its plaza), shares its chunks only with the Clearing, which adds none, and the sea, which adds the ocean. The east column starts at x 152, past the reach of the Lakeside's chunks (their neighbours' strips end at x 131), so the Lava Fields' dirt and lava never reach them, and the Snowfield starts at y 153, past the strips of the chunk row the Clearing shares with the Lakeside. Every chunk lists 4 to 14 materials, 14 in chunk (1, 0), and the wilds' weeds, meadow and tall grass add none to it. The north coast is held at the Coast region's rows (The sea).

Roads, stone and 3 cells wide unless gravel (2 wide), each leaving its port straight on its region road's rows:
- the Clearing's east port to the Village Forest's street, its only port, on one row;
- the Lakeside's east port to the Lava Fields' west port, on one row;
- the Snowfield's east port to the Coast's west port;
- the Clearing's north gravel path round the Snowfield's east side to that road, and its south gravel path round the Lakeside's east side to the Lakeside's road (neither the Snowfield nor the Lakeside has a port facing the Clearing);
- the Lava Fields' east port up the island's east side, past the Village Forest, to the Coast's east port;
- a road down the west side joining the Snowfield's, the Clearing's and the Lakeside's west ports, so every port is linked.

A player starts on the Clearing's road, inside its clear disc, at its waystone's plaza. Each region is as its seed 1 layout was, ringed by grass with weed, meadow and tall grass patches where its edge meets the wilds; walking out along a road leads through the wilds to the next region's gate and on to its waystone. Mobs stand in every region as its generator placed them and a few in the wilds. The sea surrounds it all; nowhere can a player see past the world's edge.

### Generating

In the editor, from the World Generator window:

1. **Compose.** The asset's settings give a composition in memory, previewed one pixel per cell with the chunk grid, each chunk's layer count (red past 16), the regions' outlines, ports, gates, waystones and mobs, and the warnings.
2. **Write changed chunks.** Every chunk whose composed content differs from its written hash, and which is neither locked nor painted by hand since, is written; the others are reported as unchanged, locked or changed by hand. Missing chunks are created first.
3. **Then the whole-world steps**, in order: Sync layer lists (covering locked neighbours' border materials), Bake changed coasts, Repair ids (which keeps derived ids, being unique), Validate. The window reports each step's lines and times.

Writing is not an undo step: it spans closed scenes. It asks first, listing the chunks it will write, and git is the way back.

### Regenerating

- **Changing a region** (its seed, its generator's settings, its place) changes its cells, the roads to its ports, its gates, the wilds patches of the chunks it covers or borders, and its mobs' places. Its mobs keep their ids by ordinal: a mob killed before stays dead at its new place, a mob past the old count is new, and a record for a mob no longer laid is never read again. Its waystone moves with its spawn cell and keeps its name, so it stays discovered.
- **Changing the wilds or the sea** changes only the chunks whose cells change: the wilds' streams are per chunk.
- **Locking a chunk** keeps it exactly as it is, hand work included. Its neighbours are still written, and their seams with it are drawn as the skirt finds them; the preview marks the locked chunks.
- **A chunk painted by hand** without being locked is refused at the next write and named, so it can be locked, or forced to be regenerated.
- **Objects placed by hand** (exits, extra spawn points, props) are never touched, locked or not.
- **Saves.** See Multiplayer: the host's save after a regeneration.

### Playing

Nothing is new at runtime. The Overworld is a world of chunk scenes like Testlands: it streams around each player, draws per chunk, joins its navigation across borders, spawns each chunk's mobs, and saves deaths, edits, waystones and places as WorldStreaming.md describes. The only runtime changes are Phase 5's: a saved place on ground that is no longer walkable moves to the nearest walkable cell, and GameScenes can be copied for tests.

### Multiplayer

- **Every machine loads the same chunk scenes.** The result of generation is in the scenes, so a host and its clients load the same ground, raised ground, spawn points and entries from their builds, as for a hand-painted world. Nothing about generation is seeded, sent or checked at runtime, so no difference in floating-point results or random numbers between machines can matter.
- **The build version.** World content is part of the build: a host and its clients must have the same chunk scenes. A build whose worlds changed ships as a new build version (Player Settings), as a protocol change does, and `JoinApproval` refuses any other version. Phase 5 bumps it for the Overworld. A content hash per world in the join payload, which would catch a forgotten bump, is an open question.
- **The host's save after a regeneration.** Only the host saves, and every key in its save survives a regeneration: worlds by id (the `WorldDefinition`'s GUID, which regenerating keeps), killed mobs by spawn point id (derived, so the same spawn point keeps its id), terrain edits by `AreaTerrain` layer index (appended to only), discovered waystones by entry name (the region's key) and places by position (moved to the nearest walkable cell when the ground there changed, Phase 5). A session continued after a regeneration keeps its dead mobs, edits, discoveries and players' places wherever they still apply. An edit lands on the cell it named, whatever the ground there now is; a discovery of an entry that is gone is ignored, since `WaystoneRules` only looks up the world's own entries.
- **A late joiner** loads the chunk scenes around its player like any client and receives the host's mobs and terrain edits per chunk as it reports each chunk ready (WorldStreaming.md's Networking). It generates nothing and needs nothing new.
- **A client in a region the host is not in.** Regions exist only in the editor; at runtime the Overworld is one world. A client in the Lava Fields while the host is in the Clearing is a client in another part of the same world: the host streams and simulates the chunks around it, owns its mobs, decides its waystone discoveries and respawns it at its nearest discovered waystone, as for any player apart from the host (WorldStreaming.md's Phase 7 and 9 checks).

## Architecture

### Pieces

All of it is editor-only code in `TopDownRPG.Editor`, under `Assets/Editor/World/Generation`, beside the area generators. The six generators, `AreaLayout`, `AreaLayoutBuilder`, `AreaLayoutWriter`, `GroundLayerMigration`, `AreaGroundSetup` and the Scene Generator are unchanged.

| Type | Role |
|---|---|
| `WorldGenerator` | `ScriptableObject` (create menu `TopDownRPG/World/World Generator`), a world's generation settings, under `Assets/Settings/Generation` (`WorldGenerator_Overworld.asset`): the `WorldDefinition` it writes, a world seed, the sea ring's depth, the coast line's wobble and the beach's widths, the region placements (`RegionPlacement`: key, `AreaGenerator`, seed, cell where the layout's origin goes), the road links (`RoadLink`: from a region's port, to another port or the nearest road, stone or gravel, width), the gaps (`regionGap`, `regionShoreGap`), the plaza's radius, the gates' distance, the wilds (patch counts and radii per chunk, mob prefab, mobs per chunk, distances) and a record per chunk (`ChunkRecord`: coordinates, written hash, locked, allowed wide). `Compose()` returns a `WorldComposition`. Editor-only data, edited by editor tools only. |
| `WorldComposition` | Plain data in world-local cells: the cells of each material, raised cells and ramps, mobs (prefab, cell, derived id, scope), entries (name, position, waystone), each region's outline, ports and gates, each chunk's material list (its cells and its neighbours' 4-cell strips) and content hash, and the warnings. |
| `WorldComposer` | Internal, the composition's steps in order (below), on the area generators' `AreaLayoutBuilder` helpers where they fit (`SampleSpline`, `Paint`, `PaintPatches`, `RemoveFragments`, `FloodFill`) and `CellComparer` for every set of cells. |
| `GeneratedIds` | Static: `For(worldId, scope, kind, ordinal)`, the derived id (Decisions). |
| `WorldGenerationWriter` | Writes a composition into the world's chunk scenes (below). `WriteChunk(grid, composition, coords)` writes one open chunk and is what the tests drive in memory; `Write(generator, composition, chunks, force)` opens, writes, saves and closes the chunk scenes one at a time and returns a line per chunk. |
| `WorldGeneratorWindow` | `Tools/TopDownRPG/World/World Generator`. A thin window over the steps above and `WorldAuthoring`'s whole-world steps, as the World Editor is: the asset picker and inspector, the preview, the chunk grid with each chunk's layer count, lock and wide allowance, the warnings, and Write changed chunks, Write selected chunks and Write every chunk, each asking first, then the whole-world steps. Disabled in play mode. Its only standing work is redrawing the preview when the asset changes. |

### Composition

The steps, in order:

1. **Regions.** Each placement's generator lays its layout from the placement's seed (the generator asset's own by default) around the origin, and the composition moves every cell, raised cell, ramp and mob by the placement's cell. Its outline, its ports (with the side each faces) and its spawn cell are read from the moved layout. No world step writes inside an outline but the waystone's plaza.
2. **Land and sea.** The land is the world's bounds inset by the sea ring, its coast wobbling along each side as the Coast generator's lines do (sums of sine waves). Outside it lies ocean to the world's edge; along its edge, a beach of sand between the beach's two widths, so the ocean meets only sand, with grass under the beach and none under the ocean. Where the Coast region's outline meets the sea, its own ocean and beach take over: the world's coast along that side is held at the rows the region's coast holds at its sides and eases back to the ring's depth beyond them, and the world's beach beside the region takes the region's beach width at its side, so beach meets beach level and ocean meets ocean.
3. **Wilds ground.** Grass on every land cell outside the regions, the beach included.
4. **Roads.** Each link's course is the cheapest route over the wilds (higher costs near a region's outline, the beach and other roads), entering a region only at its port, and leaving the port straight on the rows its region's road holds at the outline for a few cells, so the two roads join without a step. The route is sampled into a spline through every eighth cell and painted at the link's width, as the regions' own roads are; where it meets another road, it joins it. A port no link names is a warning.
5. **Waystones and gates.** Each region's spawn cell becomes a waystone entry named by its key, the first placement's first in the list, so it is the default. A disc of plaza (the plaza's radius, 3) lies under it on the region's ground, under its road, as the Village Forest lays its square: any road cell inside the disc is paved too, under the stone. Each linked port's gate stands on its link `gateDistance` (2) cells outside the outline.
6. **Wilds detail**, per chunk on its own stream: weed, meadow and tall grass patches on wilds grass two cells or more from any outline, the beach and the roads, and a cell from each other, so each meets only grass; pieces under four cells are dropped.
7. **Wilds mobs**, per chunk on the same stream: up to the asset's count on wilds grass, at least 14 cells from every waystone and gate and 10 from every other mob, regions' included.
8. **Checks**, each a warning shown in the preview:
   - placement: every region inside the land, `regionGap` cells or more from every other region and `regionShoreGap` from the beach (the Coast region's sea side aside), on chunks the world has;
   - layer lists: every chunk at 16 materials or fewer, unless its record allows it wide;
   - reachability: every waystone, gate and mob connected to the default entry over walkable cells (the top material walkable by the movement profile, raised ground crossed only at a ramp's foot, as `RaisedGround.CanCross` rules);
   - ids unique, and every port linked.

### Writing

For each chunk to write, alone and with its grid inactive while it is written, so its renderer builds once:

- **Ground.** Its renderer's layers become the materials of its list, in the style's order, plus any layer its `AreaTerrain` lists, created as `GroundLayerMigration.AddLayer` creates them. Every ground layer is cleared first (`ClearAllTiles`, so no record of an old tile stays) and filled with its material's tile in exactly the composition's cells of the chunk. A layer neither listed nor in `AreaTerrain` is removed. The raised tilemap is set up and written like the area writer's (`AreaGroundSetup.SetUpRaised`), or removed when the chunk has no raised ground.
- **`AreaTerrain`.** `CreateChunk` lists the grass layer (and the walls, when the chunk has them). The writer appends each base ground layer it creates (snow, dirt), never removes or reorders an entry, and points each entry's fill tile at its layer's tile.
- **Mobs.** Every `MobSpawnPoint` under `GeneratedMobSpawns` (created when missing) is destroyed, then one is made per mob of the chunk at its cell's centre, with its prefab and its derived `PersistentId` set before the editor's change events reach `PersistentIdWatcher`, which keeps an id that is set and unique. Spawn points elsewhere in the scene are left alone.
- **Records.** The chunk's written hash, over what it wrote (each ground layer's material and cells, the raised cells and ramps, the generated spawn points), goes into its `ChunkRecord`. Before a later write, a chunk whose composed hash equals its record is skipped without being opened, and one whose current content no longer hashes to its record is refused unless forced.
- **The world.** The generated entries are set first in the `WorldDefinition`'s list, in composition order; entries made by hand keep their order after them. A chunk the world lacks is created first (`WorldAuthoring.CreateChunk`), so it is in the build and the definition.

The writer refuses to run in play mode, and refuses a composition with warnings about layer lists or reachability unless asked to write anyway.

### Runtime changes

Only two, both in Phase 5:

- **Places that are no longer walkable.** Once the chunks around a saved place are up (`IWorldSession.PrepareAsync` and `EnterAsync` already wait for them), a place on a cell its world's navigation cannot walk moves to the nearest walkable cell, or to the player's nearest discovered waystone when none lies within the search radius. That removes the Known gap "Players' places are checked only against chunks".
- **A test copy of GameScenes.** An `internal` copy naming another starting world and entry (`Object.Instantiate` of the asset, destroyed with the test), registered into the Main scope by `SceneBootTestHelper` beside the save store it already enqueues. The asset itself is never written.

### Generating a world

1. Create the world in the World Editor: its name, its size in chunks, Create every chunk and List in Game Scenes ticked.
2. Create its world generator (Create > TopDownRPG > World > World Generator) under `Assets/Settings/Generation` and point it at the world. Add a placement per region (key, generator asset, seed, cell), the road links, and the sea and wilds settings.
3. Open `Tools/TopDownRPG/World/World Generator`, pick the asset, and move the placements until the preview shows no warnings: every chunk at 16 layers or fewer, every port linked, everything reachable.
4. Write changed chunks. Each chunk is reported as written, unchanged, locked or changed by hand; then the whole-world steps run.
5. Look at the result with the World Editor's Open with neighbours. To keep hand work in a chunk, paint it, then lock it in the window before writing again.
6. Check `git diff` (a second write must change no file), then commit the chunk scenes, their coast maps, the `WorldDefinition` and the generator asset together.

## Phases

### Phase 0: spike

Throwaway code, in the scratchpad or a scratch folder, never committed; the scratch world stays out of the build and of `GameScenes`. The notes record the numbers and the decisions.

- [x] Compose the draft Overworld in memory: the six seed 1 layouts moved into place on a 5 x 4 world, grass between them, an island coast and straight roads between the ports. Time it, and list each chunk's materials with its neighbours' 4-cell strips.
- [x] A synthetic 16 x 16 world (the six regions repeated, grass between): composing time and memory.
- [x] Write both into chunk scenes of a scratch world. Measure:
  - the time per chunk to create, open, write, save and close;
  - each chunk's scene size by kind (wilds, region, shore, open sea), with and without grass under the open sea and under the non-grass regions' edges;
  - the whole-world steps (Sync layer lists, Bake every coast, Repair ids, Validate) on 20 and on 256 chunks, and the editor's memory with all of them open.
- [x] Looks, captured in crisp and soft for sign-off before Phase 1 builds them:
  - snow and the Lava Fields' dirt meeting the wilds' grass, with and without grass under their outer cells, where a road crosses;
  - the world's beach on the west and south shores (the bank), and where it joins the Coast region's beach;
  - a waystone's plaza under a road;
  - a world road joining a region's road at its port.
- [x] One generated chunk in a Windows development build: its size in the build and its load and activation time (the Known gap "Chunk loads hitch in the editor" says a player build is unmeasured).
- [x] Decide, in Decisions:
  - the world's size, the placement and the road links;
  - the sea ring's depth and the beach's widths;
  - how far grass goes under the ocean and under a non-grass region's edge;
  - whether the whole-world steps must work over windows of chunks for 16 x 16;
  - the targets.

Notes (the spike's code was not kept; editor times are debug code with the editor in the background, best of 2 or 3):

- **The draft Overworld**, composed in memory on 320 x 256 cells: the six seed 1 layouts moved by hand (Behaviour: The Overworld), grass on the rest of the land, the island's coast and beach, straight roads with hand-placed bends between the ports, and each waystone's plaza. It took 0.4 to 0.7 s, 0.36 to 0.46 s of it the six generations; the world's own steps (land and sea, roads, plazas, checks, chunk lists) took about 0.1 s. Every chunk lists 4 to 14 materials with its neighbours' strips, 14 in chunk (1, 0): the Lakeside's twelve, the ocean and its plaza. Adding the wilds' weeds, meadow and tall grass to every chunk with wilds raises sixteen chunks by 1 to 3 and none past 14. All eighteen materials are laid, no region comes within 12 cells of another or 6 of the beach, and 17 cells of sea or more lie between the land and the world's edge. A 4 x 4 world in the same arrangement fits too (13 at most), with no room for the west road and the east road 6 cells from the beach.
- **Ports.** Each region's stone road leaves by its west and east edges, but the Village Forest's street, its only port, leaves by the west (its trails stop inside), so the Lava Fields' and the Coast's east roads cannot reach it and join each other instead. The Snowfield and the Lakeside have no port facing the Clearing, so the Clearing's gravel paths go round them to the nearest road. A port is a run of 2 to 6 outline cells; one column inside the outline its region's road is 3 cells wide, 4 where it bends, with a stray cell or two where the outline clips it.
- **A 16 x 16 world** (88 regions: the five inland ones repeated on a grid 120 x 88 cells apart, a row of Coasts along the north, straight roads between neighbours in a row, the top chunk row open sea): composed in 4.6 s, 3.9 s of it the 88 generations and 0.66 s the world's steps (land and sea 0.37 s, the checks 0.13 s), or 0.7 to 0.9 s with each generator's layout generated once and copied to its placements. The composition held 27 MiB: 7 MiB of per-cell arrays (a bit per material, the region and the land of each cell), the rest the 88 layouts. The same cells as a set of cells per material would take 42 MiB. Its chunks list 13 materials at most.
- **Writing chunk scenes**, into scratch worlds under `Assets/Dev` at the first free slots ((8192, 0) and (10240, 0)), one chunk at a time: create the chunk (`WorldAuthoring.CreateChunk`), open it, fill it with its grid inactive (each material's layer added through `GroundLayerMigration.AddLayer`, cleared and filled in one batch; raised ground; `AreaTerrain` appended to; generated spawn points with derived ids), activate the grid, save, close. Median ms per chunk:

  | | create | open | fill | activate | save | close | in all |
  |---|---|---|---|---|---|---|---|
  | first write, the Overworld | 255 | 4 | 21 | 137 | 121 | 2 | 526 |
  | first write, 16 x 16 | 219 | 4 | 16 | 8 | 97 | 1 | 350 |
  | rewrite, coast maps not baked | | 178 | 11 | 148 | 140 | 1 | 430 to 482 |
  | rewrite, coast maps baked | | 25 | 8 | 7 | 100 | 0 | 140 |

  Activating is the chunk renderer's build: 5 to 19 ms without ocean, 120 to 155 ms with it while its coast map is not baked, since it then builds one live. Opening a written chunk builds it too. The 16 x 16 world was written in 97 s, 306 MB of scenes.
- **Scene sizes**, about 270 bytes per tile whatever the material:

  | kind | as decided, no grass under the ocean or the edges | with grass under all the ocean |
  |---|---|---|
  | wilds and region | 1.06 to 1.39 MB | the same |
  | region and shore | 1.16 to 1.42 MB | 1.52 to 1.84 MB |
  | shore | 1.17 to 1.24 MB | 1.56 to 1.79 MB |
  | open sea | 1.05 MB (4096 tiles of ocean) | 2.09 MB |

  Grass under the non-grass regions' outer 3 cells adds about 0.03 MB to a chunk holding such an edge, and grass under the first 2 cells of ocean about 0.05 MB to a shore chunk (the 16 x 16 world was written with both, the Overworld with and without). No chunk of the Overworld is all wilds; the 16 x 16 world's mostly wild chunks are 1.06 to 1.1 MB.
- **The whole-world steps**, best of 2 (in brackets the first run, which baked every map for the first time and so built live coast maps as it opened the chunks):

  | | 20 chunks | 256 chunks |
  |---|---|---|
  | Sync layer lists | 1.1 s (2.4) | 12.2 s (20.8) |
  | Bake every coast | 7.6 s (13.7), 14 maps | 47.6 s (81.7), 74 maps of about 475 ms each |
  | Repair ids | 1.1 s (1.2) | 11.7 s (14.3) |
  | Validate | 1.2 s (1.3) | 11.7 s (11.8) |

  Opening every chunk takes most of each step but the bake: 1.1 s for 20, 9.4 s for 256 (18 s before the maps were baked). Bake changed coasts, after the north coast moved, baked the 7 chunks whose sea had changed in 6.9 s and kept the other 13.
- **Memory with every chunk open.** 20 chunks took the editor's private memory from 7.7 to 8.4 GB; 256 chunks from 8.6 to 16.6 GB, 31 to 34 MB a chunk: 17 MB of Unity's own allocations and 16 MB of graphics, mostly each chunk renderer's texture arrays, which are built per renderer and not shared between renderers listing the same materials. 8.2 GB of the machine's 32 GB stayed free, so the run went on to the end; closing the chunks gave the memory back.
- **Looks**, captured in crisp and soft at one screen pixel per art pixel, at 1.5 s and 2.75 s, each from a later editor update than the build that drew it, and signed off:
  - snow and the Lava Fields' dirt meeting the wilds' grass where a road crosses the edge, at four ports: the same pixel for pixel with grass under the outer 3 cells and without (16 pairs);
  - the west and south shores, the south one showing the land's bank above the water, and both joins with the Coast region's beach: the same with grass under the first 2 cells of ocean and with none (16 pairs);
  - the joins with the Coast region's beach: with the north coast 20 cells from the edge, it dropped to the Coast's rows in 45-degree stairs beside the region; held at the Coast's rows (43 cells from the edge) and easing back over 40 cells, both joins are level, with a sand tongue 2 rows deep left at the west join, where the world's beach is wider than the region's at its side;
  - a waystone's plaza under a 3-wide road: at radius 2 a row of slabs shows on either side of the road, at radius 3 a small square (chosen);
  - world roads joining a region's road at a port: seamless where the region's road runs level at its outline; the steps beside the Snowfield's and the Lava Fields' ports are those roads' own wandering just inside the outline. The spike's bends were square corners, which the spline rounds.
- **A chunk in a Windows player** (development, Mono, uncompressed, D3D11 in batchmode so that no window took the focus; nothing rendered, so frame times are CPU only and the first draw of the ground is not measured). A bootstrap scene loaded chunk (1, 0) (14 layers, ocean with a baked coast map) and chunk (2, 2) (6 layers, no ocean) additively, again and again, and turned each one's ground on as `LocalWorldPresentation` does; best of 2 runs, ms:

  | | to loaded (0.9) | activation | ground (`SetDrawn`) |
  |---|---|---|---|
  | (1, 0), first load in the session | 41.0 | 38.3 | 54.0 |
  | (1, 0), later loads | 1.6 to 1.8 | 0.6 to 0.8 | 11.4 to 12.3 |
  | (1, 0), after `UnloadUnusedAssets` | 16.3 to 17.0 | 0.9 | 12.5 to 13.3 |
  | (2, 2), later loads | 1.4 to 1.7 | 0.5 to 0.8 | 2.6 to 3.0 |
  | (2, 2), after `UnloadUnusedAssets` | 13.5 to 15.0 | 0.6 | 2.6 to 2.7 |

  Loading is spread over frames (the worst 0.1 to 1.2 ms, 3.6 ms on the very first), activation lands in one frame and the ground in the next (in the game the two share a frame), and unloading takes 0.2 to 0.3 ms. In the build, chunk (1, 0)'s scene is 180 KB against 1.44 MB of YAML, and its coast map, stored uncompressed (592 x 592, 16 bits), another 701 KB; chunk (2, 2)'s scene is 152 KB. What every chunk shares (the terrain shader, 3.4 MB, and the materials' textures, 22 MB) is stored with the first chunk scene and loaded once a session. The only error was the one each load of a chunk on its own logs, a chunk scope with no world scope above it; the ground built on every load (16 quads with their control map).
- **What surprised.**
  - Grass under a non-grass edge or under the ocean changes nothing on screen: the renderer already takes a lower layer to run on under an upper one where they meet, so the third look the plan guarded against does not show at these edges. Only the scenes grow.
  - The draft expected the Lakeside's chunks to list the Lava Fields' dirt and lava; placing the east column past their reach keeps both out, so they list 14, not 16.
  - In a player the scene's activation is no hitch at all (under 1 ms against the editor's 18 to 26 ms); the ground's build is, growing with the chunk's layers: 3 ms for 6 layers, 12 ms for 14, close to a 60 Hz frame, and 54 ms for the first chunk of a session, which belongs behind the loading screen. The scene files are small in a build, so the hybrid of compact data written into tilemaps at load is not needed; a shore chunk's coast map is the biggest thing it ships.
  - Opening a chunk scene builds its renderer, so rewriting a chunk whose coast map is not baked builds a live coast map twice (about 140 ms each); with baked maps a rewrite takes 140 ms in all.
  - The spike's writer made its new objects (the generated spawn points' root) in the active scene and then moved them into the chunk's, which left the open scratch scene marked changed, and a later build stopped on Unity's "save the modified scenes?" dialog. Phase 2's writer makes each chunk's scene the active scene while it writes it.
- **Decisions** (in Decisions, with their reasons):
  - the world: 5 x 4 chunks, the placement and the eight road links of Behaviour: The Overworld;
  - the sea: the coast 20 cells from the edge, give or take 3, held at the Coast region's rows along its side and easing back over 40 cells; a beach 4 to 7 cells wide;
  - grass under the ocean and under a non-grass region's edge: none;
  - the whole-world steps: all of a world's chunks open at once up to 16 x 16, no windows;
  - the targets, set from the numbers above; and the waystone's plaza at radius 3.

### Phase 1: composing a world

- [ ] `WorldGenerator`, `RegionPlacement`, `RoadLink`, `ChunkRecord`, `WorldComposition` and `WorldComposer`, with steps 1 to 8 of Architecture: Composition.
- [ ] Regions moved into place cell for cell, with their outlines, ports and spawn cells.
- [ ] Land, sea and the world's beach, joined to the Coast region's coast.
- [ ] Wilds ground.
- [ ] Roads between ports.
- [ ] Waystones with their plazas, gates, and the default entry.
- [ ] Wilds detail and mobs, per chunk.
- [ ] Derived ids (`GeneratedIds`).
- [ ] The checks and their warnings.
- [ ] Tests: `WorldGeneratorTests` (see Testing).
- [ ] Docs: Architecture.md's Editor tools (Generation/) and Tests.

### Phase 2: writing into chunk scenes

- [ ] `WorldGenerationWriter`: ground layers, raised ground, `AreaTerrain` appended to, generated spawn points with derived ids, the generated entries, missing chunks created.
- [ ] Written hashes: unchanged chunks skipped without opening, locked chunks never written, chunks changed by hand refused unless forced, objects outside the generated roots kept.
- [ ] The whole-world steps after a write, with Repair ids keeping derived ids.
- [ ] Tests: `WorldGenerationWriterTests`, and the generated case of `ChunkedGroundRenderTests` (see Testing).
- [ ] Docs: Architecture.md's Editor tools (Generation/ and Worlds/), Tests and Known gaps; SceneGeneration.md's "No area scenes" paragraph and Multiplayer section.

### Phase 3: the World Generator window

- [ ] `WorldGeneratorWindow`: the preview, the chunk grid with layer counts, locks and wide allowances, the warnings, writing changed, selected or every chunk, then the whole-world steps, with each step's time.
- [ ] The targets measured on the draft Overworld and a synthetic 16 x 16 world, recorded in the notes.
- [ ] Docs: Architecture.md's Editor tools, and this file's Generating a world checked against the window.

### Phase 4: the Overworld

- [ ] The world created with the World Editor at the first free slot, listed in `GameScenes` but not yet the starting world; its generator asset set from Phase 0's decisions; composed with no warnings, written, its coasts baked, its ids repaired and validated; the scenes checked for churn (a second write changes nothing).
- [ ] Every region edge, shore, plaza and road join looked at in the editor, in crisp and soft, and signed off.
- [ ] Tests: `OverworldTests` (EditMode) and `OverworldPlayModeTests` (PlayMode).
- [ ] Multiplayer Play Mode check with 2 and 4 players (steps added to this phase's notes): players spread over regions, a client in a region the host is not in, a late joiner in another region, respawns at different regions' waystones.
- [ ] Docs: Architecture.md's World and Worlds and streaming (the Overworld), and WorldStreaming.md's Decisions (The first world) and Authoring (Generators).

### Phase 5: the starting world and saves across regenerations

- [ ] GameScenes' starting world becomes the Overworld at `clearing`.
- [ ] The PlayMode tests that start from the menu (Host Game, New Game, Continue) pin Testlands through the test copy of GameScenes.
- [ ] A saved place on ground that is no longer walkable moves to the nearest walkable cell once its chunks are up, or to the player's nearest discovered waystone (Architecture: Runtime changes), with the Known gap removed.
- [ ] Tests: a `WorldPersistencePlayModeTests` case, a place saved on water starting on the nearest walkable cell.
- [ ] The build version bumped (1.4 to 1.5): the worlds changed, and a host and client must agree on them.
- [ ] Docs: Architecture.md (GameScenes, Save data, Known gaps), SceneGeneration.md's "What the PlayMode tests rely on".
- [ ] The full EditMode and PlayMode suites pass before merging into `main`.

### Later

- Region generators that fill a shape the world gives them, so regions meet with no wilds, and placement by rules, for later worlds.
- Transition materials (dry grass towards the Lava Fields, patchy snow towards the Snowfield), rivers and cliffs between regions.
- Props, once there is prop art: the regions' scatter layers through the writer under `GeneratedProps`, which `WorldValidation` and the Prop Placement window learn to read, footprints kept inside one chunk, and the wilds' forest.
- Interiors: each Village Forest doorstep an exit to an interior world, once buildings have art.
- A marker for waystones on their plazas.
- A content hash per world in the join payload.
- The hybrid of compact chunk data written into tilemaps at load, if scene sizes or player-build loads call for it.

## Testing

- **EditMode:**
  - `WorldGeneratorTests`, on compositions of small worlds of the project's generators:
    - determinism: the same settings give the same composition, cell for cell and in order (cells, mobs, ids, entries, chunk lists), over several world and region seeds, and another seed another one;
    - locality: re-seeding one region changes only the chunks its cells, roads and gates touch and their neighbours, and retuning the wilds of one chunk only that chunk and its neighbours;
    - regions: each region's cells, raised ground and mobs are its generator's seed layout moved by its cell, plus only its plaza;
    - placement and adjacency rules: a region outside the land, within `regionGap` of another, within `regionShoreGap` of the beach or on a missing chunk is warned about; the Coast region's ocean joins the sea and its beach the world's;
    - continuous ground: every land cell has ground, every cell of the sea ring is ocean, the beach lies between them all round, and across every chunk border the ground runs on without a gap;
    - layer lists: each chunk's list (its cells and its neighbours' 4-cell strips) is in the style's order and within 16, a chunk past 16 is warned about unless allowed wide, and the eighteen materials are all laid;
    - reachability: every waystone, gate and mob connected to the default entry; ports linked; roads and gates on walkable ground;
    - persistent ids: derived ids are unique within the world, and stay the same when a region is re-seeded or moved, the wilds retuned, or the world composed again.
  - `WorldGenerationWriterTests`, writing in memory into chunk grids (`WriteChunk`), and once on disk into chunk scenes in a temporary folder (`Write`), deleted after:
    - each chunk's layers in the style's order with its neighbours' border materials, each layer holding exactly its composed cells and tile, raised ground and ramps, and its mobs under `GeneratedMobSpawns` with derived ids that `PersistentIdWatcher` keeps;
    - `AreaTerrain` only appended to: an existing list keeps every entry and index, a new base ground layer is appended, a listed layer stays when empty;
    - a locked chunk is never opened, a chunk changed since its last write is refused unless forced, an unchanged chunk is skipped, objects outside the generated roots and entries made by hand are kept;
    - a second write changes no file;
    - a written world passes `WorldValidation` and bakes its coasts.
  - `ChunkedGroundRenderTests`: a 2 x 2 block of generated chunks around a non-grass region's edge, a road crossing it and the shore draws the same per chunk as whole, in crisp and soft.
  - `OverworldTests`, on the committed Overworld:
    - its chunks match the composition of its generator asset (no drift between the asset and the scenes), locked chunks aside;
    - every chunk within 16 layers;
    - no coast map stale, and `WorldValidation` passes;
    - with every chunk open, `EditModeWorldNavigation` connects every waystone, gate and mob to the default entry across chunk borders.
  - `AreaGenerationTests`, `WorldBrushStrokeTests` and `WorldCoastBakeTests` stay as they are.
- **PlayMode:**
  - `OverworldPlayModeTests`:
    - the Overworld boots at `clearing`, and the chunks around the player load, link, draw and spawn their mobs;
    - the player walks from `clearing` along the road out of the Clearing to a gate, across its outline, then on to a connected cell of the next chunk, across a chunk border, with no error logged;
    - a respawn at each region's waystone: the player is sent there, discovers it, dies a few cells away and arrives back at it after the respawn delay, one case per region.
  - The tests that boot into Testlands stay on it, those started from the menu through the test copy of GameScenes (Phase 5).
- **Manual:** Multiplayer Play Mode, Phase 4's steps.

## Open questions

- **Regions in shapes the world gives.** Should later worlds have region generators that fill a mask, knowing their neighbours, road ports and sea, so regions meet with no wilds? Every generator's ground, edge, road and coast steps would be rewritten for arbitrary shapes, and the seed layouts would no longer carry over.
- **Stale save records.** A death recorded for a spawn point no longer laid is never read but stays in the save. Prune a chunk's records of ids it no longer has as it loads, or leave them?
- **A content hash in the join payload**, against relying on the build version being bumped whenever a world changes.
- **The wilds' mobs.** Only the Weasel exists; how many per chunk, and whether the wilds should have mobs at all before there are more kinds.
- **Gates.** Plain entries that give the tests, the manual checks and later exits named points at the regions' edges. Worth the dozen extra entries in the World Editor's list, or should the tests find the border points another way?
