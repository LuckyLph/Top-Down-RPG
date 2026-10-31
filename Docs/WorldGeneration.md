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
- **Their assets** (`Assets/Settings/Generation`) hold seed 1. Since [PropGeneration.md](PropGeneration.md)'s Phase 5 their scatter layers list the generated natural props, but they set no buildings or village furniture, so they lay no building or square; since [Lighting.md](Lighting.md)'s Phase 5 they set the light props, so the Clearing, the Snowfield, the Coast and the Lava Fields lay their campsites and the Village Forest its lamp posts, and the world generator stands a lamp post on each waystone's plaza (`WaystoneLamp`). Since its Phase 6 the world lays them with their props (Props, below). Their sizes run from 46 x 32 (the Clearing) to 92 x 62 (the Village Forest), about 19 000 cells in all, under five chunks' worth. One generation takes 8 to 80 ms without props and about 7% more with them; the six together about 0.3 s. Their seed 1 layouts lay seventeen of the eighteen materials: plaza comes only with the Village Forest's square, laid only with a fountain. The Lakeside lays twelve on its own.
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
- Built props, buildings and interiors: there is no art for them yet (only art generated in this project is allowed). The world writes the generated natural props (PropGeneration.md's Phase 6) and nothing built.
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
| Between regions | The wilds: grass laid by the world on every cell of the land outside the regions, with patches of weeds, meadow and tall grass, a few mobs, no paths of its own, and a forest of the woods' props: trees thickening the band of trees of each region whose ground is grass, groves near the outlines and in the open, and a scatter of lone trees, bushes, rocks and plants (PropGeneration.md's Phase 6). |
| How regions meet | Each region keeps its outline and its band of trees and meets the wilds' grass there, with at least `regionGap` (12) cells of wilds between two regions and `regionShoreGap` (6) between a region and the beach. A region whose ground is not grass (the Snowfield's snow, the Lava Fields' dirt) meets the grass with no grass laid under its edge: the renderer already takes the lower layer to run on under the upper one where they meet, and Phase 0's captures with and without grass under the outer 3 cells, where a road crosses the edge, were the same pixel for pixel. One road network joins them: links between the regions' ports. |
| The sea | Around the whole land: the Overworld is an island. At least 16 cells of sea lie between any walkable cell and the world's edge, so the void beyond never shows (the view is 20 x 11.25 cells). The Coast region gives the north shore, as it was built to. Elsewhere the world lays a beach of sand between the land and the ocean, so the ocean meets only sand; along the south shore the land's bank shows above the water, as a pond's far bank does. The coast lies 20 cells from the world's edge, give or take 3 (so 17 cells of sea or more), wobbling as the Coast generator's lines do (sums of sine waves 12 to 36 cells long); along the side the Coast region lies on, it is held at the rows the region's coast holds at its sides (44 cells from the edge at the Overworld's Coast's west end and 43 at its east end, Phase 1's notes) and eases back to 20 over 40 cells beyond them, so the two beaches meet level, the world's beach there as wide as the region's at that end (5 cells at both ends). The beach is 4 to 7 cells wide, the Coast generator's widths. Grass lies under the beach, as under all land, and none under the ocean: the captures with grass under its first 2 cells and with none were the same, and grass under all of it doubles an open-sea chunk's scene (1.05 to 2.09 MB). |
| Layers per chunk | At most 16: a chunk's materials, with its neighbours' 4-cell strips, are counted in the composition and shown in the preview, and a chunk past 16 is refused. Regions that would push a chunk past it are kept apart by placement. A chunk may be allowed the wide variant only by an explicit entry in the generator asset. |
| The eighteen materials | All of them, in the style's order. The regions lay seventeen; the waystones' plaza discs lay the eighteenth. |
| Entries and waystones | A waystone per region at its spawn cell, named after the region's key, with a disc of plaza of radius 3 under the road there, which also gives waystones a visible place (at radius 2 only a row of slabs shows on either side of a 3-wide road; at 3 it reads as a small square). The Clearing's is first, so it is the default entry and the start of a new game; it keeps the PlayMode margins (6 open cells, mobs 14 or more away). A plain entry, a gate, on the world road just outside each linked port. The generator owns the entries it makes; entries made by hand stay. |
| Starting world | The Overworld, at `clearing`, from Phase 5. The PlayMode tests stay on Testlands: every boot through `SceneBootTestHelper` gets a test copy of GameScenes starting at Testlands' `start`, those that boot by path as well as those that start from the menu, since a joining player the host does not know starts at the starting world in either; only the cases that check the project's own starting world boot without it. |
| Exits and interiors | None in the Overworld yet. The Village Forest lays no buildings without building art; once it does, each doorstep's exit to an interior world is Later. |
| Persistent ids | Derived, not random: the first 32 hex digits of a SHA-256 of the world's id, the scope (a region's key, or the wilds and a chunk), the kind (`mob`) and the ordinal within that scope, the format of `PersistentIds.NewId`. A spawn point keeps its id through re-seeding and retuning, so a mob killed before stays dead at its new place. |
| Layer lists | Only `AreaTerrain`'s list, which saved edits index, is append-only: the writer appends a chunk's new base ground layers to it and never removes or reorders an entry. A layer it lists stays in the renderer, even empty. The renderer's own list follows the style's order and may gain and lose layers. A chunk is written when what it holds changes; when only a neighbour's border strip changes its list, Sync layer lists adds the layer it now lacks (Architecture: Writing). |
| Hand work | The writer never opens a locked chunk for writing, and refuses, unless forced, a chunk whose content (its ground, its raised ground, its generated spawn points and its generated props) differs from what it last wrote there (painted, or a generated prop moved, by hand since), or, never written, holds any. It replaces only objects under its own roots (`GeneratedMobSpawns` and `GeneratedProps` under the chunk's grid) and never touches the walls, so hand-placed exits, spawn points, props (under the scene's own `Props` root) and walls stay. Regenerating one region needs nothing more: the world is composed again and only the chunks whose content changed are written. |
| Determinism | One `System.Random` per stream, iteration over ordered lists only, as in the area generators: each region from its own seed, the coast from the world's seed, each road's wander from the world's seed and its link's place in the list, and the wilds per chunk from the world's seed, the chunk's coordinates and its record's wilds seed (patches, then mobs, then groves and props), so a change in one place moves the wilds of at most the chunks around it, and re-rolling one chunk's wilds moves nothing else. A chunk's patches, groves and props keep a cell inside its border and its mobs 5 cells, so no chunk's wilds read a neighbour's. |
| Props | Since PropGeneration.md's Phase 6: each region's layout comes with its scatter layers' props, moved with it, but each blocking prop whose footprint would cross a chunk's border (a chunk's navigation sees only its own props) and each prop on its waystone's plaza (a plant beside it too), which are left out. The wilds lay their forest per chunk, each footprint a cell inside the chunk. The writer writes each chunk's props as prefab instances under its grid's `GeneratedProps`, and its written hash covers them. Laid with props, a region's mobs and its terrain laid after its props (the Lakeside's ponds, the Coast's plateau) differ from the layout laid without them, which the Overworld held until then. |
| Tooling | A World Generator window (`Tools/TopDownRPG/World/World Generator`) over the world generator asset: the preview, the chunk grid with each chunk's state, lock and wide allowance, and writing changed chunks, or the selected chunks or every chunk forced, followed by the World Editor's whole-world steps. Its logic sits in a session object the tests and tools drive with the confirmation passed in, so no dialog ever runs under a tool. Chunk scenes of the world open with unsaved changes stop a write. The World Editor and the Scene Generator stay as they are; the Scene Generator is where a region's generator is tuned on its own. |
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

A player starts on the Clearing's road, inside its clear disc, at its waystone's plaza. Each region is as its seed 1 layout was, its props included, ringed by grass with weed, meadow and tall grass patches where its edge meets the wilds, the regions of grass ringed by a thicker band of trees; walking out along a road leads through the wilds, past groves and lone trees, to the next region's gate and on to its waystone. Mobs stand in every region as its generator placed them and a few in the wilds. The sea surrounds it all; nowhere can a player see past the world's edge.

### Generating

In the editor, from the World Generator window:

1. **Compose.** The asset's settings give a composition in memory, composed again 0.4 s after the last change to the generator, its world or its region generators (an inspector edit, an undo), and previewed one pixel per cell with the chunk grid, each chunk's layer count (red past 16, blue when its record allows it wide), the locked chunks hatched, the regions' outlines, ports, gates, waystones and mobs, and the warnings, those that stop a write set apart. A grid of the chunks gives each one's state from its record and its composed hash alone, never opening a scene: unchanged, changed, locked or missing. Chunks are selected there or in the preview, and locked or allowed wide there, with undo; a lock recomposes nothing.
2. **Write changed chunks.** Every chunk whose composed content differs from its written hash, and which is neither locked nor painted by hand since, is written; the others are reported as unchanged, locked or changed by hand. Missing chunks are created first. **Write selected chunks** and **Write every chunk** are forced: they write the selected chunks or every chunk whatever their records say, unchanged ones and hand work included, but never a locked one. Write selected chunks is how one chunk painted by hand is regenerated without forcing the rest.
3. **Then the whole-world steps**, in order: Sync layer lists (covering locked neighbours' border materials), Bake changed coasts, Repair ids (which keeps derived ids, being unique), Validate. The window reports each chunk's line and each step's lines and times.

Writing is not an undo step: it spans closed scenes. It asks first, listing the chunks it will write and the warnings, offering Write anyway when a warning stops it (layer lists, reachability), and git is the way back. A chunk scene of the world open with unsaved changes stops every write until it is saved or closed: the writer and the whole-world steps save the chunk scenes they change, which would save the unsaved work with them. An open chunk scene with no changes is written in place and stays open.

### Regenerating

- **Changing a region** (its seed, its generator's settings, its place) changes its cells, the roads to its ports, its gates, the wilds patches of the chunks it covers or borders, and its mobs' places. Its mobs keep their ids by ordinal: a mob killed before stays dead at its new place, a mob past the old count is new, and a record for a mob no longer laid is never read again. Its waystone moves with its spawn cell and keeps its name, so it stays discovered.
- **Changing the wilds or the sea** changes only the chunks whose cells change: the wilds' streams are per chunk.
- **Locking a chunk** keeps it exactly as it is, hand work included. Its neighbours are still written, and their seams with it are drawn as the skirt finds them; the preview marks the locked chunks.
- **A chunk painted by hand** without being locked is refused at the next write and named, so it can be locked, or forced to be regenerated.
- **Objects placed by hand** (exits, extra spawn points, props) are never touched, locked or not.
- **Saves.** See Multiplayer: the host's save after a regeneration.

### Playing

Nothing is new at runtime. The Overworld is a world of chunk scenes like Testlands: it streams around each player, draws per chunk, joins its navigation across borders, spawns each chunk's mobs, and saves deaths, edits, waystones and places as WorldStreaming.md describes. The only runtime changes are Phase 5's: a saved place on ground that is no longer walkable moves to the nearest walkable cell, or to the player's nearest discovered waystone when none lies near, and GameScenes can be copied for tests.

### Multiplayer

- **Every machine loads the same chunk scenes.** The result of generation is in the scenes, so a host and its clients load the same ground, raised ground, spawn points and entries from their builds, as for a hand-painted world. Nothing about generation is seeded, sent or checked at runtime, so no difference in floating-point results or random numbers between machines can matter.
- **The build version.** World content is part of the build: a host and its clients must have the same chunk scenes. A build whose worlds changed ships as a new build version (Player Settings), as a protocol change does, and `JoinApproval` refuses any other version. Phase 5 bumps it for the Overworld. A content hash per world in the join payload, which would catch a forgotten bump, is an open question.
- **The host's save after a regeneration.** Only the host saves, and every key in its save survives a regeneration: worlds by id (the `WorldDefinition`'s GUID, which regenerating keeps), killed mobs by spawn point id (derived, so the same spawn point keeps its id), terrain edits by `AreaTerrain` layer index (appended to only), discovered waystones by entry name (the region's key) and places by position (moved to the nearest walkable cell, or the player's nearest discovered waystone, when the ground there changed, Phase 5). Only the host, or an offline game, moves a place, once the chunks around it are up on its own machine: its own place as the session starts, a joining player's before it spawns there. A client never corrects its own place; it, and a late joiner like any other, gets its player spawned at the corrected place. The corrected place is the player's place from then on, so the next save keeps it. A session continued after a regeneration keeps its dead mobs, edits, discoveries and players' places wherever they still apply. An edit lands on the cell it named, whatever the ground there now is; a discovery of an entry that is gone is ignored, since `WaystoneRules` only looks up the world's own entries.
- **A late joiner** loads the chunk scenes around its player like any client and receives the host's mobs and terrain edits per chunk as it reports each chunk ready (WorldStreaming.md's Networking). It generates nothing and needs nothing new.
- **A client in a region the host is not in.** Regions exist only in the editor; at runtime the Overworld is one world. A client in the Lava Fields while the host is in the Clearing is a client in another part of the same world: the host streams and simulates the chunks around it, owns its mobs, decides its waystone discoveries and respawns it at its nearest discovered waystone, as for any player apart from the host (WorldStreaming.md's Phase 7 and 9 checks).

## Architecture

### Pieces

All of it is editor-only code in `TopDownRPG.Editor`, under `Assets/Editor/World/Generation`, beside the area generators. The six generators, `AreaLayout`, `AreaLayoutBuilder`, `AreaLayoutWriter`, `GroundLayerMigration`, `AreaGroundSetup` and the Scene Generator are unchanged.

| Type | Role |
|---|---|
| `WorldGenerator` | `ScriptableObject` (create menu `TopDownRPG/World/World Generator`), a world's generation settings, under `Assets/Settings/Generation` (`WorldGenerator_Overworld.asset`, Phase 4): the `WorldDefinition` it writes, a world seed, the terrain style (its layer order is every chunk's), the movement profile the reachability check walks by, the world's own materials (`GroundMaterials`: grass, sand, ocean, stone, gravel, plaza, weeds, meadow, tall grass), the sea ring's depth, the coast line's wobble and wavelengths, the beach's widths, the island's corner radius and the distance the coast eases over beside a sea region, the region placements (`RegionPlacement`: key, `AreaGenerator`, its seed or the generator's, cell where the layout's origin goes), the gaps (`regionGap`, `regionShoreGap`), the plaza's radius, the road links (`RoadLink`: from a region's port, to another port, to the road an earlier link laid to or from a port, or to the nearest road, stone or gravel, width), the roads' lead-in, spline spacing, margin, avoidance and wander, the gates' distance, the wilds (`Patches` of tall grass, meadow and weeds per chunk: edge and open counts, radii, reach; mob prefab, mobs per chunk, distances), the wilds' props (the groves as `Patches`, the edge reach, the props' path and entry clearances, and the scatter layers, `WildsScatter`: an area generator's `PropScatter` with a zone, Edge, Grove or Open) and a record per chunk (`ChunkRecord`: coordinates, written hash, locked, allowed wide, wilds seed), and the names of the entries the writer last set on the world (`WrittenEntries`). `Compose()` returns a `WorldComposition`. Editor-only data, edited by editor tools only; tests and tools build one with the internal `Create` and `Set*` methods. |
| `WorldComposition` | Plain data in world-local cells: a bit per material of the style's layer order for every cell (`MaskAt`, `Has`, `TopAt`, `CellsOf(material, area)`), the region owning each cell or the wilds or the sea (`RegionAt`), raised cells and ramps (`RaisedAt`), the regions (key, generator, seed, cell, bounds, spawn cell, outline, ports with the side each faces and their gates, whether it lays the ocean), the roads (link, material, width, course, cells), mobs (prefab, cell, derived id, scope, ordinal), props (prefab, cell, offset in its cell, cells its footprint covers, scope: a region's key or a chunk's wilds; each in one chunk), entries (`WorldDefinition.Entry`: name, position, waystone; the waystones first, then the gates), the terrain style, the base ground materials (`Grounds`: the wilds' grass and each region's ground), each chunk's material list (its cells and its neighbours' 4-cell strips), own materials and content hash (`ChunkContent`), the warnings and those among them that stop a write (`BlockingWarnings`: layer lists past 16, reachability), and how long each step took. |
| `WorldComposer` | Internal, the composition's steps in order (below), on dense arrays over the world: a `uint` of material bits, a `short` owner and a `byte` of raised ground per cell, with steps from the nearest region, the beach and the roads in byte arrays while it runs. Of the area generators' `AreaLayoutBuilder` helpers only `SampleSpline` fits: `Paint`, `PaintPatches`, `RemoveFragments` and `FloodFill` work on the builder's own sets of cells, so the composer has array versions of them, and of `WobblyShape`. |
| `GeneratedIds` | Static: `For(worldId, scope, kind, ordinal)`, the derived id (Decisions). |
| `PortSide` | The side a port faces (east, north, west, south), with `PortSides.Step`. |
| `WorldGenerationWriter` | Writes a composition into the world's chunk scenes (below). `WriteChunk(grid, composition, coords)` writes one open chunk and returns the hash it wrote, and is what the tests drive in memory; `Write(generator, composition, chunks, force, despiteWarnings)` opens, writes, saves and closes the chunk scenes one at a time and returns a `ChunkWrite` per chunk (its outcome, its time and a line); `ContentHash(grid, coords, layerOrder)` reads a chunk scene's hash; `RunWorldSteps(world)` runs the whole-world steps after a write. |
| `ChunkContent` | Internal: what a chunk holds in the written hash's terms (a bit per material for each of its cells, its raised ground, its generated spawn points, its generated props and where their roots stand, `RootOf`), read from a composition (`Of`) or from a chunk's grid (`Read`), and its `Hash`, the one hash function the composition's content hashes and the writer's records share. |
| `WorldGeneratorWindow` | `Tools/TopDownRPG/World/World Generator`. A thin window over a `WorldGeneratorSession`, as the World Editor is over `WorldAuthoring`: the asset picker and inspector, the preview (zoomed to fit or by whole pixels a cell, the selected chunks framed, the cell under the pointer described, a click selecting its chunk), the warnings with those that stop a write set apart, the chunk grid with each chunk's state, layer count, lock and wide allowance, and Write changed chunks, Write selected chunks and Write every chunk, each asking first, then the whole-world steps, with the last write's report. Writes, selection changes and composing run after the GUI pass. Disabled in play mode. Its only standing work is following changes to the generator, its world and its region generators, which recompose the world a moment after the last one. |
| `WorldGeneratorSession` | Internal: what the window works on, apart from its GUI, so tests and tools drive every step without a dialog. One generator's composition, composed again only when what it is made from changes (`SettingsKey`: the generator's settings, of its chunk records only the wide allowances and wilds seeds, its world's size and chunks, its region generators' settings), `RecomposeDelay` (0.4 s) after the last change (`NoteChange`, `FollowRecompose`); each chunk's state (`ChunkState`: unchanged, changed, locked, missing) from its record and its composed hash alone; the `WritePlan` of each button (`Plan`: the chunks it covers and writes, those it creates, the warnings, the problems that stop it); `Write(plan, confirm)`, which asks through the callback it is given (the window's dialog, a test's fixed answer) and runs the whole-world steps after; and `SetLocked`, `SetAllowWide` on the records, with undo. |
| `WorldGeneratorPreview` | Internal, static: the preview's pixels for a composition (`Pixels`, `Draw` into a texture), one per cell, Phase 1's signed-off look with the locked chunks hatched and counts past 16 blue when allowed wide, and the props' footprints dark green and plants light green, as the Scene Generator's map draws them. |

### Composition

The steps, in order:

1. **Regions.** Each placement's generator lays its layout from the placement's seed (the generator asset's own by default) around the origin, once, and the composition moves every cell, raised cell, ramp, mob and prop by the placement's cell, but a blocking prop whose footprint would cross a chunk's border. Its outline, its ports (with the side each faces) and its spawn cell are read from the moved layout. A port is a run (8-connected) of the region's stone, gravel or plaza cells on its outline; it faces the side most of its cells have outside them, and holds the rows (an east or west port) or columns (north or south) of those cells, whose middle its road leaves on. No world step writes inside an outline but the waystone's plaza. Regions that overlap (a placement mistake, warned about) keep both layouts' materials and the later one's ownership.
2. **Land and sea.** The land is the world's bounds inset by the sea ring, its coast wobbling along each side as the Coast generator's lines do (sums of sine waves 12 to 36 cells long), with corners rounded to `coastCornerRadius` (24). Outside it lies ocean to the world's edge; along its edge, a beach of sand between the beach's two widths, so the ocean meets only sand, with grass under the beach and none under the ocean. Where a sea region (one that lays the ocean, the Coast) lies along a side of the world its ocean faces, its own ocean and beach take over: the world's coast along that side is held at the rows the region's coast holds at its two ends (read from the three outermost columns with ocean at each end) across the region's span and eases back to the ring's depth over `coastEase` (40) cells beyond them, and the world's beach there takes the region's beach width at that end, so beach meets beach level and ocean meets ocean. A sea region whose ocean lies further from that side than the ring's depth, its wobble and the ease holds nothing (it is warned about instead of pulling the coast to it).
3. **Wilds ground.** Grass on every land cell outside the regions, the beach included.
4. **Roads.** Each link's course is the cheapest route (A*, 8 neighbours) over the wilds, closed within half the road's width and `roadMargin` (2) of a region's outline or the beach and dearer over the next `roadAvoidance` (5) cells and near other roads, most of all across one, so it enters a region only at its port. It leaves the port straight on the rows its region's road holds at the outline for `portLeadIn` (6) cells, so the two roads join without a step, and ends at the other port's lead-in or on the first cell it reaches of the road an earlier link laid to or from the named port (or of any road, for a link with no target). The route is pulled straight wherever a straight line crosses no cell dearer than the route there, resampled into a point every `splineSpacing` (8) cells, moved aside by up to `roadMeander` (1.5) cells by a smooth noise along it on the link's own stream (from the world's seed and its place in the list), sampled into a spline (`AreaLayoutBuilder.SampleSpline`) and painted at the link's width on the wilds off the beach, as the regions' own roads are; where it meets another road, it joins it. A port no link names is a warning.
5. **Waystones and gates.** Each region's spawn cell becomes a waystone entry named by its key, the first placement's first in the list, so it is the default. A disc of plaza (the plaza's radius, 3) lies under it on the region's ground, under its road, as the Village Forest lays its square: any road cell inside the disc is paved too, under the stone. The region's props on the plaza go, and its plants beside it. Each linked port's gate, named by the region's key and the side (`clearingEast`), stands on the middle of its first link's road `gateDistance` (2) cells outside the outline; the gates follow the waystones in the entries, in region order and each region's ports in row order.
6. **Wilds detail**, per chunk in row order, each on its own stream (the world's seed, the chunk's coordinates and its record's wilds seed): tall grass, then meadow, then weed patches (each kind's edge patches centred on and kept to cells within its reach of an outline, then its open ones anywhere) on wilds grass two cells or more from any outline, the beach and the roads, a cell from each other, so each meets only grass, and a cell inside the chunk's border, so no chunk's patches depend on a neighbour's; pieces under four cells are dropped.
7. **Wilds mobs**, per chunk on the same stream: up to the asset's count (1) on wilds grass and its patches, off the roads and the beach, 5 cells or more inside the chunk's border (so two chunks' mobs keep 10 cells apart without either reading the other), at least 14 cells from every waystone and gate and 10 from every other mob, regions' included.
8. **Wilds props**, per chunk on the same stream after its mobs, so the patches and mobs are the same with or without them (PropGeneration.md's Phase 6): first the chunk's groves (`Groves`: wobbly patches of 5 to 9 cells, two centred near an outline and two in the open, on cells a blocking prop may take), then the wilds' scatter layers (`WildsProps`) in order, each with a zone: Edge (within `edgeReach`, 3, of the outline of a region whose ground is the wilds' grass), Grove or Open (the rest). Each tries the zone's cells a cell inside the chunk's border in random order with a random prefab of the layer, keeping its spacing from the others it placed in the chunk. A blocking prop's footprint stands on wilds grass and its patches more than `propPathClearance` (2) from every road and beach cell, on cells no other prop takes and with no other footprint beside it; a walk-through prop stands off the tall grass, more than a step from the roads and the beach. Every prop keeps `propEntryClearance` (4) from the waystones and gates and a cell from every mob.
9. **Checks**, each a warning shown in the preview:
   - placement: every region inside the land (but a sea region's ocean and beach), `regionGap` cells or more from every other region and `regionShoreGap` from the beach (sea regions aside), all from one pass outwards from every region's outline at once that notes, where two regions' fronts meet, how far apart they are; on chunks the world has; and a sea region's ocean meeting the world's sea;
   - layer lists: every chunk at 16 materials or fewer, unless its record allows it wide;
   - reachability: every waystone, gate and mob connected to the default entry over walkable cells (the top material walkable by the movement profile, off the props' footprints, raised ground crossed only at a ramp's foot, as `RaisedGround.CanCross` rules);
   - ids unique, and every port linked.

### Writing

For each chunk to write, alone, with its grid inactive while it is written, so its renderer builds once, and with its scene the active scene, so every object made lands in it:

- **Ground.** Its renderer's layers become the materials of its list, in the style's order, plus any layer its `AreaTerrain` lists, created as `GroundLayerMigration.AddLayer` creates them (`AreaGroundSetup.SetUpLayers`), a material drawn already keeping its tilemap. Every ground layer is cleared first (`ClearAllTiles`, so no record of an old tile stays) and filled in one batch with its material's tile in exactly the composition's cells of the chunk. A layer neither listed nor in `AreaTerrain` is removed. The raised tilemap is set up and written like the area writer's (`AreaGroundSetup.SetUpRaised`), with the raised and ramp tiles of the region owning each cell, or removed when the chunk has no raised ground.
- **`AreaTerrain`.** `CreateChunk` lists the grass layer (and the walls, when the chunk has them). The writer appends each base ground layer (the wilds' grass, a region's ground: snow, dirt) with cells in the chunk that it does not list yet, never removes or reorders an entry, and points each entry's fill tile at its layer's tile. A layer it lists stays drawn, emptied, where the chunk no longer lays its material.
- **Mobs.** The spawn points under the chunk grid's `GeneratedMobSpawns` (created when missing, removed when the chunk has no mob) become the chunk's mobs in the composition's order, each at its cell's centre, named after its prefab, scope and ordinal, with its prefab and its derived `PersistentId` set as it is made, before the editor's change events reach `PersistentIdWatcher`, which keeps an id that is set and unique. The spawn points already there are reused in order, so a rewrite whose mobs did not move leaves the scene file as it was. Spawn points elsewhere in the scene are left alone.
- **Props.** The prop instances under the chunk grid's `GeneratedProps` (created when missing, removed when the chunk has no prop) become the chunk's props in the composition's order: an instance there of the same prefab is reused in its place, so a rewrite whose props did not move leaves the scene file as it was, another is made in the chunk's scene; each stands where the area writer stands it (a blocking prop's footprint centred on its cells, `PropGridSnapping.RootOnCells`; a walk-through prop at its point, to the pixel). A prop's sorting order is the one its `YPositionSorter` gives it as the grid comes back, which the save writes as a prefab override, the same every time.
- **Records.** A chunk's written hash (`ChunkContent`, shared by the composition's content hashes and the writer's reading of a scene) covers what it holds: each material with cells in the chunk and those cells, its raised cells and their kinds, its generated spawn points (prefab, cell, id) and, when it has any, its generated props (prefab, place to the pixel), so a chunk written before there were props keeps its hash. Empty layers, the walls and every other object count for nothing, so a chunk just written reads back with its composed hash whatever layers its `AreaTerrain` keeps or a sync added, while paint on any ground layer, empty or not, on raised ground, or a generated spawn point moved changes it. It goes into the chunk's `ChunkRecord` once the scene is saved, with the ids of the generated spawn points written there (`MobIds`), which tell a later write which ids a chunk it does not write still holds. Before a later write, a chunk whose composed hash equals its record is skipped without being opened, and one whose scene no longer hashes to its record (to an empty chunk's hash when it has none, so a chunk painted before its world had a generator is not overwritten unasked) is refused and named, unless forced. Forcing writes every chunk asked for but the locked ones, an unchanged one too. A chunk's list is not part of its hash, since its own scene cannot show its neighbours' strips: when only a neighbour's strip changes, the chunk is not written, Sync layer lists adds the layer it now lacks, and a layer it no longer needs stays, empty, until it is next written.
- **The world.** The generated entries are set first in the `WorldDefinition`'s list, in composition order; entries made by hand keep their order after them. The generator remembers the names it set (`WrittenEntries`), so an entry it no longer makes (a gate whose link went) goes at the next write. A chunk the world lacks is created first (`WorldAuthoring.CreateChunk`), so it is in the build and the definition.

The writer refuses to run in play mode, refuses a composition with warnings about layer lists or reachability (`BlockingWarnings`) unless asked to write anyway, and refuses to create chunks while an untitled scene has unsaved changes, which `CreateChunk` would replace. After a write, `RunWorldSteps` runs the whole-world steps in order: Sync layer lists, Bake changed coasts, Repair ids, Validate (Behaviour: Generating).

### Runtime changes

Only two, both in Phase 5:

- **Places that are no longer walkable.** Once the chunks around a saved place are up (`IWorldSession.PrepareAsync` and `EnterAsync` already wait for them), a place on a cell its world's navigation cannot walk moves to the nearest walkable cell, or to the player's nearest discovered waystone when none lies within the search radius. `WalkablePlaces` (Gameplay scope) holds the rule, with the radius in `GameplaySettings` (`SavedPlaceSearchCells`, 16); `WorldStreaming` asks it with authority only, in `EnterAsync` for the local player and in `PrepareAsync`, which now takes the joining player's id and returns where to spawn it. That removes the Known gap "Players' places are checked only against chunks".
- **A test copy of GameScenes.** An `internal` copy naming another starting world and entry (`GameScenes.CopyStartingAt`: a `GameScenesCopy` holding every serialized field of the asset, `DontSave`), registered into the Main scope by `SceneBootTestHelper` beside the save store it already enqueues, for every boot, and destroyed with that scope (a dispose callback). The asset itself is never written.

### Generating a world

1. Create the world in the World Editor: its name, its size in chunks, Create every chunk and List in Game Scenes ticked.
2. Create its world generator (Create > TopDownRPG > World > World Generator) under `Assets/Settings/Generation` and point it at the world. Add a placement per region (key, generator asset, seed, cell), the road links, and the sea and wilds settings.
3. Open `Tools/TopDownRPG/World/World Generator`, pick the asset, and move the placements in its Settings until the preview shows no warnings: every chunk at 16 layers or fewer, every port linked, everything reachable. The preview follows each edit a moment later, the warnings that stop a write listed first, and the line under it names the cell under the pointer, which is how a placement's cell is read off.
4. Save or close any of the world's chunk scenes open with unsaved changes, then Write changed chunks. It asks first, listing the chunks it writes (the missing ones created) and the warnings. Each chunk is reported as written, created, unchanged, locked or changed by hand; then the whole-world steps run, each reported with its time.
5. Look at the result with the World Editor's Open with neighbours. To keep hand work in a chunk, paint it, then select it in the window's grid or preview and tick Locked before writing again. To regenerate a chunk painted by hand instead, select it and Write selected chunks, which is forced.
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

- [x] `WorldGenerator`, `RegionPlacement`, `RoadLink`, `ChunkRecord`, `WorldComposition` and `WorldComposer`, with steps 1 to 8 of Architecture: Composition.
- [x] Regions moved into place cell for cell, with their outlines, ports and spawn cells.
- [x] Land, sea and the world's beach, joined to the Coast region's coast.
- [x] Wilds ground.
- [x] Roads between ports.
- [x] Waystones with their plazas, gates, and the default entry.
- [x] Wilds detail and mobs, per chunk.
- [x] Derived ids (`GeneratedIds`).
- [x] The checks and their warnings.
- [x] Tests: `WorldGeneratorTests` (see Testing).
- [x] Docs: Architecture.md's Editor tools (Generation/) and Tests.

Notes (the preview helper, under `Assets/Editor/Spike`, was deleted before the commit; editor times are debug code with the editor in the background, best of 3):

- **Built**, in `Assets/Editor/World/Generation` as Architecture: Pieces lists them, with three more small types: `PortSide` (and `PortSides.Step`), the `WorldGenerator.GroundMaterials` the world lays itself and the `WorldGenerator.Patches` of each wilds material. `WorldGenerator_Overworld.asset` waits for Phase 4: the preview and the tests build the Overworld's Phase 0 settings in code on an unsaved generator (`WorldGenerator.Create`, `SetRegions`, `SetLinks`, `SetSeed`, `SetRecord`) and an unsaved world (`WorldDefinition.Create`).
- **Where things differ from Pieces.** The asset also holds the terrain style (whose layer order is every chunk's), the movement profile the reachability check walks by and the world's own nine materials, and these settings the plan did not name: the island's corner radius (24), the coast's ease (40) and wavelengths (12 to 36), the roads' lead-in (6), spline spacing (8), margin (2), avoidance (5) and wander (1.5 cells over waves 24 to 64 cells long). A `RoadLink` can also end on the road an earlier link laid to or from a port (`toRoad`), and a `ChunkRecord` holds a wilds seed that re-rolls that chunk's wilds alone. Of `AreaLayoutBuilder`'s helpers only `SampleSpline` fits the composer's dense arrays; `Paint`, `PaintPatches`, `RemoveFragments`, `FloodFill` and `WobblyShape` work on the builder's own sets of cells, so the composer has array versions of them. The composition also records each step's time, which the window's preview will show.
- **The Overworld**, from Phase 0's settings (the six regions and the eight links of Behaviour: The Overworld, the west road as two links meeting at the Clearing's west port), composes with no warnings: all eighteen materials laid, every chunk listing 5 to 14 (14 in chunk (1, 0), as Phase 0 found), 6 waystones (the Clearing's first) and 13 gates, 8 roads of 104 to 526 cells, every port linked, and 48 mobs, 28 in the regions and 20 in the wilds, every one reachable from the Clearing's waystone. Its preview (one pixel per cell: the materials' base colours, the chunk grid and each chunk's material count, outlines, ports, gates, waystones and mobs) was signed off for the road courses, the coast and the wilds detail.
- **Timings**, of `Compose()` end to end with the generator assets loaded: each placement's layout generated once, every step, the chunk lists and hashes, and the checks; not drawing the preview. Retained is the managed memory the composition holds, measured around it.

  | | Compose | six or 88 generations | the world's steps | retained |
  |---|---|---|---|---|
  | the Overworld (5 x 4, 6 regions, 8 roads) | 0.40 s (0.40 to 0.42) | 0.28 s | 0.12 s: roads 40 ms, wilds 47, chunk lists and hashes 12, checks 7, distances 7, land and sea 6 | 2.4 to 2.9 MiB |
  | a synthetic 16 x 16 (88 regions, 63 roads) | 5.4 s (5.4 to 5.6; 7.0 the first in a session) | 4.1 s | 1.35 s: wilds 0.71, roads 0.22, chunk lists and hashes 0.15, checks 0.12, land and sea 0.08, distances 0.08 | 7.6 MiB |

  Both are under the targets (1 s and 10 s). The synthetic world repeats the five inland regions on a grid 120 x 88 cells apart with a row of Coasts along the north, a road between each region and the next in its row (but from the Village Forest, which has no east port); it is not checked for warnings (68, mostly unlinked ports and two regions near the beach). Its per-cell arrays take 7 MiB (a 32-bit mask of materials, a 16-bit owner and a byte of raised ground per cell), as Phase 0 planned; while composing, the distances and the route search add about 30 bytes a cell, given back after.
- **Phase 0's problems.**
  - The sand tongue at the Coast's west join is gone: the world's beach beside the Coast takes the region's own width at that end (5 cells at both ends), its coast the region's own rows (44 cells from the edge at the west end, 43 at the east end, read from the three outermost columns with ocean at each end). What is left is a step of one row at the foot of the west join, where the region's outermost column's sand stops a row short of the next column's, its outline's corner.
  - The square road bends: every road is a spline through its pulled course, its corners rounded.
  - The slow per-region checks: the gaps between regions and to the beach come from one pass outwards from every outline at once, 7 ms for the Overworld and 80 ms for 16 x 16, with the checks themselves 7 and 120 ms.
- **What surprised.**
  - "The nearest road" sends both of the Clearing's gravel paths to its own road to the Village Forest, the nearest by the route's cost, rather than round the Snowfield and the Lakeside as Behaviour describes, so a link can name the road it ends on by a port (`toRoad`).
  - Pulled straight, the routes came out as long straight diagonals, stiff beside the regions' wandering roads; a wander of 1.5 cells on each link's own stream fixed that.
  - A Coast placed inland pulled the whole coast to its rows; a sea region now holds the coast only when its ocean lies within the sea depth, its wobble and the ease of that side, and is otherwise warned about.
  - The wilds are the dearest of the world's steps at 16 x 16 (0.7 s of 1.35), for their wobbly patch shapes and each chunk's lists of patch centres.
  - Grass, weeds and meadow share one base colour, so the preview tinted weeds and meadow.
  - Placements that overlap (warned about) keep both layouts' materials: that is how `WorldGeneratorTests` lays the Lakeside, the Snowfield and the Lava Fields over one another to put more than 16 materials in one chunk, since regions kept their gaps apart cannot.
- **Decisions changed**, with their sections updated: the roads wander, each on a stream of its own link (Decisions: Determinism; Composition, step 4); a chunk's wilds keep inside its border and can be re-rolled alone (Determinism; steps 6 and 7); a sea region holds the coast only when it lies by the sea (step 2); the wilds lay one mob per chunk for now (Open questions).
- **Tests.** `WorldGeneratorTests`, 12 tests in 6.2 s: the Overworld composed once for the fixture, a row of three regions on 6 x 2 chunks composed eight times for determinism over two world and two region seeds and eight more for locality and ids, small worlds of one or two Clearings for the placement warnings, an inland Coast and the three regions laid over one another. `AreaGenerationTests` passes unchanged (79 tests, 32 s).

### Phase 2: writing into chunk scenes

- [x] `WorldGenerationWriter`: ground layers, raised ground, `AreaTerrain` appended to, generated spawn points with derived ids, the generated entries, missing chunks created.
- [x] Written hashes: unchanged chunks skipped without opening, locked chunks never written, chunks changed by hand refused unless forced, objects outside the generated roots kept.
- [x] The whole-world steps after a write, with Repair ids keeping derived ids.
- [x] Tests: `WorldGenerationWriterTests`, and the generated case of `ChunkedGroundRenderTests` (see Testing).
- [x] Docs: Architecture.md's Editor tools (Generation/ and Worlds/), Tests and Known gaps; SceneGeneration.md's "No area scenes" paragraph and Multiplayer section.

Notes (the measurement helper, under `Assets/Editor/Spike`, was deleted before the commit; editor times are debug code with the editor in the background, each the median per chunk over a world's chunks, best of 2 rounds on a fresh scratch world each):

- **Built**, in `Assets/Editor/World/Generation` as Architecture: Pieces and Writing describe: `WorldGenerationWriter` (`WriteChunk`, `Write`, `ContentHash`, and `RunWorldSteps`, the four whole-world steps in order) and `ChunkContent`, the one hash the composition and the writer share. `Write` returns a `ChunkWrite` per chunk: its outcome (written, created, unchanged, locked, changed by hand, outside the world, failed), its time and its line, with each stage's time. The composition gained its terrain style, its base ground materials (`Grounds`) and its `BlockingWarnings`; the generator, the names of the entries it last set (`WrittenEntries`) and the internal `SetWrittenHash`.
- **Where things differ from Pieces and Writing** (both updated):
  - The written hash covers what a chunk holds, not its list: each material with cells in the chunk and those cells, its raised cells and their kinds, and its generated spawn points. A chunk's list holds its neighbours' strip materials, whose layers are empty in its own scene, and its renderer can draw layers the composition does not list (the grass layer `CreateChunk` lists in every `AreaTerrain`, a layer a sync added for a locked neighbour's hand work), so a hash over the list could not read back the same. Phase 1's content hash covered the list; it no longer does. When only a neighbour's strip changes, the chunk is not written; Sync layer lists adds the layer it lacks, and one it no longer needs stays, empty, until it is next written (Architecture.md's Known gaps).
  - A chunk the generator never wrote counts as written empty, so it is refused, unless forced, when it already holds ground, raised ground or generated spawn points; a chunk `CreateChunk` made, an empty grass layer, passes.
  - Forcing writes every chunk asked for but the locked ones, an unchanged one too (the window's Write every chunk), not only one changed by hand.
  - `GeneratedMobSpawns` is a child of the chunk's grid rather than a root of its scene, so a scene holding several chunk grids (the tests') keeps each chunk's spawn points apart; the chunk scope and the validation find spawn points anywhere in the scene. The spawn points there are reused in order, so a rewrite whose mobs did not move leaves the scene file as it was: a forced rewrite of the unchanged scratch Overworld, followed by the whole-world steps, changed none of its 115 files.
  - The generator remembers the entries it set (`WrittenEntries`), so the next write drops one it no longer makes and keeps every entry made by hand, in its order, after the generated ones.
  - The writer records no undo for its tiles, spawn points and references; the layer set-up it shares with `GroundLayerMigration.AddLayer` still records its own, which closing the scene drops.
- **Timings**, on a scratch world under `Assets/Dev/Worlds` at the first free slot of the global layout ((8192, 0)), 5 x 4 chunks written from the Overworld's Phase 0 settings (the six regions and eight links), with the generator built in code:

  | per chunk | median, best of 2 | what it includes | target |
  |---|---|---|---|
  | created and written | 639 ms (512 to 826) | `CreateChunk` 290 to 407 (the scene, its definition, the world's entry, `SaveAssets`), open 7 to 11, write 27 to 48 without ocean and 148 to 219 with it (filling it, then the renderer's build as the grid comes back, a live coast map for a chunk with ocean), save 116 to 196, close 1 to 2 | under 1 s |
  | rewritten, coast map not baked | 459 ms (157 to 512) | open 33 to 206 and write 22 to 215, a shore chunk's renderer building a live coast map in each, save 97 to 138; the six inland chunks 157 to 205 ms | under 0.5 s |
  | rewritten, coast map baked | 155 ms (132 to 213) | open 27 to 58, write 18 to 43, save 81 to 125 | under 0.5 s |
  | rewritten after a change, checked first | 200 ms (181 to 507) | open 28 to 55, reading the scene's hash 1 to 6, write 18 to 64, save 125 to 160 (one 452) | under 0.5 s |
  | unchanged, skipped | under 0.01 ms | comparing the composed hash with the record; nothing opened | |

  The forced rewrites skip the check; the last row is the ordinary case, every chunk changed by a new world seed and checked for hand work before it is written, its coast map baked from the old seed (still the renderer's until the bake). The whole-world steps on the 20 chunks:

  | | after the first write | nothing to do | after the world seed changed |
  |---|---|---|---|
  | Sync layer lists | 3.8 s | 1.5 s | 1.5 s |
  | Bake changed coasts | 18.5 s, 14 maps | 1.6 s, none | 13.2 s, 14 maps |
  | Repair ids | 1.4 s | 1.4 s | 1.6 s |
  | Validate | 1.5 s | 1.4 s | 1.6 s |
  | in all | 25.2 s | 6.0 s | 17.8 s |

  The whole Overworld from nothing took 12.5 s of writing and 25.2 s of steps, 38 s (target: under a minute); rewritten after a change once its chunks exist, 4.3 s and 17.8 s, 22 s (target: under 30 s). Every per-chunk number is under its target. The editor ran slower than in Phase 0 throughout, for code this phase did not change: `CreateChunk` took 290 to 407 ms against 219 to 255, and a coast map 630 to 840 ms against about 475, so the comparison with Phase 0's numbers is loose; Phase 3 measures the targets again.
- **Scene sizes** (YAML), by kind: open sea 1.04 to 1.05 MB (4096 tiles of ocean and the empty grass layer its `AreaTerrain` keeps), all wilds 1.09 MB, shore 1.17 to 1.20 MB, region 1.19 to 1.35 MB, region and shore 1.20 to 1.39 MB, all under the 1.5 MB target. The Overworld has no chunk of all wilds or all sea; those two kinds come from a scratch world of 8 x 6 chunks with an 84-cell sea ring and the regions moved a chunk in, written once (48 chunks, 610 ms each) and deleted. Its coast maps are 43 to 120 KB. Both scratch worlds were deleted through the AssetDatabase, and EditorBuildSettings, GameScenes and ProjectSettings were byte for byte as before.
- **What surprised.**
  - The open-sea chunks keep an empty grass layer: `CreateChunk` lists grass in every `AreaTerrain`, which is only appended to, so a chunk of sea draws a layer it never shows (Architecture.md's Known gaps).
  - Sync layer lists right after the first write took 3.8 s against 1.5 s later: it opens every chunk, and each shore chunk builds a live coast map until the first bake.
  - A save is most of a rewrite once the coast maps are baked: about 80 to 160 ms of 155 to 200.
  - The renderer still schedules a rebuild when its layers change while its grid is inactive, which runs at the next editor update on a chunk left open (the tests' grids in memory, a chunk open in the editor); `Write` closes each scene in the same call, so its chunks build once.
  - An unassigned `Tilemap` field reads as null to Unity's `==` but not to NUnit's `Is.Null` or `Is.SameAs(null)`; the tests compare Unity objects with `==`.
- **Decisions changed**, with their sections updated: the written hash covers a chunk's content, not its list (Decisions: Layer lists and Hand work; Architecture: Writing); a chunk never written must be empty to be written unforced; forcing writes unchanged chunks too; the generated root is a child of the chunk's grid; the generator records the entries it set.
- **Tests.** `WorldGenerationWriterTests`, 7 tests in 20 s, 16.8 s of it the case on disk (a 2 x 2 world created in a temporary folder, written four times, the whole-world steps run twice and each chunk read back from its scene, then deleted); the other six run in memory on the Overworld's composition, composed once, in about 3 s. The generated case of `ChunkedGroundRenderTests` takes 3.9 s and draws the same per chunk as whole in both looks. `WorldGeneratorTests` builds its generators through a helper it now shares with both (`TestWorldGenerators`). With `PersistentIdTests`, `WorldValidationTests`, `WorldBrushStrokeTests`, `WorldCoastBakeTests` and `AreaGenerationTests`, the last three unchanged, 121 tests passed in 83 s.

### Phase 3: the World Generator window

- [x] `WorldGeneratorWindow`: the preview, the chunk grid with layer counts, locks and wide allowances, the warnings, writing changed, selected or every chunk, then the whole-world steps, with each step's time.
- [ ] The targets measured on the draft Overworld and a synthetic 16 x 16 world, recorded in the notes: all of the Overworld's and the 16 x 16 world's composition; the 16 x 16 world written from nothing with its steps, its scene sizes and the memory with every chunk open are left, the memory having been short (see the notes, and Phase 5's: still short).
- [x] Docs: Architecture.md's Editor tools, and this file's Generating a world checked against the window.

Notes (the measurement helper, under `Assets/Editor/Spike`, was deleted before the commit; editor times are debug code with the editor in the background, through the window's code path, `WorldGeneratorSession`, best of 2 rounds on a fresh scratch world each, the first round in brackets):

- **Built**, in `Assets/Editor/World/Generation` as Architecture: Pieces lists them: `WorldGeneratorWindow`, a thin window over `WorldGeneratorSession`, which holds the logic (the composition and when to compose again, each chunk's state, the `WritePlan` of each button, the write and the whole-world steps after it, the records' locks and wide allowances with undo), and `WorldGeneratorPreview`, the preview's pixels. The writer, the composer and `WorldAuthoring` are unchanged: the window needed no API from them, the states, the plans' checks and the settings key being the session's own. The window's own drawing was compiled but not drawn under a tool (an editor in the background draws no window, and bringing it forward from a script is ruled out); what it calls is what the tests and the measurements ran, and Phase 4 uses the window on the Overworld.
- **Where things differ from Pieces and Behaviour: Generating** (both updated):
  - The logic sits in the session and the preview's pixels in their own class, not in the window, so tests and tools drive every step with the answer passed in (`Write(plan, confirm)`) and no dialog ever runs under a tool; the window's dialog is one such callback.
  - Composing again follows a settings key: the generator's serialized settings without its chunk records and written entries, then of the records only the wide allowances and wilds seeds that are set, the world's id, size and chunks, and each region generator's settings. A change (an inspector edit, an undo, an `ObjectChangeEvents` change to one of those assets, the window gaining focus) is compared against it and recomposes 0.4 s after the last one that changed it. A lock, a write's new hashes and entries, and an undo elsewhere in the editor recompose nothing; a lock only redraws the preview's hatching.
  - Write selected chunks is forced, like Write every chunk: it is how one chunk painted by hand is regenerated without forcing the rest. Each button hands the writer every chunk it covers, so each gets its line, written, created, unchanged, locked or changed by hand, while the question lists only those it writes.
  - Chunk scenes of the world open with unsaved changes stop every write, named, until saved or closed (Decisions changed, below).
  - No progress bar: `EditorUtility.DisplayProgressBar` can come to the front, so the write runs as one call and reports when it ends.
  - Selecting, zooming and picking another asset apply after the GUI pass, so a pass never draws other controls than it laid out.
- **The preview** of the Overworld's Phase 0 settings is Phase 1's signed-off one pixel for pixel at 1x and 3x (an image diff of the two found no pixel apart); the locked chunks' hatching (dark diagonal stripes every 8 cells) and the count of a chunk past 16 in blue once allowed wide were signed off with it.
- **Composing**, `Recompose` as the window runs it (the settings key, `Compose()` with the region generators loaded, each chunk's state), the in-memory Overworld of Phase 0's settings and Phase 1's synthetic 16 x 16 world (the five inland regions repeated on a grid 120 x 88 cells apart, a row of Coasts along the north, roads between neighbours in a row):

  | | Recompose | of it the world's steps | preview drawn | Phase 1 | target |
  |---|---|---|---|---|---|
  | the Overworld (5 x 4, 6 regions, 8 roads) | 0.58 s (0.75) | 0.17 s: roads 71 ms, wilds 64, distances 12, checks 10, land and sea 9, chunk lists and hashes 8 | 30 ms | 0.40 s | under 1 s |
  | a synthetic 16 x 16 (88 regions, 63 roads) | 6.5 s (7.7) | 1.6 s: wilds 0.86, roads 0.25, chunk lists and hashes 0.15, checks 0.14, land and sea 0.10, distances 0.09 | 320 ms | 5.4 s | under 10 s |

  The region generations are the rest (0.40 s and 4.9 s). Both are under their targets, slower than Phase 1 (by 45 % for the Overworld and 20 % for 16 x 16, generations and steps alike) with the machine's commit charge at 96 % during this phase. The synthetic world composes with its 68 warnings, none of them stopping a write. Comparing a change against the settings key takes 1.0 ms for the Overworld and 2.8 ms for the synthetic world.
- **Writing**, on a scratch Overworld under `Assets/Dev/Worlds` at the first free slot ((8192, 0)), its generator an asset beside it and its world created with no chunks, so the first write creates all 20. Per chunk, the median over the 20 from each write's lines:

  | per chunk | median | what it includes | Phase 2 | target |
  |---|---|---|---|---|
  | created and written | 654 ms (669), 512 to 765 | `CreateChunk` 328 to 423 (the scene, its definition, the world's entry, `SaveAssets`), open 7 to 12, write 24 to 240 (filling it, then the renderer's build as the grid comes back, a live coast map for a chunk with ocean), save 112 to 158, close 1 to 3 | 639 ms | under 1 s |
  | rewritten, forced, nothing changed, coast map baked | 134 ms (150), 113 to 171 | open 26 to 39, write 16 to 52, save 68 to 104 | 155 ms | under 0.5 s |
  | rewritten after a change, checked first | 182 ms (220), 163 to 243 | open 27 to 57, reading the scene's hash 1 to 4, write 17 to 31, save 113 to 189 | 200 ms | under 0.5 s |
  | unchanged | not opened | its state, the composed hash compared with its record | | |

  The whole world, `Write(plan, confirm)` from the plan to the report, the whole-world steps included:

  | | the first write | a forced rewrite of every chunk, nothing changed | after the world seed changed |
  |---|---|---|---|
  | writing the chunks | 12.8 s (13.5) | 2.8 s (3.1) | 3.8 s (4.6) |
  | Sync layer lists | 3.5 s | 1.4 s | 1.4 s |
  | Bake changed coasts | 16.4 s, 14 maps of 692 to 960 ms | 1.5 s, none | 14.0 s, 14 maps |
  | Repair ids | 1.5 s | 1.5 s | 1.7 s |
  | Validate | 1.4 s | 1.4 s | 1.6 s |
  | in all | 35.7 s (39.1) | 8.6 s (9.8) | 22.5 s (23.7) |
  | target | under a minute | | under 30 s |
  | Phase 2 | 38 s | its steps 6.0 s | 22 s |

  Every number is under its target and within the noise of Phase 2's.
- **A 16 x 16 world was not written.** Its whole-world steps open all 256 chunks at once, which needs 8 to 10 GB more of the editor (31 to 40 MB a chunk), and only 7.9 GB of the machine's 32 GB was free, with the commit charge at 39.4 of 40.8 GB. So writing one from nothing with its first bake and the other steps (target under 4 minutes; Phase 0 3 to 3.8 minutes), its scene sizes by kind and the editor's memory with every chunk open (Phase 0: 16.6 GB) are left to measure with the memory free; only its composition is measured above.
- **Scene sizes** (YAML, in MB of 2^20 bytes as in Phases 0 and 2, about 270 bytes a tile): the Overworld's region and shore chunks 1.20 to 1.38 MB, shore 1.17 to 1.19, region 1.19 to 1.34, as in Phase 2; its coast maps 43 to 121 KB. The Overworld has no chunk of all wilds or all sea; Phase 2's sizes for those stand.
- **Memory with every chunk open**, the editor's private memory read from outside it: the Overworld's 20 chunks took it from 8.60 to 9.40 GB, 40 MB a chunk (Phase 0: 31 to 34).
- **What surprised.**
  - Locking a chunk with no record at first had the world recomposed: the new record changed the serialized settings though the composer reads nothing of it but its wide allowance and wilds seed, so the key now leaves the records out and adds only those two fields.
  - The Clearing's waystone lies under chunk (1, 1)'s count, which is drawn last; the test picks a waystone clear of the counts. The count stays on top, as in Phase 1's preview.
  - `Process.PrivateMemorySize64` reads 0 under the editor's Mono, so memory was read from outside the editor between calls that opened and closed the chunks.
  - `WorldAuthoring.CreateWorld` over an untitled empty scene leaves the new root scene open in its place, unchanged.
  - Earlier phases' scene sizes are in MB of 2^20 bytes: a chunk of 4096 ocean tiles is 1.10 million bytes, Phase 0's "1.05 MB".
- **Decisions changed**, with their sections updated (Decisions: Tooling; Behaviour: Generating; Architecture: Pieces):
  - Chunk scenes of the world open with unsaved changes stop every write until saved or closed: the writer writes into an open chunk and saves it, and the whole-world steps save every chunk scene they change, so unsaved work there would be saved with them, and a chunk's hand-work check would read paint that is not on disk. An open chunk scene with no changes is written in place and stays open.
  - Write selected chunks is forced.
  - The window's logic is a session object that tests and tools drive, the preview's pixels a static class.
- **Tests.** `WorldGeneratorWindowTests`, 5 tests in 3.9 to 4.4 s, about 3.1 s of it the case on disk (a world of one chunk with no region, whose one warning, no default entry, stops a write: created, written and run through the whole-world steps once, then reseeded, then locked, and deleted); the other four run in memory in about 0.2 s on the Overworld's Phase 0 settings on a world lacking chunk (4, 3), composed once for the fixture. With `WorldGenerationWriterTests` and `WorldGeneratorTests`, unchanged, 24 tests passed in 35 s; `ChunkedGroundRenderTests` was not run, the writer being unchanged.

### Phase 4: the Overworld

- [x] The world created with the World Editor at the first free slot, listed in `GameScenes` but not yet the starting world; its generator asset set from Phase 0's decisions; composed with no warnings, written, its coasts baked, its ids repaired and validated; the scenes checked for churn (a second write changes nothing).
- [x] Every region edge, shore, plaza and road join looked at in the editor, in crisp and soft, and signed off.
- [x] Tests: `OverworldTests` (EditMode) and `OverworldPlayModeTests` (PlayMode).
- [ ] Multiplayer Play Mode check with 2 and 4 players (steps added to this phase's notes): players spread over regions, a client in a region the host is not in, a late joiner in another region, respawns at different regions' waystones.
- [x] Docs: Architecture.md's World and Worlds and streaming (the Overworld), and WorldStreaming.md's Decisions (The first world) and Authoring (Generators).

Notes (the helper that made the generator asset and the captures, under `Assets/Editor/Spike`, was deleted before the commit):

- **Built.** The world with the World Editor's own steps (`WorldAuthoring.CreateWorld`, `CreateChunk` for each of its 20 chunks, `ListInGameScenes`) at the first free slot, (8192, 0): `Assets/Worlds/Overworld`, its root and chunk scenes in the build list, listed in `GameScenes`, Testlands still the starting world. Its generator, `Assets/Settings/Generation/WorldGenerator_Overworld.asset`: Phase 0's six regions and eight road links (Behaviour: The Overworld), every other setting at its default, composing with no warnings. Written through the window's code path (`WorldGeneratorSession`: Write changed chunks, then the whole-world steps): the 20 chunks in 5.6 s, the steps in 20.3 s (Sync layer lists added nothing in 3.4 s, Bake changed coasts baked 14 maps in 14.2 s, Repair ids changed nothing, Validate found nothing). 48 generated spawn points, 19 entries (the six waystones, `clearing` first, then the 13 gates), every chunk 5 to 14 layers and all eighteen materials laid.
- **No churn.** A forced rewrite of every chunk followed by the whole-world steps changed none of the world's 115 files (scenes, definitions, coast maps, metas) or the generator asset, and neither did the test runs. Scenes are 1.17 to 1.38 MB (2^20 bytes) each, 24.9 MB in all; coast maps 43 to 121 KB. `GameScenes` gains the world and the build list its 21 scenes; nothing else outside the world's folder and the generator asset changed.
- **Looks**, rendered in the editor with the chunks open (`TerrainBlendProbe`, one screen pixel per art pixel, animations at 1.5 s), in crisp and soft, 34 spots: the 13 road joins at the ports, the two gravel paths' joins with the roads they end on, the six plazas, the Snowfield's and the Lava Fields' edges with the wilds away from the roads, the west, south, east and north-west shores, both joins with the Coast region's beach and three of the island's corners; and the whole world in 20 chunk renders. Signed off as they are, the two oddities below kept as known gaps for later.
- **What surprised.**
  - In the open sea within about 12 cells of the world's top edge, a few wave crests stand on end, running north and south (at cells x 3, 164, 184, 195, 216 and 234), lined up with the bumps of the Coast region's beach to the south: seams in the baked coast maps far from any shore. They lie 20 cells or more from every walkable cell, past the view (10 cells to either side, 5.6 up and down), so no player sees them (Architecture.md's Known gaps).
  - The step of one row Phase 1 left at the Coast region's joins shows from the beach as a nub of a cell or two of sand poking into the sea at both ends (Known gaps).
  - One of the Clearing's own Weasels stands 2.5 cells from its road to the east gate; the walk test keeps the mobs deactivated as their chunks load.
- **Tests.** `OverworldTests`, 4 tests in 2.6 s, with the chunks opened once for the fixture; `OverworldPlayModeTests`, 8 tests in 40 s, the walk out of the Clearing 12.4 s and each region's respawn 4.2 to 4.8 s. `EditModeWorldNavigation` gained `NavigationOf(world)`, the joined navigation `OverworldTests` walks.
- **Decisions changed**: none.

#### Checking with Multiplayer Play Mode

The Overworld is the starting world since Phase 5, so New Game, Host Game with no save and every joining player the host does not know start at `clearing`, and nothing needs setting up before this check; the virtual players run the main editor's build, so they share its version (1.5). Waystones have no art: each lies on its region's road on a small square of plaza, and counts within two cells (world-local cells: `clearing` (73, 124), `villageForest` (192, 122), `lakeside` (73, 48), `snowfield` (74, 164), `coast` (182, 186), `lavaFields` (166, 48)). Every region's road leaves it through a gate onto a world road: the Clearing's east road leads to the Village Forest, its north gravel path to the road from the Snowfield to the Coast, its south gravel path to the road from the Lakeside to the Lava Fields, its west gate to the west road between the Snowfield and the Lakeside, and the Lava Fields' east road runs up the island's east side to the Coast. To die, stand beside a Weasel and let it attack (about eight seconds); every region has some, at least 14 cells from its waystone. Keep the main editor's Console in view: every step should log no error.

1. Start clean (delete `save.json` and `save.Player2.json` to `save.Player4.json`). Activate Player 2, press Play, Host Game in the main editor, Join Game in Player 2: both stand at `clearing`'s plaza on the Clearing's road.
2. Walk Player 2 east out through the Clearing's east gate, along the world road through the wilds to the Village Forest's street and on to its waystone: "Waystone discovered" floats over Player 2 on Player 2's screen only. The host stays in the Clearing, so Player 2 is in a region the host is not in: its Weasels idle, notice Player 2 and attack it, and Player 2 can kill one, while the host's screen shows only the Clearing.
3. Walk the host north out through the Clearing's north gate, up the gravel path to the road and west along it into the Snowfield, to its waystone: the host sees its own popup only. The two players are now in two regions over 100 cells apart, and both play on without hitches.
4. Let Player 2 die beside a Village Forest Weasel: about three seconds later only Player 2 fades out and back in, at the Village Forest's waystone. Let the host die beside a Snowfield Weasel: only the host fades, coming back at the Snowfield's.
5. Stop Play Mode. Start clean again, activate Player 3 and Player 4 too, press Play, Host Game, and Join Game in Player 2 and Player 3.
6. Spread them: Player 2 south out through the Clearing's south gate and west along the Lakeside road to the Lakeside's waystone; Player 3 the same way south but east along that road through the Lava Fields' west gate to its waystone; the host north to the road and east along it through the Coast's west gate to the Coast's waystone. Each discovers its own.
7. Join Game in Player 4, a late joiner: it arrives at `clearing`, where nobody is now, sees the Clearing's Weasels move and fight it, while the others play on in their regions.
8. Let Player 2 and Player 3 die at about the same time beside their regions' Weasels: each comes back at its own region's waystone, three seconds after its own death; nothing restarts and nobody returns to the menu.
9. Leave the game on Player 3 (in-game menu, Leave game) and Join Game again: it arrives at its place in the Lava Fields. Stop Play Mode in the main editor, press Play and Continue: the host is back at the Coast.

### Phase 5: the starting world and saves across regenerations

- [x] GameScenes' starting world becomes the Overworld at `clearing`.
- [x] The PlayMode tests that start from the menu (Host Game, New Game, Continue) pin Testlands through the test copy of GameScenes.
- [x] A saved place on ground that is no longer walkable moves to the nearest walkable cell once its chunks are up, or to the player's nearest discovered waystone (Architecture: Runtime changes), with the Known gap removed.
- [x] Tests: a `WorldPersistencePlayModeTests` case, a place saved on water starting on the nearest walkable cell.
- [x] The build version bumped (1.4 to 1.5): the worlds changed, and a host and client must agree on them.
- [x] Docs: Architecture.md (GameScenes, Save data, Known gaps), SceneGeneration.md's "What the PlayMode tests rely on".
- [x] The full EditMode and PlayMode suites pass before merging into `main`.

Notes (no spike or measurement code this phase):

- **Built.**
  - **The starting world.** `Assets/Data/Scenes/GameScenes.asset`'s Starting World is the Overworld and its entry `clearing`, set through a `SerializedObject`: two lines of its YAML. New Game, Host Game with no save and a joining player the host does not know start on the Clearing's waystone, on the Clearing's road; `OverworldTests` checks the asset, `OverworldPlayModeTests` the three from the menu.
  - **The test copy of GameScenes.** `GameScenes.CopyStartingAt(source, world, entry)` (internal) returns a copy of the asset, every serialized field of it, starting elsewhere, `DontSave`. `SceneBootTestHelper` registers one into the Main scope on every boot, after the asset and beside the save store it already enqueued, and a dispose callback destroys it with the scope; it registers only in a scope that registers the asset, so no other scope built while the installer is enqueued gets it. By default it starts at Testlands' `start`; `BootIntoMainMenu(store, startingWorldPath, entry)` names another world, or the project's own with a null path. The in-process host and client need nothing of their own: `InProcessClient` resolves `GameScenes` from the Gameplay scope and `InProcessHost` is handed `GameFlow.Scenes`, both the copy. No EditMode test reads the project's starting world but `OverworldTests`, which checks it: `PlayerStartsTests`, `PlayerDeathHandlerTests` and `WorldTripTests` build their own `GameScenes`.
  - **Places no longer walkable.** `WalkablePlaces` (Gameplay scope, `Assets/Scripts/World/Persistence`): `TryMove(world, place, playerId, out moved)` over the loaded world's navigation by the players' movement profile, and the rule itself, `Find`, internal and static over any `INavigation2D`. `WorldStreaming.EnterAsync` asks it, with authority, once the chunks around the start are up, and moves the players it placed on to the place it gives, then waits for the chunks there too; `PrepareAsync(world, position, playerId)` asks it for the joining player once the chunks around its place are up, streams the moved place the same way and returns it, and `GameplayPlayers` spawns the player there. A moved place logs one warning naming the world, the player id and both places in world-local cells (`Sandbox: player ...'s place (40.50, 90.50) (world-local) is not walkable ground any more; it starts at (33.50, 90.50), the nearest walkable cell.`); nothing is logged on the normal path. The radius is `GameplaySettings.SavedPlaceSearchCells` (16, `Min(1)`, with a tooltip, under a Saved places header), written into `GameplaySettings.asset`.
  - **The build version** is 1.5 (Player Settings, `bundleVersion`, the only change to ProjectSettings). Nothing quoted 1.4: `JoinApproval`'s refusal names the host's version as it runs, `JoinApprovalTests` uses versions of its own, and the session tests build theirs from `Application.version`. Architecture.md's Networking says the version went to 1.5 here.
- **Where things differ from Architecture: Runtime changes** (updated):
  - The copy is not `Object.Instantiate` of the asset. VContainer refuses a second singleton registration of the same concrete type ("Conflict implementation type"), so a `GameScenes` cannot be registered beside the asset the way the in-memory save store is beside the file one: every boot failed. The copy is a `GameScenesCopy`, an internal subclass of `GameScenes` in a file of its own, with the asset's fields copied through `JsonUtility` (the runtime counterpart of `EditorUtility.CopySerialized`; its references come back by instance id), and VContainer resolves it over the asset as the later registration of another type. Swapping the asset's registration in the builder for the copy's instead (the builder's public indexer) broke VContainer's diagnostics, which the editor runs with: a registration traced but never built makes every later resolve throw.
  - Every boot gets the copy, not only the tests that start from the menu: those that boot by path meet the starting world too, through a joining player the host does not know (`NetworkWorldsPlayModeTests`' client starts there), `BootIntoWorld`'s fallback to a new game when a boot lands in the menu, and `SceneFlowPlayModeTests`' offline new game in the starting world. Their code is unchanged; only `OverworldPlayModeTests`' two menu cases boot with the project's own starting world.
  - `IWorldSession.PrepareAsync` takes the joining player's id and returns where to spawn it, since the fallback is that player's own nearest discovered waystone.
- **The search radius**, 16 cells by Manhattan distance, as the navigation's own nearest-cell search counts (`TryGetNearestWalkableCell`, rings of growing Manhattan distance). `EnterAsync` and `PrepareAsync` wait for the chunks within the load extents of the place, 22 x 18 cells to either side, so a radius of 18 or less reads only loaded ground (an unloaded cell reads as blocked); 16 keeps two cells inside that. It covers ground that turned to water or wall up to 16 cells deep (11 cells along a diagonal), a coast or a lake moved by a regeneration. A place further than that from every walkable cell lies deep in the sea or a lake, where the nearest shore may be anywhere; the player's nearest discovered waystone, where it would respawn after dying, is a known place to come back to.
- **Multiplayer** (Behaviour: Multiplayer and Architecture.md's join order updated). Only the host, or an offline game, moves a place, once the chunks around it are up on its own machine: its own as the session starts (`EnterAsync`), a joining player's before spawning it (`PrepareAsync`). A client never checks or corrects its own place (`JoinAsync` does nothing of the kind); it, and a late joiner like any other, gets its player spawned at the corrected place and reports that world's chunks as usual. The corrected place is the player's place from then on, so the save keeps it at its next write: for a joining player the save as it enters the world, once a frame after it spawns; for the host's own or an offline player the autosave or the session's end, since the save as it enters the world comes a few frames before the chunks are up, at the old place (a Continue before the next save corrects it again).
- **A place outside every chunk, in a wall, on raised ground.**
  - Outside every chunk: already handled before this phase, and unchanged: `PlayerStarts` starts the player at the starting world's entry, with a warning, as for a world this build does not list.
  - In a wall: a wall's cells are blocked, so the place moves to the nearest walkable cell beside the wall (`WalkablePlacesTests`).
  - On raised ground: a place on a plateau's top is walkable and stays. One on its face (the rows the cliff face covers whole, removed like walls) moves to the nearest walkable cell, on the top or at the foot of the face, whichever is nearer; the cliff's line itself blocks no cell.
  - Anywhere: the nearest walkable cell may be a pocket cut off from the rest of the world (an islet in a lake, a plateau with no ramp), and nothing gets the player out of it (Architecture.md's Known gaps).
  - A place whose chunk has no navigation page yet is kept as it is: nothing can tell.
- **Phase 3's 16 x 16 measurement was left again.** Before the full suites the machine had 12.9 GB of its 32 GB free and the commit charge at 28.6 of a 35.2 GB limit: 6.6 GB of headroom, against about 10 GB for 256 chunks open at once (Phase 4's 40 MB a chunk). It needs the memory free first.
- **Tests.** EditMode: `WalkablePlacesTests`, 4 tests in 0.1 s; `OverworldTests` checks the starting world too (4 tests, 2.7 s). PlayMode: `WorldPersistencePlayModeTests`, 6 tests in 9.3 s, its three new cases 1.5 to 2.0 s each (the pond's middle, 7 cells from its shore, moved to (33.5, 90.5), every nearer cell water; the Overworld's corner of open sea, no walkable cell within 16 cells, to the Lakeside's waystone, nearer than the Clearing's); `OverworldPlayModeTests`, 10 tests in 42 s, its two menu cases under 4.2 s each. The full suites: EditMode 962 tests, 956 passed and 6 explicit ones skipped, in 95 s; PlayMode 82 tests, 81 passed and the explicit benchmark (`NetworkBench`) skipped, in 217 s.
- **What surprised.**
  - VContainer's refusal of a second singleton of one concrete type (above). The save store never met it: the file store and the memory store are two types.
  - One failed boot fails every PlayMode test after it. The Test Framework abandons a test's coroutine when an error is logged, so the `using` around `LifetimeScope.Enqueue` in `SceneBootTestHelper` never disposes, the installer stays in VContainer's static list until a domain reload, and every later boot runs it again and fails on a second in-memory save store ("Conflict implementation type ... MemorySaveStore"). The first run, the copy registered beside the asset, failed 27 of 27, even the two menu cases that register no copy; the second, with the registration swapped, failed its first test on the diagnostics and the other five on the leaked installers. Worth knowing when a whole PlayMode run fails at once.
  - Nothing else: the pages of the chunks around a place were always in when it was checked, and no test needed a frame's wait.
- **Decisions changed**, with their sections updated: every boot through the helper is pinned to Testlands, not only those that start from the menu (Decisions: Starting world); the test copy is a `GameScenesCopy` holding the asset's fields rather than `Object.Instantiate` of it, and `PrepareAsync` returns the place to spawn at (Architecture: Runtime changes).

### Review fixes (after the merge into main)

A review of Phases 0 to 5 on `main` found these, fixed with tests on `feature/character-animation`:
- **Entries move with their ground.** The writer set every composed entry after each write, even when the chunk under an entry's new place was locked, refused, failed or not written, so the default entry (where new games start) and gates could stand on old ground (water, a cliff). An entry now moves only once the chunk under its new place holds the composition's ground; until then it stays where the world has it, or waits to be made, with a warning.
- **A scene that cannot be saved is not recorded.** `SaveScene`'s result was ignored, so a read-only chunk scene was recorded as written and skipped as unchanged from then on. It now fails and is tried again at the next write.
- **No two mobs share a derived id.** A locked, refused or unwritten chunk keeps its old generated spawn points, so a regeneration moving a mob out of it wrote the same id into a second chunk (and Repair ids then re-id'd one of them). Each chunk record now lists the ids written into it (`ChunkRecord.MobIds`), and a composed mob whose id a chunk keeping its content still holds is left out of the chunk it moved to (recorded and reported so) until that chunk no longer holds it; a chunk written before such a chunk is refused in the same write is written again without it. Records written before this list no ids, so they cannot be checked until their chunk is written once more.
- **Deaths follow ids across chunks** (WorldStreaming.md's review fixes), as the Persistent ids decision above always said.
- **The window's settings key reads past nested arrays.** The key that tells whether the composition is current stripped the chunk records with a pattern that stopped at the first `]`, which each record's id list now holds; it scans brackets instead.

### Later

- Region generators that fill a shape the world gives them, so regions meet with no wilds, and placement by rules, for later worlds.
- Transition materials (dry grass towards the Lava Fields, patchy snow towards the Snowfield), rivers and cliffs between regions.
- Interiors: each Village Forest doorstep an exit to an interior world, once buildings have art.
- A marker for waystones on their plazas.
- A content hash per world in the join payload.
- The hybrid of compact chunk data written into tilemaps at load, if scene sizes or player-build loads call for it.

## Testing

- **EditMode:**
  - `WorldGeneratorTests`, on compositions of small worlds of the project's generators:
    - determinism: the same settings give the same composition, cell for cell and in order (cells, mobs, ids, entries, chunk lists), over several world and region seeds, and another seed another one;
    - locality: re-seeding one region changes only the chunks its cells, roads and gates touch and their neighbours, and retuning the wilds of one chunk only that chunk and its neighbours;
    - regions: each region's cells, raised ground, mobs and props are its generator's seed layout moved by its cell, plus only its plaza, but the props whose footprints would cross a chunk's border and those on its plaza (and plants beside it);
    - placement and adjacency rules: a region outside the land, within `regionGap` of another, within `regionShoreGap` of the beach or on a missing chunk is warned about; the Coast region's ocean joins the sea and its beach the world's;
    - continuous ground: every land cell has ground, every cell of the sea ring is ocean, the beach lies between them all round, and across every chunk border the ground runs on without a gap;
    - layer lists: each chunk's list (its cells and its neighbours' 4-cell strips) is in the style's order and within 16, a chunk past 16 is warned about unless allowed wide, and the eighteen materials are all laid;
    - reachability: every waystone, gate and mob connected to the default entry around the props' footprints; ports linked; roads and gates on walkable ground;
    - persistent ids: derived ids are unique within the world, and stay the same when a region is re-seeded or moved, the wilds retuned, or the world composed again;
    - the wilds: patches on the wilds' grass in pieces of four cells or more, two cells or more from every region, the beach and the roads and a cell from another kind, and the wilds mobs on the wilds' grass, 14 cells from every waystone and gate and 10 from every other mob;
    - the wilds' props: on the wilds' grass inside their chunks, a cell from the border, blocking ones past the path clearance from the roads and the beach with no other footprint beside them, plants off the tall grass, all clear of the entries and the mobs and of each other's cells; and edge trees only beside the regions of grass.
  - `WorldGenerationWriterTests`, writing in memory into chunk grids (`WriteChunk`), and once on disk into chunk scenes in a temporary folder (`Write`), deleted after:
    - each chunk's layers in the style's order with its neighbours' border materials, each layer holding exactly its composed cells and tile, raised ground and ramps, its mobs under `GeneratedMobSpawns` with derived ids that `PersistentIdWatcher` keeps, and its props under `GeneratedProps`, each blocking its composed cells, a rewrite reusing every instance;
    - `AreaTerrain` only appended to: an existing list keeps every entry and index, a new base ground layer is appended, a listed layer stays when empty;
    - a locked chunk is never opened, a chunk changed since its last write is refused unless forced, an unchanged chunk is skipped, objects outside the generated roots and entries made by hand are kept;
    - a chunk's hash read back from its grid follows paint on any ground layer (an empty one its `AreaTerrain` keeps included), on raised ground, a generated spawn point moved and a generated prop moved, and nothing else (walls, other tilemaps, hand-made spawn points, props placed by hand);
    - a composition with warnings about layer lists or reachability is refused unless written anyway;
    - a second write changes no file;
    - a written world passes `WorldValidation`, bakes its coasts and keeps its derived ids through Repair ids.
  - `WorldGeneratorWindowTests`, on the logic the window calls (`WorldGeneratorSession`, `WorldGeneratorPreview`), in memory on the Overworld's Phase 0 settings on a world lacking a chunk, and once on disk on a world of one chunk in a temporary folder, deleted after:
    - the preview's pixels: a material in its base colour, a linked port, a region's mob and a wilds mob, a waystone and a gate, a chunk's count in black, red past 16 and blue once allowed wide, and a locked chunk's stripes;
    - the chunk states from the records and the composed hashes alone, opening no scene, and after a write and after a change, a recompose waiting out its delay;
    - the chunks each button writes: changed skips unchanged and locked chunks, selected and every chunk are forced and take unchanged ones, none takes a locked one;
    - a lock or a wide allowance set through the session changes only that record, with undo, and a lock recomposes nothing;
    - a write asks once through the answer it is given, writes nothing answered Write past a warning that stops it, writes answered Write anyway with a line per chunk and per step with its time, and asks nothing with nothing to write or with a chunk scene of the world open with unsaved changes; an unsaved untitled scene stops a write that creates chunks.
  - `ChunkedGroundRenderTests`: a 2 x 2 block of generated chunks around a non-grass region's edge, a road crossing it and the shore draws the same per chunk as whole, in crisp and soft.
  - `OverworldTests`, on the committed Overworld:
    - its chunks match the composition of its generator asset (no drift between the asset and the scenes), locked chunks aside, and its entries start with the composition's;
    - every chunk within 16 layers, and all eighteen materials laid;
    - no coast map stale, and `WorldValidation` passes;
    - with every chunk open, `EditModeWorldNavigation` connects every waystone, gate and mob to the default entry across chunk borders;
    - GameScenes starts at the Overworld's `clearing`, its default entry.
  - `WalkablePlacesTests`, the rule for saved places on a standalone grid: a walkable place stays, one on water or in a wall moves to the nearest walkable cell, one with none within the radius to the nearest discovered waystone, the default entry counting, and one with nowhere to go stays.
  - `AreaGenerationTests`, `WorldBrushStrokeTests` and `WorldCoastBakeTests` stay as they are.
- **PlayMode:**
  - `OverworldPlayModeTests`:
    - the Overworld boots at `clearing`, and the chunks around the player load, link, draw and spawn their mobs;
    - the player walks from `clearing` along the road out of the Clearing to a gate, across its outline, then on to a connected cell of the next chunk, across a chunk border, with no error logged;
    - a respawn at each region's waystone: the player is sent there, discovers it, dies a few cells away and arrives back at it after the respawn delay, one case per region.
    - from the menu with the project's own GameScenes: New Game, and Host Game with no save and a joining player the host does not know, start at `clearing`.
  - `WorldPersistencePlayModeTests`: a place saved on the Sandbox's pond starts on the nearest walkable cell, offline on Continue and on the host for a joining player it knows, and is saved there; a place saved in the Overworld's open sea starts at the player's nearest discovered waystone.
  - The tests that boot into Testlands stay on it, and every boot gets the test copy of GameScenes starting at Testlands' `start` (Phase 5).
- **Manual:** Multiplayer Play Mode, Phase 4's steps.

## Open questions

- **Regions in shapes the world gives.** Should later worlds have region generators that fill a mask, knowing their neighbours, road ports and sea, so regions meet with no wilds? Every generator's ground, edge, road and coast steps would be rewritten for arbitrary shapes, and the seed layouts would no longer carry over.
- **Stale save records.** A death recorded for a spawn point no longer laid is never read but stays in the save. Prune a chunk's records of ids it no longer has as it loads, or leave them?
- **A content hash in the join payload**, against relying on the build version being bumped whenever a world changes.
- **The wilds' mobs.** Only the Weasel exists; how many per chunk, and whether the wilds should have mobs at all before there are more kinds. Phase 1 lays one per chunk by default (20 in the Overworld, against the regions' 28).
- **Gates.** Plain entries that give the tests, the manual checks and later exits named points at the regions' edges. Worth the dozen extra entries in the World Editor's list, or should the tests find the border points another way?
