<!-- GSD:project-start source:PROJECT.md -->

## Project

**Top-Down-RPG**

Top-Down-RPG is a solo, local Unity project for building the reusable tools and gameplay systems of an original top-down action RPG. It takes high-level inspiration from the accessible world-building of *Necesse*, the story and town-facing elements of *Stardew Valley*, and the readable action combat of *RuneScape: Dragonwilds*, while developing its own mechanics, art direction, and content.

The immediate product is an iterative clearing-to-town vertical slice: the player begins with equipment and abilities, learns through combat along a linear path, and reaches a starting town that introduces the foundations of quests, crafting, and character progression. The project is deliberately a proving ground for systems and feel before it becomes a full game.

**Core Value:** Make fast, satisfying, highly readable top-down combat whose visual effects push sprite-based presentation beyond basic sprite rendering.

### Constraints

- **Technology**: Continue with Unity 6000.4.2f1, URP, C#, the Input System, 2D Tilemaps, and the existing test assemblies — they are the established project platform.
- **Architecture**: Prefer runtime discovery, ownership-based lookups, and local helper components for scene relationships. Reserve serialized fields for configuration, asset references, and intentional overrides.
- **Visuals**: Effects must reinforce player understanding of attacks, enemy telegraphs, damage, and space; spectacle must not obscure gameplay decisions.
- **Scope**: Build a small, revisable vertical slice and reusable system/tooling foundations before expanding world scale, content breadth, or narrative depth.
- **Environment**: Develop and run locally on Windows. Unity MCP remains a loopback, project-scoped editor tool, not a runtime dependency.

<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->

## Technology Stack

## Languages

- C# 9.0 - Gameplay, UI, combat, AI, editor tooling, and tests under `Assets/Scripts/`, `Assets/Editor/`, and `Assets/Tests/`; Unity-generated project files set `LangVersion` to 9.0 in `TopDownRPG.Gameplay.csproj`.
- ShaderLab and HLSL - Rendering and TextMesh Pro shader assets under `Assets/TextMesh Pro/Shaders/`.
- YAML - Unity scenes, prefabs, ScriptableObject data, and project settings such as `Assets/Scenes/SampleScene.unity`, `Assets/Data/Weapons/Sword.asset`, and `ProjectSettings/ProjectSettings.asset`.
- JSON - Unity package/input configuration in `Packages/manifest.json`, `Packages/packages-lock.json`, and `Assets/InputSystem_Actions.inputactions`.

## Runtime

- Unity Editor 6000.4.2f1 - Required engine version recorded in `ProjectSettings/ProjectVersion.txt`; the generated C# projects target `.NET Standard 2.1` in `TopDownRPG.Gameplay.csproj`.
- Unity's managed scripting runtime compiles the `TopDownRPG.Gameplay`, `TopDownRPG.EditModeTests`, and `TopDownRPG.PlayModeTests` assemblies defined in `Assets/Scripts/TopDownRPG.Gameplay.asmdef`, `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef`, and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`.
- Unity Package Manager (UPM) - Direct dependencies are declared in `Packages/manifest.json`.
- Lockfile: present at `Packages/packages-lock.json`; use this file with `Packages/manifest.json` to reproduce resolved package versions.
- No Node, Python, Go, Rust, or other language-package manifest is present at the repository root; Unity/UPM is the build dependency authority.

## Frameworks

