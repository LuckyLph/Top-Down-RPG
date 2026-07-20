# Coding Conventions

**Analysis Date:** 2026-07-18

## Naming Patterns

**Files:**
- Name each C# source file after its primary public type in PascalCase: `Assets/Scripts/Combat/Health.cs` defines `Health`, `Assets/Scripts/AI/Navigation/GridAStarPathfinder2D.cs` defines `GridAStarPathfinder2D`, and `Assets/Editor/PixelArtEditorSnapSettings.cs` defines `PixelArtEditorSnapSettings`.
- Keep tests in PascalCase with a `Tests` suffix, such as `Assets/Tests/Editor/NavigationGridPathfindingTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`.
- Keep production code grouped by domain under `Assets/Scripts/` and editor-only tooling under `Assets/Editor/`; do not put custom runtime code in Unity package/example folders.

**Functions:**
- Use PascalCase for public methods, properties, events, constructors, and private methods: `ApplyDamage`, `TryGetTraversal`, `ResolveReferences`, and `EnsureInitialized` in `Assets/Scripts/Combat/Health.cs`, `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`, and `Assets/Scripts/AI/Core/MobController.cs`.
- Keep Unity message methods private and PascalCase (`Awake`, `OnEnable`, `Update`, `FixedUpdate`, `OnValidate`, `OnDrawGizmosSelected`), as in `Assets/Scripts/Player/PlayerController.cs`.
- Name boolean-returning query methods with `Is`, `Has`, `Can`, or `Try` when the result represents a condition or lookup, for example `IsCellWalkable`, `HasLineOfSightCells`, and `TryGetNearestWalkableCell` in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Name test methods `Subject_ExpectedBehavior[_Condition]`, for example `AStar_ReturnsPartial_WhenGoalUnreachableAndPartialAllowed` in `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`.

**Variables:**
- Use camelCase for parameters, locals, and private fields (`maxHealth`, `currentHealth`, `movementProfile`, `pathfinder`); keep serialized backing fields private in `Assets/Scripts/Combat/Health.cs` and `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Use PascalCase for constants and static cached values (`PlayerTag`, `DefaultLifetime`, `IsMovingHash`) in `Assets/Scripts/UI/HudController.cs`, `Assets/Scripts/Combat/FloatingDamageText.cs`, and `Assets/Scripts/Player/PlayerController.cs`.
- Use descriptive booleans for state (`initialized`, `subscribedToPlayer`, `drawGridBoundsGizmo`) rather than abbreviated flags.

**Types:**
- Use PascalCase for classes, structs, enums, and nested data types; prefix interfaces with `I`, as in `Assets/Scripts/AI/Core/IMobState.cs` and `Assets/Scripts/AI/Navigation/IPathfinder2D.cs`.
- Use `sealed` for concrete asset or implementation types only where inheritance is deliberately blocked (`PlayerWeapon` in `Assets/Scripts/Combat/PlayerWeapon.cs`, private `CellData` in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`).
- Runtime scripts currently use the global namespace. Keep new types global unless a deliberate, assembly-wide namespace migration changes that convention; do not introduce isolated namespaces.

## Code Style

**Formatting:**
- Use four-space indentation, Allman braces, and blank lines between logical blocks, matching every custom `.cs` file under `Assets/Scripts/` and `Assets/Tests/`.
- The generated Unity projects target C# 9.0 and `netstandard2.1` (`TopDownRPG.Gameplay.csproj` and `TopDownRPG.EditModeTests.csproj`). Target-typed construction (`new(...)`, `new()`) and switch expressions are established patterns in `Assets/Scripts/Combat/DamageReceiver.cs` and `Assets/Scripts/AI/Core/MobController.cs`.
- No committed formatter configuration (`.editorconfig`, `.prettierrc`, or equivalent) is detected. Preserve the surrounding file's whitespace and brace style rather than assuming an automated formatter will correct it.

**Linting:**
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

**Order:**
1. System namespaces when needed (`System`, `System.Collections.Generic`, `System.Reflection`).
2. Third-party Unity package namespaces (`TMPro`, `NUnit.Framework`).
3. Unity namespaces (`UnityEngine`, `UnityEngine.Tilemaps`, `UnityEngine.UI`).
4. Conditional `UnityEditor` imports inside `#if UNITY_EDITOR` blocks.

The order is not enforced uniformly: `Assets/Scripts/Combat/Health.cs` follows system-then-Unity order, while `Assets/Tests/Editor/MobMotor2DTests.cs` places `System.Reflection` last. For new files, use the order above and group contiguous imports without unused directives.

