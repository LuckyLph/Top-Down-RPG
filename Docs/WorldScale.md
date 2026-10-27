# World scale

Plan for worlds up to the 16 x 16 chunk limit. It covers two things. Sprites must sort correctly anywhere in a world that size, which the sorting order cannot do past 327 units. And a chunk's cost must be measured in a player build, now that the Overworld's chunks hold up to 524 props: its scene size, load, activation and memory. Then we fix what those numbers say needs fixing. [Architecture.md](Architecture.md) describes what exists today. [WorldStreaming.md](WorldStreaming.md), [WorldGeneration.md](WorldGeneration.md) and [PropGeneration.md](PropGeneration.md) are the plans this builds on. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Sorting is local and visual only, and props are static scenery, the same on every machine, so nothing here is networked. Each phase is still checked with a host and a client, since every machine sorts and loads chunks on its own.

## Where we start

- **Sorting.** `YPositionSorter` writes `offset - round(y * 100)` into a renderer's or sorting group's sorting order. That order is 16-bit (-32768 to 32767), so it runs out about 327 units from y = 0.
  - Worlds sit at origins along x (the Overworld at (8192, 0)), so a world's y runs from 0 to its height. The Overworld is 4 chunks (256 units) tall. A world 6 chunks tall would stop sorting in its sixth row, and the limit is 16 rows (1024 units).
  - The 2D renderer (`Assets/Settings/Renderer2D.asset`) already sorts by a custom axis, (0, 1, 0), so sprites of equal sorting order are ordered by their height. Every sprite's sort point is still Center.
  - Six prefabs carry the sorter: `Player`, `Weasel` (seven sprite renderers, no sorting group), `Arrow`, `Impact` (offset 5, in front of what it hits), `DeathPuff` and `Prop`, which `BlockingProp` and every prop variant inherit (`sortsOnce`). `EffectPlayer` sorts an effect at the struck unit's height (`SetSortingReferenceY`). A prop's shadow draws on the `Terrain` sorting layer, under every unit.
  - In edit mode the sorter writes each prop's sorting order into its scene as a prefab override (Architecture.md's Known gaps).
