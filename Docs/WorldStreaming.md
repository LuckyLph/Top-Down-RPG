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
| The first world | The Overworld (`Assets/Worlds/Overworld`, 5 x 4 chunks at (8192, 0)), generated in the editor by the world generation plan ([WorldGeneration.md](WorldGeneration.md)): the six generated areas are its regions, at their seed 1 layouts, on an island joined by roads, written into its chunk scenes from `WorldGenerator_Overworld.asset`. The terrain materials built so far, all eighteen in the style's order, are its materials. It is listed in `GameScenes` and is the starting world, at `clearing`, since WorldGeneration.md's Phase 5 (Testlands stays the PlayMode tests' starting world); Testlands, the Sandbox and its interior stay painted by hand. Other worlds get their own materials later. |
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

- **New game or Continue offline, or hosting:** the local player enters its saved world and position, or the starting world's entry. The local machine fades, loads that world's root scene and the chunks around the player, places it and fades in. A saved place the player can no longer stand on (its world regenerated or repainted) moves on to the nearest walkable cell, or the player's nearest discovered waystone, once the chunks there are up, on the host for a joining player too (WorldGeneration.md's Phase 5).
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

1. A player walks into a world exit, a rectangle of cells in a chunk naming a target world and entry (`WorldExit`). Its own machine asks the host for the trip, naming the exit by its persistent id. On the host, or offline, the request is local. An exit fires once per step onto it from a cell that is no exit, and never right after a trip (see Phase 6's notes).
2. The host checks the trip:
   - the exit exists in the player's world;
   - the player is at the exit;
   - the target is a listed world.
3. The host loads the target world, if no other player is in it, and its chunks around the entry, in the background: the entry is streamed as an interest of its own from the moment the trip is approved.
4. The host sets the player's world and tells the player's machine to go.
5. The player's machine:
   - fades out and blocks its input, with game time running;
   - keeps the old world, untouched, lit and drawn until the player has moved, then for the empty-world grace, then releases its chunks (see Networking) and unloads it. A trip back within the grace reuses it;
   - loads the new world's root scene and its chunks around the entry (offline and on the host, already loading since step 3);
   - moves its player to the entry, since a client owns its player's movement;
   - waits until the ground around the entry is loaded, reports the chunks ready and fades in.
6. The host unloads the old world once no player has been in it for the empty-world grace and no client still holds any of its chunks.

No other machine fades or pauses. On the host, its own player's trip is a local fade only: the host keeps simulating every other world.

### Death and respawn

- A player discovers a waystone (an entry marked as one) by coming within two cells of its cell along each axis. The host decides it for every player, since it has every position, and keeps it per player id in the save. The discovering player sees "Waystone discovered" on its own HUD's notice line (a floating popup until UiRevamp.md's Phase 3).
- A world's default entry counts as discovered from the start.
- A dead player respawns at the nearest waystone they have discovered in their world, in a straight line from where they died (the first listed on a tie), after `GameplaySettings.RespawnDelaySeconds`. The respawn is a trip within that world: the host holds the arrival, so the waystone's chunks load at once, and puts the player in the world for one more trip; the player's own machine fades, moves its player there, waits for the chunks and reports them with the new trip count, as on any trip. The player stays dead on the way, and the host restores its health once it stands at the waystone with that chunk loaded and, for a client's player, held by its client.
- A player that dies on a trip respawns once the trip has arrived, measured from its end.
- A dead player is saved at the waystone it will respawn at, so leaving or quitting while dead neither skips the respawn nor brings it back beside its killer.
- Nothing happens when every player is dead at once: each still respawns on their own, and no world restarts.
- While its player is dead, a player's own machine says so on the HUD's notice line (UiRevamp.md's Phase 3).
  - It shows "You fell. Respawning in 3", counting whole seconds of game time down from `GameplaySettings.RespawnDelaySeconds` since the death reached that machine.
  - Then it shows "Respawning..." until the player is restored, so a respawn that waits for a trip's arrival or the waystone's chunk keeps saying so.
  - The countdown reads only what that machine already knows: the death, the delay and the restore. A joined client learns of the death from the host's replicated hit and of the restore from `RestoredRpc`. Nothing more is sent.
- Each machine also announces the world its own player enters, by the world's display name ("Entering the Overworld"): at a session's start, on joining and after a trip to another world. A trip within the world, a respawn's included, announces nothing.

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
  - the entries and waystones (data on its `WorldDefinition`; discoveries are per player in the `SessionSave`).

  Disposing it is the cleanup hook for the whole world.
- **`ChunkLifetimeScope`** registers what a chunk scene contains:
  - its navigation page;
  - its mob spawner (`ChunkMobSpawner`);
  - its terrain sync;
  - its persistent objects.

  Disposing it unregisters them.
- **`Worlds`** (Gameplay scope, Phase 1) is the registry of loaded worlds: `TryGet(definition, out LoadedWorld)`, each with its definition, root scene, scope, lighting, navigation (Phase 3) and loaded chunk scenes, and events as they come and go. A player finds its own world's navigation through it, by its player's world (`PlayerNavigation`, which replaced `ActiveNavigationGrid` in Phase 3); `ActiveSpawnPoint` went with the areas (Phase 9).
- **`PlayerWorlds`** (Gameplay scope, Phase 1) records each player's world and trip count. Offline it is set directly; on a client `PlayerWorldSync` fills it from the host's `NetworkVariable` (Phase 7).
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
- **`GameScenes`** lists the worlds (Phase 1) and the starting world and entry (Phase 7); the areas it listed beside them went in Phase 9. Every world scene has a `SceneDefinition` beside it, so `SceneBuildValidator` already checks it is in the build.
- **`ChunkCoords`** is static chunk arithmetic: cell to chunk, chunk bounds, rectangle overlap, world origins. It is plain C# and tested in EditMode.

### Streaming

- **`ChunkStreamer`** (Gameplay) is the decision part. It is plain C#: each streamed player's world and interest rectangle in; worlds and chunks to load and unload out, with the hysteresis. It is tested with fakes.
- **Who a machine streams for:**
  - offline or on the host, every registered player;
  - on a client, its own player.
- **`WorldStreaming`** (Gameplay, Phase 1) is the doing part, and the `IWorldSession` `GameFlow` enters and leaves worlds through.
  - Each frame it gives the streamer the streamed players' worlds and positions, and runs its next action through `SceneLoader`: a world's root scene under the Gameplay scope, a chunk under its world's scope (`LifetimeScope.EnqueueParent`, as `GameFlow` does), with the world's definition or the chunk's identity enqueued into the new scope.
  - It records what is up in `Worlds`.
  - From Phase 7 it does not unload a chunk some client still holds (see Networking).
- **`GameFlow`** keeps the menu, starting a session, and the local player's fade. It no longer loads gameplay content itself. A trip is one of its transitions (`TravelAsync`, `IsTravelling`), which `TransitionTimeFreeze` leaves alone, so a trip never stops time, offline or not.
- **Trips** (Phase 6) go through `ITripAuthority`: a player's machine sends `TripRequest`s (`LocalTripTraveller`, which watches the local player's cell against the loaded `WorldExits`), the authority (`LocalTripAuthority` offline) checks them, holds the arrival in `TripArrivals` for the streaming and sets the player's world, and its `Approved` event tells the player's machine to go.

### Presentation per machine

- **`LocalWorldPresentation`** (Gameplay) follows the local player's world, once the player stands in it (during a trip the player is put in the target world before it is moved there, and the world being left stays shown until then):
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
- **The API.** `INavigation2D` is what paths, players and mobs use: walkability, costs, line costs, nearest walkable cell, connectivity, `CrossesCliff`, `MoveOffCliffFace`, the pathfinder and the version. `NavigationMap2D` implements it over pages in a pool; `WorldNavigation` is a world's map, and a standalone `NavigationGrid2D` (a Scene Generator scratch scene, tests) is a map of a single page.
  - `PathFollower2D` and the mob components take an `INavigation2D`; from Phase 4 a chunk's mobs take their world's `WorldNavigation` by injection from their chunk's scope.
  - `PlayerMotor2D` looks its own up through `PlayerNavigation`: its world's through `Worlds` by its player's world.
