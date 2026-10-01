# Multiplayer plan

Plan for taking the game from single player to online co-op. [Architecture.md](Architecture.md) describes what exists today; this file describes where it is going and the rules new code should follow so it does not have to be rewritten later. Update the phase checklist as work lands.

## Decisions

| Topic | Decision |
|---|---|
| Mode | Co-op only. No PvP. |
| Topology | Host and play (listen server). One player is both server and client. No dedicated server. |
| Players | Designed and balanced for 2, must support up to 4. Single player is the same code path with one player. |
| Trust | Co-op between friends: each client owns its own player's movement. The host owns everything else (mobs, health, damage, spawning, area changes). |
| Library | Netcode for GameObjects (NGO) + Unity Transport. Multiplayer Play Mode for multi-player testing in one editor. |
| Internet play | No paid or account-bound services for now: direct IP only, and friends play over the internet through a virtual LAN (Tailscale, ZeroTier). A hosted relay is deferred until there is a distribution platform (see Phase 6). |
| Saving | One local save file on the machine that decides game state (the host, or the single player). Joining clients get the host's world through normal replication and save nothing. |
| Areas | The party is always in the same area. The host decides area changes. |
| Death | A dead player respawns at the area's spawn point after a delay while a teammate is alive. Everyone dead at once restarts the area. |
| Mob counts | The large-crowd stress scene stays a local, single-player profiling tool. Online gets its own test scenes sized for 2–4 players. |

## Authority model

| Thing | Authority | Replicated to clients as |
|---|---|---|
| Player movement and facing | Owning client | Transform + facing/animation state |
| Player attack | Owning client decides to swing, host resolves hits on mobs | Attack event (every client plays the slash visual) |
| Mob AI, movement, attacks | Host | Transform + compact state (animation state, facing, attacking) |
| `Health` of players and mobs | Host | Current HP + damage events |
| Spawning/despawning players and mobs | Host | NGO spawn/despawn |
| Player death, respawn and party-wipe restart | Host | `Health` restore + transform |
| Current area | Host | Area change message; each client loads and fades locally |
| Save data | Host | Not replicated. The host saves on area entry and continues from its own save. |
| Visual effects (slashes, death animations, damage popups, camera, HUD) | Each client, locally | Never networked objects |

## Rules for new code (starting now)

- Never assume one player. Code that needs "the player" either takes a specific player or asks the player registry. The camera, HUD and input only ever deal with the local player.
- Keep "what happened" apart from "how it looks". Game state changes (damage, death, spawn, attack resolution) go through one service or component that the host will own; visuals react only to events.
- Plain C# logic stays free of networking types so EditMode tests keep working with fakes.
- Things that exist per player or per mob are spawned at runtime from a prefab, not placed in scenes. Scenes hold markers (spawn points).
- Timers go through `IClock` and randomness through an injected source, so the host's simulation is not tied to `Time`/`UnityEngine.Random` statics.
- Persistent state goes through `GameSave` and is written only where `IGameAuthority.IsAuthoritative`. Add fields to `GameSaveData` and bump `CurrentVersion` instead of writing new files; identify assets in saves by GUID, not path.

## Where the current code assumes one local player

| Area | Today | Target |
|---|---|---|
| `SwordSlashAttack` | The slash object decides what it hit (damage itself goes through `DamageService`) | Only the host's copy resolves hits; other clients' slashes are visual only |
| `GameFlow` | Local player triggers area loads | Host decides the area; clients follow |

## Phases

### Phase 1: multiplayer-ready refactors (no netcode package)

Each step ships on its own, keeps single player working, and comes with EditMode tests.