**Path Aliases:**
- Not applicable. Custom code has no C# namespace aliases or `using` aliases; assembly boundaries are defined by `Assets/Scripts/TopDownRPG.Gameplay.asmdef` and the test `.asmdef` files.

## Error Handling

**Patterns:**
- Use guard clauses and safe return values for invalid runtime state. `Assets/Scripts/Combat/Health.cs` returns `0` for invalid/dead damage targets, and `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` returns `false` when traversal cannot be resolved.
- Validate authored values with `[Min]`, `OnValidate`, and defensive clamping (`Mathf.Max`/`Mathf.Clamp`), as in `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Scripts/Combat/Health.cs`, and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.
- Use `Try...` methods with `out` values for recoverable lookup or calculation failure (`TryGetTraversal`, `TryGetNearestWalkableCell`, `TryResolveCellTraversal`); callers branch on the boolean instead of catching exceptions.
- Do not introduce exceptions for ordinary gameplay validation. No custom exception type or intentional `throw` is present in custom scripts or tests. The only `try/catch` in runtime code, `Assets/Scripts/Combat/FloatingDamageText.cs`, protects an optional fallback font creation and degrades to `null`.

## Logging

**Framework:** Unity `Debug` console APIs.

**Patterns:**
- Log actionable missing configuration with `Debug.LogError` or `Debug.LogWarning`, including the relevant Unity context object when available; see `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- Suppress repetitive warnings with a local boolean when the check runs every frame, as `warnedMissingPlayer` and `warnedMissingUiReferences` do in `Assets/Scripts/UI/HudController.cs`.
- Use `Debug.Log` for explicit editor-tool completion/status only, as in `Assets/Editor/PixelArtTextureDefaults.cs` and `Assets/Editor/PixelArtEditorSnapSettings.cs`. No dedicated logging abstraction is detected.

## Comments

**When to Comment:**
- Comment non-obvious decisions, invariants, or engine-specific behavior, not straightforward control flow. Examples explain collider edge distance in `Assets/Scripts/AI/Core/MobController.cs`, waypoint jitter avoidance in `Assets/Scripts/AI/Core/MobPathAgent2D.cs`, and diagonal corner-cut prevention in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.

**JSDoc/TSDoc:**
- Not applicable. No XML documentation comments (`///`) are present in custom runtime, editor, or test scripts. Use concise `//` comments only where the reason is not clear from the code.

## Function Design

**Size:**
- Keep Unity lifecycle methods thin: cache/resolve in `Awake`, subscribe in `OnEnable`, unsubscribe in `OnDisable`/`OnDestroy`, and delegate domain behavior to named helpers. `Assets/Scripts/UI/HudController.cs` and `Assets/Scripts/Player/PlayerController.cs` demonstrate this sequence.
- Decompose repeated dependency setup into `Resolve...`, initialization into `Ensure...`/`Initialize...`, and programmatic test seams into `Configure...`; see `Assets/Scripts/AI/Core/MobController.cs` and `Assets/Scripts/AI/Navigation/NavigationTerrainSource2D.cs`.

**Parameters:**
- Use explicit primitive and Unity-value parameters, optional parameters only for genuine defaults, and `out` parameters for allocation-free lookup results. Examples include `Health.ApplyDamage(int amount, GameObject source = null)` and `NavigationGrid2D.TryGetNearestWalkableCell(...)`.
- Prefer an owned object (`MobController` passed to a state constructor in `Assets/Scripts/AI/StateMachine/MobStateBase.cs`) over broad scene references.

**Return Values:**
- Expose read-only state with expression-bodied properties (`CurrentHealth`, `IsBuilt`, `CurrentStateId`) and return operation results where callers need to branch (`ApplyDamage`, `TryAttack`, `BuildPathToWorld`).
- Use `IReadOnlyList<T>` for externally visible collections, as in `Assets/Scripts/AI/Navigation/PathTypes.cs` and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.

## Module Design

**Exports:**
- Keep one primary public type per file and match it to the file name. Public types are globally visible within the relevant Unity assembly because custom scripts do not declare namespaces.
- Keep ScriptableObject configuration immutable to consumers through read-only properties where practical (`Assets/Scripts/Combat/PlayerWeapon.cs`); retain public fields only for authored data containers such as `MobConfig` and `TerrainMovementProfile2D.TerrainRule`.

**Barrel Files:**
- Not used. Unity discovers scripts directly and compiles runtime code through `Assets/Scripts/TopDownRPG.Gameplay.asmdef`; do not add barrel files.

---

*Convention analysis: 2026-07-18*