- **What a chunk scene holds** (the Overworld's, as YAML in the project). The scenes are 1.41 to 2.70 MB each, 38.7 MB in all.
  - Chunk (3, 2), the densest forest, holds 524 props in 1.30 MB and 4895 tile records in 1.25 MB. That is about 2.5 KB a prop: twelve overrides, of which only the position and the sorting order say anything. A tile record is about 260 bytes.
  - Git stores both committed versions of the twenty chunks in 1.85 MB, since the YAML compresses about 35 times.
- **A chunk in a player** (WorldGeneration.md's Phase 0, before props: a Windows development player in batchmode, CPU only).
  - A 1.44 MB YAML chunk was 180 KB in the build.
  - Its activation took under 1 ms, against the editor's 18 to 26 ms. The ground's build took 3 ms for 6 layers and 12 ms for 14 layers, plus 54 ms for the first chunk of a session.
- **A chunk in the editor with props** (PropGeneration.md's Phase 6, `OverworldPlayModeTests.Benchmark_StreamingAlongARouteThroughEveryRegion`).
  - A chunk load's worst frame (of the load and the 3 frames after it) went from about 43 ms to about 80 ms, and the worst frame of the route from 68 to 94 ms to 116 ms. Draw calls went from 20 to 96, and allocation from 35 to 42 KB a frame.
  - A dense chunk activates about a thousand GameObjects: each prop is a root, a `Visuals` child and a `Shadow` child.
- **Whole-world steps in the editor** (sync layer lists, bake coasts, repair ids, validate) open every chunk at once, at about 40 MB of editor memory each. That is 0.8 GB for the Overworld and about 10 GB for a 16 x 16 world.

## Goals

- Sprites sort correctly anywhere in a world of 16 x 16 chunks, as precisely as today (a hundredth of a unit or better), with nothing written per object to encode its height.
- A chunk's cost in a player, measured on the Overworld with its props:
  - its scene's size in the build;
  - the time from a load request to drawn;
  - the worst frame of a load;
  - the memory a loaded chunk holds;
  - the first chunk of a session.
- If the numbers show a load hitching in the player, the hitch is brought down without changing what a chunk holds or how it is authored.

## Non-goals (for now)

- Replacing chunk scenes with another format (data assets, DOTS subscenes). Streaming, per-chunk networking, hand-work locks, exits and persistence all build on them, and the build already stores them compactly.
- Addressables for chunk scenes (WorldStreaming.md's Non-goals).
- Worlds larger than 16 x 16 chunks.
- The whole-world editor steps working through a few chunks at a time (Later). They matter for a world much larger than the Overworld, which no plan makes yet.
- Fewer draw calls for the forest (PropGeneration.md's Open questions), unless Phase 0's numbers make it the cost that matters.

## Decisions

Sorting and the budget were chosen when the plan was written, and the phases come in the order measure, then sort. Phase 2's aim (the ground build) was chosen on Phase 0's numbers. The rest are proposals, confirmed as their phases land.

| Topic | Decision |
|---|---|
| Sorting | By the 2D renderer's custom axis, which it already uses: units, props and effects keep a constant sorting order within their layer, and Unity orders equal orders by height, in floats, with no range to run out of. A prop's sprite sorts at its pivot, its base (Sprite Sort Point: Pivot). A unit of several sprites sorts as one group at its root. An effect sorts at the height of what it hit by where its root stands, with its sprite offset from the root. `Impact`'s offset of 5 becomes a nudge of its sort point toward the camera. Props then need no sorter, so nothing is written per prop and nothing runs when one starts. Fallback, if the axis cannot do something the orders do: keep the sorter but sort by height above the world's origin at 32 a unit, one step a pixel. That fits a 16-row world exactly and leaves no room for offsets. |
| Measuring | A dev-only benchmark (DevTools, so in development builds only) that the PlayMode benchmark and a player share. In a player a command-line flag starts it: it boots into the Overworld and carries the player along the PlayMode benchmark's route. It writes the same line to a file, with each load's times split as WorldGeneration.md's Phase 0 split them, and then quits. The player runs in batchmode (no window, so nothing takes the focus, and CPU only, since no camera renders). Memory is read from outside the process. The scene sizes come from the build report. |
| The budget | None set ahead: Phase 0 records the numbers, and whether Phase 2 goes ahead, and with which step, is decided on them. A 60 Hz frame (16.7 ms) is the reference they are read against. The session's first chunk is reported apart, since it loads behind the loading screen. |
| If it hitches | Lighten the ground build, the largest part of a load in the player (Phase 0). The texture arrays, and what else of the build depends only on the materials, are built once per terrain style, holding every material the style orders, and shared by every renderer. Each renderer maps its own layers onto the shared slices, so a load builds only its control map, its lookup and its chunk quads. Sharing only between renderers that list the same materials would barely help: the Overworld's 20 chunks list 16 different sets. Props stay GameObjects: in the player they add about 2.5 ms to the densest chunk's load. Spreading their activation over frames, or making them compact data, is Later. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: a chunk's cost in a player

- [x] The streaming benchmark moved into DevTools (`StreamingBenchmark`: the route, the stepping and the statistics), with the PlayMode benchmark running it unchanged.
- [x] A player entry: a command-line flag that runs the benchmark on boot, writes its line and quits, and the split of each load (its chunk and props, its worst frame, the scene activation and the ground build in it).
- [x] A Windows development build with the Overworld, the size of every chunk scene in it, and the benchmark run in batchmode (best of 3), memory read from outside.
- [x] The numbers recorded here against the editor's, and the decision, taken on them, on whether Phase 2 is needed and with which of its two steps.

- **The decision.** Phase 2 goes ahead, aimed at the ground build instead of the props. The ground build is the largest part of a load in the player (10 ms on average, up to 20 ms) and does not depend on props. Each loaded chunk's renderer also holds about 36 MB of graphics memory for arrays of materials that other chunks hold too. Props add about 2.5 ms to the densest chunk, so Phase 2's two prop steps went to Later (Decisions: If it hitches).

- **The benchmark.** `StreamingBenchmark` (DevTools) holds what the PlayMode benchmark did: the route, the warm-up, the stepping, the statistics.
  - It adds, from `ProfilerRecorder`s, each load's scene activation (Unity's `Application.LoadLevelAsync Integrate` marker) and ground build (`TerrainBlendRenderer.Build`, a marker around the renderer's build when it is enabled). It also adds the memory used (`System Used Memory`, `Gfx Used Memory`) once the start is warm and at most, and, in `LoadDetails`, each load's chunk, its props and its times.
  - The props are counted once a load's window has closed, so the count costs no measured load frame.
  - `WorldStreaming.IsSettled(world, position)` lets it wait for the chunks around the start without the test assembly's access to the streamer.
  - The PlayMode benchmark now only boots, runs it and appends its two lines.
- **The player entry.** `StreamingBenchmarkRunner` (DevTools, development builds only) runs when the command line holds `-streamingBenchmark <file>` (and optionally `-streamingBenchmarkLabel <label>`). It goes through the following steps:
  - Before the first scene loads, it enqueues in-memory save and settings stores (`BenchmarkSaveStore`) for the Main scope, so a run reads and writes no save.
  - It waits for the main menu and its transition, then reads the memory used there.
  - It presses New Game, which starts at the Overworld's Clearing.
  - It runs the benchmark, appends its lines to the file and quits, with exit code 1 and the reason on a failure.

  The first run failed because New Game was pressed while the menu's own transition still ran, and `StartNewGame` ignores a press during a transition. The runner now waits for the transition to end and reports a press that starts nothing.
- **The build.** A Windows development player (Mono, D3D11, every build scene): 300 MB, built in 66 s, and 16 to 21 s for the later scripts-only builds.
  - As in WorldGeneration.md's Phase 0, the build wrote a preloaded asset into `ProjectSettings.asset` and enabled `UnityConnectSettings` while it ran. Both were reverted after each build.
  - The player's `boot.config` was given `player-connection-listen-address=127.0.0.1` and `player-connection-multicast=false`, so no firewall prompt took the focus.
  - Run in batchmode: no window and no camera renders, so frame times are CPU only and the GPU's side of a load (the first draw of a chunk's ground and props) is not in them.
- **Chunk scenes in the build** against the YAML in the project:

  | | YAML | Build |
  |---|---|---|
  | A chunk scene | 1.41 to 2.70 MB | 262 to 871 KB |
  | All twenty | 38.7 MB | 9.8 MB, and 9.4 MB of coast maps beside them (thirteen shore chunks, about 700 KB each, stored uncompressed) |
  | A prop (fitted over the twenty) | about 2.9 KB | about 1.4 KB |
  | A tile record | about 290 bytes | about 36 bytes |

  In the build a prop instance is flattened into its three GameObjects and their components, so the props are most of a dense chunk: chunk (3, 2) holds 524 of them in about 0.7 of its 871 KB.
- **Streaming in the player** (three runs, which agree within a millisecond on every mean; run 3 shown), against the editor through the same code (run 2; run 1, with the machine busier, was half as slow again):

  | | Editor | Player |
  |---|---|---|
  | Frame, mean | 4.97 ms | 0.56 ms (CPU only) |
  | Frame, 99th percentile | 23.9 ms | 6.5 ms |
  | Worst frame | 120.1 ms | 34.4 ms |
  | A load's worst frame (of the load and the 3 after it), mean of 15 | 83.9 ms | 21.0 ms |
  | Scene activation in a load, mean (range) | 45.6 ms (26 to 69) | 7.1 ms (3.9 to 17.8) |
  | Ground build in a load, mean (range) | 11.9 ms (5.8 to 22.3) | 10.1 ms (4.8 to 19.8) |
  | Allocated a frame | 44.4 KB | 24.7 KB |

  - **Props cost little in the player.** Across the loads, activation is 3.9 to 7.5 ms for the sparse chunks (120 to 227 props) and 6.2 to 8.9 ms for the dense ones (430 to 524). That is about 0.005 ms a prop, so a dense chunk's props add about 2.5 ms to its load. In the editor activation grows by about 0.1 ms a prop, twenty times as much, from 26 ms (120 props) to 69 ms (524). The editor's doubled hitch was the editor's.
  - **One chunk activates slowly whatever its props.** Chunk (3, 3) (174 props, the Coast region's north with the open sea) activates in 17.4 to 21.7 ms in every run, where chunks like it take 4 to 7 ms. Its load is the run's worst frame (34.4 to 39.9 ms). Why is not known yet.
  - **The ground build is the biggest part of a load**, and it does not depend on props. It grows with a chunk's layers and its coast map: 3.8 to 4.8 ms for chunk (3, 2) (8 layers, no sea); 6.6 to 7.9 ms for 8-layer shore chunks; 17.5 to 19.8 ms for the Lakeside's chunks (1, 0) and (1, 1), with 14 and 13 layers.
  - **Against a 60 Hz frame**, 3 of a run's 15 loads have a worst frame under 16.7 ms, and every load is under 40 ms. Sharing a frame, activation and ground make about 17 ms on average before anything else in that frame.
  - The session's first chunk (54 ms of ground in WorldGeneration.md's Phase 0) loads before the warm-up, behind the loading screen, and is not in these numbers.
- **Memory in the player.**
  - In the process, the main menu used 303 to 307 MB (78 MB of graphics). With the 4 chunks around the Clearing warm it used 449 to 450 MB (222 MB of graphics), and at most 483 MB (250 MB) along the route.
  - That is about 36 MB of graphics memory for each loaded chunk, the world's root and the player included. Most of it is each chunk renderer's texture arrays, which are built per renderer and not shared between renderers listing the same materials, as in the editor (Open questions).
  - From outside, the process's private memory peaked at 1030 to 1060 MB and its working set at 482 to 485 MB.

### Phase 1: sorting by the custom axis

- [ ] The sorting rules checked in Unity's docs (custom axis, sort point, sorting groups), and a probe at y = 10 and y = 1000 showing two sprites a hundredth of a unit apart sort by height on both.
- [ ] Units (`Player`, `Weasel`), `Arrow` and the effects sort by the axis: a sorting group at the root where a unit has several sprites, sort points at the feet, effects placed by their sorting height. `YPositionSorter` goes, or keeps only what the axis cannot do.
- [ ] Props sort by their pivot with no sorter. The sorting-order overrides go from every chunk and area scene (the Overworld rewritten, hand scenes cleaned of unused overrides), with no other diff.
- [ ] A render test: a unit behind and in front of a tree, an impact on a unit and a death puff, each at a low and a high y in a 16-row world, draw in the same order.
- [ ] CLAUDE.md's 2D line, Architecture.md's sorting section and Known gaps, and PropGeneration.md's sorting Open question updated.
- [ ] Checked in Multiplayer Play Mode with a host and a client in the Overworld: both see units, props and effects sort as before.

### Phase 2: a lighter ground build

Follows [TerrainBlending.md](TerrainBlending.md) for everything it changes in the terrain renderer.

- [ ] What the ground build spends its time and graphics memory on, part by part, in the player: the control map, the texture arrays, the lookup, the material, the chunk quads and the coast map. Measured on the Lakeside's chunks (1, 0) and (1, 1) (14 and 13 layers), chunk (3, 2) (8 layers, no sea) and a shore chunk, and with it how the shader reaches a layer's slice of the arrays.
- [ ] The parts that depend only on the materials (the texture arrays at least) built once per terrain style and shared by every renderer drawing with that style. They are kept while any renderer uses them and released with the last, each renderer mapping its layers onto them. In the editor a change to a material or the style rebuilds them, as it rebuilds a renderer now.
- [ ] The ground drawn the same, pixel for pixel, in both looks: `ChunkedGroundRenderTests`, `TerrainBlendRendererTests`, and the `ShaderCompile` category if the shader changes.
- [ ] The player benchmark run again: each load's ground build and the graphics memory a chunk holds, against Phase 0's. If a load's worst frame is still well over a 60 Hz frame, the next step is chosen on the numbers: spreading the build over frames, or chunk (3, 3)'s slow activation.
- [ ] TerrainBlending.md and Architecture.md updated.
- [ ] Checked in Multiplayer Play Mode with a host and a client in the Overworld: both draw the same ground while streaming, and a client's chunks unload and load again with their ground.

## Testing

- EditMode: the shared benchmark statistics (mean, percentile, load windows) on made-up frame times. The sorting setup of every prefab that sorts: a constant order, its sort point or group, no sorter where none is needed. The writer writing no sorting overrides.
- PlayMode: the existing benchmark running through the shared code. The render test of Phase 1 at a low and a high y. The Overworld walk and respawn tests unchanged.
- Manual: Multiplayer Play Mode checks per phase, and the player benchmark (Phase 0, again in Phase 2).

## Later

- Whole-world editor steps working through a few chunks at a time, so a 16 x 16 world fits in the editor's memory.
- Props switched on over several frames after their chunk's scene loads, or kept as compact data in the chunk (a prefab index and a position each) built from a pool, if their 2.5 ms on the densest chunk's load comes to matter. Compact props would also shrink a dense chunk's YAML by about half, and its scene in the build to about a sixth (chunk (3, 2)'s 524 props are about 730 of its 871 KB).
- The GPU side of streaming: draw calls and the first draw of a chunk's ground, which a batchmode player does not render.

## Open questions

- Whether Unity's custom-axis sorting treats a sorting group's position, a sprite's pivot and a tilemap's chunks the way the decision assumes. Phase 1 starts by checking.
- What memory a loaded chunk holds in a player. Phase 0 measured about 36 MB of graphics memory a chunk, as in the editor (about 40 MB in all), mostly each chunk renderer's texture arrays, which are not shared between renderers that list the same materials.
- Why chunk (3, 3) activates in 17 to 22 ms in the player where chunks like it take 4 to 7 ms (Phase 0).