- Unity Engine 6000.4.2f1 - Game runtime and editor platform for all C# assemblies; scene entry is `Assets/Scenes/SampleScene.unity` and engine configuration is in `ProjectSettings/`.
- Universal Render Pipeline 17.4.0 (`com.unity.render-pipelines.universal`) - 2D rendering pipeline declared in `Packages/manifest.json` and configured by `Assets/Settings/UniversalRP.asset` and `Assets/Settings/Renderer2D.asset`.
- Unity 2D Tilemap 1.0.0 and Tilemap Extras 7.0.1 - Tilemap-based terrain and navigation used by `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` and tile assets under `Assets/Tiles/`.
- Unity Input System 1.19.0 - Action-based player input declared in `Packages/manifest.json`, referenced by `Assets/Scripts/TopDownRPG.Gameplay.asmdef`, and configured in `Assets/InputSystem_Actions.inputactions`.
- Unity UI (UGUI) 2.0.0 and TextMesh Pro - HUD UI uses `UnityEngine.UI` and `TMPro` in `Assets/Scripts/UI/HudController.cs`; TextMesh Pro is referenced by `Assets/Scripts/TopDownRPG.Gameplay.asmdef` rather than separately pinned in `Packages/manifest.json`.
- Unity Test Framework 1.6.0 - Edit-mode and play-mode tests are enabled by `Packages/manifest.json` and the test assembly definitions in `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`.
- NUnit - Test source imports `NUnit.Framework` in `Assets/Tests/Editor/CombatComponentTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`; the resolved dependency chain is locked in `Packages/packages-lock.json`.
- Unity Editor/UPM - Import, compile, test, and player build workflow configured by `ProjectSettings/`, `Packages/manifest.json`, and `ProjectSettings/EditorBuildSettings.asset`.
- Microsoft.NET.Sdk project generation - IDE project files `TopDownRPG.Gameplay.csproj`, `TopDownRPG.EditModeTests.csproj`, and `TopDownRPG.PlayModeTests.csproj` target `netstandard2.1`; Unity marks these generated files as non-authoritative.
- Visual Studio integration 2.0.27 and JetBrains Rider integration 3.0.39 - Editor support packages declared in `Packages/manifest.json`.
- Unity MCP (`mcpforunityserver` 10.1.0) - Developer-operated MCP bridge for project-scoped Unity Editor inspection, scene/object operations, script edits, console checks, and Unity Test Framework runs. It is local workstation tooling supplied by the project owner, not a UPM dependency or a runtime player feature.

## Key Dependencies

- `com.unity.render-pipelines.universal` 17.4.0 - Supplies the active URP/2D renderer configured in `Assets/Settings/UniversalRP.asset` and `Assets/Settings/Renderer2D.asset`.
- `com.unity.inputsystem` 1.19.0 - Supplies action maps consumed by `Assets/Scripts/Player/PlayerController.cs` and stored in `Assets/InputSystem_Actions.inputactions`.
- `com.unity.ugui` 2.0.0 and Unity TextMesh Pro - Supply the `Image` and TMP UI types used by `Assets/Scripts/UI/HudController.cs` and `Assets/Scripts/Combat/FloatingDamageText.cs`.
- `com.unity.2d.tilemap` 1.0.0 plus `com.unity.2d.tilemap.extras` 7.0.1 - Support authored tile assets in `Assets/Tiles/` and the navigation grid implementation in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- `com.skner.dualgrid` (Git dependency pinned by hash `493500b6ac59f5713b7d9d9bdff6d6a37016da78`) - Provides DualGrid tile authoring assets referenced under `Assets/Tiles/tilesets/`; the source URL and resolved hash are in `Packages/manifest.json` and `Packages/packages-lock.json`.
- `com.unity.2d.animation` 14.0.4, `com.unity.2d.aseprite` 4.0.1, and `com.unity.2d.psdimporter` 13.0.2 - 2D animation and art import tooling declared in `Packages/manifest.json`; related animation assets live in `Assets/Animations/`.
- `com.unity.timeline` 1.8.12 and `com.unity.visualscripting` 1.9.11 - Installed Unity feature packages declared in `Packages/manifest.json`; no authored runtime source imports these APIs under `Assets/Scripts/`.
- `com.unity.collab-proxy` 2.12.4 and `com.unity.multiplayer.center` 1.0.1 - Installed editor-oriented Unity packages in `Packages/manifest.json`; no authored multiplayer or collaboration client is present under `Assets/Scripts/`.

## Configuration

