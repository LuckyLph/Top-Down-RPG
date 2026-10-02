# Player controls rework

Spec for replacing WASD movement and the facing-direction sword swing with click-to-move controls in the style of League of Legends and Lost Ark: right click moves or attacks, and keys cast abilities toward the cursor. [Architecture.md](Architecture.md) describes what exists today; this file describes the target and the order in which to build it. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies: nothing here may assume one player, and each step is checked with a host and 2–4 players.

## Goals

- Right click on the ground moves the player there along a path around walls. Holding right click keeps steering toward the cursor.
- Right click on an enemy chases it into weapon range and auto attacks it with the equipped weapon (the existing slash) until it dies or the player gives another order.
- Six abilities: four come from the player's class and two from the equipped weapon. They fire toward the cursor as soon as their key is pressed.
- Abilities are blanks for now. They go through the whole pipeline (cooldown, cast time, movement during the cast, networking, HUD) but have no effect. Real abilities are built later on top of that pipeline.

## Non-goals (for this rework)

- Real ability effects, class selection, weapon pickups, and saving the loadout.
- Gamepad gameplay. The gamepad keeps working in menus, and a stick-based scheme can be specced later as a separate input mode.
- Cast indicators (show on hold, cast on release) and attack-move.
- Camera changes: the camera keeps following the local player.

## Decisions

| Topic | Decision |
|---|---|
| Movement | Mouse only. Right click on the ground gives a move order, and holding the button keeps updating the destination. WASD movement is removed. |
| Auto attack | Right click on an enemy gives an attack order: chase into the weapon's attack range, then swing the weapon's slash at the target whenever its cooldown allows. |
| Ability keys | Q W E R for the four class abilities, A S for the two weapon abilities (Lost Ark layout). D and F stay free for later. |
| Cast style | Quick cast: the ability fires on key press, aimed at the cursor. There is no indicator and no confirm click. |
| Casting and orders | A cast pauses the current order. Each ability decides how the caster moves during its cast time (stand still, keep moving, or the ability moves the caster). When the cast ends, the paused order resumes. |
| Gamepad | Not supported in gameplay for now. |
| Targets | Auto attacks target only mobs (`Enemy` layer, with a `DamageReceiver`); allies cannot be attacked (no friendly fire). Unit-targeted abilities choose enemies or allies through a per-ability filter. |
| Visibility | Players have no line-of-sight rule. Anything targetable under the cursor can be clicked, and an order on it continues whether or not walls are in the way. Mob line of sight stays as it is. |
| Player bodies | Players pass through each other, so click-to-move never gets body-blocked by an ally. They still collide with walls and mobs. |
| Cast buffering | Off by default. Abilities can opt in individually to being buffered while another cast runs. |
| Authority | Unchanged trust model: the owner moves its player, decides when to swing and when to cast, and runs its own cooldowns. The host resolves hits and, later, ability effects. |

## Controls

| Input | Action |
|---|---|
| Right mouse button, pressed on the ground | Move order to the cursor's point |
| Right mouse button, pressed on an enemy | Attack order on that enemy |
| Right mouse button, pressed on an ally | Move order to the clicked point (the same as clicking the ground) |
| Right mouse button, held | Re-evaluated at a throttled rate: the order follows the cursor, attacking an enemy under the cursor or moving to the ground under it |
| Q, W, E, R | Class abilities 1–4 |
| A, S | Weapon abilities 1–2 |
| X | Stop: cancel the current order, any order waiting to resume, and any buffered cast. It uses X because LoL's S belongs to a weapon ability here. |
| Left mouse button | No gameplay action. Reserved for UI, and later for selecting or interacting. |

Clicks over UI never reach gameplay. Bindings live in the `Player` map of `InputSystem_Actions.inputactions`; the unused template actions (Look, Interact, Crouch, Jump, Previous, Next, Sprint) can stay until something needs them.

## Behaviour

### Orders

The player always has exactly one current order, plus at most one paused order while a cast runs.

| Order | Behaviour | Ends when |
|---|---|---|
| Idle | Stand still. | Any new order. |
| Move(point) | Follow a path to the point. A click on an unwalkable cell moves to the nearest walkable cell. | Arrived, or stuck for longer than the stuck timeout, then Idle. |
| Attack(target) | Out of range: path toward the target, repathing when the target moves more than a threshold or at a fixed interval. In range: stop, face the target and swing whenever the weapon cooldown allows. Falls back to chasing if the target leaves range. Walls between the player and the target do not cancel the order; the path goes around them. There is no leash: distance never cancels the order. | The target dies or despawns, then Idle. |
| CastWhenInRange(slot, target) | Unit-targeted cast out of range: walk toward the target, then cast once in range. | Cast started, target lost (Idle), or another order. |
| Casting(slot, aim) | Runs the cast time with the ability's movement mode, keeping the order it interrupted as the paused order. | Cast time over: the paused order resumes (Idle if there was none). |

