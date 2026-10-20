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
| Load rule | Each machine keeps an interest rectangle around each player it streams for: the view (20 x 12 cells) plus `loadMargin` cells on every side. Every chunk the rectangle overlaps loads. A chunk unloads once no rectangle widened by a further `unloadHysteresis` overlaps it, so walking back and forth over a border never reloads it. With the margins Phase 2 measured for (`loadMargin` 12, `unloadHysteresis` 8), the interest rectangle is 45 x 37 cells and the one a chunk must leave to unload 61 x 53, both under a chunk's 64, so a player never holds more than four chunks and the host at most 16 for four players, however many worlds they are in. |
| Ownership | Everything placed in a chunk belongs to the chunk holding its root. A blocking prop's footprint, a spawn point and a container lie inside their chunk; the editor tools refuse anything else. A mob belongs to the chunk of its spawn point, its home chunk, for its whole life, and never leaves its world. |
| Leash | A mob never goes more than `leash` cells beyond its home chunk's edge. It gives up and returns home when its path or its target would take it further. With `leash` + the mob's detection radius under `loadMargin` + half the view (on the view's shorter side: 12 + 6 = 18 cells), a mob near any player always has its home chunk loaded, and with `leash` + its lose distance under the unload rectangle's reach (26), that chunk never unloads under a mob that is still chasing. |
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
- **`Worlds`** (Gameplay scope, Phase 1) is the registry of loaded worlds: `TryGet(definition, out LoadedWorld)`, each with its definition, root scene, scope, lighting, navigation (Phase 3) and loaded chunk scenes, and events as they come and go. A player finds its own world's navigation through it, by its player's world (`PlayerNavigation`, which replaced `ActiveNavigationGrid` in Phase 3); `ActiveSpawnPoint` goes with the waystones (Phase 9).
- **`PlayerWorlds`** (Gameplay scope, Phase 1) records each player's world. Offline it is set directly; Phase 7 fills it from the host's `NetworkVariable`.
- **The streaming** lives in the Gameplay scope, since it streams across worlds (see Streaming).

### World data (Core)

- **`WorldDefinition`** is a ScriptableObject. It holds:
  - a stable world id (its asset GUID);
  - the root scene's `SceneDefinition`;
  - the world's origin;
  - the size in chunks (at most 16 x 16; the chunk size, 64, is `ChunkCoords.Size` for every world);
  - a `SceneDefinition` per present chunk;
  - the world's ground materials, for its terrain drawing;
  - its entries: name, position, whether it is a waystone.

  `WorldLayout` (Editor) checks that no two worlds' rectangles in the global layout come within 1024 units of each other and that each stays within 16 384 units of the origin; the World Editor checks it as a world is made and validated, and the build refuses a layout that breaks it.
- **`GameScenes`** lists worlds beside the areas (Phase 1), and instead of them with a starting world and entry once the areas go. Every world scene has a `SceneDefinition` beside it, so `SceneBuildValidator` already checks it is in the build.
- **`ChunkCoords`** is static chunk arithmetic: cell to chunk, chunk bounds, rectangle overlap, world origins. It is plain C# and tested in EditMode.

### Streaming

- **`ChunkStreamer`** (Gameplay) is the decision part. It is plain C#: each streamed player's world and interest rectangle in; worlds and chunks to load and unload out, with the hysteresis. It is tested with fakes.
- **Who a machine streams for:**
  - offline or on the host, every registered player;
  - on a client, its own player.
- **`WorldStreaming`** (Gameplay, Phase 1) is the doing part, and the `IWorldSession` `GameFlow` enters and leaves worlds through.
  - Each frame it gives the streamer the streamed players' worlds and positions, and runs its next action through `SceneLoader`: a world's root scene under the Gameplay scope, a chunk under its world's scope (`LifetimeScope.EnqueueParent`, as `GameFlow` does), with the world's definition or the chunk's identity enqueued into the new scope.
  - It records what is up in `Worlds`.
  - From Phase 7 it does not unload a chunk whose networked objects are still visible to some client (see Networking).
- **`GameFlow`** keeps the menu, starting a session, and the local player's fade. It no longer loads gameplay content itself. `TransitionTimeFreeze` follows only offline transitions, or goes.

### Presentation per machine

- **`LocalWorldPresentation`** (Gameplay) follows the local player's world:
  - turns on that world's global light and turns off every other loaded world's: each root scene holds its lights under a `WorldLighting` object it shows or hides. URP warns about more than one global light per sorting layer and lights everything with one of them, whichever world it is in (Phase 0);
  - points the camera and the HUD at it;
  - enables only the chunk renderers of that world within the local player's interest rectangle, keeping each until it leaves the rectangle widened by the unload hysteresis, so walking back and forth over the rectangle's edge rebuilds nothing. A chunk scene's terrain starts disabled in play mode (its `WorldChunk` disables it before it builds), so nothing is built for a chunk that will not be drawn.

  Worlds the host loads for other players are simulated but never drawn on the host, since they are thousands of units away from its camera.

### Terrain

Decided in Phase 0: **a renderer per chunk, with a skirt of its neighbours' border cells and a baked coast map.** See Phase 0's notes for the measurements.

- **One renderer per chunk.** Each chunk scene keeps a `TerrainBlendRenderer` on its grid over its own ground layers, as an area does today. Its chunk quads cover only its own 64 x 64 cells.
- **The skirt.** The renderer's control map and elevation map also read a 4-cell strip along the border of each loaded neighbour, the diagonal ones included: the neighbour's ground layers and raised ground, matched by material. Each chunk's grid has a `WorldChunk` naming its world and coordinates; when enabled it finds the neighbours whose scenes are open (loaded by the streaming at runtime, opened beside it in the editor) and links with them, and when disabled or unloaded it unlinks, so the same code runs in play and in edit mode.
  - The renderer fills the strip when a neighbour loads, follows that neighbour's tile changes inside the strip (runtime edits, and painting in the editor), and empties it when the neighbour unloads.
  - Only drawing reads the skirt. Navigation never does.
  - A chunk at the edge of the loaded set has no skirt on its far side, so it draws its ground's edge there over nothing. That border lies outside every interest rectangle, at least `loadMargin` beyond the view, so no one sees it.
  - A chunk's layer list holds the materials of its own cells and of its neighbours' border strips, in the style's order. The Terrain Brush (and `WorldAuthoring.Paint`) keeps it that way for the open neighbours and `SyncLayerLists` for a whole world, which the World Editor runs. A material that turns up in a skirt anyway, through a runtime edit, rebuilds the renderer with a layer for it (its saved list is untouched).
- **The coast map is baked per chunk.** The waves read a distance field that reaches 32 cells from the coast, more than any skirt. Computed per chunk, it drew three ocean pixels wrong on the Coast, and open sea far from the shore would be worse. Computing it at runtime is also slow: 22 µs per cell in the editor. So the World Editor bakes each chunk's coast map from the whole world into a texture asset beside its scene, and the renderer uses that texture instead of building one.
  - Runtime edits never touch the sea (the ocean is not an `AreaTerrain` layer), so the baked map stays right.
  - A world without waves bakes nothing.
- **Only the local world draws.** `LocalWorldPresentation` enables chunk renderers only in the local player's world and within its interest rectangle. The host builds no ground for the chunks it loads for other players.
- **Memory.** Per-chunk renderers cost more texture memory than one renderer would, since each holds its own arrays (about 3 MiB per layer at 512 px). Only the few chunks around the local player have them.

Walls stay dual-grid tilemaps per chunk. Their render tilemap is offset by half a cell, so a chunk's walls need its neighbours' border wall cells to pick the corner sprites along the border. Each chunk's `DualGridTilemapModule` reads the neighbouring chunk's data tilemap for those cells when it is loaded, and refreshes the border when a neighbour loads or unloads. A render tile belongs to the chunk holding its cell; a chunk draws those along its upper and right border too, but only while the chunk they belong to is not loaded, so each is drawn once.

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
- **The API.** `INavigation2D` is what paths, players and mobs use: walkability, costs, line costs, nearest walkable cell, connectivity, `CrossesCliff`, `MoveOffCliffFace`, the pathfinder and the version. `NavigationMap2D` implements it over pages in a pool; `WorldNavigation` is a world's map, and an area's `NavigationGrid2D` is a map of a single page until the areas go (Phase 9).
  - `PathFollower2D` and the mob components take an `INavigation2D`; from Phase 4 a chunk's mobs take their world's `WorldNavigation` by injection from their chunk's scope.
  - `PlayerMotor2D` looks its own up through `PlayerNavigation`: its world's through `Worlds` by its player's world, or the area's.
- **What stays.** `NavigationTerrainSource2D` and `TerrainMovementProfile2D` stay as they are. `NavigationGrid2D` is the page builder: it sits on every chunk's grid, holding what the page reads (ground layers, walls, raised ground, obstacle layers).
- **Cell data.** `CellData`'s per-cell `List<TerrainType2D>` became flat per-page arrays (the layer bits of the highest priority per cell, a layer's terrain type found by its bit), so a page allocates its arrays once.

