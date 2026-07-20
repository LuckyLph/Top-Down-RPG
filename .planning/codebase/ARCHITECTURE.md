<!-- refreshed: 2026-07-18 -->
# Architecture

**Analysis Date:** 2026-07-18

## System Overview

```text
┌─────────────────────────────────────────────────────────────────────────┐
│ Unity scene and prefab composition                                       │
│ `Assets/Scenes/SampleScene.unity`, `Assets/Prefabs/`                     │
├──────────────────┬─────────────────────┬────────────────────────────────┤
│ Player           │ Mob AI              │ Presentation                   │
│ `Scripts/Player` │ `Scripts/AI`        │ `Scripts/Camera`, `Scripts/UI` │
└────────┬─────────┴──────────┬──────────┴───────────────┬────────────────┘
         │                    │                          │
         ▼                    ▼                          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ Shared gameplay components                                                │
│ `Assets/Scripts/Combat/` — health, damage routing, attacks, death FX    │
└───────────────────────────┬─────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ Unity services and authored data                                          │
│ Physics2D, Tilemap, Input System, TextMeshPro; ScriptableObjects in       │
│ `Assets/Data/Weapons/` and `Assets/Settings/AI/`                         │
└─────────────────────────────────────────────────────────────────────────┘
```

## Component Responsibilities

| Component | Responsibility | File |
|-----------|----------------|------|
| Scene composition | Loads the only enabled build scene; owns the `Grid`, `GrassTilemap`, and `Main Camera` objects. | `Assets/Scenes/SampleScene.unity` |
| Player root | Receives input, drives 2D physics movement, holds health and equips the starting weapon. | `Assets/Prefabs/Player/Player.prefab` |
| Mob root | Composes the controller, state-machine collaborators, navigation, combat, health, death, and sprite-sorting behaviours. | `Assets/Prefabs/Mobs/Weasel.prefab` |
| Mob controller | Initializes owner-local collaborators, creates states, ticks perception and the active state, and handles transitions. | `Assets/Scripts/AI/Core/MobController.cs` |
| Navigation grid | Builds walkable cells from tilemap terrain sources and exposes an A* pathfinder. | `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` |
| Combat foundation | Owns health events and routes received damage to visual feedback. | `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/Combat/DamageReceiver.cs` |
| Player attack | Spawns and positions a short-lived slash hitbox from the equipped weapon asset. | `Assets/Scripts/Player/PlayerWeaponController.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs` |
| HUD | Locates the Player by tag, subscribes to health/weapon events, and updates its own named child controls. | `Assets/Scripts/UI/HudController.cs`, `Assets/Prefabs/UI/PlayerHudCanvas.prefab` |

## Pattern Overview

**Overall:** Unity component-oriented gameplay with prefab composition, ScriptableObject configuration, local runtime discovery, and an explicit finite-state machine for mobs.

**Key Characteristics:**

- Add behaviour as a focused `MonoBehaviour` in `Assets/Scripts/<domain>/`; compose it on an owned prefab in `Assets/Prefabs/<domain>/`.
- Use `GetComponent`, `GetComponentInChildren`, runtime `Configure(...)`, and owner-level discovery for required scene collaborators. `MobController` resolves its co-located components and discovers the scene grid/target provider when no intentional override is supplied in `Assets/Scripts/AI/Core/MobController.cs`.
- Use serialized fields for numeric settings, masks, colours, ScriptableObject assets, prefabs, animation clips, and intentional overrides. `MobConfig` and `PlayerWeapon` are the primary authored configuration assets in `Assets/Scripts/AI/Core/MobConfig.cs` and `Assets/Scripts/Combat/PlayerWeapon.cs`.
- Separate frame decisions from physics execution: controllers decide in `Update`, state machines/path agents set desired motion, and `Rigidbody2D` velocity is applied in `FixedUpdate`/`FixedTick`.
- Keep rendering concerns local: `YPositionSorter` applies sort order in `LateUpdate`, `CameraFollow2D` follows in `LateUpdate`, and `FloatingDamageText` projects world positions to UI in `LateUpdate`.

**Development automation boundary:**

