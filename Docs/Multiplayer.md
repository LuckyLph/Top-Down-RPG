# Multiplayer plan

Plan for taking the game from single player to online co-op. [Architecture.md](Architecture.md) describes what exists today; this file describes where it is going and the rules new code should follow so it does not have to be rewritten later. Update the phase checklist as work lands.

## Decisions

| Topic | Decision |
|---|---|
| Mode | Co-op only. No PvP. |
| Topology | Host and play (listen server). One player is both server and client. No dedicated server. |
| Players | Designed and balanced for 2, must support up to 4. Single player is the same code path with one player. |
| Trust | Co-op between friends: each client owns its own player's movement. The host owns everything else (mobs, health, damage, spawning, deaths and respawns, trips between worlds). |
| Library | Netcode for GameObjects (NGO) + Unity Transport. Multiplayer Play Mode for multi-player testing in one editor. |
| Internet play | No paid or account-bound services for now: direct IP only, and friends play over the internet through a virtual LAN (Tailscale, ZeroTier). A hosted relay is deferred until there is a distribution platform (see Phase 6). |
| Saving | One local save file on the machine that decides game state (the host, or the single player), holding each player's place by player id and the state of every world visited (see [WorldStreaming.md](WorldStreaming.md#persistence)). Joining clients get the host's world through normal replication and save nothing. |
| Worlds | Each player is in one world at a time and travels between worlds on their own, through exits the host validates, with no party (see [WorldStreaming.md](WorldStreaming.md)). Every session, offline, hosted or joined, runs in seamless worlds; the area path went in its Phase 9. |
| Death | Each player on their own: a dead player respawns at the nearest waystone they discovered in their world, after `GameplaySettings.RespawnDelaySeconds`, the host deciding where and the player's own machine travelling there; a world's default entry counts as discovered. Nothing restarts when every player is dead at once, offline or online (see [WorldStreaming.md](WorldStreaming.md#death-and-respawn)). |
| Friendly fire | Off. Hostile effects only land on the other faction and helpful ones within a faction, enforced by `DamageService` (see [DamageAndStatusEffects.md](DamageAndStatusEffects.md#decisions)). |
| Mob counts | The large-crowd stress world stays a local, single-player profiling tool. Online gets its own test scenes sized for 2–4 players. |

## Authority model

| Thing | Authority | Replicated to clients as |
|---|---|---|
| Player movement and facing | Owning client | Transform + facing/animation state |
| Player attack | Owning client runs the attack's timeline (start, windup cancels, contact), host resolves its hit at contact around where the owner stood: a melee hit at once, a projectile's when it reaches a unit; a client holds its own contact frame on its own prediction of a melee hit (KnownGaps.md's Phase 4) | Start, contact and early-end events naming the weapon (every other machine plays the attack on its copy, its windup waiting for the contact at most a quarter second past its end, and flies its projectile from the contact, visual only) |
| Player's equipped weapon | Owning client | Owner-written `NetworkVariable` (the weapon's index in the weapon catalog); copies and late joiners equip it |
| Whether a player's lantern burns | Owning client (it lights itself at sunset and goes out at sunrise by the shared time of day, and its key toggles it) | Owner-written `NetworkVariable<bool>`; copies and late joiners show it (Lighting.md's Phase 9) |
| Mob AI, movement, attacks | Host (runs each attack's timeline, holding the mob still, and resolves its hit at contact in the area ahead of it) | Transform + move (its actual velocity over its move speed) and facing (`MobNetworkSync`) + attack start (which attack, its direction), contact and early-end events; every client plays the attack on its copy, visual only, a late joiner from the next start |
| `Health` of players and mobs | Host | Current HP + typed hit (naming its source unit and the point it came from, so every machine turns its feedback alike), immune hit and heal events |
| Status effects on players and mobs | Host (applies, ticks, expires; holds a loading client's player) | Status list per unit (`NetworkStatusEffects`) + blocked-status events |
| Spawning/despawning players and mobs | Host | NGO spawn/despawn |
| Player death, respawn and waystone discoveries | Host (discovers for every player, picks the respawn waystone, restores health once the player stands there) | `Health` restore; a respawn is a trip within the player's world through its networked world, its owner moving itself there; a discovery reaches its owner as an RPC for its popup |
| Runtime terrain edits (`AreaTerrain`) | Host | Per-cell edit list per chunk (`NetworkAreaTerrain`, shown to the clients holding that chunk); each client applies it and its grid follows |
| Each player's world and trips | Host (a client asks for its trip by RPC naming the exit) | The player's world, trip count and trip entry as a `NetworkVariable` on the player (`PlayerWorldSync`); a client loads its own player's world and fades locally |
| Save data | Host | Not replicated. The host (or the single player) saves when any player enters a world, on an autosave timer and as the session ends; a joining player the host knows starts at its saved place. |
| Visual effects (character animation, projectiles in flight, hit feedback and impacts, death effects, damage popups, camera, HUD) | Each client, locally | Never networked objects |

## Rules for new code (starting now)

- Never assume one player. Code that needs "the player" either takes a specific player or asks the player registry. The camera, HUD and input only ever deal with the local player.
- Keep "what happened" apart from "how it looks". Game state changes (damage, death, spawn, attack resolution) go through one service or component that the host will own; visuals react only to events.
- Plain C# logic stays free of networking types so EditMode tests keep working with fakes.
- Things that exist per player or per mob are spawned at runtime from a prefab, not placed in scenes. Scenes hold markers (spawn points).
- Timers go through `IClock` and randomness through an injected source, so the host's simulation is not tied to `Time`/`UnityEngine.Random` statics.
- Every RPC names who may invoke it (`InvokePermission`): `Owner` for an owner's messages, `Server` for the host's. Netcode lets anyone invoke an RPC by default, and the host relays a client's `SendTo.NotServer` call to every other client, so a host message left open could be faked by any client (`NetworkRpcTests` checks every RPC).
- Persistent state goes through `GameSave` and is written only where `IGameAuthority.IsAuthoritative`. Add fields to `GameSaveData` and bump `CurrentVersion` instead of writing new files; identify assets in saves by GUID, not path. Per-machine preferences (key bindings, later audio and video) are not game state: they go through `UserSettings` (`settings.json`) on every machine, clients included, and are never replicated (see [KeyBindings.md](KeyBindings.md#saving)).

## Where the current code assumes one local player

| Area | Today | Target |
|---|---|---|
| `SwordSlashAttack` | Gone. Each machine's slash used to decide what it hit | Done in [Animation.md](Animation.md)'s Phase 3: only the host resolves an attack's hit, at contact (`AttackService`; a client's contact reaches it as `AttackContactRpc` with the owner's position), and other machines' attacks are visual only |
| `GameFlow` | Each machine loads its own worlds and chunks; the host decides each player's world | Done in [WorldStreaming.md](WorldStreaming.md)'s Phase 7 |

## Phases

### Phase 1: multiplayer-ready refactors (no netcode package)

Each step ships on its own, keeps single player working, and comes with EditMode tests.

- [x] Player registry + local player; mob perception picks among players.
- [x] Runtime player spawning through `resolver.Instantiate`; `AreaEntry` places all registered players.
- [x] Player command source split out of `PlayerController`.
- [x] Combat: damage application in one place (`DamageService`); popups driven by `CombatEvents`, death effects by `Health.Died`. Which machine resolves an attack's hit was settled in Animation.md's Phase 3 (the host, at contact).
- [x] Mob spawn markers + area mob spawner (replaces in-scene mob instances and the build-callback injection).
- [x] Per-player death and respawn: a dead player respawns at the area's spawn point after `GameplaySettings.RespawnDelaySeconds` while a teammate is alive; everyone dead at once restarts the area. Replaced in WorldStreaming.md's Phase 9: each player respawns on its own and nothing restarts.
- [x] Remaining gameplay timers through `IClock`, injected RNG (`IRandom`). Visual-only animations keep `Time.deltaTime`.

### Phase 2: networking foundation

- [x] Add NGO 2.13.3, Unity Transport (pinned to 2.6.0, see [Architecture.md](Architecture.md#packages)) and Multiplayer Play Mode 2.0.2.
- [x] `NetworkSession` in the Main scope owns the `NetworkManager` (spawned from a prefab rather than placed in `Main`, so it dies with the scope); Host Game / Join Game (direct IP) in the main menu; returning to the menu ends the session, and a lost connection returns a client to the menu.
- [x] VContainer integration: `NetworkSession.RegisterPrefab` installs an `InjectingNetworkPrefabHandler` that creates client-side instances through `resolver.Instantiate`. The handler's `Instantiate` is not called on the host, so the host creates its instances with `resolver.Instantiate` and then spawns them (first used in Phase 3).

Until Phase 3, a client that joins only connects: it loads the starting area locally with its own player and mobs, and nothing is replicated.

### Phase 3: players

- [x] `NetworkObject` on the player prefab, owner-authoritative `NetworkTransform` (x/y only), remote copies kinematic; move and facing replicated so remote animation matches.
- [x] Attack sent by the owner (`AttackRpc`); every other machine replays the slash; only the host's `DamageService` applies damage (clients apply none). Replaced in Animation.md's Phase 3: the owner sends its attack's start, contact and early end, and only the host resolves the hit, at contact.
- [x] Per-player HUD and camera bound to the local player only, through `LocalPlayerTracker`.
- [x] Ready handshake: a joining client reports ready once its Gameplay scope can receive spawns; until then the host hides spawned objects from it, then shows them and spawns its player.

Known gaps (expected, not bugs):
- A teleport by the owner is interpolated on other machines instead of snapping. Fixed in WorldStreaming.md's Phase 7: the owner's teleports go through `NetworkTransform.Teleport`.
- Mobs spawned by the dev stress-test spawner stay local to the host.

### Phase 4: mobs

- [x] Mob AI ticks only on the host; clients show replicated position and state (`MobNetworkSync`). Mob transforms use the same measured smoothing as players (`LegacyLerp`, unreliable deltas). The player smoothness regression test passes with mobs replicating alongside; there is no mob-specific benchmark yet.
- [x] `Health` HP replicated for players and mobs (`NetworkHealth`); hits reach clients through `DamageService.ApplyReplicatedDamage`, so popups and HUD are unchanged. The host's respawns reach the owner, which places itself at its spawn slot.
- [x] Mob death despawns on the host; the death animation plays locally on each machine.
- [x] Clients report ready only after their area has registered its mob prefabs, so late spawns always arrive injected.

### Phase 5: areas and joining

Decision: keep `GameFlow` in charge with Netcode's scene management off (Option A). Netcode's scene management mirrors the host's loaded scenes onto every client; we load scenes ourselves and tell clients which area to load. What we give up, and how we cover it:
- Objects placed in scenes are not synced to late joiners. Rule: every networked object (players, mobs, and future doors, chests, pickups) is spawned at runtime from a marker, never placed in a scene.
- Clients are not automatically sent to the host's scenes. The host announces its area (`AreaAnnouncement`) on every change and to every client that connects.
- No built-in "all clients finished loading" events. The ready handshake, tagged with the area epoch, plays that role.
- No scene-migration sync. Players live in the Gameplay scene and mobs in their area, so nothing networked moves between scenes.
This also keeps each machine loading only what it needs, which leaves the door open to players in different areas later.

Replaced in [WorldStreaming.md](WorldStreaming.md)'s Phase 7: scene management stays off, but area announcements, the area epoch and the single ready set went. Each player's world is a host-written network value on the player, a client loads its own player's world, and readiness is per chunk (a chunk's objects reach a client while it holds the chunk, tagged with the player's trip count). The rule above about runtime-spawned networked objects still holds, and a chunk's objects live in their home chunk's scene.

- [x] Host-driven area changes: `GameFlow.AreaLoading` on the host despawns the scene's objects, bumps the area epoch and announces; clients follow through `GameFlow`, and new objects reach a client only after it reports ready for that epoch.
- [x] Late join: a client connecting mid-game is told the current area, loads it, reports ready, and then receives the players, mobs and current HP.
- [x] Party wipe: the host's restart is announced as a new session, so clients reload with it. Gone since WorldStreaming.md's Phase 7 (a hosted session never restarted) and Phase 9 (nothing restarts).
- [x] Disconnects: a leaving client's player is removed by Netcode; the host leaving sends every client back to the menu, including a client still loading or changing area (it goes back once that load ends).
- [x] Connection approval: the host refuses clients beyond `NetworkSettings.MaxPlayers` (4, host included) and clients running another build version, and the joining player sees why.

### Phase 6: services and persistence

Decision: no Unity Gaming Services for now. Relay has a free tier (50 average monthly CCU, 3 GiB per CCU), but it is a paid product tied to a Unity Cloud account, and the current Multiplayer Services package (2.3.x) requires Unity Transport 2.7.3, which this project avoids (see [Architecture.md](Architecture.md#packages); 2.2.4 is the last release on Transport 2.6.0). Free alternatives considered:

| Option | Why not now |
|---|---|
| Virtual LAN (Tailscale, ZeroTier) | Chosen: no code, works with the existing direct-IP join. Each player installs the app once. |
| Steam Datagram Relay through a community NGO transport | Free relay and friend invites, but shipping needs a Steam app and the transport is community-maintained. The natural choice if the game ships on Steam. |
| Epic Online Services P2P | Free relay, but no maintained NGO transport; we would own one. |
| UPnP port mapping | Fails on routers with UPnP off and behind carrier-grade NAT. |

A relay is a transport swap: it replaces how `NetworkSession.StartHost`/`JoinAsync` configure the transport, while the replicated worlds and the per-chunk readiness handshake stay as they are. No connection-method abstraction was added ahead of that, since its shape depends on the transport chosen.

Save data decisions (format version 2 since [WorldStreaming.md](WorldStreaming.md)'s Phase 8; this phase saved the party's area and spawn point, version 1, which is now ignored):
- What persists: per player id, its world, position (a dead player's is the waystone it respawns at) and discovered waystones; per world visited, a record per chunk of its killed mobs (with when they died, on a world clock that continues across sessions) and runtime terrain edits. Health is restored on respawn, and the equipped weapon (switched only with the dev panel so far) is not saved: every session starts with the sword. Per-player progression will be added to `GameSaveData` behind a format-version bump when it exists.
- Where: a local JSON file at `Application.persistentDataPath/save.json`, written by the host (or the single player) only. No Cloud Save, so offline and direct-IP games need no sign-in.
- When: whenever any player enters a world (the session's start, a trip, a player joining), every `GameplaySettings` autosave interval (60 s of game time), and as the session ends (the menu, the host leaving, quitting).
- Menu: Continue continues the save offline, the local player at its saved place; New Game starts over in the starting world (replacing the save at its first write); Host Game continues the save when there is one, else starts over.
- Joining clients: get the host's world through their player's networked world and replication, and never write a save. A client the host knows by its player id starts at its saved place; the host loads that world before spawning it there.

- [x] Save data: host-only local save of the current area and spawn, Continue in the menu, hosting continues from the save (now world saves, WorldStreaming.md's Phase 8).
- [x] Internet play without Unity services: documented virtual LAN setup over the existing direct-IP join (see Testing).
- [ ] Hosted relay for internet play without a virtual LAN (deferred; Steam is the likely transport if the game ships there).

## Testing

- EditMode: all new plain-class logic (registry, targeting, commands, respawn rules) with fakes, as today.
- PlayMode: host and client in the same process for spawn, replication and damage flows.
- Manual: Multiplayer Play Mode with 2 and 4 players, following [WorldStreaming.md](WorldStreaming.md#checking-with-multiplayer-play-mode)'s steps for seamless worlds online. Testing by hand like this is a later feature of its own ([Roadmap.md](Roadmap.md)); until then features are checked by the EditMode and PlayMode tests alone, and feature plans carry no Multiplayer Play Mode checks (CLAUDE.md).

### Running two players in the editor

1. Open `Window > Multiplayer > Multiplayer Play Mode` and activate Player 2 (up to Player 4).
2. Press Play. Every player boots to the main menu.
3. Click Host Game in the main editor, then Join Game in a virtual player (the address field defaults to `127.0.0.1`).

Hosting listens on `0.0.0.0:7777`, so Windows may ask to allow the Unity editor through the firewall the first time.

Virtual players use the same company and product name as the main editor, so they share its data folder, but each keeps files of its own there (`save.Player2.json`, `settings.Player2.json`, and so on, from the `-name` argument Multiplayer Play Mode launches it with): its own player id, key bindings and offline save, as a separate install would.

### Checking saves

1. Play, click New Game (Testlands' `start`), walk somewhere, kill a Weasel, then return to the menu (or stop and press Play again). Continue is now enabled and selected.
2. Click Continue: the game starts where the player stood, and the Weasel stays dead until 300 s of game time have passed and its chunk loads again (walk two chunks away and back).
3. In Multiplayer Play Mode, Host Game in the main editor continues the same save; Join Game in a virtual player starts it at the starting entry the first time, and at the place it left the next time (see WorldStreaming.md's Phase 8 steps). A virtual player writes nothing while joined.
4. The file is `%USERPROFILE%/AppData/LocalLow/<company>/<product>/save.json` on Windows (`save.Player2.json` for Player 2). Delete it to get a clean menu. Automated tests use an in-memory store and never touch it.

### Checking runtime terrain edits

Nothing in gameplay edits terrain yet, so the editor menu Tools > TopDownRPG > Dev > Toggle Terrain Cell At View Centre makes an edit by hand: in play mode it toggles the cell at the Scene view's pivot (or at the centre of the Game view when no Scene view is open) on the tilemap selected in the Hierarchy, through its chunk's `AreaTerrain`, as gameplay would. It runs in the main editor, which hosts. Edits replicate per chunk: a client receives a chunk's edits while it holds that chunk.

1. Open `Window > Multiplayer > Multiplayer Play Mode` and activate Player 2 and Player 3.
2. Press Play. Click Host Game in the main editor and Join Game in Player 2. Both start at Testlands' `start`, in chunk (0, 0).
3. In the main editor's Hierarchy, under the `Grid` of the scene `World_Testlands_0_0`, select `GrassTilemap`. Frame open grass near the players in the Scene view (its centre is the cell edited) and run the menu: a hole opens in the ground on the host, and the same hole opens in Player 2's Game view. Run it again to fill the cell back, and check Player 2 follows. The Console logs each edit.
4. Select that chunk's `CollisionTilemap`, frame another grass cell and run the menu: a wall piece appears on both machines, and a click past it on Player 2 walks around it.
5. Leave a hole and a wall in place, then click Join Game in Player 3: the late joiner sees both as soon as its start chunk is up. Walk Player 2 east past x 100 and back (it releases chunk (0, 0) after the hysteresis and holds it again on its return): the edits are still there.

The edits travel as cell records (the runtime terrain edits row of the authority model); each machine redraws its ground from its own tilemaps, so nothing about the drawing is sent.

### Playing over the internet (virtual LAN)

1. Every player installs [Tailscale](https://tailscale.com) (or ZeroTier) and joins the same network: the host invites the others to its tailnet, or shares the node with them.
2. The host clicks Host Game. Its virtual LAN address is shown in the Tailscale app (`100.x.y.z`).
3. Each other player types that address in the join field and clicks Join Game.
4. If the join times out, allow the game (or the Unity editor) through the host's Windows firewall for UDP 7777 on that network.

### In-process test limits

The PlayMode tests fake a second player with a second `NetworkManager` inside the editor (`InProcessClient`), or a bare host (`InProcessHost`) for the editor's own session to join. The in-process client streams nothing, so it reports chunks itself: every chunk the host has loaded as it joins (`SendReady`), one for a trip (`SendChunkReady`), or every one of its player's world again on each new trip, as a real client's streaming does (`FollowTrips`). Letting the host reload the Gameplay scene while an in-process client is connected froze the editor every time (a deadlock inside the scene load, before any scene code ran). Sessions now run in worlds, whose chunks and trips load and unload scenes without reloading Gameplay, and nothing restarts on a party wipe any more, so no test needs it. The in-process client shares the host's scenes, so it loads no world of its own: it sends the join and chunk messages a real client's streaming sends, while the client side of streaming, releasing and trips is tested with the editor's own session joined to an `InProcessHost`. Check a real joined player in Multiplayer Play Mode, which uses separate processes (see WorldStreaming.md's Phase 7 steps).

### Testing under a bad connection

During Play Mode, select `NetworkManager` under `DontDestroyOnLoad` in the Hierarchy and pick a Connection Preset on its Network Simulator (for example Home Broadband with Congested Network), or set a custom preset from code with `NetworkSimulatorPreset.Create`. The simulator affects every connection in that editor process, and its stage delays and drops what a driver sends and what it receives alike: with one simulator per process, each way passes through it once.

In the PlayMode tests the host and its in-process clients share one process, so the simulator's preset passed each way through two drivers: `NetworkSmoothnessPlayModeTests`' 150 ms / 20 ms jitter / 4% loss preset is about 300 ms each way, a 609 ms round trip measured (KnownGaps.md's Phase 4). The tests delay their connections with `NetworkDelay` (Assets/Tests/PlayMode) instead, built in KnownGaps.md's Phase 4:

1. `NetworkDelay.Install()` before hosting or joining: every driver created from then on applies its simulator stage to what it sends only, from a fixed seed, so each way is delayed once, by its sender, and loss and jitter fall on the same packets run after run. The Network Simulators stay disabled while it is installed.
2. `Set(NetworkDelay.Conditions.Typical)` (or `Fast`, `Slow`, or `new Conditions(roundTripMs, jitterMs, lossPercent)`) gives every driver, running or created later, half the round trip, give or take the jitter, and the loss, both ways alike.
3. `SetSending(manager, delayMs, jitterMs, lossPercent)` sets one manager's way out alone: a later cancel, or 100% loss to hold everything a client sends back for a while.
4. Dispose it in the test's teardown.

Measured (`NetworkDelayPlayModeTests`, unreliable pings at 3.8 ms a frame): no delay 7 ms round trip; Fast (50 ms, ±5 ms each way, 1% loss each way) 55 ms; Typical (150, ±15, 2%) 158 ms; Slow (300, ±30, 4%) 301 ms, each way about half. The reliable channel loses nothing, but a lost packet holds the ones after it until it is resent, so its median rises with loss (171 ms at Typical, 596 at Slow). `NetworkTimingPlayModeTests` measures what each machine shows of the others under these settings.

## Open questions

- Does difficulty scale with player count (mob HP, damage, spawn count)?
- Should each player's camera stay independent, or should the game keep the party on one screen?