### Mobs

- **Spawning.** `ChunkMobSpawner` replaces `AreaMobSpawner`.
  - On the host or offline, it spawns each of its chunk's mob spawn points when the chunk becomes ready. A spawn point whose mob the world state marks as dead and not yet due to return is skipped.
  - It despawns the chunk's mobs before the chunk unloads.
  - Clients register the mob prefabs per chunk as they do per area today.
  - Clients register the mob prefabs per chunk through `ChunkNetworkPrefabs`, which keeps Netcode's one handler per prefab and sends each copy to its chunk (see Phase 4's notes).
- **Leash.** A mob carries its home chunk and its leash. Its state machine treats a target or path beyond the leash like a lost target and walks home, ignoring targets until it has idled there. `MobPatrolAnchor` picks patrol points inside the home chunk.
- **Targets.** A mob considers only players in its own world: the world's scope registers `WorldPlayers`, the players in that world, as its `IPlayerRegistry`. Worlds are far apart, so distances already rule the others out; the filter makes it explicit and saves the distance checks.
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

- **World Editor window** (`Tools/TopDownRPG/World/World Editor`, `WorldEditorWindow` over `WorldAuthoring`, `WorldLayout`, `PersistentIds` and `WorldValidation`).
  - It creates a world at the first free origin of the global layout (or one typed in and checked), its root scene and its chunk scenes, registers them in the `WorldDefinition`, the `SceneDefinition`s and the build list, and lists the world in `GameScenes`.
  - It opens a chunk with its eight neighbours additively, or alone, so the Scene view shows the world around the chunk being edited, and closes them again, asking to save first. Opening two worlds at once is harmless, since their origins differ.
  - It gives the selected open chunk walls or raised ground, for the Terrain Brush to paint.
  - It edits the world's entries, the first the default, and marks waystones.
  - It runs the whole-world steps: syncing the layer lists, baking the coast maps of the chunks whose sea changed, repairing persistent ids and validating.
