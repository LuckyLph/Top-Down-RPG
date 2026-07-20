# Codebase Structure

**Analysis Date:** 2026-07-18

## Directory Layout

```text
Top-Down-RPG/
├── Assets/
│   ├── Animations/             # Player, mob, and weapon animation clips/controllers
│   ├── Data/Weapons/           # Authored `PlayerWeapon` ScriptableObjects
│   ├── Editor/                 # Unity Editor-only import and prefab maintenance tools
│   ├── Prefabs/                # Reusable Player, Mobs, Combat, and UI compositions
│   ├── Scenes/                 # Build scene (`SampleScene.unity`)
│   ├── Scripts/                # Runtime gameplay code by domain
│   │   ├── AI/                 # Core, Navigation, and StateMachine layers
│   │   ├── Camera/             # Camera follow and draw ordering
│   │   ├── Combat/             # Health, damage, attacks, and death effects
│   │   ├── Player/             # Input, movement, and weapon ownership
│   │   └── UI/                 # Player HUD behaviour
│   ├── Settings/AI/            # Mob and terrain navigation configuration assets
│   ├── Sprites/                # Game art organized by subject
│   ├── Tests/                  # Editor and play-mode test assemblies
│   └── Tiles/                  # Tile assets, palettes, and collision tiles
├── Packages/                   # Unity package manifest and resolved package lock
├── ProjectSettings/            # Unity project, render, physics, and build settings
├── .planning/codebase/         # Generated architecture maps
├── Library/                    # Unity-generated local import/cache data
├── Temp/                       # Unity-generated transient build/editor data
└── UserSettings/               # Unity local editor preferences
```

## Directory Purposes

**`Assets/Scripts/AI/`:**

- Purpose: Owns mob decisions and terrain-aware navigation.
- Contains: `Core/` components and configuration, `Navigation/` contracts/grid/A*, and `StateMachine/` decision objects.
- Key files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`, and `Assets/Scripts/AI/StateMachine/MobStateBase.cs`.

**`Assets/Scripts/Combat/`:**

- Purpose: Provides gameplay-neutral damage, health, attacks, death visuals, and transient feedback shared by player and mobs.
- Contains: `Health`, `DamageReceiver`, attack/dealer components, `PlayerWeapon` ScriptableObject, death handlers, and visual helpers.
- Key files: `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/Combat/DamageReceiver.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, and `Assets/Scripts/Combat/PlayerWeapon.cs`.

**`Assets/Scripts/Player/`:**

- Purpose: Owns player-only input, 2D movement, facing, animation parameters, weapon ownership, and attack cooldown start.
- Contains: The Player controller and weapon controller.
- Key files: `Assets/Scripts/Player/PlayerController.cs` and `Assets/Scripts/Player/PlayerWeaponController.cs`.

**`Assets/Scripts/Camera/`:**

- Purpose: Keeps the camera following the player and orders sprites by world Y.
- Contains: `CameraFollow2D` and reusable `YPositionSorter` components.
- Key files: `Assets/Scripts/Camera/CameraFollow2D.cs` and `Assets/Scripts/Camera/YPositionSorter.cs`.

**`Assets/Scripts/UI/`:**

- Purpose: Binds the Player's health and equipped weapon to the prefab-local HUD controls.
- Contains: One HUD controller component.
- Key file: `Assets/Scripts/UI/HudController.cs`.

**`Assets/Prefabs/`:**

- Purpose: Stores reusable component compositions rather than placing gameplay logic directly in the scene.
- Contains: `Combat/`, `Mobs/`, `Player/`, and `UI/` prefab categories.
- Key files: `Assets/Prefabs/Player/Player.prefab`, `Assets/Prefabs/Mobs/Weasel.prefab`, `Assets/Prefabs/Combat/SwordSlash.prefab`, `Assets/Prefabs/Combat/MobDeathAnimation.prefab`, and `Assets/Prefabs/UI/PlayerHudCanvas.prefab`.

**`Assets/Data/Weapons/`:**

- Purpose: Holds editable weapon values and asset references shared by player weapon instances.
- Contains: `PlayerWeapon` assets.
- Key file: `Assets/Data/Weapons/Sword.asset`.

**`Assets/Settings/AI/`:**

- Purpose: Holds authored ScriptableObject configurations for mob behaviour and navigation terrain.
- Contains: `MobConfig`, terrain type, and terrain-movement-profile assets.
- Key files: `Assets/Settings/AI/Mob_Default.asset`, `Assets/Settings/AI/Terrain_Grass.asset`, and `Assets/Settings/AI/TerrainMovement_Default.asset`.

**`Assets/Animations/`:**

- Purpose: Holds runtime animation clips and controllers for player/mob movement plus combat effects.
- Contains: `Player/`, `Mobs/`, and `Weapons/` subdirectories.
- Key files: `Assets/Animations/Player/Player.controller`, `Assets/Animations/Mobs/Weasel/Weasel.controller`, `Assets/Animations/Weapons/Slash.anim`, and `Assets/Animations/Mobs/MobDeath.anim`.