- Engine and player settings are versioned Unity YAML in `ProjectSettings/ProjectVersion.txt` and `ProjectSettings/ProjectSettings.asset`; the configured product is `Top-Down-RPG` and the active editor build defines target standalone Windows.
- Input mappings are versioned in `Assets/InputSystem_Actions.inputactions`; use this asset when adding player actions rather than hard-coding input bindings in scripts.
- Gameplay data is asset-backed through ScriptableObjects, with weapon data in `Assets/Data/Weapons/Sword.asset` and AI data in `Assets/Settings/AI/`; their types are defined in `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Scripts/AI/Core/MobConfig.cs`, and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.
- No `.env` files or environment-variable configuration were detected in the repository root; `Packages/manifest.json` and `ProjectSettings/` contain the project's tracked configuration.
- Build settings are in `ProjectSettings/EditorBuildSettings.asset`, which enables `Assets/Scenes/SampleScene.unity` as the sole configured scene.
- Player build options, platform identifiers, Android/WebGL settings, and scripting backend configuration are in `ProjectSettings/ProjectSettings.asset`.
- Package sources and resolved dependency versions are controlled by `Packages/manifest.json` and `Packages/packages-lock.json`; do not hand-edit generated IDE projects such as `TopDownRPG.Gameplay.csproj`.

## Platform Requirements

- Install Unity Editor 6000.4.2f1 (normally through Unity Hub) to match `ProjectSettings/ProjectVersion.txt` and restore UPM dependencies from `Packages/manifest.json`.
- Use a C# IDE supported by the installed UPM integrations in `Packages/manifest.json` (Visual Studio or Rider); generated projects require .NET Standard 2.1 support as shown in `TopDownRPG.Gameplay.csproj`.
- The Unity editor needs access to `Packages/packages-lock.json` and the Git-hosted `com.skner.dualgrid` source specified in `Packages/manifest.json` when restoring dependencies.
- For MCP-assisted editor work, start the project-scoped Unity MCP server on the developer machine after opening this project in Unity:
- The current editor-generated compilation symbols target standalone Windows in `TopDownRPG.Gameplay.csproj`; player platform settings also contain Android and WebGL options in `ProjectSettings/ProjectSettings.asset`.
- Production content is a local Unity player built from `Assets/Scenes/SampleScene.unity`; no hosting, backend deployment, or custom native plugin artifact is configured under `Assets/`.

<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->

## Conventions

## Naming Patterns

- Name each C# source file after its primary public type in PascalCase: `Assets/Scripts/Combat/Health.cs` defines `Health`, `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs` defines `GridAStarPathfinder2D`, and `Assets/Editor/PixelArtEditorSnapSettings.cs` defines `PixelArtEditorSnapSettings`.
- Keep tests in PascalCase with a `Tests` suffix, such as `Assets/Tests/Editor/NavigationGridPathfindingTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`.
- Keep production code grouped by domain under `Assets/Scripts/` and editor-only tooling under `Assets/Editor/`; do not put custom runtime code in Unity package/example folders.
- Use PascalCase for public methods, properties, events, constructors, and private methods: `ApplyDamage`, `TryGetTraversal`, `ResolveReferences`, and `EnsureInitialized` in `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`, and `Assets/Scripts/AI/Core/MobController.cs`.
- Keep Unity message methods private and PascalCase (`Awake`, `OnEnable`, `Update`, `FixedUpdate`, `OnValidate`, `OnDrawGizmosSelected`), as in `Assets/Scripts/Player/PlayerController.cs`.
- Name boolean-returning query methods with `Is`, `Has`, `Can`, or `Try` when the result represents a condition or lookup, for example `IsCellWalkable`, `HasLineOfSightCells`, and `TryGetNearestWalkableCell` in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Name test methods `Subject_ExpectedBehavior[_Condition]`, for example `AStar_ReturnsPartial_WhenGoalUnreachableAndPartialAllowed` in `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`.
- Use camelCase for parameters, locals, and private fields (`maxHealth`, `currentHealth`, `movementProfile`, `pathfinder`); keep serialized backing fields private in `Assets/Scripts/Combat/Health.cs` and `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Use PascalCase for constants and static cached values (`PlayerTag`, `DefaultLifetime`, `IsMovingHash`) in `Assets/Scripts/UI/HudController.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`, and `Assets/Scripts/Player/PlayerController.cs`.
- Use descriptive booleans for state (`initialized`, `subscribedToPlayer`, `drawGridBoundsGizmo`) rather than abbreviated flags.
- Use PascalCase for classes, structs, enums, and nested data types; prefix interfaces with `I`, as in `Assets/Scripts/AI/Core/IMobState.cs` and `Assets/Scripts/AI/Navigation/IPathfinder2D.cs`.
- Use `sealed` for concrete asset or implementation types only where inheritance is deliberately blocked (`PlayerWeapon` in `Assets/Scripts/Combat/PlayerWeapon.cs`, private `CellData` in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`).
- Runtime scripts currently use the global namespace. Keep new types global unless a deliberate, assembly-wide namespace migration changes that convention; do not introduce isolated namespaces.