- **Terrain Brush across borders.** On a chunk, the brush splits a stroke's cells by chunk and writes each part to that chunk's tilemap of the same kind as the paint target (`WorldBrushStroke`): the layer of the target's material (added to a chunk that lacks it, and to each open neighbour whose skirt the stroke reaches), the walls, or the raised ground. Box fills, erases and flood fills go across borders the same way, the flood fill over the open chunks' cells. A part whose chunk is not open is skipped with a warning, and the stroke is one undo step across scenes. Its preview shows on the renderer of each chunk it covers. A neighbour's skirt shows the stroke once it is painted. A standalone area is painted as before.
- **Layer lists and coast maps.** The brush keeps each chunk's layer list covering its open neighbours' border materials, and Sync layer lists covers the whole world. Each chunk records what its coast map was baked from (a hash of the sea within the bake's reach), so Bake changed coasts bakes again only the chunks whose sea or shore changed.
- **Generators.** Writing generated layouts into a world, and the first world's regions, belong to the world generation plan. The Scene Generator window keeps working on standalone scenes until then.
- **Validation** (the World Editor's Validate, `Tools/TopDownRPG/World/Validate Worlds` and a pre-build check).
  - A blocking prop whose footprint crosses a chunk border, or anything placed outside its chunk (a spawn point, a mob spawn point, a prop, an object with a persistent id), is refused with an error that names it.
  - Every mob spawn point in a chunk has a `PersistentId`, every id is set, and every id is unique within the world. The editor gives a new or duplicated object its id as it is made.
  - Worlds keep the layout.
- **The edit-mode navigation.** Outside Play Mode the open chunks of a world join their navigation pages, as in play, so the movement blocker gizmo draws a border the ground crosses as the game reads it.

#### Authoring a world

1. Open the World Editor (`Tools/TopDownRPG/World/World Editor`). Under New world, type a name (letters and digits) and the size in chunks; the origin is the first free slot of the layout. Keep "Create every chunk" and "List in Game Scenes" ticked for an ordinary world, untick the first for a world with holes and create its chunks one by one from the grid. Create world.
2. Pick a chunk in the grid and Open with neighbours. Each chunk starts as a grid at the world's origin with an empty grass layer, a terrain renderer and a navigation grid.
3. Paint with the Terrain Brush (the Tile Palette's default brush) on any open chunk's tilemap: a ground layer, the walls or the raised ground. A stroke goes into whichever chunks it covers. To paint a material the chunk has no layer for, pick its tile and press the palette's "Add the <material> layer" button. Walls and raised ground start on one chunk with the window's Add walls and Add raised ground (shown for the selected chunk while it is open and lacks them); after that the brush gives them to the chunks a stroke reaches. Open the chunks around the area being painted: a part of a stroke on a chunk that is not open is skipped, with a warning.
4. Place props with the Prop Placement window, and mob spawn points under a `MobSpawns` object in each chunk scene. Each must stand on its own chunk's cells, and a blocking prop's footprint must not cross a border. Mob spawn points get their `PersistentId` as they are made.
5. Fill in the entries in the window: the first is where a new game starts; tick Waystone for the ones players discover.
6. Run Sync layer lists, then Bake changed coasts when the world has an ocean, then Repair ids and Validate. Save the scenes, and check `git diff` for churn in the chunk scenes (wall render tiles redrawn as neighbours open) before committing.

## Removals and migrations

- **Area types.** `AreaLifetimeScope`, `AreaEntry`, `IAreaEntry`, `AreaEntryRequest`, `AreaMobSpawner`, the area half of `PlayerNavigation` (which replaced `ActiveNavigationGrid` in Phase 3), `ActiveSpawnPoint`, `AreaAnnouncement`, `NetworkAreaSync` and `AreaEpoch` go once worlds replace them.
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
  - The PlayMode tests that boot into the Clearing boot into the hand-painted test world, Testlands, instead. It keeps the margins in SceneGeneration.md's "What the PlayMode tests rely on" (`TestlandsPlayModeTests`).
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

- [x] `WorldDefinition` with origins, `ChunkCoords`, world root and chunk scene setup, `WorldLifetimeScope`, `ChunkLifetimeScope`, `Worlds`.
- [x] Each player's world (offline: `PlayerWorlds`, which Phase 7 fills from the host's `NetworkVariable`).
- [x] `ChunkStreamer` and `WorldStreaming` stream around the players offline, with hysteresis, the empty-world grace and one load at a time.
- [x] `GameFlow` starts a session into a world (`StartWorldGameAsync`, and `RestartAsync` for a party wipe), and `BootFlow` boots into one opened in the editor.
- [x] `LocalWorldPresentation`: the global light follows the local player's world; the camera and the HUD already follow the local player.
- [x] The Sandbox (2 x 2 chunks at origin (2048, 0)) and the SandboxInterior (one chunk at (4096, 0)), made with `WorldAuthoring` and painted by script.

Notes:

- **Where things went.** The loading half of the streaming is `WorldStreaming` itself, not a separate `ChunkLoader`: one class runs the streamer's actions and is the `IWorldSession`, and nothing else loads chunks. `WorldDefinition` keeps no chunk size of its own; every world uses `ChunkCoords.Size`.
- **Areas stay** beside worlds until Phase 9: `GameFlow` runs either an area session or a world session, and leaving a world session (to the menu, a new game or another world) unloads its chunks and worlds before anything else.
- **Teleports.** The streamer reads the players' positions each frame, so a teleport (the entry, a test) streams like walking; `EnterAsync` waits until the chunks around the entry are up before the fade in.
- **Tests:** `ChunkCoordsTests`, `ChunkStreamerTests` and `WorldStreamingPlayModeTests` (see Architecture.md's Tests).

### Phase 2: terrain across chunks

- [x] Chunk renderers with live 4-cell skirts from their loaded neighbours, raised ground included, and layer lists covering the neighbours' border materials.
- [x] Coast maps baked per chunk from the whole world by an editor command (the World Editor's from Phase 5), used instead of building one.
- [x] A test drawing ground per chunk against the same ground drawn whole, in crisp and soft, as the spike compared them.
- [x] Walls drawn across a border.
- [x] Chunk scene load and activation timed with ground, walls and mob spawn points; `loadMargin` and `unloadHysteresis` tuned from those times.
- [x] Only the local world draws: `LocalWorldPresentation` enables chunk renderers only in the local player's world and near it.

Notes:

- **Where things went.**
  - `WorldChunk` (on each chunk's grid) links a chunk with its open neighbours. It finds them by their scenes rather than through `Worlds`' events, so linking works the same in the editor, where chunk scenes are opened by hand. `WorldAuthoring.MarkChunks` gave the Sandbox's chunks theirs, and `CreateChunk` adds it.
  - The skirt lives in the renderer, not on tilemaps: `TerrainControlMap` reads extra sources, each counted only in its strip, and `TerrainElevationMap` raised ones. Navigation sits on the layer tilemaps, so it never sees a skirt cell.
  - A chunk's control map covers a fixed area, its cells and the skirt plus the margin (74 x 74 cells), so the coast map baked for it lines up with what the shader samples (`_TerrainCoast` over the control map's rectangle). Its quads cover only its own cells.
  - The coast bake (`WorldCoastBake`, run by `WorldAuthoring.BakeCoastMaps`) works over a window 40 cells around the chunk, clipped to the world's ground and its margin, and crops it to the chunk's control map. The waves' distance field reaches 32 cells and its blurs 3 more, so the chunk's texels equal those of a map over the whole world. The maps are 16-bit PNGs beside the chunk scenes (592 x 592 for a chunk), imported as `R16`; the round trip loses nothing.
- **Two bugs the chunks brought out.**
  - The terrain shader read `_TerrainCoast` and `_TerrainRaised`, which its graph did not list. A material sets such a texture, but the SRP Batcher binds per material only what the shader's Properties list, so with several chunk renderers on screen every chunk drew one chunk's elevation and coast maps: a plateau's cliff vanished in the chunk drawn second. The graph now lists both as hidden properties whose HLSL declaration is "Do Not Declare", so the includes keep declaring them and the compiled code is unchanged; the `ShaderCompile` test passes.
  - The coast map's box blur summed in floats, so a map over a window of the world differed from one over the whole world by one step of 65 535 in places (a 64th of a pixel). The sums are now doubles and the bake matches the whole map exactly.
  - Also fixed on the way: a renderer that another component's `OnEnable` rebuilt before its own `OnEnable` ran built twice and leaked a set of chunk quads on every domain reload.
- **The comparison.** `ChunkedGroundRenderTests` draws a 2 x 2 chunk world at origin (2048, 0) per chunk and whole: an ocean with a beach across a border, a road and a trail across borders, a plank bridge over a pond on the corner where the four chunks meet, tall grass and a plateau with a ramp across borders. With 4-cell skirts and baked coast maps every pixel matches, in crisp and in soft (every one of the four chunks' 2048 x 2048 pixels, in each look).
- **Bridges and layer lists.** Before every grid listed the same materials, the comparison found 24 to 26 pixels (crisp) and 2 (soft) differing at the ends of the plank bridge. They were not the skirt: the whole renderer lists the ocean, empty there, between the water and the planks, and the shader hides a layer under a bridge directly above it but leaves it its edge pixel under a bridge two or more layers up. Chunks list different materials, so a bridge ending near a border can draw differently on either side. It is left as a known gap for the terrain shader (Architecture.md); the test gives every grid the world's list.
- **The coast bake** takes about 220 ms per chunk of the 2 x 2 test world in the editor (a window of about 105 x 105 cells, 20 µs per cell). A chunk inside a large world reads 144 x 144 cells, about 0.4 s, so a whole 16 x 16 world would take under two minutes; Phase 5 bakes only the chunks within 32 cells of a change.
- **Load timing** (PlayMode in the editor, unfocused, through the Test Runner; the Sandbox's chunks: 1.25 to 1.3 MB scenes with 4 to 6 ground layers, walls, raised ground and three mob spawn points each). The frame took 3.1 ms (median) before. Loading one chunk, eight times over the four chunks: 3 frames from the request to its ground drawn, 35 to 46 ms in all, the worst frame 18 to 26 ms and the next 15 to 19 ms. The ground's build, when the presentation enables a chunk, is 3.5 ms of that; most of the worst frame is the scene's activation, which the editor reads from YAML. That is a hitch over a 16.7 ms frame in the editor; a player build is unmeasured.
- **The margins.** At the player's 5 cells per second, a load of 50 ms is a quarter of a cell, and four chunks after a teleport about one cell. A load margin of 12 cells is about 2.4 s of walking, ample even on a machine ten times slower, and keeps the interest rectangle at 45 x 37 cells: a player holds about 2.6 chunks on average instead of 3.1 with 16. The hysteresis stays 8 cells, 1.6 s of walking back before a chunk unloads, which keeps the unload rectangle at 61 x 53, under a chunk, so no player holds more than four. Phase 4's leash has to keep leash plus detection radius (6 cells for the Weasel) under 22 cells, the margin plus half the view.
- **The Sandbox** got the content of a realistic chunk for the timing and for checking borders by eye: a sandy shore round the pond, a meadow and tall grass across borders, walls across borders and over the corner where the four chunks meet, a plateau with a ramp across a border, and three mob spawn points per chunk.
- **Tests:** `TerrainSkirtTests`, `WorldChunkTests`, `WorldCoastBakeTests`, `ChunkedGroundRenderTests`, `LocalWorldPresentationTests` (EditMode), and `WorldStreamingPlayModeTests` checks that neighbouring chunks link their ground and walls and that the chunks around the player draw.

### Phase 3: navigation across chunks

- [x] `WorldNavigation` with pages in a shared slot pool, cross-border steps, cliff sides along borders, and region union-find.
- [x] Players and mobs use it, and `ActiveNavigationGrid` goes.
- [x] Partial paths stop at the loaded edge.
- [x] A path across a border matches the single-grid path on the same cells.

Notes:

- **One abstraction for areas and worlds.** Paths, players and mobs use `INavigation2D`. `NavigationMap2D` implements it over `NavigationPage`s laid on a grid of equal pages and placed in a `NavigationPagePool`; every query, the cost fields, the regions and A* are written once, against pages.
  - **An area** is a map of one page: `NavigationGrid2D` builds a page over its walkable bounds into a map and a one-slot pool of its own on every `BuildGrid`, and passes its public API through to that map. Its behaviour is unchanged (every area test passes as it was); a single-area map also reads its tilemaps outside its page, as the grid always did, and a partial path toward a goal far outside it still fails, so the player motor still walks back along the line.
  - **A world** is a `WorldNavigation`, registered by its `WorldLifetimeScope`: a map with a page per loaded chunk on a 16 x 16 grid of 64-cell pages. Its pages live in the Gameplay scope's `NavigationPagePool`, shared by every loaded world: 24 slots of 4096 cells (16 chunks in interest on the host plus those waiting out their hysteresis), growing with a warning past that. The cost fields and A*'s stamped search arrays are sized to the pool and shared by every map in it, so nothing allocates once warm.
  - **The page builder** is `NavigationGrid2D`, now also on every chunk's grid (`WorldAuthoring.SetUpNavigation`; `CreateChunk` adds it): its ground layers, walls, raised tilemap and obstacle layers say what the page reads. On a chunk it builds nothing in `Awake`; the chunk scope's `ChunkNavigation` entry point adds its page to the world's navigation in `Start` (after every collider of the scene is in the physics world) and takes it out when the scope is disposed.
- **Players.** `ActiveNavigationGrid` became `PlayerNavigation` (Gameplay scope): `Of(player)` gives a player in a world its world's navigation, through `PlayerWorlds` and `Worlds` (`LoadedWorld.Navigation`, resolved from the world's scope as its root scene loads), and a player in no world the area's, which `AreaEntry` sets and clears as before. The area half goes in Phase 9. `PlayerMotor2D` asks it on every `MoveTo`.
- **Mobs** take `INavigation2D` by injection: the area scope registers its grid as one, and the world scope registers its `WorldNavigation` as one, so Phase 4's chunk spawners inject it from the chunk scope (a child of the world's).
- **Borders.**
  - Each page knows the loaded pages around it (`Around`); A* steps off a page's edge into the neighbouring page's index directly, and a page's interior cells (all but its outer ring) take a fast path that never checks an edge.
  - The edge cost of a cell beside a cell where no page is loaded counts it as blocked; beside a loaded page it reads that page's terrain.
  - The cliff sides and the rows a cliff face covers read raised ground through `RaisedGround.IKinds`, from whichever page holds each cell. Loading or unloading a page reads again its neighbours' border cliff sides and the face rows of the page above, whose foot may be on the page below. With nothing loaded below, a plateau's lowest rows on a page count as face, as if level ground were below; they sit on the loaded edge, where no path goes.
  - Every change stamps the pages it touches (a loaded or unloaded page's neighbours, a tile edit's page and, on a border cell, its neighbours), so a cost field bakes again only those pages, about 1 ms each in the editor.
  - Live tile edits (`AreaTerrain`, painting outside Play Mode) refresh only the changed cells, in whichever pages they touch.
- **Regions.** Each page floods its own 4-connected regions, labelled from its first index in the pool; after any change a union-find joins the regions meeting along loaded borders with no cliff between them and is flattened, so `AreCellsConnected` stays one read.
- **Partial paths.** Toward a goal where no page is loaded and no walkable cell lies within the nearest-cell radius, a world's A* heads for the goal anyway, capped by `MaxSearchCells` (4096) as toward any unreachable goal, and returns the path to the loaded cell closest to it: the loaded edge. Strict requests and `CanReach` fail there.
- **Versions.** A world's `Version` changes whenever any of its pages loads, unloads or changes, so path followers check their path again as after a terrain edit. A chunk grid's own `Version` (for the gizmo) also changes when a neighbour loads or unloads.
- **The gizmo.** `MovementBlockerGizmo` works per chunk grid. In play mode a chunk's page reads its neighbours, so no edge line runs along a border the ground crosses. Outside Play Mode each chunk builds its page alone: its borders are outlined, and a plateau crossing a border shows a cliff line there and face rows at the upper chunk's bottom edge, which the game does not have (Architecture.md's Known gaps).
- **Measured** (the editor, debug code):
  - A Sandbox chunk's page (4 to 6 ground layers, walls, raised ground) builds in 1.0 ms alone and 2.8 to 3.1 ms beside loaded neighbours, whose borders it reads again; baking the players' profile for it and the neighbours it touched takes 0.9 ms a page (1 to 3.8 ms). A path from chunk (0, 0) to (1, 1) across the Sandbox: 71 cells.
  - `PathfindingBenchmarkTests` on the single-area grid, 100 searches each, before (three runs) and after (two runs, final code), timed alternately with recompiles between: Maze 9.34 / 8.83 / 8.77 ms per search before, 9.05 / 9.03 after; Open field 0.144 / 0.133 / 0.138 before, 0.138 / 0.137 after; Unreachable goal (partial) 9.10 / 10.27 / 8.61 before, 9.32 / 8.82 after; BuildPathToWorld (maze) 25.1 / 23.3 / 16.4 before, 16.1 / 16.2 after. The same cells are expanded (5608, 57, 4096) and nothing allocates once warm. A first version that read each page's fields per expansion was about 12% slower on the maze; caching the current page and the interior fast path brought it back within the noise.
- **Pages read row z = 0** of their tilemaps, as raised ground always did; the area grid's support for several z layers, which no scene used, is gone.
- **The scenes.** Saving the Sandbox's chunks from the editor also rewrote the sprite indices of three chunks' wall render tilemaps (their border tiles are redrawn as linked neighbours open); only the added `NavigationGrid2D` was kept.
- **Tests:** `WorldNavigationTests` (EditMode: a 2 x 2 chunk world against one grid over the same cells, and the borders, regions, partial paths, versions, tile edits, the shared pool and allocations), `PlayerNavigationTests` (was `ActiveNavigationGridTests`), a chunk test in `MovementBlockerGizmoTests`, and `WorldStreamingPlayModeTests` walking the player across a border in the Sandbox.

### Phase 4: mobs per chunk

- [x] `ChunkMobSpawner` spawns and despawns with its chunk.
- [x] Home chunk and leash, patrol inside the home chunk, and targets only in the mob's world.

Notes:

- **Spawning.** `ChunkMobSpawner` is a chunk-scope entry point. It spawns in `PostStart`, which VContainer runs after every `Start`, so `ChunkNavigation` has added the chunk's page by then. Offline or on the host it instantiates one mob per spawn point through the chunk's scope, so the mob gets its world's `WorldNavigation` and `WorldPlayers` from the world's scope. It moves the mob into the chunk's scene, gives it the chunk as its home and network-spawns it in a session.
  - It despawns its mobs when `Worlds.ChunkUnloading` names its chunk, before the scene unloads, so in a session clients drop their copies first. Its `Dispose` takes out whatever is left, so no path leaves a mob behind.
  - A mob lives in its home chunk's scene for its whole life, even after walking into a neighbour, so it can never outlive that chunk.
  - Mobs are not pooled: a chunk loads three of them at most every second or two of walking.
- **Shared with areas.** `AreaMobSpawner` and `ChunkMobSpawner` share `MobSpawning`: who spawns, the distinct networked prefabs, and spawning and despawning one mob. `INetworkObjectSpawner` gained `Despawn` and `RegisterPrefab` with a handler of one's own.
- **The client path** (exercised with fakes until Phase 7 hosts worlds). Netcode keeps one prefab handler per prefab: a second `AddHandler` for the Weasel fails, and the first chunk to unload would remove the handler every other chunk relies on. So a client's chunks register through `ChunkNetworkPrefabs` (Gameplay scope):
  - It installs one routing handler per prefab when the first chunk registers it, and removes it when the last one unregisters.
  - Each copy is created through the scope of the registered chunk holding its spawn position, or the nearest registered chunk.
  - Phase 7, which shows objects per chunk, must route by home chunk instead, for example with Netcode's instantiation data. A mob shown after it walked across a border would otherwise land in the neighbour's scene (Architecture.md's Known gaps).
- **The seam for dead mobs.** `ChunkMobSpawner.ShouldSpawn` decides per spawn point and always says yes. Phase 8 asks the world state there.
- **The leash.** `MobConfig.LeashCells`, 8 for every mob config. `MobPatrolAnchor` holds the home (the chunk's 64 x 64 cells in world units) and answers `IsWithinLeash` for a point and `IsPathWithinLeash` for a path's waypoints.
  - Every state's "spots target" check (`MobStateBase.SpotsTarget`) also requires the target and the mob within the leash.
  - Chase and AttackRange give up when either is beyond it. Chase also gives up when a repath's waypoints leave it, and Search when the last known position lies beyond it.
  - Giving up marks the mob leashed and sends it home (Return). A leashed mob spots no target until Idle's timer runs out at home. Without that, a target inside the leash that is reachable only by a path leaving it pulled the mob between Chase and Return every frame.
  - An area's mob has no home, so every leash check passes and it behaves as before.
- **The constraint.** `WorldStreamingSettings.KeepsHomeChunkLoaded` checks two sums against the shorter side of each rectangle, since the view is 20 x 12 and a player may stand above or below the mob:
  - leash plus detection radius under the load extents (22 x 18, so 18);
  - leash plus lose distance under the keep extents (30 x 26, so 26), since a chasing mob keeps its target to its lose distance.

  The Decisions' "under 22" holds only across. The Weasel's 8 + 6 = 14 and 8 + 8 = 16 leave room. `MobLeashConstraintTests` runs the check over every `MobConfig` asset against the project's settings, so a config edit that breaks it fails a test.
- **Targets.** `WorldPlayers` is the Gameplay registry narrowed by `PlayerWorlds`. It follows players in and out of the world with no per-frame work, and a target that leaves the world is dropped like one that leaves the registry.
- **The Sandbox.** Its twelve spawn points (three per chunk, all the Weasel) stand on walkable ground and needed no change. One of them, at (20.5, 54.5) in chunk (0, 0), is the one the PlayMode leash test uses.
- **Assets.** `Mob_Default` and `Mob_StressTest` were saved with the leash, which also wrote `Mob_StressTest`'s missing search and crowd values (the C# defaults it already ran on), so that Known gap went.
- **Tests:** `ChunkMobSpawnerTests`, `WorldPlayersTests`, `MobLeashConstraintTests` and leash and home-patrol cases in `MobStateMachineTests` (EditMode). `WorldStreamingPlayModeTests` covers:
  - a chunk's mobs spawning into its scene and going with it;
  - a mob chasing the player across a border, stopping within its leash and walking home;
  - no mob left after the menu.

### Phase 5: authoring

- [x] World Editor window: create a world at a free origin and its chunks, and open a chunk with its neighbours.
- [x] Terrain Brush across chunk borders.
- [x] Ownership, `PersistentId` and origin validation.
- [x] A hand-painted test world with the Clearing's PlayMode margins, painted with the tools above.

Notes:

- **The World Editor** is a thin window over static steps that are tested on their own. It lists every `WorldDefinition` in the project, not only the listed ones, so a world being built can be picked before it is listed. Its only standing work is a flag set when a tile of an open chunk of the picked world changes, which shows a hint to check the coasts; nothing is computed per frame.
- **Free origins.** New worlds take the first slot of a grid 2048 units apart, row by row from the origin, where the world keeps 1024 units from every other world's rectangle and stays within 16 384 units of the origin; the slot at the origin is skipped while the areas live there (Phase 9). The Sandbox and its interior hold (2048, 0) and (4096, 0), so Testlands took (6144, 0). An origin typed in by hand is checked against the same rule, measured as the distance between the rectangles, and so is every world by Validate and the build.
- **The brush.** Unity's grid brush paints and erases through its box fill and box erase, so `TerrainBrush` overrides those two and the flood fill. The stroke's cells are world cells whichever chunk was the paint target, since every chunk's grid sits at the world's origin, so each cell goes to the chunk owning it, found among the open scenes. `GroundLayerMigration.AddLayer` gained a way to join the current undo group instead of making its own, so a stroke that adds layers to several chunks is still one undo step. A walls stroke on a chunk without walls copies the target's walls there, so the brush never needs the area template open. A tilemap of any other kind is written only inside its own chunk, so nothing lands silently in the wrong chunk. The ground preview shows on the target's renderer and its linked neighbours', each given the cells within its own cells and skirt; the walls' preview stays the Dual Grid Brush's on the target chunk (Architecture.md's Known gaps).
- **Coast maps.** Rather than track which cells a stroke changed, each chunk's `WorldChunk` records what its map was baked from: a hash of the material with waves, the window the bake reads and which of its cells count as sea (`WorldCoastBake.SourceOf`). Reading the world and hashing every chunk takes a few milliseconds, so Check coasts and Bake changed coasts compare them for the whole world and bake only the chunks whose sea or shore within the bake's 40-cell reach changed. Ground that is no sea (grass, a path under the ocean's place in the order) changes nothing.
- **`PersistentId`** is a component holding a GUID string. `PersistentIdWatcher` gives a mob spawn point in a chunk scene its id as it is created, and a duplicate or a paste a new one when its copied id is already loaded, through the editor's object change events; Repair ids makes every id of a world unique (the first holder keeps its id) for whatever the events missed, such as scenes written by script. The Sandbox's twelve mob spawn points got theirs (only those components were kept from the save; the wall render tiles it rewrote were not).
- **Ownership.** `WorldValidation` reads the objects of a chunk scene: spawn points, mob spawn points, objects with a persistent id and the children of its `Props` root on its own cells, and a blocking prop's footprint, through `PropGridSnapping.BlockedCells` (the navigation's rule), only on its cells. The World Editor's Validate, `Tools/TopDownRPG/World/Validate Worlds` and `WorldBuildValidator`, a pre-build check after `SceneBuildValidator`, run it with the layout check and the id check over every world; it opens the chunk scenes that are closed and closes them unchanged, about 0.2 s for the three worlds.
- **Navigation in the editor.** The Phase 3 gap is closed: `EditModeWorldNavigation` joins the open chunks' pages in a `WorldNavigation` of its own outside Play Mode, following scenes opening and closing and the hierarchy changing, once per editor update at most, so the movement blocker gizmo no longer outlines a border the ground crosses or draws a cliff along it. Only chunks in their world's saved scenes take part, up to 24 at once, and every page leaves before Play Mode.
- **Testlands** (2 x 2 chunks at (6144, 0)) was made with the window's steps and painted with brush strokes run through `TerrainBrush`, each across the borders it crossed: grass everywhere, a stone road across column 64, a gravel trail across row 64, a meadow across row 64, tall grass across column 64, dirt on the corner, a pond with a sandy shore across column 64, a wall line across column 64 and a plateau with a ramp across row 64. Its entry, `start` on the road at (24.5, 40.5), keeps the Clearing's margins: every cell within 6 of it is open ground, a seven-cell wall two cells to its right leaves room to walk around, and its eight Weasel spawn points are 23 cells or more away. `TestlandsPlayModeTests` boots into it and checks them; the Clearing's tests move there in Phase 9.
- **Tests:** `WorldBrushStrokeTests`, `WorldLayoutTests`, `WorldValidationTests`, `PersistentIdTests`, `EditModeWorldNavigationTests` and a coast source case in `WorldCoastBakeTests` (EditMode), and `TestlandsPlayModeTests`.

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
  - `WorldNavigation` paths and regions across pages, and pages loading and unloading under a path (Phase 3);
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