**`Assets/Tiles/`:**

- Purpose: Holds rule tiles, palette assets, and collision tile assets used by the scene tilemap hierarchy.
- Contains: `tilesets/`, `Palettes/`, and `Collision/`.
- Key files: `Assets/Tiles/tilesets/grass_DualGridRuleTile.asset` and `Assets/Tiles/Collision/CollisionTile.asset`.

**`Assets/Editor/`:**

- Purpose: Contains editor-only setup and asset maintenance code; do not place runtime gameplay components here.
- Contains: An asset postprocessor, snap initializer, and combat prefab bootstrap.
- Key files: `Assets/Editor/PixelArtTextureDefaults.cs`, `Assets/Editor/PixelArtEditorSnapSettings.cs`, and `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs`.

**`Assets/Tests/`:**

- Purpose: Separates unit-like Editor tests from Unity runtime PlayMode tests.
- Contains: Test assembly definitions and C# test fixtures.
- Key files: `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef`, `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`, `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`, and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`.

**`Assets/TextMesh Pro/`:**

- Purpose: Stores TextMeshPro resource, shader, font, and example assets used by `TextMeshProUGUI` gameplay/UI components.
- Contains: Resources, shaders, fonts, and package examples.
- Key locations: `Assets/TextMesh Pro/Resources/` and `Assets/TextMesh Pro/Examples & Extras/`.

**`Packages/`:**

- Purpose: Declares Unity packages and locks resolved package versions.
- Contains: Package manifest and lock data.
- Key files: `Packages/manifest.json` and `Packages/packages-lock.json`.

**`ProjectSettings/`:**

- Purpose: Stores Unity project-wide settings and the build scene list.
- Contains: Input, physics, graphics, render pipeline, and build settings.
- Key files: `ProjectSettings/ProjectVersion.txt`, `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/Physics2DSettings.asset`, and `ProjectSettings/GraphicsSettings.asset`.

## Key File Locations

**Entry Points:**

- `Assets/Scenes/SampleScene.unity`: Sole enabled build scene; composes the grid, tilemaps, main camera, and prefab instances.
- `ProjectSettings/EditorBuildSettings.asset`: Marks `Assets/Scenes/SampleScene.unity` as enabled for build.
- `Assets/Scripts/AI/Core/MobController.cs`: Mob lifecycle/state-machine entry point through Unity `Awake`/`Start`/`Update`/`FixedUpdate`.
- `Assets/Scripts/Player/PlayerController.cs`: Player lifecycle/input entry point through Unity `Awake`/`OnEnable`/`Update`/`FixedUpdate`.

**Configuration:**

- `Assets/Scripts/TopDownRPG.Gameplay.asmdef`: Runtime assembly definition for all gameplay scripts.
- `Assets/InputSystem_Actions.inputactions`: Authored Input System action asset used by `Assets/Scripts/Player/PlayerController.cs` in the editor.
- `Assets/Settings/AI/Mob_Default.asset`: Default mob movement, perception, combat, patrol, and navigation values.
- `Assets/Settings/AI/TerrainMovement_Default.asset`: Terrain walkability and cost rules.
- `Assets/Data/Weapons/Sword.asset`: Sword damage, cooldown, directional offsets, slash prefab, and HUD icon.

**Core Logic:**

- `Assets/Scripts/AI/Core/MobController.cs`: Owns mob dependencies and state transitions.
- `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`: Builds the scene navigation grid.
- `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`: Implements grid A* search.
- `Assets/Scripts/Combat/Health.cs`: Defines health events and damage application.
- `Assets/Scripts/Combat/SwordSlashAttack.cs`: Implements player slash spawning, follow, hit filtering, and lifetime.

**Presentation:**

- `Assets/Scripts/Camera/CameraFollow2D.cs`: Follows the Player-tagged object.
- `Assets/Scripts/Camera/YPositionSorter.cs`: Applies depth order from Y position.
- `Assets/Scripts/UI/HudController.cs`: Updates player health/weapon HUD controls.
- `Assets/Scripts/Combat/FloatingDamageText.cs`: Creates and updates a transient screen-space damage popup canvas.

**Testing:**

- `Assets/Tests/Editor/`: EditMode coverage for combat components, mob motor/state machine, navigation grid/pathfinding, and weapons.
- `Assets/Tests/PlayMode/`: PlayMode mob behaviour coverage.

## Naming Conventions

**Files:**

- Use PascalCase filenames matching the top-level C# type: `Assets/Scripts/Combat/Health.cs` defines `Health`; `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` defines `NavigationGrid2D`.
- Use a domain role suffix for Unity components when it clarifies responsibility: `Controller`, `Provider`, `Receiver`, `Dealer`, `Agent2D`, `Sorter`, and `Animation` in `Assets/Scripts/`.
- Put AI state implementations in PascalCase `*State.cs` files under `Assets/Scripts/AI/StateMachine/`, such as `Assets/Scripts/AI/StateMachine/ChaseState.cs`.
- Name ScriptableObject assets by their configured role and variant: `Assets/Settings/AI/Mob_Default.asset`, `Assets/Settings/AI/TerrainMovement_Default.asset`, and `Assets/Data/Weapons/Sword.asset`.
- Match Unity's paired metadata convention: every committed asset has a `.meta` neighbour, such as `Assets/Prefabs/Player/Player.prefab.meta`.

**Directories:**

- Use singular domain folders for runtime code: `Assets/Scripts/Player/`, `Assets/Scripts/Camera/`, `Assets/Scripts/Combat/`, and `Assets/Scripts/UI/`.
- Use structural subfolders only where a domain has distinct layers: `Assets/Scripts/AI/Core/`, `Assets/Scripts/AI/Navigation/`, and `Assets/Scripts/AI/StateMachine/`.
- Mirror authored asset categories under `Assets/Prefabs/`, `Assets/Animations/`, `Assets/Sprites/`, and `Assets/Tests/` when the runtime domain needs a corresponding asset type.

## Where to Add New Code

**New Player or Combat Feature:**

- Primary code: Put input/character-ownership behaviour in `Assets/Scripts/Player/`; put reusable health, damage, hitbox, weapon, death, and feedback behaviour in `Assets/Scripts/Combat/`.
- Prefab composition: Add the component to `Assets/Prefabs/Player/Player.prefab`, `Assets/Prefabs/Mobs/Weasel.prefab`, or a new appropriate prefab below `Assets/Prefabs/`.
- Assets: Add reusable weapon configuration below `Assets/Data/Weapons/` and animations below `Assets/Animations/Weapons/`.
- Tests: Add pure/component behaviour coverage to `Assets/Tests/Editor/` and scene/physics integration coverage to `Assets/Tests/PlayMode/`.

**New Mob AI Behaviour:**

- Controller-local capability: Add a focused component to `Assets/Scripts/AI/Core/` and have `MobController` resolve it from the same GameObject in `Assets/Scripts/AI/Core/MobController.cs`.
- New decision state: Add a `MobStateBase` subclass to `Assets/Scripts/AI/StateMachine/`, add its identifier to `Assets/Scripts/AI/Core/MobStateId.cs`, and register it in `Assets/Scripts/AI/Core/MobController.cs`.
- Navigation data/algorithm: Add contracts, grid-related components, and pathfinding implementations to `Assets/Scripts/AI/Navigation/`; add terrain/config assets to `Assets/Settings/AI/`.
- Composition: Add required components to the relevant mob prefab under `Assets/Prefabs/Mobs/` and protect hard requirements with `[RequireComponent]` where appropriate.

**New Camera or UI Component:**

- Camera/view logic: Put follow, sorting, or display-side world rendering components in `Assets/Scripts/Camera/`.
- HUD/UI behaviour: Put UI controllers in `Assets/Scripts/UI/` and compose their owned controls in `Assets/Prefabs/UI/`.
- References: Resolve owned children locally using `GetComponent`/`GetComponentInChildren` or a local helper. Supply dynamic scene participants at runtime; reserve serialized fields for assets, configuration, and intentional overrides.

**Utilities:**

- Shared runtime utility directory: Not detected. Place a new helper in the narrowest owning domain under `Assets/Scripts/` until it is genuinely reused across domains.
- Editor-only utility: Put `UnityEditor`-dependent code below `Assets/Editor/`; do not reference it from runtime components under `Assets/Scripts/`.

## Special Directories

**`Assets/Editor/`:**

- Purpose: Runs only inside the Unity Editor for import/setup tools.
- Generated: No; source-controlled project code.
- Committed: Yes.

**`Assets/TextMesh Pro/`:**

- Purpose: TextMeshPro package resources and examples required by UI/damage-text components.
- Generated: No; imported package assets and project resources.
- Committed: Yes.

**`Library/`:**

- Purpose: Unity asset import cache and local editor state.
- Generated: Yes.
- Committed: No; do not add project source here.

**`Temp/`:**

- Purpose: Unity transient editor/build output.
- Generated: Yes.
- Committed: No; do not add project source here.

**`UserSettings/`:**

- Purpose: Local Unity editor preferences.
- Generated: Yes, per workspace/user.
- Committed: No; do not use it for project configuration.

**`.planning/codebase/`:**

- Purpose: Generated codebase reference documents used by planning and execution workflows.
- Generated: Yes, by mapping workflows.
- Committed: Project workflow dependent; update only mapping documents from codebase evidence.

---

*Structure analysis: 2026-07-18*