## Code Style

- Use four-space indentation, Allman braces, and blank lines between logical blocks, matching every custom `.cs` file under `Assets/Scripts/` and `Assets/Tests/`.
- The generated Unity projects target C# 9.0 and `netstandard2.1` (`TopDownRPG.Gameplay.csproj` and `TopDownRPG.EditModeTests.csproj`). Target-typed construction (`new(...)`, `new()`) and switch expressions are established patterns in `Assets/Scripts/Combat/DamageReceiver.cs` and `Assets/Scripts/AI/Core/MobController.cs`.
- No committed formatter configuration (`.editorconfig`, `.prettierrc`, or equivalent) is detected. Preserve the surrounding file's whitespace and brace style rather than assuming an automated formatter will correct it.
- No committed analyzer, linter, StyleCop, Roslyn ruleset, or warnings-as-errors configuration is detected.
- Unity-generated `.csproj` files suppress `0169` and `USG0001`; treat those generated settings as Unity tooling output, not as a project code-style policy.

## Unity Serialization and References

- Use `[SerializeField] private` fields for gameplay configuration, asset references, and explicit inspector overrides, with inspector validation attributes such as `[Min]`, `[Header]`, and `[Tooltip]`. `Assets/Scripts/Combat/PlayerWeapon.cs` and `Assets/Scripts/AI/Core/MobConfig.cs` are the reference patterns.
- Avoid serialized component references that merely connect scene objects. Prefer local `GetComponent`/`GetComponentInChildren` resolution, ownership-based child discovery, or runtime discovery. `ResolveHealth` in `Assets/Scripts/Combat/DamageReceiver.cs`, `ResolveLocalUiReferences` in `Assets/Scripts/UI/HudController.cs`, and `ResolveTerrainSources` in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` show the intended lookup shapes.
- Declare mandatory co-located components with `[RequireComponent]` and prevent invalid duplicates with `[DisallowMultipleComponent]`, as in `Assets/Scripts/Combat/DamageReceiver.cs` and `Assets/Scripts/Combat/FloatingDamageText.cs`.
- Put `UnityEditor` imports and editor-only code behind `#if UNITY_EDITOR`, as in `Assets/Scripts/Player/PlayerController.cs` and `Assets/Scripts/AI/Core/MobController.cs`; editor utilities belong in `Assets/Editor/`.

## MCP-Assisted Unity Editor Work

- Use the project-scoped Unity MCP server described in `.planning/codebase/STACK.md` and `.planning/codebase/INTEGRATIONS.md` only against an open local editor instance. Begin by checking editor readiness and discovering the target GameObjects/components; do not rely on stale scene assumptions.
- After an MCP script change, wait for Unity compilation/domain reload to finish and inspect console errors before attaching the component, editing a prefab, or running tests.
- Prefer small, ownership-scoped edits and existing local discovery patterns. In particular, do not introduce serialized component links merely because the MCP bridge can address arbitrary scene objects.