Rules:
- A new order replaces the current one at once, except during a cast: then it replaces the paused order and starts when the cast ends. Stop clears both: during a cast it clears the paused order (and any buffered cast), while the started cast itself runs to its end and the player is then Idle.
- Range checks use collider-to-collider distance (`Collider2D.Distance`), as mob attacks already do.
- Facing follows the movement direction while moving, and the aim direction while attacking or casting.
- Death clears all orders. Respawn starts at Idle.
- When gameplay input is disabled (during transitions), no new orders are made and the current order runs on.

### Abilities

Each ability is a ScriptableObject that only describes how it is cast. There is no effect field until real abilities exist.

| Field | Meaning |
|---|---|
| Display name, icon | HUD |
| Cooldown | Seconds, measured on `IClock`. Starts when the cast starts. |
| Cast time | Seconds the caster spends in Casting. 0 means instant. |
| Targeting | `None` (self, aim is ignored), `Direction` (toward the cursor; range is ignored), `Point` (the cursor's point, clamped to max range), `Unit` (the unit under the cursor that passes the unit filter; nothing happens if there is none, and out of range becomes CastWhenInRange) |
| Unit filter | For `Unit` targeting only: `Enemy` (mobs), `Ally` (living registered players, the caster included) or `Any` |
| Range | Max range for `Point` and `Unit` |
| Cast movement | `Stop` (the caster stands still for the cast time), `Continue` (the paused order keeps moving the caster during the cast), `Ability` (the caster's normal movement is suspended and the ability will drive the body itself, e.g. a dash; with blanks it behaves like `Stop`) |
| Bufferable | Off by default. When on, pressing the ability while another cast runs queues it instead of failing (see below). |

A cast attempt fails without side effects, and reports why, when the slot is empty, the ability is on cooldown, another cast is running (and the ability is not bufferable), a `Unit` cast has no valid target, or the player is dead. The HUD flashes the slot on failure.

Buffering:
- There is at most one buffered cast, and a newer bufferable press replaces it.
- The buffered cast keeps the aim taken when its key was pressed.
- It fires as soon as the running cast ends, and it pauses the order that was about to resume. A buffered `Unit` cast whose target is out of range by then becomes CastWhenInRange, which replaces that order like any new order.
- It is dropped when it is older than the buffer window in `PlayerControlSettings`, when the player gives a new order or presses Stop, or when it is no longer valid as it fires (for example its unit target died). A dropped buffered cast reports its failure like any other.

Blanks: the placeholder class and the sword get six distinct blank abilities with different cooldowns, cast times, targeting modes and movement modes, so every branch of the pipeline can be exercised in play.

### Loadout

- `PlayerClass` asset: display name and exactly four ability slots.
- `PlayerWeapon` (existing asset) gains an attack range for auto attacks and exactly two ability slots. Everything it already has (damage, cooldown, slash prefab, offsets, icon) stays as it is.
- The six slots are built from the class plus the equipped weapon, and the two weapon slots are rebuilt on `EquippedWeaponChanged`.
- For now every player gets the same class and weapon from the player prefab, so a slot index is enough to identify an ability across machines. The loadout has to be replicated (and later saved) before players can pick different classes or weapons. See Later.

## Architecture

New code follows the existing layout: device input in Core, intent and logic in Gameplay, decisions as plain C# classes driven by `IClock` so EditMode tests cover them with fakes, and thin MonoBehaviours around them.

### Input (Core)

- `IPlayerInput` is reworked around the mouse: pointer screen position, move button pressed this frame and held, stop pressed, ability pressed this frame by slot index, and whether the pointer is over UI. `AttackPressedThisFrame` and the vector `Move` go away.
- `PlayerInputService` binds the new actions (`MoveClick`, `Point`, `Stop`, `Ability1`…`Ability6`) and gets the Main-scope `EventSystem` injected to answer "pointer over UI".
- `GameplayInputGate` is unchanged.

### Commands (Gameplay)

- `PlayerCommand` becomes one frame of mouse intent in world space: pointer world point, the target under the pointer (if any), move pressed and held, stop, and the ability slot pressed (none, or 0–5).
- `LocalPlayerCommandSource` builds it with the Main-scope `Camera` (screen to world) and a target picker.
- Target picker: a non-allocating `Physics2D` overlap at the pointer on inspector-set layer masks (`Enemy` for mobs, `Player` for allies), with a forgiveness radius. It returns the closest living unit and its team (enemy or ally); allies are matched against `IPlayerRegistry`. There is no line-of-sight test: being under the cursor is enough. It is also used for hover feedback.
- Remote players no longer read commands. `PlayerNetworkSync` stops implementing `IPlayerCommandSource` and drives a remote copy's animation from the replicated move and facing, the same way `MobMotor2D.ShowRemoteMovement` does for mobs.

### Orders, movement and attacks (Gameplay)

| Piece | Kind | Role |
|---|---|---|
| `PlayerOrders` | Plain class | The order state machine above. Inputs are commands, the clock, the player position, the weapon range, target queries and the caster's answers. Outputs are a destination for the motor, swing requests with a direction, and cast requests. It has no engine lookups, like the mob states. |
| `PlayerMotor2D` | Component | Moves the `Rigidbody2D` along the current path in `FixedUpdate`, drives animator parameters (same ones as today) and reports stuck time. |
| Path following | Plain class, shared | Path building, smoothing and waypoint following extracted from `MobPathAgent2D` so players and mobs use one implementation. `MobPathAgent2D` keeps its public API and wraps it. A straight line is used when it is clear, so short clicks skip A*. |
| `ActiveNavigationGrid` | Gameplay-scope holder | The current area's `NavigationGrid2D`, set by `AreaEntry` and cleared when the area unloads (the same pattern as `ActiveSpawnPoint`). The player lives in the Gameplay scope and cannot inject the area's grid directly. Without a grid, movement falls back to straight lines. |
| `PlayerController` | Component, slimmed | Wires the command source, `PlayerOrders`, motor, weapon and abilities, and keeps `Teleport`, `Face`, `FacingDirection`, `CurrentMove` and `SimulatesMovement`, which other systems use. |
| `PlayerWeaponController` | Component, extended | `TryAttack(direction)` swings toward the target rather than the facing direction. Cooldown, slash spawning, `Attacked` and `PlayRemoteAttack` stay as they are. |

Players pass through each other through the physics layer collision matrix (`Player` vs `Player` off). This is a project setting, so mob separation and mob collisions are unaffected.

Tuning (move speed, repath interval, hold re-evaluation interval, target move threshold, arrival distance, stuck timeout, pick radius, cast buffer window, terrain profile) moves into a `PlayerControlSettings` ScriptableObject referenced by the player prefab. `PlayerController.moveSpeed` moves into it as well.

### Abilities (Gameplay)

| Piece | Kind | Role |
|---|---|---|
| `AbilityDefinition` | ScriptableObject | The fields in the Abilities table. Assets in `Assets/Data/Abilities`. |
| `PlayerClass` | ScriptableObject | Name plus four abilities. Asset `Assets/Data/Classes/Class_Placeholder.asset`. |
| Ability cooldowns | Plain class | Per-slot cooldown end times on `IClock`, readiness, and remaining time for the HUD. |
| `PlayerAbilities` | Component | Builds the six slots from class and weapon, checks and starts casts (`TryCast(slot, aim)` returns a result: started, or the failure reason) and raises `CastStarted`, `CastEnded`, `CastFailed` and `SlotsChanged`. |
| `AbilityService` | Gameplay-scope singleton | The one place ability effects will be applied, mirroring `DamageService`: it acts only where `IGameAuthority.IsAuthoritative`. For now it does nothing beyond being called exactly once per cast on the host, which tests assert. |

Aim data: point, direction and an optional target unit (a mob or a player). The target is a reference on the local machine and a `NetworkObjectReference` on the wire; players and mobs are both network objects.

### Networking

| Thing | Authority | How |
|---|---|---|
| Path finding and movement | Owner | Unchanged: the owner moves its own body, and `NetworkTransform` plus the move and facing `NetworkVariable`s replicate it. Ability-driven movement (dashes) also runs on the owner. |
| Auto attack swing | Owner decides, host resolves hits | Unchanged `AttackRpc(direction)`. Only the host's slash applies damage. |
| Cast | Owner decides, host resolves effects | The owner starts the cast locally at once (no round trip), then sends `CastRpc(slot, point, direction, target)` to everyone else. The host calls `AbilityService`, and other clients face the aim and will play the cast visuals. |
| Cooldowns, orders, cast state | Owner only | Never replicated: they are transient and only the owner's HUD shows them. A late joiner sees nothing it needs to catch up on. |
| Loadout | Prefab default | Identical on every machine for now, so the RPC sends a slot index. It must replicate before class or weapon choice exists. |

The host's own player takes the same path: offline and host-owned casts call `AbilityService` directly.

### HUD and feedback (local only)

- Ability bar: six slots labelled Q W E R | A S, each with the ability icon (or an empty state), a radial cooldown fill with seconds remaining, and a flash on failed casts. It is a passive `AbilityBarView` with an `AbilityBarPresenter` bound to the local player through `LocalPlayerTracker`, in the same shape as the health HUD. It goes on `PlayerHudCanvas`.
- Hover feedback on the targetable enemy under the cursor: highlight and an attack cursor.
- A pooled click marker at move destinations.
- Nothing here is a networked object.

## Removals and migrations

- WASD and the left-click/space Attack binding, `IPlayerInput.Move` and `AttackPressedThisFrame`, and `PlayerCommand.Attack` and `Move`.
- `PlayerNetworkSync` as an `IPlayerCommandSource`.
- Tests that depend on the old input: `PlayerWeaponSystemTests` (input asset Attack binding, command-source facing and attack, `LocalPlayerCommandSource`), `InProcessClient`'s scripted input, and the network smoothness tests. Those move the client's player with scripted input and need a scripted far-away move order that gives the same constant speed. Rerun the smoothness benchmark afterwards, because the movement code changes.
- Architecture.md: the Input, Player, Combat (weapon), UI, configs, events and tests sections change with each phase.

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 1: shared path following
- [x] Extract path building, smoothing and following from `MobPathAgent2D` into a plain class. Mob behaviour must not change, and the mob tests and the stress scene stay green with no new allocations.
- [x] `ActiveNavigationGrid` set by area entry and cleared on unload.

### Phase 2: click-to-move and auto attack
- [x] Input actions, the reworked `IPlayerInput` and the pointer-over-UI check. WASD and the old attack binding are removed.
- [x] `PlayerCommand` in world space, the target picker and `LocalPlayerCommandSource`.
- [x] `PlayerOrders` with Idle, Move and Attack, `PlayerMotor2D`, `PlayerControlSettings`, the weapon attack range, and `TryAttack(direction)`.
- [x] Players pass through each other (`Player` vs `Player` collision off).
- [x] Remote players animated from the replicated state. Old-input tests are migrated and the smoothness benchmark is rerun.

### Phase 3: ability pipeline with blanks
- [x] `AbilityDefinition`, `PlayerClass`, the weapon's two ability slots, and the placeholder class plus six blank assets.
- [x] Cooldowns, `PlayerAbilities`, the Casting and CastWhenInRange orders, movement modes, and pausing and resuming orders.
- [x] Unit filters (enemy, ally, any) and per-ability cast buffering. At least one blank is bufferable and at least one targets allies.
- [x] `CastRpc` and `AbilityService`, called once per cast on the host only.

### Phase 4: HUD and feedback
- [ ] Ability bar view and presenter.
- [ ] Hover highlight, attack cursor and pooled move marker.

### Later
- Real ability effects through `AbilityService`, with visuals reacting to cast events on every machine.
- Class selection; replicating the loadout (a content catalog of classes and weapons, identified by GUID); saving each player's class in `GameSaveData` behind a version bump.
- Optional cast modes (indicator on hold, click to confirm) as a player setting.
- Attack-move and a gamepad scheme.

## Testing

- EditMode (with `ManualClock` and fakes): every `PlayerOrders` transition, including pause and resume around casts and Stop clearing the paused order; range checks; target loss; cooldowns and each failure reason; each targeting and movement mode; unit filters (enemy, ally, the caster as an ally); buffering (non-bufferable fails, bufferable fires after the running cast, newest press wins, expiry, dropped on a new order or Stop, re-validated when it fires); target picker rules (closest living unit, the dead ignored, right team, no line-of-sight test); input service bindings and the UI-blocking check; path follower parity with the old mob agent.
- PlayMode: in the Clearing, a right click behind a wall paths around it; clicking a mob behind a wall gives an attack order that paths around and kills it; two players walk through each other; a client's cast reaches the host's `AbilityService` exactly once (including one aimed at the host's player as an ally) and the client's facing reaches the host's copy; a dead player's orders are cleared.
- Manual: Multiplayer Play Mode with 2 and 4 players, moving, attacking the same mob and casting every blank, plus a late join during combat.

## Open questions

None at the moment. Add new ones here as implementation raises them.
