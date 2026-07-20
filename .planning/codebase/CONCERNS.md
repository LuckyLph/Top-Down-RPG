# Codebase Concerns

**Analysis Date:** 2026-07-18

## Tech Debt

**Scene-wide dependency resolution and single-player assumptions:**
- Issue: Gameplay components mix intentional configuration references with global discovery. `MobController` falls back to `FindAnyObjectByType<NavigationGrid2D>()` and `FindAnyObjectByType<MobTargetProvider>()`; each mob prefab contains its own `MobTargetProvider`, but its `navigationGrid` and `targetProvider` references are empty. Targeting, HUD binding, and camera following also select the first object tagged `Player`.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Scripts/UI/HudController.cs`, `Assets/Scripts/Camera/CameraFollow2D.cs`, `Assets/Prefabs/Mobs/Weasel.prefab`, `Assets/Prefabs/Player/Player.prefab`
- Impact: Multiple mobs currently converge on the same tagged player, but additive scenes, multiple players, split-screen, factions, delayed spawning, or more than one navigation grid can resolve a different object with no deterministic ownership rule. `CameraFollow2D` does not retry when the player is absent during `OnEnable`.
- Fix approach: Introduce a scene-owned gameplay context that injects the navigation grid and target provider when a mob is created. Resolve player-owned collaborators from the player root, and let cameras/HUDs receive an explicit target or subscribe to a player-spawn event. Keep serialized fields for configuration assets and intentional overrides only.

**Dual input definitions with environment-dependent selection:**
- Issue: The player prefab leaves `inputActionsAsset` unset. `PlayerController` loads `Assets/InputSystem_Actions.inputactions` through `AssetDatabase` in the Editor, then creates a separate hard-coded input map outside the Editor.
- Files: `Assets/Prefabs/Player/Player.prefab`, `Assets/Scripts/Player/PlayerController.cs`, `Assets/InputSystem_Actions.inputactions`
- Impact: Editor play mode and player builds use different control definitions. Changes to bindings in the Input System asset can silently diverge from standalone/web builds; the input-asset test only asserts an editor asset binding.
- Fix approach: Assign the InputActionAsset as a prefab configuration asset, enable and dispose action instances consistently, and retain the code-created bindings only as an explicit, tested fallback if it is required.

**Editor startup mutates gameplay assets without user intent or Undo:**
- Issue: An `[InitializeOnLoad]` bootstrap schedules an editor-startup check that may regenerate `SwordSlash.prefab` and writes its reference into `Sword.asset` with `ApplyModifiedPropertiesWithoutUndo`.
- Files: `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs`, `Assets/Prefabs/Combat/SwordSlash.prefab`, `Assets/Data/Weapons/Sword.asset`
- Impact: Opening the project can modify source-controlled assets, create merge noise, and overwrite an intentionally removed or customized slash component. The automatic path has no Undo record.
- Fix approach: Make asset generation an explicit menu command or an import-time validation that reports missing assets without writing them. Keep the rebuild command, use Undo for intentional edits, and fail validation in CI when required references are absent.

**Local Unity MCP bridge requires workstation discipline:**
- Issue: The project owner uses `mcpforunityserver` 10.1.0 as a local HTTP bridge for MCP-assisted Unity Editor work. It can inspect and mutate project-scoped editor state when the developer starts it.
- Files: The launch configuration is documented in `.planning/codebase/STACK.md` and `.planning/codebase/INTEGRATIONS.md`; it is not a tracked Unity asset or package dependency.
- Impact: Binding beyond `127.0.0.1`, exposing the port through a tunnel, or applying broad scene changes without confirming editor state would unnecessarily broaden access and raise the risk of incorrect asset edits.
- Fix approach: Keep `--http-url http://127.0.0.1:8080` and `--project-scoped-tools`, inspect the target/editor readiness before mutating, and always allow compilation to finish before reading the console and verifying tests.

**Runtime fallback objects conceal broken asset wiring:**
- Issue: Missing mob configuration creates an in-memory `MobConfig`; missing slash prefabs create a GameObject, Animator, collider, and renderer at runtime.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, `Assets/Scripts/Combat/PlayerWeapon.cs`
- Impact: Missing content does not fail at authoring time and can appear as a partially configured, invisible, or unanimated gameplay feature. The fallback objects also use defaults that can differ from authored assets.
- Fix approach: Validate required configuration and prefabs in `OnValidate`/build validation, log one actionable error and disable the dependent feature in non-development builds. Reserve runtime fallback creation for a deliberately supported debug mode.