## Import Organization

- Not applicable. Custom code has no C# namespace aliases or `using` aliases; assembly boundaries are defined by `Assets/Scripts/TopDownRPG.Gameplay.asmdef` and the test `.asmdef` files.

## Error Handling

- Use guard clauses and safe return values for invalid runtime state. `Assets/Scripts/Combat/Health.cs` returns `0` for invalid/dead damage targets, and `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` returns `false` when traversal cannot be resolved.
- Validate authored values with `[Min]`, `OnValidate`, and defensive clamping (`Mathf.Max`/`Mathf.Clamp`), as in `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Scripts/Combat/Health.cs`, and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.
- Use `Try...` methods with `out` values for recoverable lookup or calculation failure (`TryGetTraversal`, `TryGetNearestWalkableCell`, `TryResolveCellTraversal`); callers branch on the boolean instead of catching exceptions.
- Do not introduce exceptions for ordinary gameplay validation. No custom exception type or intentional `throw` is present in custom scripts or tests. The only `try/catch` in runtime code, `Assets/Scripts/Combat/FloatingDamageText.cs`, protects an optional fallback font creation and degrades to `null`.

## Logging

- Log actionable missing configuration with `Debug.LogError` or `Debug.LogWarning`, including the relevant Unity context object when available; see `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Suppress repetitive warnings with a local boolean when the check runs every frame, as `warnedMissingPlayer` and `warnedMissingUiReferences` do in `Assets/Scripts/UI/HudController.cs`.
- Use `Debug.Log` for explicit editor-tool completion/status only, as in `Assets/Editor/PixelArtTextureDefaults.cs` and `Assets/Editor/PixelArtEditorSnapSettings.cs`. No dedicated logging abstraction is detected.

## Comments

- Comment non-obvious decisions, invariants, or engine-specific behavior, not straightforward control flow. Examples explain collider edge distance in `Assets/Scripts/AI/Core/MobController.cs`, waypoint jitter avoidance in `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, and diagonal corner-cut prevention in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Not applicable. No XML documentation comments (`///`) are present in custom runtime, editor, or test scripts. Use concise `//` comments only where the reason is not clear from the code.

## Function Design

- Keep Unity lifecycle methods thin: cache/resolve in `Awake`, subscribe in `OnEnable`, unsubscribe in `OnDisable`/`OnDestroy`, and delegate domain behavior to named helpers. `Assets/Scripts/UI/HudController.cs` and `Assets/Scripts/Player/PlayerController.cs` demonstrate this sequence.
- Decompose repeated dependency setup into `Resolve...`, initialization into `Ensure...`/`Initialize...`, and programmatic test seams into `Configure...`; see `Assets/Scripts/AI/Core/MobController.cs` and `Assets/Scripts/AI/Navigation/NavigationTerrainSource2D.cs`.
- Use explicit primitive and Unity-value parameters, optional parameters only for genuine defaults, and `out` parameters for allocation-free lookup results. Examples include `Health.ApplyDamage(int amount, GameObject source = null)` and `NavigationGrid2D.TryGetNearestWalkableCell(...)`.
- Prefer an owned object (`MobController` passed to a state constructor in `Assets/Scripts/AI/StateMachine/MobStateBase.cs`) over broad scene references.
- Expose read-only state with expression-bodied properties (`CurrentHealth`, `IsBuilt`, `CurrentStateId`) and return operation results where callers need to branch (`ApplyDamage`, `TryAttack`, `BuildPathToWorld`).
- Use `IReadOnlyList<T>` for externally visible collections, as in `Assets/Scripts/AI/Navigation/PathTypes.cs` and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.

## Module Design

