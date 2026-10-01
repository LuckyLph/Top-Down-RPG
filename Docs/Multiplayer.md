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
| Mob counts | The large-crowd stress scene stays a local, single-player profiling tool. Online gets its own test scenes sized for 2–4 players. |

## Authority model

| Thing | Authority | Replicated to clients as |
|---|---|---|
| Player movement and facing | Owning client | Transform + facing/animation state |
| Player attack | Owning client decides to swing, host resolves hits on mobs | Attack event (every client plays the slash visual) |
| Mob AI, movement, attacks | Host | Transform + compact state (animation state, facing, attacking) |
| `Health` of players and mobs | Host | Current HP + damage events |
| Spawning/despawning players and mobs | Host | NGO spawn/despawn |
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
| `SwordSlashAttack` | The visual object also finds hits and applies damage | Hit resolution separated from the cosmetic slash |
| Mobs in areas | Prefab instances in the scene, injected by the area scope | Spawned from mob spawn markers by a spawner |
| `PlayerDeathHandler` | A full party wipe reloads the whole session | Per-player downed/respawn; session only ends on a full party wipe (rules to be designed) |
| `GameFlow` | Local player triggers area loads | Host decides the area; clients follow |
| Time and RNG | `MobMotor2D` uses `Time.time`, patrol uses unseeded `Random` | `IClock` and an injected RNG |

## Phases

### Phase 1: multiplayer-ready refactors (no netcode package)

Each step ships on its own, keeps single player working, and comes with EditMode tests.

- [x] Player registry + local player; mob perception picks among players.
- [x] Runtime player spawning through `resolver.Instantiate`; `AreaEntry` places all registered players.
- [x] Player command source split out of `PlayerController`.
- [ ] Combat: hit resolution and damage application in one place; slash, death effect and popups driven by events only.
- [ ] Mob spawn markers + area mob spawner (replaces in-scene mob instances and the build-callback injection).
- [ ] Per-player death and respawn.
- [ ] Remaining timers through `IClock`, injected RNG.

### Phase 2: networking foundation

- [ ] Add NGO, Unity Transport and Multiplayer Play Mode.
- [ ] `NetworkManager` in `Main`; host / join (direct IP) in the main menu; connection flow in Core.
- [ ] VContainer integration: the host creates network prefabs with `resolver.Instantiate` then spawns them; clients get them through an `INetworkPrefabInstanceHandler` registered with `NetworkManager.PrefabHandler` that also calls `resolver.Instantiate`. The handler's `Instantiate` is not called on the host, only on clients.

### Phase 3: players

- [ ] `NetworkObject` on the player prefab, owner-authoritative transform/rigidbody sync, facing and animation state replicated.
- [ ] Attack sent by the owner; every client plays the slash visual; the host applies damage to mobs.
- [ ] Per-player HUD and camera bound to the local player only.

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

## Open questions

- Downed/revive or instant respawn? What ends a run with 2–4 players?
- Does difficulty scale with player count (mob HP, damage, spawn count)?
- Friendly fire: off by default for co-op?
- Should each player's camera stay independent, or should the game keep the party on one screen?