**Prefab/UI contracts depend on names and hierarchy shape:**
- Issue: The HUD searches descendant objects named `HealthFill`, `HealthText`, and `WeaponIcon`; slash rendering searches a child named `Visual`; several components select the first descendant renderer, animator, collider, or receiver.
- Files: `Assets/Scripts/UI/HudController.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs`, `Assets/Scripts/Combat/MeleeDamageDealer.cs`, `Assets/Prefabs/UI/PlayerHudCanvas.prefab`, `Assets/Prefabs/Combat/SwordSlash.prefab`
- Impact: Routine prefab restructuring can bind a visually valid object to the wrong component or silently leave a feature inactive. The first-child lookup makes a multi-renderer or multi-hitbox asset order-dependent.
- Fix approach: Use local helper components that expose the owned UI/render/collider parts, or serialize intentional local asset references. Validate the expected topology in editor-only checks rather than relying on object names.

## Known Bugs

**The recorded EditMode test invocation exits before producing results:**
- Symptoms: `EditMode.log` records a Unity `-runTests -testPlatform EditMode` process that exits with code `1`, and no `EditModeResults.xml` or `PlayModeResults.xml` is present at the project root.
- Files: `EditMode.log`, `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef`, `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`
- Trigger: Running the captured headless Unity invocation against this project environment encounters a read-only database/licensing startup failure before a test report is written.
- Workaround: Run tests in a Unity installation and workspace with a writable licensing/database location, publish the XML results as CI artifacts, and treat the test suite as unverified until a current result file is available.

**Global target selection has no uniqueness guarantee:**
- Symptoms: Mobs can obtain an arbitrary `MobTargetProvider` through `FindAnyObjectByType`, while every `Weasel` instance includes its own provider that performs a tag-based lookup.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Prefabs/Mobs/Weasel.prefab`
- Trigger: Place multiple mobs with differing target rules, or load a second scene containing a navigation grid/provider; discovery has no selection constraint.
- Workaround: Configure dependencies through `MobController.Configure` at spawn time and avoid global searches for gameplay ownership.

## Security Considerations

**No gameplay network, credential, or file-system access is detected in the scanned scripts:**
- Risk: The current runtime code has no application authentication boundary, persistence layer, or remote-input validation to audit; adding any of these services without a security design would expand the attack surface quickly.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, `Assets/Scripts/Player/PlayerController.cs`, `Packages/manifest.json`
- Current mitigation: Runtime gameplay scripts use local Unity APIs. UnityWebRequest modules are declared in `Packages/manifest.json`, but no source call to UnityWebRequest or other HTTP client is present under `Assets/Scripts` or `Assets/Editor`.
- Recommendations: Before adding online features, define an authenticated transport boundary, validate untrusted payloads, keep secrets outside `Assets`, and add dependency/CVE scanning. This audit does not assess package advisories or platform permissions.

**Git-sourced package dependency lacks an immutable manifest reference:**
- Risk: `com.skner.dualgrid` is declared as a Git URL without a tag or commit fragment in the manifest; the lockfile currently records a resolved commit, but lockfile regeneration can select a different upstream revision.
- Files: `Packages/manifest.json`, `Packages/packages-lock.json`
- Current mitigation: `Packages/packages-lock.json` records hash `493500b6ac59f5713b7d9d9bdff6d6a37016da78` for the resolved package.
- Recommendations: Pin the Git URL in `Packages/manifest.json` to a reviewed commit or a signed release tag, keep the lockfile under review, and document upgrade verification for the tilemap package chain.

## Performance Bottlenecks

**Pathfinding is synchronous, allocation-heavy, and unbounded per request:**
- Problem: A* allocates `List`, `HashSet`, and `Dictionary` instances for every request and finds the next node by linearly scanning the open list. It has no node budget, cancellation, cache, or frame-slicing.
- Files: `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`, `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, `Assets/Scripts/AI/StateMachine/IdleState.cs`, `Assets/Scripts/AI/StateMachine/PatrolRoamState.cs`, `Assets/Scripts/AI/StateMachine/ReturnToSpawnState.cs`
- Cause: `CanReachWorldTarget` executes a full path search during state ticks, and chase requests can rebuild paths at the configured repath interval. `SmoothPathCells` additionally tests line of sight across increasing path spans.
- Improvement path: Replace the list scan with a binary heap/priority queue, impose an expansion budget, reuse collections or pool request state, cache reachability where safe, and schedule path work across frames or jobs. Profile with the intended number of active mobs and map cells before setting a capacity target.

**Navigation grid construction scans complete tilemap bounds and remains static:**
- Problem: `BuildGrid` iterates every cell inside each source tilemap's rectangular `cellBounds` and stores per-cell terrain lists. No runtime tile-change hook rebuilds or incrementally updates the grid.
- Files: `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`, `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scenes/SampleScene.unity`
- Cause: The grid builds in `Awake`, can rebuild from mob initialization, and can also build while drawing editor gizmos. Tilemap changes after construction leave navigation occupancy/cost data stale.
- Improvement path: Build from used tile bounds, track dirty tile regions, rebuild only affected cells, and expose an explicit grid refresh API from tile-editing systems. Disable expensive gizmo rebuilding outside authoring workflows.