- Keep one primary public type per file and match it to the file name. Public types are globally visible within the relevant Unity assembly because custom scripts do not declare namespaces.
- Keep ScriptableObject configuration immutable to consumers through read-only properties where practical (`Assets/Scripts/Combat/PlayerWeapon.cs`); retain public fields only for authored data containers such as `MobConfig` and `TerrainMovementProfile2D.TerrainRule`.
- Not used. Unity discovers scripts directly and compiles runtime code through `Assets/Scripts/TopDownRPG.Gameplay.asmdef`; do not add barrel files.

<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->

## Architecture

## System Overview

```text

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

- Add behaviour as a focused `MonoBehaviour` in `Assets/Scripts/<domain>/`; compose it on an owned prefab in `Assets/Prefabs/<domain>/`.
- Use `GetComponent`, `GetComponentInChildren`, runtime `Configure(...)`, and owner-level discovery for required scene collaborators. `MobController` resolves its co-located components and discovers the scene grid/target provider when no intentional override is supplied in `Assets/Scripts/AI/Core/MobController.cs`.
- Use serialized fields for numeric settings, masks, colours, ScriptableObject assets, prefabs, animation clips, and intentional overrides. `MobConfig` and `PlayerWeapon` are the primary authored configuration assets in `Assets/Scripts/AI/Core/MobConfig.cs` and `Assets/Scripts/Combat/PlayerWeapon.cs`.
- Separate frame decisions from physics execution: controllers decide in `Update`, state machines/path agents set desired motion, and `Rigidbody2D` velocity is applied in `FixedUpdate`/`FixedTick`.
- Keep rendering concerns local: `YPositionSorter` applies sort order in `LateUpdate`, `CameraFollow2D` follows in `LateUpdate`, and `FloatingDamageText` projects world positions to UI in `LateUpdate`.
- Unity MCP is an optional, local development bridge rather than a gameplay layer. When it is running at `http://127.0.0.1:8080` with `--project-scoped-tools`, an MCP client can inspect and operate on this project's open Unity Editor.
- Use it to discover the live scene and component topology before editing, then apply focused editor changes. It must not become a runtime dependency, be added to player code, or replace the existing prefab/ScriptableObject authoring model.
- After MCP-driven script edits, wait for Unity compilation to complete and inspect the Unity Console before changing scene or prefab composition. Use the Unity Test Framework through the bridge or Unity Test Runner to verify behavioural changes.

## Layers

