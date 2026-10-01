# Multiplayer plan

Plan for taking the game from single player to online co-op. [Architecture.md](Architecture.md) describes what exists today; this file describes where it is going and the rules new code should follow so it does not have to be rewritten later. Update the phase checklist as work lands.

## Decisions

| Topic | Decision |
|---|---|
| Mode | Co-op only. No PvP. |
| Topology | Host and play (listen server). One player is both server and client. No dedicated server. |
| Players | Designed and balanced for 2, must support up to 4. Single player is the same code path with one player. |
| Trust | Co-op between friends: each client owns its own player's movement. The host owns everything else (mobs, health, damage, spawning, area changes). |
| Library | Netcode for GameObjects (NGO) + Unity Transport. Multiplayer Play Mode for multi-player testing in one editor. Unity Relay/Lobby (Multiplayer Services) for internet play, after direct connect works. |
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
| Visual effects (slashes, death animations, damage popups, camera, HUD) | Each client, locally | Never networked objects |

## Rules for new code (starting now)

- Never assume one player. Code that needs "the player" either takes a specific player or asks the player registry. The camera, HUD and input only ever deal with the local player.
- Keep "what happened" apart from "how it looks". Game state changes (damage, death, spawn, attack resolution) go through one service or component that the host will own; visuals react only to events.
- Plain C# logic stays free of networking types so EditMode tests keep working with fakes.
- Things that exist per player or per mob are spawned at runtime from a prefab, not placed in scenes. Scenes hold markers (spawn points).
- Timers go through `IClock` and randomness through an injected source, so the host's simulation is not tied to `Time`/`UnityEngine.Random` statics.

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

Known gaps until Phase 4 and 5 (expected, not bugs):
- Each machine still runs its own mobs. On a client nothing deals damage, so its local mobs can neither hurt nor be hurt; the host's mobs hit the host's copy of a client's player, but that health is not sent to the client.
- The host can't move a client's player (the owner moves it), so respawning a client's player only restores health on the host's copy.
- A party wipe restarts only the host's scenes; clients keep theirs and receive fresh player spawns.
- A teleport by the owner is interpolated on other machines instead of snapping.

### Phase 4: mobs

- [ ] Mob AI ticks only on the host; clients show replicated position and state.
- [ ] `Health` HP replicated; damage events reach clients and republish to `CombatEvents` so popups and HUD are unchanged.
- [ ] Mob death despawns on the host; the death animation plays locally on each client.

### Phase 5: areas and joining

- [ ] Host-driven area changes. Either use NGO scene management, or keep `GameFlow` in charge with NGO scene management disabled. With it disabled, NGO only syncs scenes that were loaded before a client joined, so everything per area must be spawned at runtime (which Phase 1 mob spawning already gives us).
- [ ] Late join: a joining player gets the current area, players, mobs and HP.
- [ ] Disconnects: a leaving player is despawned; host leaving ends the session for everyone.

### Phase 6: services and persistence

- [ ] Relay/Lobby sessions for internet play.
- [ ] Save data and what carries over between sessions.

## Testing

- EditMode: all new plain-class logic (registry, targeting, commands, respawn rules) with fakes, as today.
- PlayMode: host and client in the same process for spawn, replication and damage flows.
- Manual: Multiplayer Play Mode with 2 and 4 players, and a small online test area.

### Running two players in the editor

1. Open `Window > Multiplayer > Multiplayer Play Mode` and activate Player 2 (up to Player 4).
2. Press Play. Every player boots to the main menu.
3. Click Host Game in the main editor, then Join Game in a virtual player (the address field defaults to `127.0.0.1`).

Hosting listens on `0.0.0.0:7777`, so Windows may ask to allow the Unity editor through the firewall the first time.

## Open questions

- Does difficulty scale with player count (mob HP, damage, spawn count)?
- Friendly fire: off by default for co-op?
- Should each player's camera stay independent, or should the game keep the party on one screen?