- Unity MCP is an optional, local development bridge rather than a gameplay layer. When it is running at `http://127.0.0.1:8080` with `--project-scoped-tools`, an MCP client can inspect and operate on this project's open Unity Editor.
- Use it to discover the live scene and component topology before editing, then apply focused editor changes. It must not become a runtime dependency, be added to player code, or replace the existing prefab/ScriptableObject authoring model.
- After MCP-driven script edits, wait for Unity compilation to complete and inspect the Unity Console before changing scene or prefab composition. Use the Unity Test Framework through the bridge or Unity Test Runner to verify behavioural changes.

## Layers

**Unity composition layer:**

- Purpose: Defines the build scene, object hierarchies, and component composition.
- Location: `Assets/Scenes/SampleScene.unity`, `Assets/Prefabs/Player/Player.prefab`, `Assets/Prefabs/Mobs/Weasel.prefab`, `Assets/Prefabs/Combat/`, and `Assets/Prefabs/UI/`.
- Contains: Tilemaps, camera, player/mob/UI/combat prefabs, colliders, renderers, animators, and authored component settings.
- Depends on: Gameplay scripts in `Assets/Scripts/` and data assets in `Assets/Data/` and `Assets/Settings/`.
- Used by: Unity's scene loader; `ProjectSettings/EditorBuildSettings.asset` enables `Assets/Scenes/SampleScene.unity`.

**Player layer:**

- Purpose: Converts Input System actions into player motion, animation parameters, and attack attempts.
- Location: `Assets/Scripts/Player/PlayerController.cs` and `Assets/Scripts/Player/PlayerWeaponController.cs`.
- Contains: Input lookup/fallback binding, facing direction, `Rigidbody2D` movement, weapon cooldown ownership, and weapon-equipped events.
- Depends on: `UnityEngine.InputSystem`, `PlayerWeapon`, `SwordSlashAttack`, and local player components.
- Used by: The Player prefab at `Assets/Prefabs/Player/Player.prefab` and HUD at `Assets/Scripts/UI/HudController.cs`.

**Combat layer:**

- Purpose: Supplies reusable health, damage, hit detection, death effect, and damage-popup behaviour.
- Location: `Assets/Scripts/Combat/`.
- Contains: `Health` events, `DamageReceiver`, mob `MeleeDamageDealer`, player `SwordSlashAttack`, `MobDeathAnimation`, death handlers, and `FloatingDamageText`.
- Depends on: Unity 2D physics, TextMeshPro/UI, animation playables, and assets in `Assets/Data/Weapons/`, `Assets/Prefabs/Combat/`, and `Assets/Animations/Weapons/`.
- Used by: Both `Assets/Prefabs/Player/Player.prefab` and `Assets/Prefabs/Mobs/Weasel.prefab`.

**AI orchestration layer:**

- Purpose: Owns a mob's dependencies and finite-state-machine lifecycle.
- Location: `Assets/Scripts/AI/Core/MobController.cs` and `Assets/Scripts/AI/StateMachine/`.
- Contains: State registration, transitions, perception ticking, attack-range decisions, patrol decisions, chase/repath decisions, and returning to spawn.
- Depends on: Co-located `MobMotor2D`, `MobPerception2D`, `MobPathAgent2D`, `MobPatrolAnchor`, `MeleeDamageDealer`, and `NavigationGrid2D`.
- Used by: `Assets/Prefabs/Mobs/Weasel.prefab`.

**AI navigation layer:**

- Purpose: Converts terrain tilemaps into weighted walkability and creates/smooths 8-direction A* paths.
- Location: `Assets/Scripts/AI/Navigation/`.
- Contains: `NavigationGrid2D`, `NavigationTerrainSource2D`, `GridAStarPathfinder2D`, path request/result contracts, and terrain data assets.
- Depends on: `UnityEngine.Tilemaps` and `TerrainMovementProfile2D` assets such as `Assets/Settings/AI/TerrainMovement_Default.asset`.
- Used by: `MobPathAgent2D` in `Assets/Scripts/AI/Core/MobPathAgent2D.cs` and the state classes in `Assets/Scripts/AI/StateMachine/`.

**Presentation layer:**

- Purpose: Maintains camera position, sprite depth ordering, HUD state, animations, and transient visual feedback.
- Location: `Assets/Scripts/Camera/`, `Assets/Scripts/UI/`, and visual components in `Assets/Prefabs/`.
- Contains: Camera following, Y-based sorting, event-driven HUD updates, damage popups, and playable-driven slash/death animations.
- Depends on: Player tag/health/weapon events, cameras, renderers, TextMeshPro, UI, and animation assets.
- Used by: `Assets/Scenes/SampleScene.unity`, `Assets/Prefabs/UI/PlayerHudCanvas.prefab`, and combat components.