- Purpose: Defines the build scene, object hierarchies, and component composition.
- Location: `Assets/Scenes/SampleScene.unity`, `Assets/Prefabs/Player/Player.prefab`, `Assets/Prefabs/Mobs/Weasel.prefab`, `Assets/Prefabs/Combat/`, and `Assets/Prefabs/UI/`.
- Contains: Tilemaps, camera, player/mob/UI/combat prefabs, colliders, renderers, animators, and authored component settings.
- Depends on: Gameplay scripts in `Assets/Scripts/` and data assets in `Assets/Data/` and `Assets/Settings/`.
- Used by: Unity's scene loader; `ProjectSettings/EditorBuildSettings.asset` enables `Assets/Scenes/SampleScene.unity`.
- Purpose: Converts Input System actions into player motion, animation parameters, and attack attempts.
- Location: `Assets/Scripts/Player/PlayerController.cs` and `Assets/Scripts/Player/PlayerWeaponController.cs`.
- Contains: Input lookup/fallback binding, facing direction, `Rigidbody2D` movement, weapon cooldown ownership, and weapon-equipped events.
- Depends on: `UnityEngine.InputSystem`, `PlayerWeapon`, `SwordSlashAttack`, and local player components.
- Used by: The Player prefab at `Assets/Prefabs/Player/Player.prefab` and HUD at `Assets/Scripts/UI/HudController.cs`.
- Purpose: Supplies reusable health, damage, hit detection, death effect, and damage-popup behaviour.
- Location: `Assets/Scripts/Combat/`.
- Contains: `Health` events, `DamageReceiver`, mob `MeleeDamageDealer`, player `SwordSlashAttack`, `MobDeathAnimation`, death handlers, and `FloatingDamageText`.
- Depends on: Unity 2D physics, TextMeshPro/UI, animation playables, and assets in `Assets/Data/Weapons/`, `Assets/Prefabs/Combat/`, and `Assets/Animations/Weapons/`.
- Used by: Both `Assets/Prefabs/Player/Player.prefab` and `Assets/Prefabs/Mobs/Weasel.prefab`.
- Purpose: Owns a mob's dependencies and finite-state-machine lifecycle.
- Location: `Assets/Scripts/AI/Core/MobController.cs` and `Assets/Scripts/AI/StateMachine/`.
- Contains: State registration, transitions, perception ticking, attack-range decisions, patrol decisions, chase/repath decisions, and returning to spawn.
- Depends on: Co-located `MobMotor2D`, `MobPerception2D`, `MobPathAgent2D`, `MobPatrolAnchor`, `MeleeDamageDealer`, and `NavigationGrid2D`.
- Used by: `Assets/Prefabs/Mobs/Weasel.prefab`.
- Purpose: Converts terrain tilemaps into weighted walkability and creates/smooths 8-direction A* paths.
- Location: `Assets/Scripts/AI/Navigation/`.
- Contains: `NavigationGrid2D`, `NavigationTerrainSource2D`, `GridAStarPathfinder2D`, path request/result contracts, and terrain data assets.
- Depends on: `UnityEngine.Tilemaps` and `TerrainMovementProfile2D` assets such as `Assets/Settings/AI/TerrainMovement_Default.asset`.
- Used by: `MobPathAgent2D` in `Assets/Scripts/AI/Core/MobPathAgent2D.cs` and the state classes in `Assets/Scripts/AI/StateMachine/`.
- Purpose: Maintains camera position, sprite depth ordering, HUD state, animations, and transient visual feedback.
- Location: `Assets/Scripts/Camera/`, `Assets/Scripts/UI/`, and visual components in `Assets/Prefabs/`.
- Contains: Camera following, Y-based sorting, event-driven HUD updates, damage popups, and playable-driven slash/death animations.
- Depends on: Player tag/health/weapon events, cameras, renderers, TextMeshPro, UI, and animation assets.
- Used by: `Assets/Scenes/SampleScene.unity`, `Assets/Prefabs/UI/PlayerHudCanvas.prefab`, and combat components.

## Data Flow

### Primary Player Attack Path

### Mob Perception, Navigation, and Attack Path

### Player HUD Path

- Per-instance game state resides in `MonoBehaviour` fields on player, mob, combat, camera, and UI objects.
- Mob state is held by `MobController.currentState`; immutable-ish state IDs and the `IMobState` interface are in `Assets/Scripts/AI/Core/MobStateId.cs` and `Assets/Scripts/AI/Core/IMobState.cs`.
- Authored shared data resides in ScriptableObjects: `PlayerWeapon`, `MobConfig`, `TerrainType2D`, and `TerrainMovementProfile2D`.
- The only project-level static mutable runtime cache is the damage-popup canvas/font in `Assets/Scripts/Combat/FloatingDamageText.cs`.

## Key Abstractions