- **What stays.** `NavigationTerrainSource2D` and `TerrainMovementProfile2D` stay as they are. `NavigationGrid2D` is the page builder: it sits on every chunk's grid, holding what the page reads (ground layers, walls, raised ground, obstacle layers).
- **Cell data.** `CellData`'s per-cell `List<TerrainType2D>` became flat per-page arrays (the layer bits of the highest priority per cell, a layer's terrain type found by its bit), so a page allocates its arrays once.

### Mobs

- **Spawning.** `ChunkMobSpawner` replaces `AreaMobSpawner`.
  - On the host or offline, it spawns each of its chunk's mob spawn points when the chunk becomes ready. A spawn point whose mob the world state marks as dead and not yet due to return is skipped.
  - It despawns the chunk's mobs before the chunk unloads.
  - Clients register the mob prefabs per chunk through `ChunkNetworkPrefabs`, which keeps Netcode's one handler per prefab and sends each copy to its chunk (see Phase 4's notes).
- **Leash.** A mob carries its home chunk and its leash. Its state machine treats a target or path beyond the leash like a lost target and walks home, ignoring targets until it has idled there. `MobPatrolAnchor` picks patrol points inside the home chunk.
- **Targets.** A mob considers only players in its own world: the world's scope registers `WorldPlayers`, the players in that world, as its `IPlayerRegistry`. Worlds are far apart, so distances already rule the others out; the filter makes it explicit and saves the distance checks.
- **Death.** A mob's death writes its spawn point's persistent id and the time into its world's state (see Persistence).

### Networking

Scene management stays off: each machine loads its own worlds and chunks, as it loads areas today.