**Combat feedback creates and destroys managed objects for every event:**
- Problem: Slash attacks, damage popups, and death animations instantiate or construct GameObjects, create PlayableGraphs, synchronize physics transforms, allocate overlap arrays, and destroy objects after short lifetimes.
- Files: `Assets/Scripts/Combat/SwordSlashAttack.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`, `Assets/Scripts/Combat/MobDeathAnimation.cs`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs`, `Assets/Scripts/Combat/DamageReceiver.cs`
- Cause: `SwordSlashAttack` calls `Physics2D.SyncTransforms` and `Physics2D.OverlapBoxAll` per attack; `FloatingDamageText` performs camera/UI projection in each popup's `LateUpdate`; no object pool is present.
- Improvement path: Pool slash, popup, and death-effect instances; use non-alloc physics queries with an explicit layer mask; centralize popup projection; and reuse animation playback resources. Profile allocation rate and physics cost under simultaneous damage events.

**Pixel-art import defaults force uncompressed RGBA32 on all target platforms:**
- Problem: The asset postprocessor applies uncompressed RGBA32 settings and enables platform overrides for Standalone, WebGL, Android, and iPhone for every sprite under `Assets/Sprites`.
- Files: `Assets/Editor/PixelArtTextureDefaults.cs`, `Assets/Sprites`
- Cause: `SetPlatformCompression` explicitly selects `TextureImporterCompression.Uncompressed` and `TextureImporterFormat.RGBA32`.
- Improvement path: Retain point filtering and mipmap policy, but define platform-specific compression and size budgets. Audit alpha requirements per sprite atlas before applying a compressed format globally.

**Per-frame sorting and camera searches have no workload budget:**
- Problem: Each `YPositionSorter` runs in `LateUpdate`; each active damage popup also runs `LateUpdate` and may resolve `Camera.main`. The HUD retries a player tag lookup each frame until it finds at least one player collaborator.
- Files: `Assets/Scripts/Camera/YPositionSorter.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`, `Assets/Scripts/UI/HudController.cs`
- Cause: The systems are component-local and lack central registration, pooling, or event-driven availability notifications.
- Improvement path: Profile renderer/popup counts, register the active gameplay camera once, batch sort updates where possible, and signal player availability to the HUD rather than polling.

## Fragile Areas

**Mob state and navigation behavior:**
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`, `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`, `Assets/Scripts/AI/StateMachine/ChaseState.cs`
- Why fragile: State transitions combine perception timing, collider-distance attack checks, partial-path semantics, target reachability, grid cell conversion, and fixed-step steering. A missing grid produces a fallback configuration but leaves path-dependent states unable to navigate.
- Safe modification: Preserve the `PathResult` distinction between `Success`, `IsPartial`, `GoalWasAdjusted`, and `ReachedResolvedGoal`; add test cases before changing reachability, grid smoothing, or transition timing. Inject a deterministic pathfinder in tests instead of adding more global scene searches.
- Test coverage: `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`, `Assets/Tests/Editor/MobStateMachineTests.cs`, and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs` cover core current flows, but no capacity, dynamic-tilemap, additive-scene, or multiple-target-provider test is present.

**Health/death/effect lifecycle:**
- Files: `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/Combat/DisableOnDeath.cs`, `Assets/Scripts/Combat/DestroyMobOnDeath.cs`, `Assets/Scripts/Combat/MobDeathAnimation.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`
- Why fragile: Death is event-driven and includes subscription state, component disabling, rigidbody/collider shutdown, object destruction, and separate temporary visual objects. `handledDeath` is one-shot state and is not a reusable/pool-reset contract.
- Safe modification: Keep death ownership on one root component, define reset/respawn behavior before pooling, and test subscriber ordering and deactivation paths whenever death handling changes.
- Test coverage: `Assets/Tests/Editor/CombatComponentTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs` cover basic damage, popup cleanup, and mob death. They do not cover pooling/reset, concurrent subscribers, missing animation templates, or scene changes during effects.

**Tests are tightly coupled to implementation details and local assets:**
- Files: `Assets/Tests/Editor/MobMotor2DTests.cs`, `Assets/Tests/Editor/CombatComponentTests.cs`, `Assets/Tests/Editor/PlayerWeaponSystemTests.cs`, `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`
- Why fragile: Tests reflect private field/method names, load assets through `AssetDatabase`, select scene instances through global lookup, and assert the `SampleScene` composition. Legitimate refactors can fail tests without changing observable behavior, while build-only input behavior remains outside the asserted path.
- Safe modification: Prefer public behavior assertions and test-only builders; reserve reflection for invariant tests that document why the internal shape is contractual. Add an isolated player-build input test, multi-camera/canvas tests, and additive-scene dependency tests.
- Test coverage: The suite contains 38 declared tests across `Assets/Tests/Editor` and `Assets/Tests/PlayMode`; test execution has no current XML result because the recorded batch invocation in `EditMode.log` exits before results are written.

## Scaling Limits

**AI, tilemap, and effect workload:**
- Current capacity: No profiled mob-count, map-cell-count, frame-time, GC-allocation, or memory budget is defined in `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`, `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`, or `Assets/Scripts/Combat/FloatingDamageText.cs`.
- Limit: Large maps multiply eager grid cell scanning; many active mobs multiply synchronous route searches and linecasts; simultaneous hits multiply UI/physics/PlayableGraph allocations.
- Scaling path: Establish representative scenes and performance tests, define budgets for active mobs and cell count, then add bounded pathfinding plus pooled effects before expanding encounter scale.

**Imported sample content dominates repository asset weight:**
- Current capacity: `Assets/TextMesh Pro` contains 356 files totaling about 9.76 MB, including `Assets/TextMesh Pro/Examples & Extras` with 284 files totaling about 5.87 MB. Non-TextMesh-Pro files under `Assets` total about 5.89 MB.
- Limit: The included example package increases import, source-control, and review surface; its nested `Resources` content can also widen build-inclusion review requirements.
- Scaling path: Remove unused TextMesh Pro examples after confirming no asset references, retain only the required fonts/materials, and run a build-size report to verify resource inclusion.

## Dependencies at Risk

**`com.skner.dualgrid`:**
- Risk: The package is sourced directly from Git without a manifest commit/tag pin.
- Impact: Package resolution can change when the lockfile is regenerated, affecting tilemap/grid behavior.
- Migration plan: Pin the reviewed revision in `Packages/manifest.json`, preserve `Packages/packages-lock.json`, and retain only the API surface used by `Assets/Tiles/tilesets/grass_DualGridRuleTile.asset` and `Assets/Tiles/tilesets/grass2touse_DualGridRuleTile.asset`.

**TextMesh Pro example content:**
- Risk: Example scenes/scripts/assets are committed alongside gameplay content and include a TODO marker in an imported example script.
- Impact: The imported sample set inflates the project and makes searches/reviews report third-party/example code as if it were gameplay code.
- Migration plan: Keep the required TextMesh Pro package resources, delete unused `Assets/TextMesh Pro/Examples & Extras` content only after reference/build checks, and exclude imported examples from gameplay quality scans.

## Missing Critical Features

**Build and performance verification pipeline:**
- Problem: Test assembly definitions exist, but no result XML is available and the recorded batch test invocation exits before writing results. No source file defines a build validation, performance scene, allocation budget, or dependency audit task.
- Blocks: Reliable release readiness, regression detection for player-build input behavior, and evidence-based encounter/map scaling.
- Files: `EditMode.log`, `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef`, `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`, `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`

**Explicit gameplay composition boundary:**
- Problem: Scene-wide lookup and tag discovery supply core runtime relationships instead of a context/ownership boundary.
- Blocks: Deterministic additive-scene loading, multiple players, faction-specific targeting, and reliable spawned-mob initialization.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Scripts/Camera/CameraFollow2D.cs`, `Assets/Scripts/UI/HudController.cs`