- [x] Player registry + local player; mob perception picks among players.
- [x] Runtime player spawning through `resolver.Instantiate`; `AreaEntry` places all registered players.
- [x] Player command source split out of `PlayerController`.
- [x] Combat: damage application in one place (`DamageService`); popups driven by `CombatEvents`, death effects by `Health.Died`. Which copy of a slash resolves hits is decided in Phase 3.
- [x] Mob spawn markers + area mob spawner (replaces in-scene mob instances and the build-callback injection).
- [x] Per-player death and respawn: a dead player respawns at the area's spawn point after `GameplaySettings.RespawnDelaySeconds` while a teammate is alive; everyone dead at once restarts the area.
- [x] Remaining gameplay timers through `IClock`, injected RNG (`IRandom`). Visual-only animations keep `Time.deltaTime`.

### Phase 2: networking foundation

- [x] Add NGO 2.13.3, Unity Transport (pinned to 2.6.0, see [Architecture.md](Architecture.md#packages)) and Multiplayer Play Mode 2.0.2.
- [x] `NetworkSession` in the Main scope owns the `NetworkManager` (spawned from a prefab rather than placed in `Main`, so it dies with the scope); Host Game / Join Game (direct IP) in the main menu; returning to the menu ends the session, and a lost connection returns a client to the menu.
- [x] VContainer integration: `NetworkSession.RegisterPrefab` installs an `InjectingNetworkPrefabHandler` that creates client-side instances through `resolver.Instantiate`. The handler's `Instantiate` is not called on the host, so the host creates its instances with `resolver.Instantiate` and then spawns them (first used in Phase 3).

Until Phase 3, a client that joins only connects: it loads the starting area locally with its own player and mobs, and nothing is replicated.

### Phase 3: players

- [x] `NetworkObject` on the player prefab, owner-authoritative `NetworkTransform` (x/y only), remote copies kinematic; move and facing replicated so remote animation matches.
- [x] Attack sent by the owner (`AttackRpc`); every other machine replays the slash; only the host's `DamageService` applies damage (clients apply none).
- [x] Per-player HUD and camera bound to the local player only, through `LocalPlayerTracker`.
- [x] Ready handshake: a joining client reports ready once its Gameplay scope can receive spawns; until then the host hides spawned objects from it, then shows them and spawns its player.

Known gaps (expected, not bugs):
- A teleport by the owner is interpolated on other machines instead of snapping.
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

- [x] Host-driven area changes: `GameFlow.AreaLoading` on the host despawns the scene's objects, bumps the area epoch and announces; clients follow through `GameFlow`, and new objects reach a client only after it reports ready for that epoch.
- [x] Late join: a client connecting mid-game is told the current area, loads it, reports ready, and then receives the players, mobs and current HP.
- [x] Party wipe: the host's restart is announced as a new session, so clients reload with it.
- [x] Disconnects: a leaving client's player is removed by Netcode; the host leaving sends every client back to the menu.

### Phase 6: services and persistence

Decision: no Unity Gaming Services for now. Relay has a free tier (50 average monthly CCU, 3 GiB per CCU), but it is a paid product tied to a Unity Cloud account, and the current Multiplayer Services package (2.3.x) requires Unity Transport 2.7.3, which this project avoids (see [Architecture.md](Architecture.md#packages); 2.2.4 is the last release on Transport 2.6.0). Free alternatives considered:

| Option | Why not now |
|---|---|
| Virtual LAN (Tailscale, ZeroTier) | Chosen: no code, works with the existing direct-IP join. Each player installs the app once. |
| Steam Datagram Relay through a community NGO transport | Free relay and friend invites, but shipping needs a Steam app and the transport is community-maintained. The natural choice if the game ships on Steam. |
| Epic Online Services P2P | Free relay, but no maintained NGO transport; we would own one. |
| UPnP port mapping | Fails on routers with UPnP off and behind carrier-grade NAT. |

A relay is a transport swap: it replaces how `NetworkSession.StartHost`/`JoinAsync` configure the transport, while area announcements, epochs and the ready handshake stay as they are. No connection-method abstraction was added ahead of that, since its shape depends on the transport chosen.

Save data decisions:
- What persists now: the area the party is in and the spawn point it entered by. Health is restored on respawn and every player starts with the same weapon, so neither is saved yet. Per-player progression will be added to `GameSaveData` behind a format-version bump when it exists.
- Where: a local JSON file at `Application.persistentDataPath/save.json`, written by the host (or the single player) only. No Cloud Save, so offline and direct-IP games need no sign-in.
- When: every time an area is entered (new game, continue, area change, party-wipe restart). The save always matches the area the party is in, so leaving the session needs no extra write.
- Menu: Continue starts the saved area offline; New Game starts over from the starting area (overwriting the save once entered); Host Game continues from the save when there is one, otherwise from the starting area.
- Joining clients: get the host's area and world through the area announcement and replication, and never write a save.

- [x] Save data: host-only local save of the current area and spawn, Continue in the menu, hosting continues from the save.
- [x] Internet play without Unity services: documented virtual LAN setup over the existing direct-IP join (see Testing).
- [ ] Hosted relay for internet play without a virtual LAN (deferred; Steam is the likely transport if the game ships there).

## Testing

- EditMode: all new plain-class logic (registry, targeting, commands, respawn rules) with fakes, as today.
- PlayMode: host and client in the same process for spawn, replication and damage flows.
- Manual: Multiplayer Play Mode with 2 and 4 players, and a small online test area.

### Running two players in the editor

1. Open `Window > Multiplayer > Multiplayer Play Mode` and activate Player 2 (up to Player 4).
2. Press Play. Every player boots to the main menu.
3. Click Host Game in the main editor, then Join Game in a virtual player (the address field defaults to `127.0.0.1`).

Hosting listens on `0.0.0.0:7777`, so Windows may ask to allow the Unity editor through the firewall the first time.

Virtual players use the same company and product name as the main editor, so they share its save file. Only the host writes it during a session, but a virtual player that starts an offline game also saves there.

### Checking saves

1. Play, click New Game, then return to the menu (or stop and press Play again). Continue is now enabled and selected.
2. Click Continue: the game starts in the saved area.
3. In Multiplayer Play Mode, Host Game in the main editor continues from that save; Join Game in a virtual player follows the host's area and writes nothing.
4. The file is `%USERPROFILE%/AppData/LocalLow/<company>/<product>/save.json` on Windows. Delete it to get a clean menu. Automated tests use an in-memory store and never touch it.

### Playing over the internet (virtual LAN)

1. Every player installs [Tailscale](https://tailscale.com) (or ZeroTier) and joins the same network: the host invites the others to its tailnet, or shares the node with them.
2. The host clicks Host Game. Its virtual LAN address is shown in the Tailscale app (`100.x.y.z`).
3. Each other player types that address in the join field and clicks Join Game.
4. If the join times out, allow the game (or the Unity editor) through the host's Windows firewall for UDP 7777 on that network.

### In-process test limits

The PlayMode tests fake a second player with a second `NetworkManager` inside the editor (`InProcessClient`). Letting the host reload the Gameplay scene while that client is connected froze the editor every time (a deadlock inside the scene load, before any scene code ran), while area reloads were fine. Host restarts are therefore tested without a connected in-process client, and the client side of a restart is tested against an in-process host instead. Check a real host restart with a joined player in Multiplayer Play Mode, which uses separate processes.

### Testing under a bad connection

During Play Mode, select `NetworkManager` under `DontDestroyOnLoad` in the Hierarchy and pick a Connection Preset on its Network Simulator (for example Home Broadband with Congested Network), or set a custom preset from code with `NetworkSimulatorPreset.Create`. The simulator affects every connection in that editor process. A repeatable 150 ms / 20 ms jitter / 4% loss run lives in `NetworkSmoothnessPlayModeTests`.

## Open questions

- Does difficulty scale with player count (mob HP, damage, spawn count)?
- Friendly fire: off by default for co-op?
- Should each player's camera stay independent, or should the game keep the party on one screen?