- Purpose: Separates idle, patrol, chase, attack-range, and return decisions while retaining one controller owner.
- Examples: `Assets/Scripts/AI/StateMachine/IdleState.cs`, `Assets/Scripts/AI/StateMachine/PatrolRoamState.cs`, `Assets/Scripts/AI/StateMachine/ChaseState.cs`, `Assets/Scripts/AI/StateMachine/AttackRangeState.cs`, and `Assets/Scripts/AI/StateMachine/ReturnToSpawnState.cs`.
- Pattern: `IMobState` strategy objects are created by `MobController.RegisterStates`; each receives the owner through `MobStateBase` rather than being `MonoBehaviour` components.
- Purpose: Isolate path requests/results from the concrete grid and A* implementation.
- Examples: `Assets/Scripts/AI/Navigation/IPathfinder2D.cs`, `Assets/Scripts/AI/Navigation/PathTypes.cs`, and `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`.
- Pattern: `MobPathAgent2D` calls the `IPathfinder2D` exposed by `NavigationGrid2D`; requests carry a terrain movement profile and whether a partial path is permitted.
- Purpose: Keep walkability/cost rules editable outside code.
- Examples: `Assets/Scripts/AI/Navigation/TerrainType2D.cs`, `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`, `Assets/Settings/AI/Terrain_Grass.asset`, and `Assets/Settings/AI/TerrainMovement_Default.asset`.
- Pattern: `NavigationTerrainSource2D` maps scene tilemaps to terrain assets; movement profiles apply per-terrain walkability and traversal cost.
- Purpose: Allow presentation and death behaviour to react without combat code knowing its consumers.
- Examples: `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/Combat/DamageReceiver.cs`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs`, and `Assets/Scripts/UI/HudController.cs`.
- Pattern: `Health` publishes `Damaged` and `Died`; listeners subscribe on enable and unsubscribe on disable.
- Purpose: Define a player's attack values and visuals as data, then produce a per-attack hitbox instance.
- Examples: `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Data/Weapons/Sword.asset`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, and `Assets/Prefabs/Combat/SwordSlash.prefab`.
- Pattern: A ScriptableObject supplies damage/cooldown/prefab/offsets; the weapon controller owns timing; the slash owns only its short lifetime and one-hit-per-receiver set.

## Entry Points

- Location: `Assets/Scenes/SampleScene.unity`.
- Triggers: Unity loads it because it is the sole enabled scene in `ProjectSettings/EditorBuildSettings.asset`.
- Responsibilities: Composes the tilemap navigation hierarchy and main camera; player, mob, HUD, and combat objects are prefab instances from `Assets/Prefabs/`.
- Location: `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Triggers: Unity invokes `Awake` on the component attached to the `Grid` object in `Assets/Scenes/SampleScene.unity`.
- Responsibilities: Resolves terrain sources, indexes non-collision tile cells, calculates bounds, and exposes `GridAStarPathfinder2D`.
- Location: `Assets/Scripts/AI/Core/MobController.cs`.
- Triggers: Unity invokes `Awake`, `Start`, `Update`, and `FixedUpdate` on each mob prefab instance.
- Responsibilities: Initializes dependencies/states, ticks perception/decisions, and performs state-driven physics movement.
- Location: `Assets/Scripts/Player/PlayerController.cs`.
- Triggers: Unity invokes `Awake`, `OnEnable`, `Update`, and `FixedUpdate` on the Player prefab instance.
- Responsibilities: Resolves Input System actions, updates movement/animation state, starts attacks, and writes rigidbody velocity.
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

### UI binding through distributed child-name contracts

## Error Handling

- `NavigationGrid2D.BuildGrid` logs errors/warnings and leaves `Pathfinder` unavailable when terrain sources cannot build a valid grid (`Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`).
- `GridAStarPathfinder2D.FindPath` returns `PathResult.Failure` for unusable start/goal states rather than throwing (`Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`).
- `MobController` creates a hidden runtime `MobConfig` only when configuration is absent (`Assets/Scripts/AI/Core/MobController.cs`).
- `PlayerController` supplies runtime keyboard/gamepad input actions if the configured asset cannot supply named actions (`Assets/Scripts/Player/PlayerController.cs`).
- Component contracts use `[RequireComponent]`, `[DisallowMultipleComponent]`, `[Min]`, and `OnValidate` across `Assets/Scripts/AI/`, `Assets/Scripts/Combat/`, and `Assets/Scripts/Player/`.

## Cross-Cutting Concerns

<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->

## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/`, or `.codex/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->

## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:

- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->

## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->

## Local Project Instructions

- Avoid serialized component references for linking scene objects together whenever possible.
- Prefer runtime discovery, ownership-based lookups, or local helper components.
- Reserve serialized fields primarily for configuration values, asset references, and intentional overrides.
