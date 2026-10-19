# Seamless worlds

Plan for replacing one-area-at-a-time play with seamless worlds that players move between on their own, as in Minecraft.

- A world is a grid of chunk scenes that load and unload around the players, so a player walks a whole world without a loading screen.
- Separate worlds (the overworld, a dungeon, a building's interior) connect through exits. A player who takes one goes through a fade on their own machine.
- Players can be in different worlds at the same time. The host simulates every world that has a player in it.
- There is no party. Players who are together play together; nothing groups them.

[Architecture.md](Architecture.md) describes what exists. This file describes where it is going. Tick off the phase checklist as steps land.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. This plan replaces two of that file's decisions:

- "The party is always in the same area. The host decides area changes" becomes: each player is in one world at a time, and moves between worlds on their own, through exits the host validates.
- "Everyone dead at once restarts the area" goes. Each player dies and respawns on their own (see Death and respawn).

## Where we start

- **One area at a time.** One area scene is loaded at a time, under the Gameplay scene (`Main` → `Gameplay` → `Area_*`). `GameFlow.ChangeAreaAsync` fades out, unloads the area, loads the next one and fades in. Nothing in gameplay calls it yet.
- **Code that assumes a single area** (about 28 runtime files):
  - `GameFlow`: `AreaScene`, `CurrentArea`.
  - `ActiveNavigationGrid` and `ActiveSpawnPoint`, Gameplay-scope holders for "the" area's grid and spawn, read by `PlayerMotor2D`, `PlayerBinder` and `PlayerRespawner`.
  - `AreaEntry` teleports every player to one spawn point.
  - Mobs take the area's `NavigationGrid2D` by injection: `MobController`, `MobPathAgent2D`, `MobPatrolAnchor`, `MobSeparation2D`.
  - `AreaMobSpawner` spawns every mob of the area at `Start`.
  - `NetworkSession` has one `AreaEpoch` and one set of ready clients.
  - `NetworkAreaSync` keeps every machine in the host's area.
  - `GameSaveData` holds one area GUID and spawn id.
- **Party assumptions:**
  - `PlayerDeathHandler` restarts the whole session when every player is dead.
  - `SpawnPoint` places players side by side (`partySpacing`, `GetSlotPosition`).
  - `AreaEntry`, `PlayerBinder` and `PlayerRespawner` place players by their index in the registry.
- **Everything happens to everyone at once:**
  - `TransitionTimeFreeze` stops game time (`Time.timeScale`) during every `GameFlow` transition.
  - Each area scene has its own Global Light 2D.
- **Players have no identity across sessions.** A joining client sends only its build version (`JoinApproval.CreatePayload`).
- **Navigation.** `NavigationGrid2D` builds one flat array over its area's bounds in `Awake`. Its A* (`GridAStarPathfinder2D`) and its regions work on that array. Each cell's `CellData` holds a `List<TerrainType2D>`: fine for an area of a few thousand cells, not for a whole world.
- **Terrain drawing.**
  - `TerrainBlendRenderer` builds one control map over its layers' filled cells and treats an empty cell as nothing. Where an area's ground stops, it draws edges, outlines, shadows and tufts over nothing.
  - The coast map is a distance field 8 texels per cell over the whole control map, reaching 1024 px (32 cells) from the coast. One rebuild takes about 70 ms on the Coast.
- **Scene files.** Tilemaps are saved as YAML in the scene. `Area_VillageForest.unity` (92 x 62 cells, 8 layers) is 1.7 MB.
- **The view** is 20 x 11.25 cells: 640 x 360 reference resolution at 32 px per unit, orthographic size 5.625.
- **Networked positions** are sent at full float precision: half precision is off on the player's and the Weasel's `NetworkTransform`.
- **Persistence.** Runtime terrain edits (`AreaTerrain`, `NetworkAreaTerrain`) are per area and not saved. Nothing about an area is saved but which one the players are in.

## Goals

- A world is up to 16 x 16 chunks of 64 x 64 cells (1024 x 1024 cells), walked without loading screens. Chunks load and unload around the players on every machine, behind the view.
- Players move freely within a world and between worlds, each on their own. The host simulates every world a player is in, and every chunk around every player. Each client loads only its own player's world and chunks.
- Ground, walls, raised ground, navigation and mobs work across chunk borders. Crossing a border shows no seam, no pop-in and no hitch.
- A player's trip to another world never stops the game for anyone else.
- Everything a player expects to persist survives a chunk unloading, a world unloading, quitting and continuing. That includes each player's world and position, killed mobs and their respawn rule, opened containers, runtime terrain edits and discovered waystones.
- Authoring a world stays comfortable: paint and generate across chunk borders in the editor, with scene files of about a megabyte each.

## Non-goals

- A party system: groups, invites, shared HUD frames, shared deaths.
- A world larger than 16 x 16 chunks, or a moving world origin (see World origins).
- Showing anything about players in other worlds, such as who is where. A player in another world is unseen; that UI is later work.
- Paths through unloaded chunks.
- A world map of explored ground. It would need data from unloaded chunks (a baked low-resolution picture per chunk, or a world-level texture); a later plan.
- Streaming assets through Addressables. Chunk scenes are ordinary build scenes until load times say otherwise.
- World generation. A proper world generator, which also brings the six areas into the first world as regions, is its own plan after this one (see Phases: Later). Until then, worlds are painted by hand, and the Scene Generator keeps working on standalone scenes only.

## Decisions

| Topic | Decision |
|---|---|
| Players and worlds | Each player is in one world at a time and moves between worlds on their own. Players spread freely within a world. There is no party. |
| What the host loads | Every world with a player in it, and the union of every player's chunks. A client loads only its own player's world and chunks. A world with no player left in it unloads after the empty-world grace, once its chunks are released. |
| Empty worlds | A machine keeps a world that none of the players it streams for is in, with the chunks it had loaded, for `emptyWorldGrace` (10 s) before releasing and unloading it. Stepping straight back out of a doorway then reloads nothing. |
| The first world | The six generated areas become regions of the first world, through the world generation plan that follows this one. The terrain materials built so far, all eighteen in the style's order, are the first world's. Other worlds get their own materials later. Until world generation exists, worlds are painted by hand. |
| Unit of streaming | Fixed-size chunks: one scene per chunk, 64 x 64 cells, on a world grid of up to 16 x 16. A missing chunk (open sea, the void beyond the land) has no scene and loads nothing. |
| Between worlds | A player walks into a world exit. The host checks the trip is allowed and loads the target world's chunks around the entry for its simulation. The player's own machine then fades, loads and arrives. Nobody else sees a fade or stops. Dungeons and interiors are worlds of their own, often a single chunk. |
| World origins | Every world has a fixed, unique origin in one global layout, set on its `WorldDefinition` and used to build its scenes. All loaded scenes share one 2D physics world and the camera draws them all, so two worlds loaded together on the host must never overlap. Worlds are at least 2048 units apart, twice a world's size, so their bodies, colliders, physics queries, sprites and lights are always far apart. Every machine uses the same coordinates, so networked positions need no conversion. The layout stays within 16 384 units of the origin, where float precision is about 0.002 units, a sixteenth of an art pixel. Small worlds (interiors, dungeons) share regions of the layout. Separate physics scenes per scene do not work instead: a world spans several scenes, and players live in the Gameplay scene. |
| Coordinates inside a world | Chunk scenes hold their content at world positions. Every chunk's `Grid` sits at its world's origin and its tilemaps hold world cells, so chunk (cx, cy) owns cells `[cx * 64, cx * 64 + 63]` x `[cy * 64, cy * 64 + 63]` relative to that origin. |
| Paths | Only within loaded chunks of the walker's own world. An unloaded chunk counts as blocked. Click-to-move goals are on screen, so this never limits a player. A mob that would leave the loaded region is leashed home. |
| Load rule | Each machine keeps an interest rectangle around each player it streams for: the view (20 x 12 cells) plus `loadMargin` cells on every side. Every chunk the rectangle overlaps loads. A chunk unloads once no rectangle widened by a further `unloadHysteresis` overlaps it, so walking back and forth over a border never reloads it. A rectangle of about 52 x 44 cells overlaps at most four chunks, so the host has at most 16 loaded for four players, however many worlds they are in. |
| Ownership | Everything placed in a chunk belongs to the chunk holding its root. A blocking prop's footprint, a spawn point and a container lie inside their chunk; the editor tools refuse anything else. A mob belongs to the chunk of its spawn point, its home chunk, for its whole life, and never leaves its world. |
| Leash | A mob never goes more than `leash` cells beyond its home chunk's edge. It gives up and returns home when its path or its target would take it further. With `leash` + the mob's detection radius under `loadMargin` + half the view, a mob near any player always has its home chunk loaded, so a chunk never unloads under a mob that is still in play. |
| Simulation and presentation | Loading a world or chunk for the game's logic happens in the background, with no fade and no change to time. A fade, blocked input and waiting for chunks belong to one machine's own player only. In a session, a trip never stops game time (`TransitionTimeFreeze` holds only offline, or not at all). Lighting, ambience and music follow the local camera's world: only that world's global light is on. |
| Player identity | Each install has a player id, a GUID made once and kept in `settings.json`, sent in the join payload. The host's save keys each player's world, position and discovered waystones by player id. A player the host does not know starts at the starting world's entry. |
| Death | Each player on their own: a dead player respawns at the nearest waystone they have discovered in their world, after the respawn delay. No rule looks at the other players. |

## Terms

- **World**: a `WorldDefinition` asset (see Architecture) and its scenes.
- **World origin**: a world's position in the global layout.
- **Chunk**: one 64 x 64 cell scene of a world, named `World_<name>_<cx>_<cy>.unity`.
- **Chunk id**: (world, cx, cy).
- **Interest rectangle**: the cells around a player within which every chunk of that player's world is loaded on a machine.
- **Player's world**: the world a player is in, a networked value on the player that every machine reads.
- **World entry**: a named place a world is entered at, from an exit, a new game or Continue. It replaces the area's `SpawnPoint` ids.
- **Waystone**: a world entry a player discovers by walking to it. Respawns use waystones.
- **Persistent id**: a stable id for a placed thing whose state persists, such as a mob spawn point or a container. It is a `PersistentId` component with a GUID string assigned once by the editor.
- **Player id**: the install's GUID that identifies a player across sessions.

## Behaviour

### Starting and joining

- **New game or Continue offline, or hosting:** the local player enters its saved world and position, or the starting world's entry. The local machine fades, loads that world's root scene and the chunks around the player, places it and fades in.
- **A client joining:**
  1. It connects with its player id.
  2. The host loads that player's saved world and position (or the starting entry) for its simulation, then spawns the player there and sets its world.
  3. The client sees its player's world, loads that world and its chunks, and fades in once they are ready.

### Walking across a world

- Each machine recomputes its interest rectangles when a player it streams for moves into a new cell. This is a cheap rectangle test against the chunk grid, with no per-frame allocation.
- Chunks load additively one at a time, so two big loads never land on the same frame. A chunk that leaves every rectangle is unloaded once loads have finished.
- A chunk ready on this machine has:
  - its ground drawn, if it is in the local camera's world;
  - its navigation joined to its loaded neighbours;
  - its mobs spawned or arrived (see Mobs);
  - its persisted state applied.
- The load margin keeps the next chunk loading well before it comes into view, at walking speed and at the fastest movement in the game. Phase 2 sets `loadMargin` from measured load times.

### Travelling between worlds

1. A player walks into a world exit, a trigger in a chunk naming a target world and entry. Its own machine asks the host for the trip. On the host, or offline, the request is local.
2. The host checks the trip:
   - the exit exists in the player's world;
   - the player is at the exit;
   - the target is a listed world.
3. The host loads the target world, if no other player is in it, and its chunks around the entry, in the background.
4. The host sets the player's world and tells the player's machine to go.
5. The player's machine:
   - fades out and blocks its input;
   - keeps the old world, untouched, for the empty-world grace, then releases its chunks (see Networking) and unloads it. A trip back within the grace reuses it;
   - loads the new world's root scene and its chunks around the entry;
   - moves its player to the entry, since a client owns its player's movement;
   - reports the chunks ready and fades in.
6. The host unloads the old world once no player has been in it for the empty-world grace and no client still holds any of its chunks.

No other machine fades or pauses. On the host, its own player's trip is a local fade only: the host keeps simulating every other world.

### Death and respawn

- A dead player respawns at the nearest waystone they have discovered in their world, after `GameplaySettings.RespawnDelaySeconds`.
- A world's default entry counts as discovered from the start.
- Nothing happens when every player is dead at once: each still respawns on their own, and no world restarts.

## Architecture

### Scenes and scopes

```
Main (MainLifetimeScope)
└── Gameplay (GameplayLifetimeScope)
    ├── World_<a> (WorldLifetimeScope, parent = Gameplay)              -- one per world with a player in it
    │   └── World_<a>_<cx>_<cy> (ChunkLifetimeScope, parent = World_<a>) -- any number, streamed
    └── World_<b> ...
```

- **`WorldLifetimeScope`** replaces `AreaLifetimeScope`. It registers:
  - the world's navigation (`WorldNavigation`) and its terrain drawing (see Terrain);
  - the world's chunk registry (which chunks are loaded and ready);
  - the world state (see Persistence);
  - the entries and waystones.

  Disposing it is the cleanup hook for the whole world.
- **`ChunkLifetimeScope`** registers what a chunk scene contains:
  - its navigation page;
  - its mob spawner (`ChunkMobSpawner`);
  - its terrain sync;
  - its persistent objects.

  Disposing it unregisters them.
- **`Worlds`** (Gameplay scope) is the registry of loaded worlds: `TryGet(worldId, out LoadedWorld)`, each with its scope, navigation, entries and chunk registry. It replaces `ActiveNavigationGrid` and `ActiveSpawnPoint`. A player finds its own world's navigation through it, by its player's world.
- **`ChunkStreamer`** lives in the Gameplay scope, since it streams across worlds (see Streaming).

### World data (Core)

- **`WorldDefinition`** is a ScriptableObject. It holds:
  - a stable world id (its asset GUID);
  - the root scene's `SceneDefinition`;
  - the world's origin;
  - the chunk size (64);
  - the size in chunks (at most 16 x 16);
  - a `SceneDefinition` per present chunk;
  - the world's ground materials, for its terrain drawing;
  - its entries: name, position, whether it is a waystone.

  The editor checks that no two worlds' rectangles in the global layout come within 1024 units of each other.
- **`GameScenes`** lists worlds instead of areas, with a starting world and entry. `SceneBuildValidator` checks that every chunk scene of a listed world is in the build.
- **`ChunkCoords`** is static chunk arithmetic: cell to chunk, chunk bounds, rectangle overlap, world origins. It is plain C# and tested in EditMode.

### Streaming

- **`ChunkStreamer`** (Gameplay) is the decision part. It is plain C#: each streamed player's world and interest rectangle in; worlds and chunks to load and unload out, with the hysteresis. It is tested with fakes.
- **Who a machine streams for:**
  - offline or on the host, every registered player;
  - on a client, its own player.
- **`ChunkLoader`** (Core) is the doing part.
  - It loads one scene at a time additively through `SceneLoader`: a world's root scene under the Gameplay scope, a chunk under its world's scope (`LifetimeScope.EnqueueParent`, as `GameFlow` does now).
  - It reports when each is up.
  - It refuses to unload a chunk whose networked objects are still visible to some client (see Networking).
- **`GameFlow`** keeps the menu, starting a session, and the local player's fade. It no longer loads gameplay content itself. `TransitionTimeFreeze` follows only offline transitions, or goes.

### Presentation per machine

- **`LocalWorldPresentation`** (Gameplay) follows the local player's world:
  - turns on that world's global light and turns off every other loaded world's. URP warns about more than one global light per sorting layer and lights everything with one of them, whichever world it is in (Phase 0);
  - points the camera and the HUD at it;
  - enables only the chunk renderers of that world within the local player's interest rectangle.

  Worlds the host loads for other players are simulated but never drawn on the host, since they are thousands of units away from its camera.

### Terrain

Decided in Phase 0: **a renderer per chunk, with a skirt of its neighbours' border cells and a baked coast map.** See Phase 0's notes for the measurements.

- **One renderer per chunk.** Each chunk scene keeps a `TerrainBlendRenderer` on its grid over its own ground layers, as an area does today. Its chunk quads cover only its own 64 x 64 cells.
- **The skirt.** The renderer's control map and elevation map also read a 4-cell strip along the border of each loaded neighbour: the neighbour's ground layers and raised ground, matched by material.
  - The renderer fills the strip when a neighbour loads, follows that neighbour's tile changes inside the strip (runtime edits, and painting in the editor), and empties it when the neighbour unloads.
  - Only drawing reads the skirt. Navigation never does.
  - A chunk at the edge of the loaded set has no skirt on its far side, so it draws its ground's edge there over nothing. That border lies outside every interest rectangle, at least `loadMargin` beyond the view, so no one sees it.
  - A chunk's layer list holds the materials of its own cells and of its neighbours' border strips, in the style's order. The World Editor keeps it that way. A material that turns up in a skirt anyway, through a runtime edit, rebuilds the renderer with it.
- **The coast map is baked per chunk.** The waves read a distance field that reaches 32 cells from the coast, more than any skirt. Computed per chunk, it drew three ocean pixels wrong on the Coast, and open sea far from the shore would be worse. Computing it at runtime is also slow: 22 µs per cell in the editor. So the World Editor bakes each chunk's coast map from the whole world into a texture asset beside its scene, and the renderer uses that texture instead of building one.
  - Runtime edits never touch the sea (the ocean is not an `AreaTerrain` layer), so the baked map stays right.
  - A world without waves bakes nothing.
- **Only the local world draws.** `LocalWorldPresentation` enables chunk renderers only in the local player's world and within its interest rectangle. The host builds no ground for the chunks it loads for other players.
- **Memory.** Per-chunk renderers cost more texture memory than one renderer would, since each holds its own arrays (about 3 MiB per layer at 512 px). Only the few chunks around the local player have them.

Walls stay dual-grid tilemaps per chunk. Their render tilemap is offset by half a cell, so a chunk's walls need its neighbours' border wall cells to pick the corner sprites along the border. Each chunk's `DualGridTilemapModule` reads the neighbouring chunk's data tilemap for those cells when it is loaded, and refreshes the border when a neighbour loads. Phase 2 checks a wall drawn across a border.

### Navigation

- **`WorldNavigation`** (one per world, in its scope) holds a page per loaded chunk of that world.
  - A page is that chunk's 64 x 64 cells, built when the chunk loads. It reads the chunk's ground layers, walls, raised ground and prop footprints, as `NavigationGrid2D.BuildGrid` does today.
  - Pages live in a pool of slots. A world cell's index is `slot * 4096 + local index`, so A* keeps its flat, stamped arrays, sized to the pool and never allocating once warm.
  - The pool is shared by every loaded world: 16 chunks at most, plus the ones waiting out their hysteresis.
- **Borders.**
  - A step from one page into another reads the neighbour page directly.
  - A cell beside an unloaded chunk counts as beside a blocked cell for the edge cost.
  - Cliff sides along a border are computed when both pages are loaded.
  - Loading or unloading a page bumps the navigation's version, so paths re-check themselves as they do after a terrain edit.
- **Regions.** Each page floods its own regions. A union-find over the regions meeting along loaded borders, rebuilt when a page loads, unloads or changes, gives the O(1) `AreCellsConnected` that `CanReach` and strict requests need.
- **The API.** `NavigationGrid2D`'s public API moves to `WorldNavigation`: walkability, costs, line costs, nearest walkable cell, `CrossesCliff`, `MoveOffCliffFace`.
  - `PathFollower2D` and the mob components take their world's `WorldNavigation` by injection from their chunk's scope.
  - `PlayerMotor2D` looks its own up through `Worlds` by its player's world.
- **What stays.** `NavigationTerrainSource2D` and `TerrainMovementProfile2D` stay as they are. `NavigationGrid2D` either becomes the page builder or is replaced by it; Phase 3 decides by reading the code.
- **Cell data.** `CellData`'s per-cell `List<TerrainType2D>` becomes flat per-page arrays (a terrain index per layer bit), so a page allocates its arrays once.

### Mobs

- **Spawning.** `ChunkMobSpawner` replaces `AreaMobSpawner`.
  - On the host or offline, it spawns each of its chunk's mob spawn points when the chunk becomes ready. A spawn point whose mob the world state marks as dead and not yet due to return is skipped.
  - It despawns the chunk's mobs before the chunk unloads.
  - Clients register the mob prefabs per chunk as they do per area today.
- **Leash.** A mob carries its home chunk and its leash. Its state machine treats a target or path beyond the leash like a lost target and walks home. `MobPatrolAnchor` picks patrol points inside the home chunk.
- **Targets.** A mob considers only players in its own world. Worlds are far apart, so distances already rule the others out; the filter makes it explicit and saves the distance checks.
- **Death.** A mob's death writes its spawn point's persistent id and the time into its world's state (see Persistence).

### Networking

Scene management stays off: each machine loads its own worlds and chunks, as it loads areas today.

- **Each player's world** is a `NetworkVariable` on the player object, written by the host. It replaces area announcements, `AreaEpoch` and `NetworkAreaSync`. A client loads its own player's world when that value changes. Other players' worlds matter to it only for what it is shown.
- **Trip requests.** A client asks for a trip with an RPC naming the exit's persistent id. The host checks it, prepares the target world and sets the player's world, which tells the client to go (see Travelling between worlds).
- **Per-chunk readiness** replaces the single ready set. Every message carries the chunk id (world, cx, cy) and the player's trip count, so a report from before a trip is ignored.
  - A client reports `ChunkReady(trip, chunk)` once a chunk is up and its prefabs are registered. The host shows that client every spawned object whose home chunk that is.
  - Before unloading a chunk, a client sends `ChunkReleasing(chunk)` and waits. The host hides that chunk's objects from it (`NetworkHide`) and acknowledges, and only then does the client unload, so a client never destroys a spawned object locally.
  - The visibility check becomes "the client is ready for this object's home chunk", and `IClientReadiness` gains the chunk.
- **Players** live in the Gameplay scene and stay visible to every client, whatever their world. A client sees a far-away player as an object thousands of units away, never drawn, so movement replication keeps working across trips with nothing to re-show.
- **Host interest.** The host streams around every player, so a client's player always walks on ground the host has loaded, in a world the host simulates. The client's own movement uses the client's own loaded pages, which always include its surroundings.
- **Runtime terrain edits** go per chunk: each chunk's `AreaTerrain` and its sync object. On the host, a chunk's persisted edits are applied when it loads, before its sync object spawns, so a client that becomes ready for it receives them as a late joiner does today.
- **Join payload.** `JoinApproval`'s payload carries the player id after the version.

### Persistence

- **World state.** `WorldState` (host or offline only) holds a record per chunk of a world, keyed by persistent id:
  - killed mobs and the time they died, in the host's game clock;
  - opened containers, later;
  - runtime terrain edits.

  The states of every world visited stay in memory for the session, loaded or not. A chunk reads its record when it loads and writes changes as they happen.
- **Save format.** `GameSaveData` version 2 holds:
  - per player id: world id, position, discovered waystones;
  - per world id visited: its world state.

  A version 1 save is ignored with a warning, as other versions are today.
- **When the game saves.** When any player enters a world, on a timer (`autosaveInterval`), and when the session ends: the menu, quit, or the host leaving. It still writes only where `IGameAuthority.IsAuthoritative`. A client never saves.
- **Mob respawn.** A killed mob comes back once `respawnDelay` of game time has passed and its chunk loads again. A loaded chunk does not spawn it in front of a player. The delay is a `MobConfig` value, so bosses can stay dead.

### Authoring (Editor)

- **World Editor window.**
  - It creates a world at a free origin in the global layout, its root scene and its chunk scenes, and registers them in the `WorldDefinition`, the `SceneDefinition`s and the build list.
  - It opens a chunk with its neighbours additively, so the Scene view shows the world around the chunk being edited.
  - Opening two worlds at once is harmless, since their origins differ.
- **Terrain Brush across borders.** It splits a stroke's cells by chunk and writes each part to that chunk's layer of the material, adding the layer to a chunk that lacks it. Its preview shows on the renderer of each chunk it covers. A neighbour's skirt shows the stroke once it is painted.
- **Layer lists and coast maps.** The World Editor keeps each chunk's layer list covering its neighbours' border materials. When the sea or its shore changes, it bakes the coast maps of the chunks within 32 cells of the change.
- **Generators.** Writing generated layouts into a world, and the first world's regions, belong to the world generation plan. The Scene Generator window keeps working on standalone scenes until then.
- **Validation.**
  - A prop whose footprint crosses a chunk border, or anything placed outside its chunk, is refused with an error that names it.
  - Every `PersistentId` must be unique within the world.

## Removals and migrations

- **Area types.** `AreaLifetimeScope`, `AreaEntry`, `IAreaEntry`, `AreaEntryRequest`, `AreaMobSpawner`, `ActiveNavigationGrid`, `ActiveSpawnPoint`, `AreaAnnouncement`, `NetworkAreaSync` and `AreaEpoch` go once worlds replace them.
- **`GameFlow`.** `ChangeAreaAsync`, `AreaLoading` and `AreaTransition` go. `GameFlow` keeps the menu and sessions.
- **Party assumptions:**
  - `PlayerDeathHandler`'s restart when every player is dead goes.
  - Placing players by their index in the registry (`AreaEntry`, `PlayerBinder`, `PlayerRespawner`) goes.
  - `SpawnPoint`'s slots stay only to keep players arriving at the same entry from standing on each other.
- **`TransitionTimeFreeze`** stops holding the freeze in a session.
- **The six areas.** Their scenes, their `SceneDefinition`s and the area template go with the area path (Phase 9). Their generator assets and seeds stay, so world generation can bring them back as regions of the first world.
- **Tests:**
  - `AreaGenerationTests` keeps testing the generators' layouts, and the writer on a standalone grid.
  - `AreaTemplateTests` goes with the template.
  - The PlayMode tests that boot into the Clearing boot into a hand-painted test world instead. It keeps the margins in SceneGeneration.md's "What the PlayMode tests rely on".
- **Architecture.md.** Its "Scenes and boot flow", "Composition / DI", "Networking", "Save data", "Navigation and pathfinding" and "World and areas" sections are rewritten as each phase lands.
- **Multiplayer.md.** Its Areas and Death decisions point here, and its Phase 5 notes link here.

## Phases

### Phase 0: spike

- [x] Ground split into chunks: `Area_Coast` (a road, a trail, the beach, the ocean and the plateau) split into its four 64-cell chunks, in memory, each a grid with its own renderer, drawn per chunk and compared with the area drawn whole.
- [x] A second world at another origin: a second global light loaded alongside the first.
- [x] Terrain A, one renderer over a window: the cost of a chunk arriving, of a rebuild when its layer list changes, and of its coast map.
- [x] Terrain B, a renderer per chunk with a skirt: the seam in crisp and soft with skirts of 2, 3 and 4 cells, the ocean's waves across the border, the plateau's cliff across it, and the cost of one chunk's build.
- [x] Decide the terrain approach: B, with live skirts and baked coast maps (see Architecture: Terrain).

Moved out of the spike, since they need the real pieces: navigation across pages goes to Phase 3, whose tests check a path across a border against the single-grid path; chunk scene load times go to Phase 2, where `loadMargin` is tuned with `ChunkLoader` on real chunk scenes.

Notes (the spike's code was not kept; times are the editor's, debug code, best of 2 or 3):

- **The skirt, per chunk.** Each chunk drawn over only its own cells, stitched, against the Coast drawn whole (2144 x 1536 pixels, animation at a fixed time):
  - 4 cells: 3 pixels differ, all ocean, 13 to 20 cells from a border, in crisp and in soft (the soft look's first render after switching came out black, as TerrainBlending.md's notes warn, and the second matched).
  - 3 cells: those 3, plus 3 more, one at a border.
  - 2 cells: 85, 80 of them at a border.
  
  The plateau, which crosses a chunk border, draws its cliff the same either way. The three ocean pixels are the coast map, which each chunk built from its own cells and skirt.
- **One chunk's build** (its quarter of the Coast and the skirt): 2.6 to 3.0 ms without the ocean, 31 ms with it, nearly all of that its coast map. The Coast whole: 83 ms.
- **The coast map** costs about 22 µs per cell: 364 ms for a window of 128 x 128 cells, 810 ms for 192 x 192. Under A it is rebuilt on every change to the coast in the window. Under B, a full 64-cell chunk with its skirt would take about 115 ms. Hence the bake.
- **A's window** (128 x 128 cells, 6 materials, the ocean among them):
  - a chunk arriving: its 9922 cells set on the window's tilemaps in 19 ms, with the change callbacks handled synchronously;
  - the flush that follows: 365 ms, the coast map;
  - a full rebuild, as when its layer list changes: 389 ms, about 25 ms without the coast map;
  - at 192 x 192: 861 ms and about 50 ms.

  Besides the cost, a window spanning regions can list more than 16 of the first world's 18 materials and switch the whole view to the wide variant (about twice the cost in crisp, TerrainLayers32.md), and the Terrain Brush's preview would need a chunk's tilemap mapped to the window's. B keeps every chunk narrow and each build small.
- **Two global lights:** URP logs "More than one global light on layer Default / Terrain / Entities for light blend style index 0" and the ground kept the first light's colour: a red global light 4096 units away changed nothing. Lighting has to follow the local world (`LocalWorldPresentation`). Bodies, physics queries and sprites 2048 units apart never reach each other, so they needed no test.

### Phase 1: worlds and chunks, offline

- [ ] `WorldDefinition` with origins, `ChunkCoords`, world root and chunk scene setup, `WorldLifetimeScope`, `ChunkLifetimeScope`, `Worlds`.
- [ ] Each player's world (offline: a plain field behind the same interface the `NetworkVariable` will fill).
- [ ] `ChunkStreamer` and `ChunkLoader` stream around the local player offline, with hysteresis and one load at a time.
- [ ] `GameFlow` starts a session into a world, and `BootFlow` boots into one opened in the editor.
- [ ] `LocalWorldPresentation`: global light, camera and HUD follow the local player's world.
- [ ] A hand-made 2 x 2 dev world and a one-chunk dev interior.

### Phase 2: terrain across chunks

- [ ] Chunk renderers with live 4-cell skirts from their loaded neighbours, raised ground included, and layer lists covering the neighbours' border materials.
- [ ] Coast maps baked per chunk from the whole world by an editor command (the World Editor's from Phase 5), used instead of building one.
- [ ] A test drawing ground per chunk against the same ground drawn whole, in crisp and soft, as the spike compared them.
- [ ] Walls drawn across a border.
- [ ] Chunk scene load and activation timed with ground, walls and mob spawn points; `loadMargin` and `unloadHysteresis` tuned from those times.

### Phase 3: navigation across chunks

- [ ] `WorldNavigation` with pages in a shared slot pool, cross-border steps, cliff sides along borders, and region union-find.
- [ ] Players and mobs use it, and `ActiveNavigationGrid` goes.
- [ ] Partial paths stop at the loaded edge.
- [ ] A path across a border matches the single-grid path on the same cells.

### Phase 4: mobs per chunk

- [ ] `ChunkMobSpawner` spawns and despawns with its chunk.
- [ ] Home chunk and leash, patrol inside the home chunk, and targets only in the mob's world.

### Phase 5: authoring

- [ ] World Editor window: create a world at a free origin and its chunks, and open a chunk with its neighbours.
- [ ] Terrain Brush across chunk borders.
- [ ] Ownership, `PersistentId` and origin validation.
- [ ] A hand-painted test world with the Clearing's PlayMode margins, painted with the tools above.

### Phase 6: world exits, offline

- [ ] World exits and entries, travelling with a local fade and no time freeze in a session.
- [ ] The old world unloads once no player has been in it for `emptyWorldGrace` (10 s), and a trip back within that reuses it.

### Phase 7: multiplayer

- [ ] Each player's world as a `NetworkVariable`, and trip requests over RPC.
- [ ] The host streams for every player in every world, and a client for itself.
- [ ] `ChunkReady` and `ChunkReleasing` with `NetworkShow`/`NetworkHide` per client, tagged with the trip count.
- [ ] Per-chunk terrain sync.
- [ ] The player id in the join payload, and a joining client spawns in its saved world.
- [ ] Remove `NetworkAreaSync`, area announcements and `AreaEpoch`.
- [ ] Multiplayer Play Mode check with 2 and 4 players: spread over one world, then in different worlds, travelling while others fight.

### Phase 8: persistence

- [ ] `WorldState`, mob deaths and respawn delay, and terrain edits per chunk.
- [ ] Save version 2 keyed by player id and world id, autosave, and Continue into each player's saved world and position.

### Phase 9: death, respawn and cleanup

- [ ] Waystones: discovery per player, and respawn at the nearest one.
- [ ] Remove the restart when every player is dead, and the remaining area and party code.
- [ ] The PlayMode tests boot into the test world, and the area scenes and template go.
- [ ] Rewrite Architecture.md's sections, and update Multiplayer.md's decisions.

### Later: world generation (its own plan)

- A world generator.
- The six areas brought into the first world as regions.
- Where each region sits, and what joins them.

## Testing

- **EditMode:**
  - `ChunkCoords` and origin checks;
  - the streamer's decisions, including hysteresis, the empty-world grace, several players, players far apart and players in different worlds;
  - `WorldNavigation` paths and regions across pages, and pages loading and unloading under a path;
  - the trip and the readiness and release handshake as state machines without Netcode types, including a report from before a trip arriving after it;
  - `WorldState` round trips;
  - save version 2 parsing.
- **PlayMode:**
  - walk the player across a border and back with no hitch over a frame budget and the expected chunk set;
  - two worlds loaded at once with a body and a mob in each, never touching;
  - a mob leashed at its chunk's edge;
  - a killed mob staying dead after its chunk unloads and loads, and returning after its delay;
  - a terrain edit surviving its chunk unloading;
  - a trip offline leaving game time running in the session case.
- **Online tests.** An in-process client test where the host reloads Gameplay freezes the editor. Trips no longer reload Gameplay, so chunk and trip tests stay within one session.
- **Manual:** Multiplayer Play Mode:
  - two players walk to opposite corners of a world and back;
  - one enters a dungeon while the other keeps fighting outside;
  - the host enters a dungeon while a client fights outside.

## Open questions

- **Chunk size:** 64 cells, revisited only if Phase 2's load times call for smaller chunks. 32 cells would load less per chunk, but give four times as many scenes per world (1024).