- **Each player's world** is a `NetworkVariable` on the player object, written by the host. It replaces area announcements, `AreaEpoch` and `NetworkAreaSync`. A client loads its own player's world when that value changes. Other players' worlds matter to it only for what it is shown.
- **Trip requests.** A client asks for a trip with an RPC naming the exit's persistent id. The host checks it, prepares the target world and sets the player's world, which tells the client to go (see Travelling between worlds). Phase 6 laid this out offline: `ITripAuthority.Request` is where the RPC goes, `LocalTripAuthority.TryApprove` is the host's check, and `Approved` is where the client's go message arrives.
- **Per-chunk readiness** replaces the single ready set. Every message carries the chunk id (world, cx, cy) and the player's trip count, so a report from before a trip is ignored.
  - A client reports `ChunkReady(trip, chunk)` once a chunk is up and its prefabs are registered. The host shows that client every spawned object whose home chunk that is.
  - Before unloading a chunk, a client sends `ChunkReleasing(chunk)` and waits. The host hides that chunk's objects from it (`NetworkHide`) and acknowledges, and only then does the client unload, so a client never destroys a spawned object locally.
  - The visibility check becomes "the client is ready for this object's home chunk", and `IClientReadiness` gains the chunk.
  - A trip ends what the client held before it (KnownGaps.md's Phase 4): its holds of every other world than the trip's are released as the trip begins, their objects hidden from it, and those of the trip's world stay held, their objects still shown, but count as ready for the trip (what a respawn at a waystone waits for) only once the client reports them during it, as its streaming does for every loaded chunk of its world when the trip reaches it.
- **Players** live in the Gameplay scene and stay visible to every client, whatever their world. A client sees a far-away player as an object thousands of units away, never drawn, so movement replication keeps working across trips with nothing to re-show.
- **Host interest.** The host streams around every player, so a client's player always walks on ground the host has loaded, in a world the host simulates. The client's own movement uses the client's own loaded pages, which always include its surroundings.
- **Runtime terrain edits** go per chunk: each chunk's `AreaTerrain` and its sync object. On the host, a chunk's persisted edits are applied when it loads, before its sync object spawns, so a client that becomes ready for it receives them as a late joiner does today.
- **Join payload.** `JoinApproval`'s payload carries the player id after the version.

### Persistence

- **World state.** `WorldState` (host or offline only) holds a record per chunk of a world, keyed by persistent id:
  - killed mobs and the time they died, on the world clock: the game time played on the save, which the next session continues;
  - opened containers, later;
  - runtime terrain edits.

  The states of every world visited stay in memory for the session, loaded or not, in the Main scope's `SessionSave`. A chunk reads its record when it loads and writes changes as they happen.
- **Save format.** A file per session (`Saves/session-<id>.json`, since KnownGaps.md's Phase 5), `GameSaveData` version 4 (2 in Phase 8, 3 with the weapons in KnownGaps.md's Phase 2, 4 with the session's id and time and terrain edits naming their layer by material in its Phase 5), holds:
  - the session's id and when it was saved;
  - the world clock's time;
  - per player id: world id, position (a dead player's is the waystone it respawns at), discovered waystones (since Phase 9), the weapon it holds;
  - per world id visited: its world state.

  Continue loads the latest save, a session or a joined client's copy, and since UiRevamp.md's Phase 10 the Saved games screen lists every save to play, host or delete; the single save file of earlier builds (version 2 or 3) is imported once as a session, its terrain edits dropped, and a version 1 save is ignored with a warning, as other versions are.
- **When the game saves.** When any player enters a world, on a timer (`autosaveInterval`), and when the session ends: the menu, quit, or the host leaving. It still writes only where `IGameAuthority.IsAuthoritative`. A client writes only a copy of its own place in the host's session (`Saves/joined-<host session id>.json`, its own player's record and the host's time of day, no world's state) at the same moments, under the session id the host tells it, and can play on alone from it as a session of its own, since UiRevamp.md's Phase 10 whether that host is gone or not (KnownGaps.md's Phase 5, where only once it was gone; Multiplayer.md's save decisions).
- **Mob respawn.** A killed mob comes back once `respawnDelay` of game time has passed and its chunk loads again. A loaded chunk does not spawn it in front of a player. The delay is a `MobConfig` value, so bosses can stay dead.

### Authoring (Editor)

- **World Editor window** (`Tools/TopDownRPG/World/World Editor`, `WorldEditorWindow` over `WorldAuthoring`, `WorldLayout`, `PersistentIds` and `WorldValidation`).
  - It creates a world at the first free origin of the global layout (or one typed in and checked), its root scene and its chunk scenes, registers them in the `WorldDefinition`, the `SceneDefinition`s and the build list, and lists the world in `GameScenes`.
  - It opens a chunk with its eight neighbours additively, or alone, so the Scene view shows the world around the chunk being edited, and closes them again, asking to save first. Opening two worlds at once is harmless, since their origins differ.
  - It gives the selected open chunk walls or raised ground, for the Terrain Brush to paint.
  - It edits the world's entries, the first the default, and marks waystones.
  - It runs the whole-world steps: syncing the layer lists, baking the coast maps of the chunks whose sea changed, repairing persistent ids and validating.
- **Terrain Brush across borders.** On a chunk, the brush splits a stroke's cells by chunk and writes each part to that chunk's tilemap of the same kind as the paint target (`WorldBrushStroke`): the layer of the target's material (added to a chunk that lacks it, and to each open neighbour whose skirt the stroke reaches), the walls, or the raised ground. Box fills, erases and flood fills go across borders the same way, the flood fill over the open chunks' cells. A part whose chunk is not open is skipped with a warning, and the stroke is one undo step across scenes. Its preview shows on the renderer of each chunk it covers. A neighbour's skirt shows the stroke once it is painted. A standalone area is painted as before.
- **Layer lists and coast maps.** The brush keeps each chunk's layer list covering its open neighbours' border materials, and Sync layer lists covers the whole world, also removing an empty layer nothing within a chunk's skirt shows any more (KnownGaps.md's Phase 5). Each chunk records what its coast map was baked from (a hash of the sea within the bake's reach), so Bake changed coasts bakes again only the chunks whose sea or shore changed; the brush marks the coast map of each open chunk whose bake reaches a stroke on the sea or its shore out of date as it paints, and the glow map near lava, the World Editor naming the chunks it marked (KnownGaps.md's Phase 5).
- **Generators.** The World Generator window (`Tools/TopDownRPG/World/World Generator`, [WorldGeneration.md](WorldGeneration.md)) composes a world from a `WorldGenerator` asset, the area generators' layouts as its regions, and writes it into the world's chunk scenes, only the chunks whose content changed, never a locked one, followed by the whole-world steps above; the Overworld is written this way. The Scene Generator window keeps working on standalone scenes, where a region's generator is tuned on its own.
- **Validation** (the World Editor's Validate, `Tools/TopDownRPG/World/Validate Worlds` and a pre-build check).
  - A blocking prop whose footprint crosses a chunk border, or anything placed outside its chunk (a spawn point, a mob spawn point, a prop, an object with a persistent id), is refused with an error that names it.
  - Every mob spawn point in a chunk has a `PersistentId`, every id is set, and every id is unique within the world. The editor gives a new or duplicated object its id as it is made, and as a chunk scene saves gives its spawn points theirs and makes its ids unique within it (KnownGaps.md's Phase 5).
  - Every chunk's coast and glow maps are up to date (KnownGaps.md's Phase 5), so a build stops on a stale one.
  - Worlds keep the layout.
- **The edit-mode navigation.** Outside Play Mode the open chunks of a world join their navigation pages, as in play, so the movement blocker gizmo draws a border the ground crosses as the game reads it.

#### Authoring a world

1. Open the World Editor (`Tools/TopDownRPG/World/World Editor`). Under New world, type a name (letters and digits) and the size in chunks; the origin is the first free slot of the layout. Keep "Create every chunk" and "List in Game Scenes" ticked for an ordinary world, untick the first for a world with holes and create its chunks one by one from the grid. Create world.
2. Pick a chunk in the grid and Open with neighbours. Each chunk starts as a grid at the world's origin with an empty grass layer, a terrain renderer and a navigation grid.
3. Paint with the Terrain Brush (the Tile Palette's default brush) on any open chunk's tilemap: a ground layer, the walls or the raised ground. A stroke goes into whichever chunks it covers. To paint a material the chunk has no layer for, pick its tile and press the palette's "Add the <material> layer" button. Walls and raised ground start on one chunk with the window's Add walls and Add raised ground (shown for the selected chunk while it is open and lacks them); after that the brush gives them to the chunks a stroke reaches. Open the chunks around the area being painted: a part of a stroke on a chunk that is not open is skipped, with a warning.
4. Place props with the Prop Placement window, and mob spawn points under a `MobSpawns` object in each chunk scene. Each must stand on its own chunk's cells, and a blocking prop's footprint must not cross a border. Mob spawn points get their `PersistentId` as they are made.
5. Fill in the entries in the window: the first is where a new game starts; tick Waystone for the ones players discover. Give each exit its marker in its row (a signpost for a road to another world, a door into an interior, a doormat out of one, or none).
6. Run Sync layer lists, then Bake changed maps when the world has an ocean or lava (the coast maps and, since LightingGaps.md's Phase 5, the glow maps), then Place markers (each waystone's stone and each exit's marker, KnownGaps.md's Phase 3; again whenever an entry or an exit moves), then Repair ids and Validate, which reports markers out of step and stale coast or glow maps. Save the scenes, and check `git diff` for churn in the chunk scenes (wall render tiles redrawn as neighbours open) before committing.

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
- **Bridges and layer lists.** Before every grid listed the same materials, the comparison found 24 to 26 pixels (crisp) and 2 (soft) differing at the ends of the plank bridge. They were not the skirt: the whole renderer lists the ocean, empty there, between the water and the planks, and the shader hides a layer under a bridge directly above it but leaves it its edge pixel under a bridge two or more layers up. Chunks list different materials, so a bridge ending near a border can draw differently on either side. It is left as a known gap for the terrain shader (Architecture.md); the test gives every grid the world's list. KnownGaps.md's Phase 6 fixed it in the shader, which measures a layer's edge against the next layer covering the corner patch, skipping empty layers listed between; the test now gives each chunk only the materials of its own cells and skirt, and every pixel still matches.
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
- **Free origins.** New worlds take the first slot of a grid 2048 units apart, row by row from the origin, where the world keeps 1024 units from every other world's rectangle and stays within 16 384 units of the origin; the slot at the origin was skipped while the areas lived there (freed in Phase 9). The Sandbox and its interior hold (2048, 0) and (4096, 0), so Testlands took (6144, 0). An origin typed in by hand is checked against the same rule, measured as the distance between the rectangles, and so is every world by Validate and the build.
- **The brush.** Unity's grid brush paints and erases through its box fill and box erase, so `TerrainBrush` overrides those two and the flood fill. The stroke's cells are world cells whichever chunk was the paint target, since every chunk's grid sits at the world's origin, so each cell goes to the chunk owning it, found among the open scenes. `GroundLayerMigration.AddLayer` gained a way to join the current undo group instead of making its own, so a stroke that adds layers to several chunks is still one undo step. A walls stroke on a chunk without walls copies the target's walls there, so the brush never needs the area template open. A tilemap of any other kind is written only inside its own chunk, so nothing lands silently in the wrong chunk. The ground preview shows on the target's renderer and its linked neighbours', each given the cells within its own cells and skirt; the walls' preview stays the Dual Grid Brush's on the target chunk (Architecture.md's Known gaps).
- **Coast maps.** Rather than track which cells a stroke changed, each chunk's `WorldChunk` records what its map was baked from: a hash of the material with waves, the window the bake reads and which of its cells count as sea (`WorldCoastBake.SourceOf`). Reading the world and hashing every chunk takes a few milliseconds, so Check coasts and Bake changed coasts compare them for the whole world and bake only the chunks whose sea or shore within the bake's 40-cell reach changed. Ground that is no sea (grass, a path under the ocean's place in the order) changes nothing.
- **`PersistentId`** is a component holding a GUID string. `PersistentIdWatcher` gives a mob spawn point in a chunk scene its id as it is created, and a duplicate or a paste a new one when its copied id is already loaded, through the editor's object change events; Repair ids makes every id of a world unique (the first holder keeps its id) for whatever the events missed, such as scenes written by script. The Sandbox's twelve mob spawn points got theirs (only those components were kept from the save; the wall render tiles it rewrote were not).
- **Ownership.** `WorldValidation` reads the objects of a chunk scene: spawn points, mob spawn points, objects with a persistent id and the children of its `Props` root on its own cells, and a blocking prop's footprint, through `PropGridSnapping.BlockedCells` (the navigation's rule), only on its cells. The World Editor's Validate, `Tools/TopDownRPG/World/Validate Worlds` and `WorldBuildValidator`, a pre-build check after `SceneBuildValidator`, run it with the layout check and the id check over every world; it opens the chunk scenes that are closed and closes them unchanged, about 0.2 s for the three worlds.
- **Navigation in the editor.** The Phase 3 gap is closed: `EditModeWorldNavigation` joins the open chunks' pages in a `WorldNavigation` of its own outside Play Mode, following scenes opening and closing and the hierarchy changing, once per editor update at most, so the movement blocker gizmo no longer outlines a border the ground crosses or draws a cliff along it. Only chunks in their world's saved scenes take part, up to 24 at once, and every page leaves before Play Mode.
- **Testlands** (2 x 2 chunks at (6144, 0)) was made with the window's steps and painted with brush strokes run through `TerrainBrush`, each across the borders it crossed: grass everywhere, a stone road across column 64, a gravel trail across row 64, a meadow across row 64, tall grass across column 64, dirt on the corner, a pond with a sandy shore across column 64, a wall line across column 64 and a plateau with a ramp across row 64. Its entry, `start` on the road at (24.5, 40.5), keeps the Clearing's margins: every cell within 6 of it is open ground, a seven-cell wall two cells to its right leaves room to walk around, and its eight Weasel spawn points are 23 cells or more away. `TestlandsPlayModeTests` boots into it and checks them; the Clearing's tests move there in Phase 9.
- **Tests:** `WorldBrushStrokeTests`, `WorldLayoutTests`, `WorldValidationTests`, `PersistentIdTests`, `EditModeWorldNavigationTests` and a coast source case in `WorldCoastBakeTests` (EditMode), and `TestlandsPlayModeTests`.

### Phase 6: world exits, offline

- [x] World exits and entries, travelling with a local fade and no time freeze in a session.
- [x] The old world unloads once no player has been in it for `emptyWorldGrace` (10 s), and a trip back within that reuses it.

Notes:

- **Exits.** A `WorldExit` is a marker in a chunk scene: a target world, an entry name (empty for the default entry) and a size in cells from the cell holding its position, with a `PersistentId` (`[RequireComponent]`). It is not a trigger collider: `LocalTripTraveller` compares the local player's cell with the last one each frame and only on a new cell asks `WorldExits` (the exits of the loaded chunks, read from each chunk's scene as `Worlds` reports it loaded) whether the cell is an exit's. That keeps the per-frame work to one cell comparison, needs no physics layer or collision matrix, and works the same on a client, whose player is the only one it moves.
- **Re-trigger protection.** `ExitTriggerGuard` fires only when the player steps onto an exit from a cell that is none. It starts unarmed and is disarmed after every trip, so a player that appears on an exit (an entry placed on one, a new session) must step off first, walking along a wide exit fires nothing more, and a refused request is not sent again every frame. The Sandbox's `fromInterior` entry also stands two and a half cells in front of its exit.
- **The trip flow.** Request: `LocalTripTraveller` sends a `TripRequest` (the player, the exit's id) to `ITripAuthority`. Check: `LocalTripAuthority` finds the exit among the loaded exits of the player's world, requires the player within one cell of its cells (room for a client's position arriving late in Phase 7), requires a target that `GameScenes` lists, and resolves the entry (an unknown name falls back to the default entry with a warning; a world with no entry is refused); a player already travelling is refused, and every refusal is logged as a warning. Prepare: the arrival (target world, entry position) goes into `TripArrivals`, which `WorldStreaming` streams as an extra interest until the player stands within two cells of the entry in that world, so loading starts while the screen fades out. Set: `PlayerWorlds.Set`. Go: `Approved` reaches `LocalTripTraveller`, which waits for any running transition, stops the player and runs `GameFlow.TravelAsync`: fade out, `IWorldSession.ArriveAsync` (teleport to the entry, snap the camera, wait for `IsSettled` there), fade in.
- **No time freeze.** A trip is a `GameFlow` transition flagged `IsTravelling`, not a separate presentation transition, so `GameFlow`'s one-transition rule keeps a trip and a menu, restart or session change from overlapping either way; `GameplayInputGate` turns gameplay and menu input off for it as for any transition; `TransitionTimeFreeze` does not hold for it (`Freezes(transitioning, travelling)`). Game time runs on through the fade, offline too, so mobs and statuses keep ticking (Architecture.md's Known gaps: the arrival is not protected). A party-wipe restart whose delay ends during a trip waits for it to finish instead of being dropped.
- **Between Set and the teleport** the player is recorded in the target world while it still stands in the old one, as a client's player will be in Phase 7 until it moves. The old world's mobs drop it at once (`WorldPlayers`), the old world starts its grace, the player's own interest falls outside the target world's chunks and loads nothing, and `LocalWorldPresentation` keeps showing the old world (lighting and ground) until the player stands on the target world's cells (`PresentedWorld`), so nothing goes dark behind the fade out.
- **Global lights.** A trip loads the target world's root scene beside the one on screen, and URP logs an error as soon as a second global light is enabled, not when it draws. `WorldLighting` now hides its object in `Awake` in play mode (execution order -1000, before the lights under it enable) and `LocalWorldPresentation` shows the world on screen, also at once as a world loads.
- **The world left behind** keeps everything for the grace, as `ChunkStreamer` already did: a trip straight back reuses the same `LoadedWorld` and scenes, and a world nobody returns to unloads its chunks and itself once 10 s of game time have passed (both checked by `WorldTripPlayModeTests`).
- **Restart.** `GameFlow.CurrentWorld` is now the local player's world: the one the session started in, then each one it travelled to. `RestartAsync` (the party wipe, until Phase 9 removes it) restarts the session in that world at the entry the player last arrived by, since players move between worlds and the world the session began in may be far behind them. The save still knows only areas (Phase 8).
- **Authoring.** `WorldAuthoring.AddExit` places an exit under a chunk scene's `Exits` root with a new id, `ConfigureExit` sets where it leads, and `SetEntry` adds or moves an entry; the World Editor lists the exits of the open chunks with their target world, an entry popup and size, and adds one at the Scene view's pivot. Validation checks each exit's cells lie in its chunk and that it leads to a listed world at an entry it has.
- **The test content.** The Sandbox has an exit on the road at cells (36, 41) and (36, 42), east of its `start`, leading to the SandboxInterior's `start`, and a new entry `fromInterior` at (33.5, 42); the interior has an exit on its room's bottom row, cells (31, 24) and (32, 24), leading back to `fromInterior`. Both were placed with `AddExit` on chunks opened alone; saving them also redrew the Sandbox chunk's wall render tiles along its upper and right borders and wrote an empty `coastSource` on both chunks, which were left out.
- **Tests:** `WorldTripTests` (EditMode: the authority's checks and approval, `WorldExits`, the guard, `TripArrivals`, the time freeze), exit cases in `WorldValidationTests` and `LocalWorldPresentationTests`, and `WorldTripPlayModeTests` (the trip into the interior and back within the grace, the grace running out, the menu after a trip).

### Phase 7: multiplayer

- [x] Each player's world as a `NetworkVariable`, and trip requests over RPC.
- [x] The host streams for every player in every world, and a client for itself.
- [x] `ChunkReady` and `ChunkReleasing` with `NetworkShow`/`NetworkHide` per client, tagged with the trip count.
- [x] Per-chunk terrain sync.
- [x] The player id in the join payload, and a joining client spawns in its saved world (nothing is saved per player until Phase 8, so every joining player starts at the starting world's entry; `PlayerStarts.TryGetSaved` is the seam).
- [x] Remove `NetworkAreaSync`, area announcements and `AreaEpoch`.

Notes:

- **Sessions moved to worlds.** `GameScenes` has a starting world and entry (`StartingWorld`, `StartingWorldEntry`: Testlands' `start`). Host Game starts there and no longer continues the area save; hosting from a world save is Phase 8, and a hosted session writes no save meanwhile. Join Game connects, then `GameFlow.StartJoinedGameAsync` loads Gameplay and `IWorldSession.JoinAsync` waits for the host to place the player. Offline New Game and Continue stay on areas until Phase 9: switching them was not needed for this phase, and Continue has no world save to continue from yet.
- **Joining.** A client joins once its Gameplay scope is up (`NetworkSession.NotifyJoined`, from `GameplayPlayers.Start`, after the player prefab is registered). The host then shows it every player and spawns its own where `PlayerStarts` puts it: its saved place by player id (none until Phase 8), else the starting world's entry. The player is spawned with its world set (`PlayerWorldSync.SetSpawnWorld`), so the host starts streaming that world for it at once; spawning waits for nothing, which is fine while every start is the starting world, and Phase 8 may want to load a saved world before spawning there. A late joiner goes the same way. Players are never hidden: a client sees every player, in any world, as a far-away object that is never drawn.
- **The player's world** is a server-written `NetworkVariable<PlayerWorldState>` on a new `PlayerWorldSync` on the player prefab: the world's id, the player's trip count and the entry its latest trip leads to. The host writes it from `PlayerWorlds` (`Changed`); each client applies it to its own `PlayerWorlds` (`Apply`), so every machine knows every player's world and `WorldPlayers`, `LocalWorldPresentation` and the streaming read it as offline. The trip count lives in `PlayerWorlds` (`Travel` counts one, `TripOf`), so a trip back into the same world is still a change.
- **Trips.** `NetworkTripAuthority` is the Gameplay scope's `ITripAuthority`; `LocalTripAuthority` keeps the checks. A client's `Request` goes through the player's `ITripLink` (`PlayerWorldSync.RequestTripRpc`, naming the exit's persistent id) with the position the client has for its player, which the host checks instead of its lagging copy (clients own their movement, as with completed casts). The host approves through `LocalTripAuthority.TryApprove`, which holds the arrival (the host streams the entry at once) and puts the player in the target world for its next trip; the networked world carries that to the owner, whose `PlayerWorldSync` hands it to `NetworkTripAuthority.Deliver`: the client holds the arrival itself, so its own streaming loads the target, and `LocalTripTraveller` runs the fade as offline. The host's own trips stay local. Nobody else fades or pauses.
- **Teleports snap.** The owner's teleports now go through `NetworkTransform.Teleport` (`PlayerController.Teleported`, `PlayerNetworkSync`), so arriving at an entry thousands of units away jumps there on the other machines instead of sliding, and the host's `TripArrivals` sees the player arrive. This also fixes Multiplayer.md's Phase 3 gap for respawns.
- **Per-chunk readiness** is `ClientChunkReadiness` (Core, plain C#, `ClientChunkReadinessTests`), run by `NetworkSession` over named messages: joined, `ChunkReady(trip, chunk)`, `ChunkReleasing(chunk)` and its acknowledgement `ChunkReleased(chunk)`, all on Netcode's `ReliableFragmentedSequenced` channel, the one it creates and destroys objects on, so the acknowledgement arrives after the hides sent before it (named messages default to `ReliableSequenced`, another channel). A report tagged with an earlier trip is ignored; the host learns a client's trip as it writes that player's world (`IClientReadiness.BeginTrip`). To make up for an ignored report, a client reports every loaded chunk of its new world again when its trip count changes (a chunk kept from an earlier visit is up and may have been reported during an earlier trip). Visibility is a check per object: players are visible to joined clients, a chunk's objects to the clients holding their home chunk; joining, a report and a release each walk the spawned objects and `NetworkShow` or `NetworkHide` the ones whose check changed for that client.
- **Holding a player.** `IClientReadiness.IsClientReady(clientId)` now means the client reported a chunk during its current trip; until then `NetworkStatusEffects` holds its player (its statuses neither tick nor run down). That covers a joining client and a travelling one from the approval until it reports the target's ground, which shortens the unprotected arrival (Architecture.md's Known gaps) on clients.
- **Releasing.** `WorldStreaming` releases a chunk and waits for the acknowledgement before unloading it (a client only; nothing waits offline or on the host, and a lost connection ends the wait). The host's streamer takes the session as its `IChunkHolds` and never unloads a chunk a client holds, so neither does that chunk's world; a client's old world, kept for its grace, stays held on the host until the client releases it. Ending the session (`ReleaseAll`) ignores the holds: the host despawns everything as it leaves, and clients go back to the menu.
- **Home chunks.** The host spawns every chunk object with `INetworkObjectSpawner.Spawn(instance, homeChunk)`, which sets the chunk as Netcode's instantiation data (`NetworkChunkId`); `ChunkNetworkPrefabs`' routers are `NetworkPrefabInstanceHandlerWithData<NetworkChunkId>` and create a client's copy in that chunk's scope, so a mob that walked into a neighbour still lands in its home chunk's scene (the Phase 4 gap). Netcode sets instantiation data only for a prefab with such a handler, so the host registers the chunks' prefabs too; it never instantiates through them.
- **Per-chunk terrain sync.** Every chunk of the three worlds got an `AreaTerrain` listing its grass layer and its walls (`WorldAuthoring.SetUpTerrainEdits`, which `CreateChunk`, `AddWalls` and the brush's walls copy now run). `ChunkTerrainSync` (chunk scope) spawns its `NetworkAreaTerrain` on the host as an object of the chunk, after `ApplyPersistedEdits` (the Phase 8 seam), and despawns it as the chunk unloads; a client registers the prefab per chunk. `AreaTerrainSync` and `AreaClientReady` went with the area sessions.
- **Player id.** `UserSettings.PlayerId` is a GUID made the first time it is asked for and kept in `settings.json` (the format version stays 1: a file without one reads with an empty id). `JoinApproval`'s payload is the version, a line break, then the id; the host keys clients by it (`NetworkSession.PlayerIdOf`). Multiplayer Play Mode's virtual players share the main editor's `settings.json`, so they arrive with the same id; the host gives a duplicate a new id for the session, with a warning.
- **No restart online.** The party wipe restart reloaded the host's Gameplay scene, which clients followed through area announcements. With those gone, and an in-process test of a host reloading Gameplay freezing the editor, a hosted session no longer restarts: each player respawns on its own, as Phase 9 plans for every session. Offline the restart stays until Phase 9.
- **Exits for hosted play.** Testlands, the starting world, had no exit, so it got `Exit_Sandbox` on the road at cells (120, 39) and (120, 40) of chunk (1, 0), leading to a new Sandbox entry `fromTestlands` at (5.5, 42), beside the Sandbox's new `Exit_Testlands` (cells (3, 41) and (3, 42)), which leads back to a new Testlands entry `fromSandbox` at (117.5, 40). Placed with `AddExit` and `SetEntry` on chunks opened alone; the chunk scenes' saves also redrew wall render tiles and wrote an empty `coastSource`, which were left out.
- **Compatibility.** The protocol changed (messages, payload, a new `NetworkBehaviour` on the player), so the Player Settings version went from 1.2 to 1.3 and older builds are refused.
- **Tests:** `ClientChunkReadinessTests`, and new cases in `JoinApprovalTests`, `WorldTripTests` (`NetworkTripAuthority`, trip counts), `ChunkStreamerTests` (holds), `ChunkMobSpawnerTests` (home-chunk routing), `AreaTerrainTests` (`ChunkTerrainSync`) and `UserSettingsTests` (EditMode); `NetworkWorldsPlayModeTests` (new), and `NetworkSessionPlayModeTests` and `NetworkTerrainPlayModeTests` rewritten for worlds, with `InProcessHost` (new) standing in for a host so the editor's own session joins as a client (PlayMode). The other network PlayMode tests host Testlands from the menu and pass unchanged but for their wording. `NetworkAreaSyncPlayModeTests` and `AreaAnnouncementTests` went with what they tested.

#### Checking with Multiplayer Play Mode

The exits have no art: Testlands' is on the road far east of the start (cells (120, 39) and (120, 40)), the Sandbox's to Testlands at the road's west end (x 3), its interior's on the road just east of its start (x 36), and the interior's on its room's bottom row. Walk along the road onto them. Keep the main editor's Console and Hierarchy in view; every check below should log no error.

Two players, one world:

1. Open `Window > Multiplayer > Multiplayer Play Mode` and activate Player 2. Press Play, click Host Game in the main editor, then Join Game in Player 2. Both start at Testlands' `start` (with no save; see Phase 8 for a saved game) and see each other. Since Phase 8 each virtual player has its own `settings.Player2.json`, so the host does not warn about a shared player id.
2. On Player 2 the start chunk's two Weasels appear (both at least 23 cells from the start). Fight one on Player 2: its health, hits and death show on both machines.
3. Walk the host north-west into the meadow (chunk (0, 1)) and Player 2 east along the road past x 100 (chunk (1, 0)). On each machine the ground draws ahead of the player with no gap or visible hitch, and the next chunk's Weasels appear as it loads. In Player 2's Hierarchy only the chunks around Player 2 are loaded; in the host's, the chunks around both players.
4. Walk Player 2 back to the start: the chunk it left unloads on Player 2 (it released it, the host hid its Weasels first), and walking back into a chunk shows its Weasels again. No "destroyed a spawned object" or "despawn" error appears on Player 2.

Two players, different worlds, travelling while the other fights:

5. Walk Player 2 east along the road onto Testlands' exit. Player 2 fades out and in at the Sandbox's `fromTestlands`; the host does not fade or pause. Fight a Weasel on the host during Player 2's trip: it keeps attacking and the host keeps taking damage.
6. Player 2 sees the Sandbox drawn and lit and the host's player gone from view; after about 10 s Testlands unloads in Player 2's Hierarchy. The host keeps Testlands drawn and loads the Sandbox's chunks for Player 2 without drawing them.
7. On Player 2, fight a Sandbox Weasel near `fromTestlands`, then walk east along the road onto the interior's exit and back out through its bottom row. Each trip fades only Player 2.
8. Walk the host onto Testlands' exit: both are in the Sandbox. With Player 2 fighting a Weasel near the start, walk the host onto the interior's exit: only the host fades, Player 2's fight goes on, and the Weasel keeps attacking Player 2 (the host keeps simulating the Sandbox). Walk the host back out.
9. Walk Player 2 west along the road onto the Sandbox's exit to Testlands: it arrives at `fromSandbox` and sees Testlands' Weasels there once their chunk loads.

Four players, joining and leaving:

10. Stop, activate Players 2 to 4, press Play, Host Game, and join Players 2 and 3. Send Player 2 to the Sandbox and Player 3 into the interior. Each machine loads only its own player's world (Hierarchy); the host loads all three.
11. Join Player 4 now: it starts at Testlands' `start` (a player the host does not know), sees every player and receives the start chunk's Weasels once its ground is up. Edit terrain near it from the host (Multiplayer.md's Checking runtime terrain edits): the edit shows on Player 4 and on every client holding that chunk, and on a client arriving there later.
12. Walk Player 2 onto an exit and, while it is faded out, leave the host's game (in-game menu, Leave). Every client returns to the main menu, Player 2 once its trip ends.

### Phase 8: persistence

- [x] `WorldState`, mob deaths and respawn delay, and terrain edits per chunk.
- [x] Save version 2 keyed by player id and world id, autosave, and Continue into each player's saved world and position.

Notes:

- **Decisions taken before starting.**
  - Offline New Game and Continue moved to worlds in this phase, since the save knows only worlds: New Game starts Testlands' `start` offline and Continue the local player's saved place. Areas stay for opening an area scene in the editor and for their tests until Phase 9, and are never saved. The area save went with them: `GameSave.SaveArea`, `TryLoadArea`, `GetSavedOrStartingArea`, `GameFlow.AreaLoading`, `AreaTransition` and `GameScenes.FindAreaByGuid`.
  - Each player's discovered waystones are in the save now, as an empty list, so Phase 9 needs no format bump.
  - Death times are on a world clock that persists, not converted to the delay left: the save holds the game time played on it, and `SessionSave.Time` is that time plus the game time (`IClock`) since the session started. A time recorded in one session means the same in the next, and the delay is read from the mob's `MobConfig` as its chunk loads, so retuning a delay also applies to mobs already dead.
  - Multiplayer Play Mode's virtual players are kept apart as separate installs: in the editor, an instance launched with `-name Player2` to `-name Player4` (the package FAQ's way to tell its players apart) keeps its own `save.Player2.json` and `settings.Player2.json` (`InstanceFiles`), so its own player id, key bindings and offline save. The host's new id for a duplicate stays, for a copied `settings.json`.
- **Where the state lives.** `SessionSave` (Core, Main scope) holds the running session's state in memory: the world clock, each player's place by player id, and a `WorldState` per world visited, each a `ChunkState` per chunk. The menu starts it (`StartNew` for New Game, `StartFrom(data)` for Continue and Host Game with a save) before the session's Gameplay scene loads, and it outlives that scene, so the offline party-wipe restart, which reloads Gameplay, keeps the worlds' states. A world's scope registers its `WorldState` from it, and its chunks' spawner and terrain sync take it from there. A joined client empties it and never reads it: only the machine with authority spawns mobs, restores edits and saves.
- **Mobs.** `ChunkMobSpawner` follows each of its mobs' `Health.Died` (offline or on the host) and records the death by its spawn point's `PersistentId` at the world clock's time; `ShouldSpawn` skips a spawn point whose mob `StaysDead` (`MobConfig.Respawns`, off for bosses, and `RespawnDelaySeconds`, 300 for the Weasel), and a death whose delay has passed is forgotten as the mob spawns again. A mob comes back only as its chunk loads, which happens when the chunk enters a streamed player's interest rectangle (the view plus the 12-cell load margin, so out of sight) or behind a fade (a session's start, a trip, a join), so no further check keeps it out of view. A despawn as the chunk unloads is no death. A spawn point without an id (which validation refuses in a world) comes back at every load, with a warning when its mob dies.
- **Terrain edits.** `ChunkTerrainSync` now runs offline too: as its chunk loads it restores the chunk's edits (`AreaTerrain.RestoreEdits`, which writes and records them as the terrain's own, so the host's sync object, spawned after, sends them to clients like any edit), then records every edit made there into the chunk's record until its scope goes. Saved edits name the chunk terrain's layers by index, so re-authoring a chunk must only append layers; an edit of a layer that is gone is skipped with a warning. (Since KnownGaps.md's Phase 5 they name their layer by its material, so a chunk's layers can be reordered or pruned.)
- **Players' places.** `GameSaveRecorder` (Gameplay scope) records each registered player's place under its player id as it saves: where it stands, or, during a trip, the entry it is going to until it gets there, since the authority puts a traveller in the target world before it moves. It learns each player's id as the player registers (its owner's `NetworkSession.PlayerIdOf` on the host, this install's offline), so a client leaving still has its place recorded as its player despawns, for its next join. A place is checked against its world's chunks when it is used (`PlayerStarts`): a world no longer listed, or a position on none of its chunks, starts the player at the starting entry with a warning.
- **When it saves.** Only where `IGameAuthority.IsAuthoritative` and a world session runs: once a frame at most after any player enters a world (`PlayerWorlds.Changed`), every `GameplaySettings.AutosaveIntervalSeconds` (60) of game time, and at once as the session ends (`GameFlow.WorldSessionEnding`, raised while the players still stand in their worlds, before anything unloads: the menu, the host leaving, a new game or a restart) and on `Application.quitting` (leaving Play Mode in the editor). Area sessions never save, so booting the editor into an area scene leaves the world save alone; booting into a world scene starts a new game there whose first save replaces the save file, as booting into an area did before.
- **Joining at a saved place.** The host now loads a joining player's start before spawning it: `GameplayPlayers` awaits `IWorldSession.PrepareAsync(world, position)`, which streams the place as an interest of its own until `IsSettled` there (at once when it already is: a start in a world a player is in), then spawns the player if its client is still joined. A chunk that fails to load counts as settled, so a join never hangs on one.
- **Multiplayer.** Only the host records deaths, edits and places, and only it writes the save; a client saves nothing (it never did: `IsAuthoritative` is false while following a host). (Since KnownGaps.md's Phase 5 a client keeps a copy of its own place in the host's session, which it uses only once that host is gone.) A client in another world is simulated by the host, which records that world's state as it does its own. A chunk a client holds is never unloaded by the host (Phase 7), so a death or an edit there is recorded once; when the host's own simulation unloads a chunk nobody holds and loads it again, it restores the chunk's state before the mobs and the sync object spawn, so a client that holds it afterwards receives it as a late joiner does.
- **Compatibility.** Neither the protocol nor the join payload changed (the player id has been sent since Phase 7), so the build version stays 1.3. A version 1 save is ignored with a warning, so Continue is off until the first world save.
- **Assets.** `Mob_Default` and `Mob_StressTest` were saved with the respawn fields, and `GameplaySettings` with the autosave interval; nothing else in them changed.
- **Tests:** `WorldStateTests` and `PlayerStartsTests` (new), `GameSaveTests` (rewritten for version 2, the world clock and `InstanceFiles`), and new cases in `ChunkMobSpawnerTests` and `AreaTerrainTests` (EditMode); `WorldPersistencePlayModeTests` (new), `GameSavePlayModeTests` (rewritten for worlds: New Game, Continue, Host Game, a known player joining), and `NetworkSessionPlayModeTests`' host now saving (PlayMode). That a client writes no save stays covered by `NetworkSessionPlayModeTests`' join tests, which count the writes.

#### Checking with Multiplayer Play Mode

Start clean: in `%USERPROFILE%/AppData/LocalLow/<company>/<product>/`, delete the `Saves` folder and any `Saves.Player2` to `Saves.Player4` (keep `settings.json`; since KnownGaps.md's Phase 5 each session has a file of its own there). Keep the main editor's Console in view: every step should log no error.

1. Activate Player 2 and Player 3, press Play, click Host Game in the main editor and Join Game in Player 2. The host's Console logs no warning about a shared player id (Player 2 made its own `settings.Player2.json`); if it does, the virtual player was not launched with `-name Player2`, and its saved place cannot be kept apart from the host's.
2. On the host, kill a Weasel of Testlands' start chunk. Walk Player 2 east along the road onto Testlands' exit to the Sandbox, a few cells past `fromTestlands`, and kill a Weasel there.
3. Leave the game on Player 2 (in-game menu, Leave game), then click Join Game again: Player 2 arrives in the Sandbox where it stood, not at Testlands' `start`. The main editor's Hierarchy shows the Sandbox loading before Player 2's player appears.
4. Join Game in Player 3: it starts at Testlands' `start`, a player the save does not know.
5. Leave the game on the host: every client returns to the menu. Click Host Game again: the host stands where it left and its Weasel is still dead. Join Player 2 and Player 3: each lands where it was when the host left.
6. Walk the host two chunks away from Testlands' start and back: the dead Weasel is still gone (its 300 s have not passed). Wait five minutes of play, walk away and back again: it is back.
7. Stop Play Mode in the main editor while playing, press Play and click Continue (offline): the host's player stands where it was when Play Mode stopped.
8. On Player 2, from its menu, click New Game, play a moment and return to the menu: it writes a session file in `Saves.Player2`, and the main editor's `Saves` folder keeps its files' modified times. While joined, a virtual player writes only its copy of its place in the host's session (`Saves.Player2/joined-<id>.json`) and nothing in `Saves`.

### Phase 9: death, respawn and cleanup

- [x] Waystones: discovery per player, and respawn at the nearest one.
- [x] Remove the restart when every player is dead, and the remaining area and party code.
- [x] The PlayMode tests boot into the test world, and the area scenes and template go.
- [x] Rewrite Architecture.md's sections, and update Multiplayer.md's decisions.

Notes:

- **Decisions taken before starting.**
  - A respawn is a trip within the player's own world: the host picks the waystone, holds its arrival (so it streams the waystone's chunks at once, whichever world it is in) and puts the player in that world for one more trip; the owner hears it through its networked world, as for any trip, and fades, moves itself, waits for the chunks and reports them with the new trip count. The player stays dead during the trip, and the host restores its health once it sees it at the waystone with that chunk loaded (for a client, held by it). No new message.
  - The host detects every player's discoveries, within two cells of a waystone entry's cell (`GameplaySettings`), and the discovering player sees a short popup on its own screen.
  - The nearest waystone is the nearest in a straight line from where the player died, among the waystones it discovered in its own world and the world's default entry; a tie goes to the entry listed first.
  - A dead player is saved at the waystone it will respawn at, so leaving or quitting while dead neither skips the respawn nor brings it back beside its killer.
  - The stress-test scene became a dev world (`Assets/Dev/Worlds/StressTest`), and new chunks get their walls from a prefab saved from the area template (`Walls.prefab`). The Scene Generator writes into scratch scenes it makes (`WorldAuthoring.CreateScratchScene`) and refuses a world chunk.
  - `SpawnPoint`'s slot rule moved to world entries (`EntrySlots`) and the component went: no scene kept one once the areas, the template and the stress area went.
- **The cleanup** (its own commit, before the waystones).
  - Gone: `AreaLifetimeScope`, `AreaEntry`, `IAreaEntry`, `AreaEntryRequest`, `AreaMobSpawner`, `ActiveSpawnPoint`, `SpawnPoint`, the area half of `PlayerNavigation`, `GameFlow.StartNewGameAsync`, `ChangeAreaAsync`, `RestartAsync`, `CurrentArea` and `AreaScene`, `GameScenes`' starting area, spawn id and areas, `GameplaySettings.RestartDelaySeconds`, `PlayerBinder.PlaceAtActiveSpawn` and the owner's placement on a restore in `PlayerNetworkSync`. `PlayerDeathHandler` lost its restart and runs its respawn delay on the game clock (`IClock`, `ITickable`) instead of `Awaitable.WaitForSecondsAsync`, so EditMode tests drive it.
  - **Entry slots.** Players arriving at one place of a world (the session's start, a trip, a joining player's spawn, and from the waystones on a respawn) take the first slot no other player of that world stands on: slot 0 on the place, then alternately right and left, `GameplaySettings.EntrySlotSpacing` (0.6) apart, as `SpawnPoint` laid out a party. The registry index no longer decides it.
  - **Boot.** `BootFlow` boots a world's scene into that world, Gameplay alone into the starting world at its entry, and anything else (a scratch scene) to the menu with a warning. `GameFlow` holds the world it enters while unused assets unload: a world no loaded object references (the dev stress world, booted from the editor and listed nowhere) was otherwise unloaded under it and the session started into nothing.
  - **The time freeze.** `TransitionTimeFreeze` held for every transition that is no trip, in a session too: a machine's own start, join or leave froze its game clock while its clients played on, which made a client's status timers read short after a host transition (a Known gap, now gone). It now holds offline only, as the Simulation and presentation decision says.
  - **Scenes and assets.** The six area scenes, their `SceneDefinition`s, `AreaTemplate.unity`, `AreaTemplate.scenetemplate` and `Area_StressTest.unity` were deleted through the AssetDatabase, the build list lost the six areas, and `GameScenes.asset` and `GameplaySettings.asset` were reserialized without the removed fields. The generator assets and seeds stay. The world layout's origin slot is free again (`WorldLayout.TryFindFreeOrigin`), and the stress world took it.
  - **The stress world.** 2 x 2 chunks at (0, 0), made with `WorldAuthoring` into `Assets/Dev/Worlds` (`CreateWorld`'s parent folder), out of the build list and of `GameScenes`: `SceneBuildValidator` skips definitions under `Assets/Dev/` and `WorldAuthoring` does not add their scenes to the build. Each chunk's `StressTestMap` paints its own part of the 128 x 128 map in `Awake`, laid out alike in every chunk from the seed, before `ChunkNavigation` builds the page; the spawner in the root scene waits for every chunk's page, then spawns through the world's scope. The map's walled ring now lies on the map's outer cells, from cell (0, 0), instead of around a map centred on the origin.
  - **Tests moved to Testlands.** `SceneFlowPlayModeTests`, `PlayerControlsPlayModeTests`, `MobPlayModeBehaviorTests` and `GameMenuPlayModeTests` boot into Testlands (`SceneBootTestHelper.BootIntoTestlands`; `BootIntoWorld` replaced `BootIntoArea`). The area change and restart cases went; the time freeze case runs on an offline new game, and the death cases check that one player dying and everyone dying at once respawn without a reload or a transition. `PlayerControlsPlayModeTests` builds its wall through the start chunk's `AreaTerrain` and hides a mob of that chunk behind it. `PlayerMotor2DTests` paths on a world's navigation (a chunk page of a world built in memory) instead of an area grid. `AreaEntryTests`, `AreaMobSpawnerTests` and `AreaTemplateTests` went; `EntrySlotsTests` is new.
- **Waystones** (their own commit, after the cleanup).
  - **The rules** are `WaystoneRules` (Core, plain): `IsDiscovered` (the default entry always, a waystone once the player's list names it in that world), `TryFindNearest` and `TryFindDiscovery`. A player's discoveries are a list of `GameSaveData.Waystone` (world id, entry name), which `SessionSave` keeps per player id apart from its place (`Discover`, `WaystonesOf`) and writes into the player's record in the existing `waystones` list, so the format stays version 2. A player with discoveries but no place yet is left out of the file; every player in a world has its place recorded before each save.
  - **Player ids** moved out of `GameSaveRecorder` into `PlayerIds` (Gameplay scope), which `WaystoneDiscovery` and `PlayerRespawner` read too. It learns a player's id as the player registers, while its owner is still known, and keeps it until another player registers after it leaves, so it is readable while the registry announces a removal whatever the order of the handlers. `NetworkSession` gives the ids as `IPlayerIdSource`, so the tests fake it.
  - **Discovery** (`WaystoneDiscovery`) runs offline and on the host only, comparing each living player's cell with the last one every frame. A client's player is checked at the host's copy of its position, which follows the same cells a few frames late; arriving on a waystone by a trip discovers it too. The popup was `WaystonePopupPresenter` on the pooled popup layer, its text and colour in `GameplaySettings` (since UiRevamp.md's Phase 3 a notice on the HUD's notice line, raised by `NoticePresenter`, the colour gone); a client hears of its own player's discovery through `PlayerWorldSync.WaystoneDiscoveredRpc` (server to owner), its only new message.
  - **Respawns.** `PlayerDeathHandler` keeps the timing: the delay on the game clock, then, once no trip of the player is under way, `PlayerRespawner.TrySendToWaystone` sends it through `NetworkTripAuthority.Send`, a trip the authority starts itself with no exit to check (`LocalTripAuthority.Send`, which an approved request also ends in). That one call gives the respawn everything trips have: the host streams the waystone's chunks for the player at once, in whatever world the host is in; the player's networked world carries the trip and its entry to its owner, whose `LocalTripTraveller` runs `GameFlow.TravelAsync` (never a time freeze); the client reports its chunks with the new trip count, and `IClientReadiness.BeginTrip` holds the client's player's statuses until it reports one. Statuses are cleared as a unit dies, so the hold matters only for what lands on the player at the waystone, until its first report.
  - **The restore** waits for `PlayerRespawner.HasArrived`: the host's copy within two cells of the entry in that world (the client's teleport snaps there through `NetworkTransform.Teleport`), the entry's chunk loaded on the host and, for a client's player, held by its client. Holds outlived trips, so a waystone chunk the client already held counted at once, the host still waiting for the player to stand there, until KnownGaps.md's Phase 4: a held chunk now counts for a trip only once the client reports it during that trip, and a trip into another world releases the client's holds of the world it leaves. A client sees its player come back a round trip after it arrives, sometimes just after its fade in (Architecture.md's Known gaps). A player in no world, or in a world without entries, is restored where it stands.
  - **Saving a dead player.** `GameSaveRecorder` records a dead player that is on no trip at the waystone it will respawn at (`TryGetRespawnPlace`), and one on its respawn trip at the trip's entry, the waystone, as it records any traveller. A client that leaves while dead is recorded so as it leaves, and rejoins alive at that waystone; Continue does the same offline.
  - **Multiplayer cases.** A player dying in a world the host is not in respawns the same way: the host simulates that world and streams the waystone's chunks for it. A player dying on a trip, or while its chunks still load after joining, waits for that trip's arrival or transition first (its machine's `LocalTripTraveller` waits for any running transition). A late joiner sees a dead player arrive dead (`NetworkHealth`) and come back with the host's restore. A dead player that disconnects is forgotten by the death handler and saved at its waystone.
  - **Testlands** got a second waystone, `north` (30.5, 90.5) on the grass of chunk (0, 1), 50 cells from `start` and 19 from the nearest Weasel spawn point (`WorldAuthoring.SetEntry`); validation passes.
  - **Compatibility.** The new RPC changes the protocol, so the build version went from 1.3 to 1.4. The join payload is unchanged.
  - **Tests:** `WaystoneTests` and `PlayerDeathHandlerTests` (EditMode: the rules, the nearest choice, discoveries round-tripping through the save keyed by player id, `WaystoneDiscovery`, `PlayerIds`, a dead player's saved place, and the death handler with no restart: the delay, the trip within the world, the restore on arrival, everyone dead at once, a trip under way, a player in no world, no authority), and `WaystonePlayModeTests` (PlayMode: offline respawns at the nearest discovered waystone after the delay, walking to a waystone and Continue keeping it, and an in-process client's player respawning with the host deciding and the client moving). `SceneFlowPlayModeTests`' death cases check the trip and the respawn at `start`.

#### Checking with Multiplayer Play Mode

Testlands' waystones have no art: `start` is on the road where every new player starts, and `north` on the grass 50 cells north of it (cell (30, 90); walk north from `start` through the meadow). Waystones count within two cells. Keep the main editor's Console in view: every step should log no error. To die, stand beside a Weasel and let it attack: one kills a player in about eight seconds (10 damage every 0.75 s). One spawns 19 cells west-south-west of `north`, and the start chunk has two near its southern edge.

1. Start clean (delete the `Saves` folder and `Saves.Player2` to `Saves.Player4`). Activate Player 2 and Player 3, press Play, Host Game in the main editor, Join Game in Player 2.
2. Walk Player 2 to `north`: "Waystone discovered" floats over Player 2 on Player 2's screen only. Walk the host there too: the host sees its own popup only.
3. Let Player 2 die a few cells south of `north`. About three seconds later only Player 2 fades out and back in, standing at `north`, alive at full health; the host does not fade or pause. Let the host die near `north` while Player 2 is alive: only the host fades and respawns at `north`.
4. Let both die beside the same Weasel, the second while the first is still dead: nothing restarts and no one returns to the menu; each respawns on its own, about three seconds after its own death.
5. Walk Player 2 east along the road onto Testlands' exit to the Sandbox and die there: Player 2 respawns at the Sandbox's `start` (it discovered no Sandbox waystone), while the host, in Testlands, keeps playing. The host loads the Sandbox's `start` chunk for it without drawing it.
6. Let Player 2 die south of `north`, and while it is still dead (within three seconds) leave the game on Player 2 (in-game menu, Leave game), then Join Game again: it arrives alive at `north`, the waystone it would have respawned at.
7. Kill Player 2 and, while it is dead, Join Game in Player 3: Player 3 sees Player 2 lying dead, then come back at its waystone.
8. Stop Play Mode in the main editor, press Play and click Continue: the host's discovered `north` is kept (die near it: it respawns there). In Player 2, New Game and walking to `north` writes the discovery to a session file in `Saves.Player2` only.

### Review fixes (after the merge into main)

A review of Phases 1 to 9 on `main` found these, fixed with tests on `feature/character-animation`:
- **A world whose root scene fails to load no longer hangs the game.** `ChunkStreamer.IsSettled` accepted the failed world but waited for chunks it never tries (it loads no chunk of a world that is not loaded), so entering, arriving or preparing a joining player there waited forever with the screen faded out, and every later transition was refused. A failed world now counts as settled.
- **A chunk being released is neither reported nor stood on.** When the local player's trip count changed, a client reported every loaded chunk again, including one whose release still waited for the host's acknowledgement; the host then held it again and showed it its objects while the client unloaded it (destroy errors on the copies, a chunk held for good, its mobs invisible to that client when it came back). The release in flight is now skipped (it is reported as it loads again), and a player arriving around a chunk being unloaded is not settled until the chunk has loaded again, rather than fading in on ground about to go.
- **Deaths follow their spawn point's id across the world.** A mob's death was recorded and looked up only in the record of the chunk holding its spawn point, so a regeneration moving a spawn point (with its derived id) to another chunk brought the mob back. `WorldState.TryFindMobDeath` now finds a death in any chunk's record, the home chunk's first, and recording one drops the same id elsewhere.
- **A save this build cannot read is kept.** Host Game treated a malformed save, or one from a newer build, like no save, and its first save replaced it. `GameSave` now keeps a copy (`save.json.bak`, numbered after the first) before the first write over a save it cannot read, and writes nothing when the copy fails.
- **Creating a world or a chunk keeps the open scenes.** With any untitled scene open it replaced every open scene without asking to save. It now closes only a clean untitled scene, and refuses, making nothing, beside an untitled scene with unsaved changes.
- **A dead player cannot take an exit.** The host approved trips for players it had just seen die; `LocalTripAuthority.TryApprove` refuses them (respawns go through `Send`).
- **The palette's Add the layer relinks a chunk.** A layer added with it was not in the neighbours' skirts until the chunks were reopened.
- **A path over unloaded ground is dropped.** A follower whose navigation emptied kept walking its old waypoints; it drops the path when it cannot rebuild it.

### Later: world generation (its own plan)

- A world generator.
- The six areas brought into the first world as regions.
- Where each region sits, and what joins them.

## Testing

- **EditMode:**
  - `ChunkCoords` and origin checks;
  - waystone discovery, the nearest-waystone choice, discoveries round-tripping through the save, and the death handler with no restart (Phase 9);
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
  - a trip offline leaving game time running in the session case;
  - a dead player respawning at its nearest discovered waystone, offline and for an in-process client (Phase 9).
- **Online tests.** An in-process client test where the host reloads Gameplay freezes the editor. Trips no longer reload Gameplay, so chunk and trip tests stay within one session (`NetworkWorldsPlayModeTests`, `NetworkSessionPlayModeTests` against `InProcessHost`), and a hosted session no longer restarts.
- **Manual**, kept for the multiplayer testing feature ([Roadmap.md](Roadmap.md)): Multiplayer Play Mode (Phase 7's steps):
  - two players walk to opposite corners of a world and back;
  - one enters a dungeon while the other keeps fighting outside;
  - the host enters a dungeon while a client fights outside.

## Open questions

- **Chunk size:** 64 cells, revisited only if Phase 2's load times call for smaller chunks. 32 cells would load less per chunk, but give four times as many scenes per world (1024).