## Data Flow

### Primary Player Attack Path

1. `PlayerController.Update` reads the configured `Player/Move` and `Player/Attack` Input System actions, then asks the co-located weapon controller to attack (`Assets/Scripts/Player/PlayerController.cs:71`).
2. `PlayerWeaponController.TryAttack` enforces its weapon cooldown and calls `SwordSlashAttack.Spawn` with the owner and facing direction (`Assets/Scripts/Player/PlayerWeaponController.cs:50`).
3. `SwordSlashAttack` instantiates the slash prefab referenced by `Assets/Data/Weapons/Sword.asset`, follows the owner, and processes trigger/initial overlap hits (`Assets/Scripts/Combat/SwordSlashAttack.cs:77`).
4. `DamageReceiver.ReceiveDamage` calls `Health.ApplyDamage`, then spawns a floating number for positive damage (`Assets/Scripts/Combat/DamageReceiver.cs:25`).
5. `Health.ApplyDamage` raises `Damaged` and, at zero health, `Died`; `DestroyMobOnDeath` consumes the latter to spawn `Assets/Prefabs/Combat/MobDeathAnimation.prefab` and destroy the mob (`Assets/Scripts/Combat/Health.cs:49`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs:37`).

### Mob Perception, Navigation, and Attack Path

1. `NavigationGrid2D.Awake` builds cells from child `NavigationTerrainSource2D` components; the `Grid` scene object discovers the `GrassTilemap` source in `Assets/Scenes/SampleScene.unity` (`Assets/Scripts/AI/Navigation/NavigationGrid2D.cs:56`).
2. `MobController.Awake` resolves required co-located collaborators, discovers the scene grid/target provider when unconfigured, initializes them, and registers the five state instances (`Assets/Scripts/AI/Core/MobController.cs:56`, `Assets/Scripts/AI/Core/MobController.cs:171`).
3. `MobController.Update` ticks `MobPerception2D` and the current `IMobState`; `ChaseState` requests a path to the visible target and transitions to `AttackRangeState` at combat distance (`Assets/Scripts/AI/Core/MobController.cs:71`, `Assets/Scripts/AI/StateMachine/ChaseState.cs`).
4. `MobController.FixedUpdate` delegates state physics movement to `MobPathAgent2D`, then `MobMotor2D` moves the `Rigidbody2D` toward its desired velocity (`Assets/Scripts/AI/Core/MobController.cs:76`, `Assets/Scripts/AI/Core/MobPathAgent2D.cs:143`, `Assets/Scripts/AI/Core/MobMotor2D.cs:98`).
5. `AttackRangeState` faces the detected player and delegates cooldown-controlled damage to `MeleeDamageDealer`, which resolves a `DamageReceiver` on the target hierarchy (`Assets/Scripts/AI/StateMachine/AttackRangeState.cs`, `Assets/Scripts/Combat/MeleeDamageDealer.cs:31`).

### Player HUD Path

1. `PlayerHudController` resolves local controls named `HealthFill`, `HealthText`, and `WeaponIcon` beneath `Assets/Prefabs/UI/PlayerHudCanvas.prefab` (`Assets/Scripts/UI/HudController.cs:208`).
2. It finds the object tagged `Player`, acquires `Health` and `PlayerWeaponController`, and subscribes to their events (`Assets/Scripts/UI/HudController.cs:82`).
3. Damage/death and weapon-equipment events update fill amount, text, and icon without polling the values every frame (`Assets/Scripts/UI/HudController.cs:59`, `Assets/Scripts/UI/HudController.cs:164`).

**State Management:**

- Per-instance game state resides in `MonoBehaviour` fields on player, mob, combat, camera, and UI objects.
- Mob state is held by `MobController.currentState`; immutable-ish state IDs and the `IMobState` interface are in `Assets/Scripts/AI/Core/MobStateId.cs` and `Assets/Scripts/AI/Core/IMobState.cs`.
- Authored shared data resides in ScriptableObjects: `PlayerWeapon`, `MobConfig`, `TerrainType2D`, and `TerrainMovementProfile2D`.
- The only project-level static mutable runtime cache is the damage-popup canvas/font in `Assets/Scripts/Combat/FloatingDamageText.cs`.

## Key Abstractions

**Mob finite state machine:**

- Purpose: Separates idle, patrol, chase, attack-range, and return decisions while retaining one controller owner.
- Examples: `Assets/Scripts/AI/StateMachine/IdleState.cs`, `Assets/Scripts/AI/StateMachine/PatrolRoamState.cs`, `Assets/Scripts/AI/StateMachine/ChaseState.cs`, `Assets/Scripts/AI/StateMachine/AttackRangeState.cs`, and `Assets/Scripts/AI/StateMachine/ReturnToSpawnState.cs`.
- Pattern: `IMobState` strategy objects are created by `MobController.RegisterStates`; each receives the owner through `MobStateBase` rather than being `MonoBehaviour` components.

**Navigation contracts:**

- Purpose: Isolate path requests/results from the concrete grid and A* implementation.
- Examples: `Assets/Scripts/AI/Navigation/IPathfinder2D.cs`, `Assets/Scripts/AI/Navigation/PathTypes.cs`, and `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`.
- Pattern: `MobPathAgent2D` calls the `IPathfinder2D` exposed by `NavigationGrid2D`; requests carry a terrain movement profile and whether a partial path is permitted.

**Terrain-driven navigation data:**

- Purpose: Keep walkability/cost rules editable outside code.
- Examples: `Assets/Scripts/AI/Navigation/TerrainType2D.cs`, `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`, `Assets/Settings/AI/Terrain_Grass.asset`, and `Assets/Settings/AI/TerrainMovement_Default.asset`.
- Pattern: `NavigationTerrainSource2D` maps scene tilemaps to terrain assets; movement profiles apply per-terrain walkability and traversal cost.

**Combat event contract:**

- Purpose: Allow presentation and death behaviour to react without combat code knowing its consumers.
- Examples: `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/Combat/DamageReceiver.cs`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs`, and `Assets/Scripts/UI/HudController.cs`.
- Pattern: `Health` publishes `Damaged` and `Died`; listeners subscribe on enable and unsubscribe on disable.

**Weapon asset and transient attack:**

- Purpose: Define a player's attack values and visuals as data, then produce a per-attack hitbox instance.
- Examples: `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Data/Weapons/Sword.asset`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, and `Assets/Prefabs/Combat/SwordSlash.prefab`.
- Pattern: A ScriptableObject supplies damage/cooldown/prefab/offsets; the weapon controller owns timing; the slash owns only its short lifetime and one-hit-per-receiver set.

## Entry Points

**Build scene:**

- Location: `Assets/Scenes/SampleScene.unity`.
- Triggers: Unity loads it because it is the sole enabled scene in `ProjectSettings/EditorBuildSettings.asset`.
- Responsibilities: Composes the tilemap navigation hierarchy and main camera; player, mob, HUD, and combat objects are prefab instances from `Assets/Prefabs/`.

**Navigation grid lifecycle:**

- Location: `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Triggers: Unity invokes `Awake` on the component attached to the `Grid` object in `Assets/Scenes/SampleScene.unity`.
- Responsibilities: Resolves terrain sources, indexes non-collision tile cells, calculates bounds, and exposes `GridAStarPathfinder2D`.

**Mob controller lifecycle:**

- Location: `Assets/Scripts/AI/Core/MobController.cs`.
- Triggers: Unity invokes `Awake`, `Start`, `Update`, and `FixedUpdate` on each mob prefab instance.
- Responsibilities: Initializes dependencies/states, ticks perception/decisions, and performs state-driven physics movement.

**Player controller lifecycle:**

- Location: `Assets/Scripts/Player/PlayerController.cs`.
- Triggers: Unity invokes `Awake`, `OnEnable`, `Update`, and `FixedUpdate` on the Player prefab instance.
- Responsibilities: Resolves Input System actions, updates movement/animation state, starts attacks, and writes rigidbody velocity.

**Editor asset maintenance:**

- Location: `Assets/Editor/PixelArtTextureDefaults.cs`, `Assets/Editor/PixelArtEditorSnapSettings.cs`, and `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs`.
- Triggers: Unity Editor asset import, editor load, or `Tools/...` menu commands.
- Responsibilities: Enforces pixel-art imports/snap settings and verifies/rebuilds the sword-slash prefab/weapon link. These scripts are editor-only and are not gameplay entry points.

## Architectural Constraints

- **Threading:** Unity's main-thread component lifecycle drives gameplay. Frame decisions run in `Update`, rigidbody work in `FixedUpdate`/`FixedTick`, and camera/sorting/UI projection in `LateUpdate`; no worker-thread or job-system code is present in `Assets/Scripts/`.
- **Global state:** `Assets/Scripts/Combat/FloatingDamageText.cs` caches a static popup canvas, rect transform, and TMP font. All other observed gameplay state is component or ScriptableObject state.
- **Scene discovery:** `Assets/Scripts/AI/Core/MobController.cs` uses `FindAnyObjectByType` for the grid and target provider; `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Scripts/Camera/CameraFollow2D.cs`, and `Assets/Scripts/UI/HudController.cs` locate the player by the `Player` tag.
- **Dependency wiring:** Mob internal components are obtained from their owner hierarchy and guarded by `[RequireComponent]` in `Assets/Scripts/AI/Core/MobController.cs`; their config, terrain profile, weapon, prefab, and clip references are authored asset/configuration fields.
- **Assembly boundary:** Gameplay code compiles in `Assets/Scripts/TopDownRPG.Gameplay.asmdef`; test assemblies reference it through `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`.
- **Circular imports:** Not detected. `Assets/Scripts/` contains no C# namespace declarations or project-to-project assembly reference cycle; the gameplay assembly is the shared dependency root.

## Anti-Patterns

### Implicit global scene lookup for required actors

**What happens:** Targeting, camera follow, and HUD binding find the player by tag in `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Scripts/Camera/CameraFollow2D.cs`, and `Assets/Scripts/UI/HudController.cs`.
**Why it's wrong:** A missing tag, duplicated player, split-screen player, or scene transition can make the chosen object ambiguous or leave the consumer unbound.
**Do this instead:** Continue to use runtime ownership/discovery rather than serializing cross-scene component links, but provide the intended actor at runtime through `MobTargetProvider.SetTarget`, `CameraFollow2D.SetTarget`, or a scene/bootstrap owner before dependent gameplay starts. Keep `Player` tag lookup only as the single-player fallback.

### UI binding through distributed child-name contracts

**What happens:** `PlayerHudController` searches its hierarchy for exact object names in `Assets/Scripts/UI/HudController.cs`.
**Why it's wrong:** Renaming `HealthFill`, `HealthText`, or `WeaponIcon` in `Assets/Prefabs/UI/PlayerHudCanvas.prefab` silently breaks binding and surfaces only as a runtime warning.
**Do this instead:** Keep view references within the HUD prefab's ownership boundary: centralize them in a local helper/view component that resolves its own children in `Awake`, then let `PlayerHudController` consume that helper. Do not add serialized references from the HUD to dynamic scene actors.

## Error Handling

**Strategy:** Validate dependencies at component boundaries, represent expected navigation failure with booleans/results, use Unity warnings/errors for invalid scene setup, and fall back only where gameplay can remain functional.

**Patterns:**

- `NavigationGrid2D.BuildGrid` logs errors/warnings and leaves `Pathfinder` unavailable when terrain sources cannot build a valid grid (`Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`).
- `GridAStarPathfinder2D.FindPath` returns `PathResult.Failure` for unusable start/goal states rather than throwing (`Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`).
- `MobController` creates a hidden runtime `MobConfig` only when configuration is absent (`Assets/Scripts/AI/Core/MobController.cs`).
- `PlayerController` supplies runtime keyboard/gamepad input actions if the configured asset cannot supply named actions (`Assets/Scripts/Player/PlayerController.cs`).
- Component contracts use `[RequireComponent]`, `[DisallowMultipleComponent]`, `[Min]`, and `OnValidate` across `Assets/Scripts/AI/`, `Assets/Scripts/Combat/`, and `Assets/Scripts/Player/`.

## Cross-Cutting Concerns

**Logging:** Unity `Debug.LogWarning` and `Debug.LogError` are used for invalid navigation/HUD setup in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` and `Assets/Scripts/UI/HudController.cs`; no central logging service is present.

**Validation:** Inspector attributes and `OnValidate` enforce component presence/ranges; `NavigationGrid2D`, `MobController`, `PlayerController`, and combat components resolve owner-local collaborators at runtime.

**Authentication:** Not applicable. `Assets/Scripts/` contains local single-player gameplay and no identity, authorization, network, or remote API layer.

---

*Architecture analysis: 2026-07-18*
