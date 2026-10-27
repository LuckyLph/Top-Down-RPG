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

Sorting and the budget were chosen when the plan was written, and the phases come in the order measure, then sort. The rest are proposals, confirmed as their phases land.

| Topic | Decision |
|---|---|
| Sorting | By the 2D renderer's custom axis, which it already uses: units, props and effects keep a constant sorting order within their layer, and Unity orders equal orders by height, in floats, with no range to run out of. A prop's sprite sorts at its pivot, its base (Sprite Sort Point: Pivot). A unit of several sprites sorts as one group at its root. An effect sorts at the height of what it hit by where its root stands, with its sprite offset from the root. `Impact`'s offset of 5 becomes a nudge of its sort point toward the camera. Props then need no sorter, so nothing is written per prop and nothing runs when one starts. Fallback, if the axis cannot do something the orders do: keep the sorter but sort by height above the world's origin at 32 a unit, one step a pixel. That fits a 16-row world exactly and leaves no room for offsets. |
| Measuring | A dev-only benchmark (DevTools, so in development builds only) that the PlayMode benchmark and a player share. In a player a command-line flag starts it: it boots into the Overworld and carries the player along the PlayMode benchmark's route. It writes the same line to a file, with each load's times split as WorldGeneration.md's Phase 0 split them, and then quits. The player runs in batchmode (no window, so nothing takes the focus, and CPU only, since no camera renders). Memory is read from outside the process. The scene sizes come from the build report. |
| The budget | None set ahead: Phase 0 records the numbers, and whether Phase 2 goes ahead, and with which step, is decided on them. A 60 Hz frame (16.7 ms) is the reference they are read against. The session's first chunk is reported apart, since it loads behind the loading screen. |
| If it hitches | First spread the activation: a chunk's generated props start inactive and are switched on a batch a frame after the scene loads. Chunks load `loadMargin` ahead of the player, so they are all on before anyone reaches them. Only if that is not enough, generated props become compact data in the chunk (a prefab index and a position each) built from a pool at load. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: a chunk's cost in a player

- [ ] The streaming benchmark moved into DevTools (`StreamingBenchmark`: the route, the stepping and the statistics), with the PlayMode benchmark running it unchanged.
- [ ] A player entry: a command-line flag that runs the benchmark on boot, writes its line and quits, and the load split (request to loaded, activation, ground) per chunk.
- [ ] A Windows development build with the Overworld, its build report's size for every chunk scene, and the benchmark run in batchmode (best of 3), memory read from outside.
- [ ] The numbers recorded here against the editor's, and the decision, taken on them, on whether Phase 2 is needed and with which of its two steps.

### Phase 1: sorting by the custom axis

- [ ] The sorting rules checked in Unity's docs (custom axis, sort point, sorting groups), and a probe at y = 10 and y = 1000 showing two sprites a hundredth of a unit apart sort by height on both.
- [ ] Units (`Player`, `Weasel`), `Arrow` and the effects sort by the axis: a sorting group at the root where a unit has several sprites, sort points at the feet, effects placed by their sorting height. `YPositionSorter` goes, or keeps only what the axis cannot do.
- [ ] Props sort by their pivot with no sorter. The sorting-order overrides go from every chunk and area scene (the Overworld rewritten, hand scenes cleaned of unused overrides), with no other diff.
- [ ] A render test: a unit behind and in front of a tree, an impact on a unit and a death puff, each at a low and a high y in a 16-row world, draw in the same order.
- [ ] CLAUDE.md's 2D line, Architecture.md's sorting section and Known gaps, and PropGeneration.md's sorting Open question updated.
- [ ] Checked in Multiplayer Play Mode with a host and a client in the Overworld: both see units, props and effects sort as before.

### Phase 2: a lighter chunk load (only if Phase 0 says so)

- [ ] A chunk's generated props activated over several frames after its scene loads, before the player can reach them, with the load's frames measured again in the player.
- [ ] Only if that is not enough: generated props as compact data in the chunk, built from a pool, with the writer, the hash, validation and the Prop Placement window following.
- [ ] Checked in Multiplayer Play Mode with a host and a client: a client streaming its own chunks sees and is blocked by the same props as the host, and late joining changes nothing.

## Testing

- EditMode: the shared benchmark statistics (mean, percentile, load windows) on made-up frame times. The sorting setup of every prefab that sorts: a constant order, its sort point or group, no sorter where none is needed. The writer writing no sorting overrides.
- PlayMode: the existing benchmark running through the shared code. The render test of Phase 1 at a low and a high y. The Overworld walk and respawn tests unchanged.
- Manual: Multiplayer Play Mode checks per phase, and the player benchmark (Phase 0, again after Phase 2).

## Later

- Whole-world editor steps working through a few chunks at a time, so a 16 x 16 world fits in the editor's memory.
- Leaner chunk YAML in the project, if import or save times start to matter: props as compact data (Phase 2's second step) would also shrink a dense chunk by about half.
- The GPU side of streaming: draw calls and the first draw of a chunk's ground, which a batchmode player does not render.

## Open questions

- Whether Unity's custom-axis sorting treats a sorting group's position, a sprite's pivot and a tilemap's chunks the way the decision assumes. Phase 1 starts by checking.
- What memory a loaded chunk holds in a player. In the editor it is about 40 MB, mostly each chunk renderer's texture arrays, which are not shared between renderers that list the same materials.