## Test Coverage Gaps

**Multi-scene and multi-entity wiring:**
- What's not tested: Multiple navigation grids, multiple `MobTargetProvider` instances with distinct targets, player creation after camera enable, additive scene loading, and HUD rebinding after player replacement.
- Files: `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobTargetProvider.cs`, `Assets/Scripts/Camera/CameraFollow2D.cs`, `Assets/Scripts/UI/HudController.cs`, `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`
- Risk: The current single-scene happy path can conceal nondeterministic global discovery failures.
- Priority: High

**Performance and dynamic navigation:**
- What's not tested: Large-grid construction, pathfinding expansion cost, simultaneous mob repaths, path smoothing complexity, runtime tile changes, and line-of-sight/route work under a frame budget.
- Files: `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`, `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs`, `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`
- Risk: Correctness tests on small grids do not reveal frame stalls or stale navigation data in production-scale levels.
- Priority: High

**Build-only input and visual lifecycle behavior:**
- What's not tested: The hard-coded non-Editor input fallback, missing configuration assets, repeated camera/canvas setup, pooled/reset effect lifecycles, and absent slash/death assets.
- Files: `Assets/Scripts/Player/PlayerController.cs`, `Assets/Scripts/Combat/SwordSlashAttack.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`, `Assets/Scripts/Combat/MobDeathAnimation.cs`, `Assets/Tests/Editor/PlayerWeaponSystemTests.cs`
- Risk: Editor asset-based tests can pass while player builds or damaged content configurations behave differently.
- Priority: Medium

---

*Concerns audit: 2026-07-18*
