# Phase 1: Runtime Composition & Guardrails - Research

**Researched:** 2026-07-24
**Domain:** Unity single-scene runtime composition, Input System parity, and local build verification
**Confidence:** MEDIUM

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

## Implementation Decisions

### Startup failure policy
- **D-01:** A normal editor or Development Build launch fails closed when required core composition or setup is invalid; it must identify the fault in Unity output rather than run a misleading partial slice.
- **D-02:** Failure visibility is console/log only in Phase 1. A dedicated in-game failure or diagnostics screen is not part of this phase.
- **D-03:** The required launch core is player, navigation, and HUD/camera. Encounter and town contexts are optional until their respective content phases.
- **D-04:** The standard slice never fabricates missing gameplay objects or selects arbitrary global substitutes. Required dependency/configuration gaps fail clearly.

### Pre-content encounter and town contexts
- **D-05:** Encounter and town contexts are valid absences in Phase 1, with reserved runtime extension points rather than empty placeholders or test doubles.
- **D-06:** One scene composition root coordinates future contexts. Each domain retains ownership of its own prefab-local components; the root supplies only cross-context relationships.
- **D-07:** Bootstrap the required core first, then initialize any declared optional contexts using their explicit dependencies.
- **D-08:** Keep one configured `Assets/Scenes/SampleScene.unity` slice. Do not introduce additive content scenes or move the slice to a master-prefab architecture in this phase.

### Development Build input parity
- **D-09:** The required parity surface is existing keyboard-and-mouse play: movement with WASD/arrows plus attack with Space/left mouse. Abilities and the broader device bindings are not Phase 1 gates.
- **D-10:** The smoke path must prove genuine Input System actions reach gameplay: movement changes the player and attack enters the configured action route without directly invoking player/weapon methods.
- **D-11:** A missing Input Actions asset, `Player` map, `Move` action, or `Attack` action is a required-core failure. Do not use legacy polling or a hard-coded fallback.
- **D-12:** The player owns a local input adapter/component that translates devices to actions. The composition root verifies and initializes that player-owned input contract.

### Verification evidence
- **D-13:** Local verification requires focused EditMode checks, focused PlayMode checks, and a Development Build smoke check; each produces an explicit pass/fail result.
- **D-14:** Provide one documented local verification command or entry point that runs all three verification channels and reports the overall outcome and output paths.
- **D-15:** Preserve a console summary plus predictable, ignored local test reports and smoke logs. Do not commit run evidence.
- **D-16:** Run independent verification steps to completion, collect all failures, and fail the overall command if any step fails.

### Cross-phase delivery workflow
- **D-17:** Use one Git branch per phase. Phase 1 branches from `main`; every later phase branches from the immediately preceding phase branch.

### the agent's Discretion

No implementation decisions were delegated without a preference. Planners may choose the smallest architecture that satisfies the locked behaviors and project conventions.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within the phase scope. Encounter and town gameplay, additive loading, broader device controls, action abilities, diagnostics UI, and persistent/committed verification evidence are intentionally outside Phase 1.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RUNTIME-01 | Developer can start the vertical slice with a composition root that initializes the player, navigation, HUD/camera, encounters, and town runtime through explicit runtime ownership/injection rather than serialized cross-scene component links. | A gated `SliceCompositionRoot`, explicit core contract, and optional-context interface preserve prefab-local ownership while replacing global discovery. [VERIFIED: codebase grep] |
| RUNTIME-04 | Developer can run focused Unity EditMode and PlayMode verification for the foundation contracts and reproduce a local development-build smoke check without relying on editor-only input fallbacks. | Use the installed Unity Test Framework for isolated EditMode/PlayMode reports, plus a Development Build smoke sentinel driven only by real Input System action flow. [CITED: https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html] |
</phase_requirements>

## Project Constraints (from AGENTS.md)

- Keep Unity `6000.4.2f1`, URP, C#, the Input System, 2D Tilemaps, and the existing test assemblies; Unity/UPM remains the dependency authority.
- Use runtime discovery only inside an ownership boundary, ownership-based lookup, or local helper components for dynamic scene relationships. Do not add serialized component links merely to connect scene objects; serialize configuration values, asset references, and intentional overrides instead.
- Preserve the small, revisable local Windows slice; Unity MCP is loopback, editor-only assistance and never a runtime dependency.
- Use the established global namespace, one primary PascalCase public type per file, four-space Allman C# style, private Unity lifecycle methods, and `camelCase` private fields/parameters.
- Keep lifecycle methods thin: cache owner-local references in `Awake`, subscribe in `OnEnable`, unsubscribe in `OnDisable`/`OnDestroy`, and delegate setup to named `Resolve...`/`Initialize...`/`Configure...` methods.
- Require co-located components with `[RequireComponent]` where needed; retain inspector validation for authored configuration and keep `UnityEditor` code behind `#if UNITY_EDITOR` or in `Assets/Editor/`.
- Use guard clauses, `Try...`/result contracts, and actionable `Debug.LogError`/`Debug.LogWarning` messages with a context object; do not use exceptions for ordinary gameplay validation.
- Extend the existing NUnit-based Unity EditMode and PlayMode assemblies with real minimal GameObject graphs, private fixture builders, bounded `UnityTest` waits, observable assertions, and cleanup in `TearDown`.
- After any Unity MCP-assisted change, wait for compilation/domain reload and inspect console errors before scene/prefab edits or test runs.

## Summary

Phase 1 should introduce one active `SliceCompositionRoot` in `SampleScene` and make its owned runtime subtree quiescent until the root validates, wires, and explicitly initializes the required player, navigation, HUD, and camera contracts. The current player, HUD, camera, mob, and navigation paths use `AssetDatabase`/fallback actions or broad tag/global searches, while `MobController.Configure(...)`, `CameraFollow2D.SetTarget(...)`, and `MobTargetProvider.SetTarget(...)` already demonstrate useful explicit-injection seams. [VERIFIED: codebase grep]

Do not replace those components with a service framework or serialize scene-object references into them. Refactor each consumer so `Awake` caches only prefab-local collaborators and cross-context behavior begins only after root-owned initialization; Unity documents that `Awake` order between scene objects is nondeterministic, while all `Awake` calls precede `Start`. [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]

The verification deliverable is a Windows PowerShell entry point that completes independent EditMode, PlayMode, and Development Build smoke steps, retains all result paths, and exits nonzero if any channel fails. Build output belongs under the already ignored `Builds/` tree, while a new ignored test-results directory is necessary because the current `.gitignore` ignores logs and builds but not XML test reports. [VERIFIED: codebase grep]

**Primary recommendation:** Use an owned, inactive runtime subtree plus `SliceCompositionRoot` validation/injection/activation; make the serialized `InputActionAsset` a player-local configuration requirement and remove the Editor-only and code-created input fallbacks. [VERIFIED: codebase grep]

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|--------------|----------------|-----------|
| Required-core validation and ordered activation | Unity scene runtime composition root | Prefab-local components | The root owns only cross-context links and leaves the runtime subtree inactive on any core failure. [VERIFIED: codebase grep] |
| Player input translation | Player prefab-local input adapter | `PlayerController` / weapon controller | The player owns action lookup and action-to-gameplay translation; the root only validates and starts that contract. [CITED: https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html] |
| Navigation grid build | Navigation-owned grid hierarchy | Composition root | `NavigationGrid2D` owns terrain discovery and pathfinder construction; the root decides when a valid grid is required before dependent contexts run. [VERIFIED: codebase grep] |
| HUD player binding | HUD prefab-local controller/view helper | Composition root | The HUD should resolve only its own view hierarchy and receive the intended player health/weapon collaborators explicitly. [VERIFIED: codebase grep] |
| Camera follow target | Camera component | Composition root | `CameraFollow2D.SetTarget` already supplies an explicit cross-context binding seam. [VERIFIED: codebase grep] |
| Encounter and town startup | Future domain-owned context component | Composition root | The root invokes declared optional contexts only after the required core succeeds; absent contexts are valid and need no dummy object. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md] |
| Test, build, and smoke orchestration | Editor/PowerShell verification entry point | Unity Test Framework / Development Player | Unity provides distinct test-platform and result-path controls; the wrapper owns aggregation and artifact locations. [CITED: https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html] |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Unity Editor | `6000.4.2f1` | Scene composition, player build, and local editor execution | This is the project-pinned editor version and the existing slice runtime. [VERIFIED: codebase grep] |
| Unity Input System | `com.unity.inputsystem` `1.19.0` | Authored Player action map and player-local action adapter | An `.inputactions` asset is the established asset format for named action maps/actions; enabled maps/actions receive device input. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html] |
| Unity Test Framework | `com.unity.test-framework` `1.6.0` | Focused EditMode and PlayMode contract checks | The project already has separate test assemblies and the framework supports distinct test platforms and result-file paths. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html] |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| NUnit | Unity Test Framework-provided | Assertions and fixtures in existing Unity test assemblies | Extend the existing real-GameObject test pattern; do not introduce a mocking package. [VERIFIED: codebase grep] |
| Windows PowerShell | `5.1.26100.8875` available | One local command that invokes independent Unity processes and summarizes results | Use for the Windows-only orchestration wrapper; make Unity path an explicit parameter with the detected Unity Hub path as the documented default. [VERIFIED: environment probe] |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Player-local `InputActionAsset` adapter | `PlayerInput` | `PlayerInput` is valid Input System infrastructure, but adopting it would replace the existing player input route rather than make the smallest Phase 1 correction. Retain the existing controller/weapon shape and add a focused local adapter. [VERIFIED: codebase grep] |
| Owned-subtree composition root | A global service locator or `FindAnyObjectByType` fallback | Global discovery preserves the present ambiguity with multiple grids/providers and violates the fail-closed requirement. [VERIFIED: codebase grep] |
| Unity Test Framework reports plus player smoke sentinel | A third-party test/build framework | No external package is needed; Unity already owns the project test assemblies, player build API, and Windows player logs. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/kr/current/ScriptReference/BuildOptions.html] |

**Installation:** No package installation is recommended for this phase; use the locked Unity/UPM stack already in `Packages/manifest.json`. [VERIFIED: codebase grep]

**Version verification:** Unity `6000.4.2f1`, Input System `1.19.0`, and Test Framework `1.6.0` were verified in `ProjectSettings/ProjectVersion.txt` and `Packages/manifest.json`; npm/PyPI/cargo verification and package publish dates do not apply to this UPM-only, no-install phase. [VERIFIED: codebase grep]

## Package Legitimacy Audit

Not applicable: Phase 1 should not install external packages, so no Package Legitimacy Gate or registry lookup is required. [VERIFIED: codebase grep]

## Architecture Patterns

### System Architecture Diagram

```text
SampleScene loads
       |
       v
SliceCompositionRoot (active, scene-owned)
       |
       +--> discover exactly one owned Player / Navigation / HUD / Camera
       |          |
       |          +--> validate input asset -> Player map -> Move + Attack
       |          +--> validate prefab-local contracts and navigation sources
       |
       +-- failure --> Debug.LogError(context) --> Runtime subtree remains inactive
       |
       +-- success --> inject cross-context links
                         |             |             |
                         v             v             v
                    Player input   HUD.Bind(...)  Camera.SetTarget(...)
                         |
                         +--> Navigation.Build / optional encounter contexts
                                                        |
                                                        +--> declared town context, if present
```

The scene currently contains a navigation grid and camera while player, HUD, and mobs are prefab instances; existing consumers use global discovery and must be redirected through the root-owned path above. [VERIFIED: codebase grep]

### Recommended Project Structure

```text
Assets/
├── Scripts/
│   ├── Runtime/
│   │   ├── SliceCompositionRoot.cs       # required-core validation, wiring, and activation
│   │   └── ISliceOptionalContext.cs      # future-only extension contract; no dummy contexts
│   ├── Player/
│   │   └── PlayerInputAdapter.cs         # player-local Input System action contract
│   └── UI/
│       └── PlayerHudView.cs              # optional owned-child view lookup helper
├── Editor/
│   └── Verification/
│       └── SliceVerificationCommands.cs  # Development Build creation only
└── Tests/
    ├── Editor/RuntimeCompositionEditModeTests.cs
    └── PlayMode/RuntimeCompositionPlayModeTests.cs
scripts/
└── Invoke-SliceVerification.ps1          # runs all channels and aggregates outcomes
TestResults/                              # ignored local NUnit XML artifacts
Builds/Phase01/                           # ignored Development Build and smoke logs
```

New types must remain in the global namespace, match their PascalCase file names, and follow the existing domain-folder assembly layout. [VERIFIED: AGENTS.md]

### Pattern 1: Quiescent Runtime Subtree and Explicit Bootstrap

**What:** Keep the composition root active but hold its owned runtime children inactive (or behind an equivalent activation gate). Resolve/validate unique core collaborators through the root's hierarchy, set explicit cross-context references, then activate and initialize in the order navigation -> player input -> HUD/camera -> declared optional contexts. [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]

**When to use:** Use for the one `SampleScene` bootstrap because a root `Start` method alone does not make the existing component `Awake`/`OnEnable` behavior safe; Unity does not define an inter-object `Awake` order. [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]

**Example:**

```csharp
// Source: Unity Awake lifecycle guidance plus the project's ownership convention.
public sealed class SliceCompositionRoot : MonoBehaviour
{
    [SerializeField] private GameObject runtimeRoot; // Intentional activation/configuration override.

    private void Start()
    {
        if (!TryResolveRequiredCore(out SliceCore core, out string error))
        {
            Debug.LogError(error, this);
            return; // Leave runtimeRoot inactive: no partial slice.
        }

        core.Camera.SetTarget(core.Player.transform);
        core.Hud.Bind(core.Player.Health, core.Player.WeaponController);

        if (!core.Player.Input.TryInitialize(out error) || !core.Navigation.TryBuild(out error))
        {
            Debug.LogError(error, this);
            return;
        }

        runtimeRoot.SetActive(true);
        InitializeDeclaredOptionalContexts(core);
    }
}
```

The implementation should use local helpers/owner-scoped discovery for `SliceCore` rather than serialized fields from the root to player, grid, HUD, or camera objects. [VERIFIED: AGENTS.md]

### Pattern 2: Player-Owned, Build-Safe Input Contract

**What:** Put the `InputActionAsset` asset reference and exact required action identifiers on a player-local adapter. The adapter resolves `Player`, `Move`, and `Attack`, enables/disables the selected map, and emits movement/attack events to the existing player controller; it returns a descriptive failure instead of creating bindings. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html]

**When to use:** Use for all gameplay input in Phase 1, both editor and Development Player. The current `AssetDatabase` lookup is compiled only in the editor and the current hard-coded fallback duplicates bindings outside the authored asset. [VERIFIED: codebase grep]

**Example:**

```csharp
// Source: InputActionAsset action-map and enable/disable APIs.
public bool TryInitialize(out string error)
{
    InputActionMap playerMap = inputActionsAsset != null
        ? inputActionsAsset.FindActionMap("Player", throwIfNotFound: false)
        : null;

    moveAction = playerMap?.FindAction("Move", throwIfNotFound: false);
    attackAction = playerMap?.FindAction("Attack", throwIfNotFound: false);
    if (moveAction == null || attackAction == null)
    {
        error = "Player input requires InputSystem_Actions: Player/Move and Player/Attack.";
        return false;
    }

    playerMap.Enable();
    error = null;
    return true;
}
```

Keep a Development-only smoke observer separate from gameplay outcome code: it may report that the adapter received move/attack actions and that movement/attack consequences occurred, but it must not call `PlayerController` or `PlayerWeaponController` methods to manufacture a pass. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

### Pattern 3: Optional Context Extension Contract

**What:** Define a minimal `ISliceOptionalContext.Initialize(SliceCore core)` contract and have the root enumerate only real, owned optional-context components after core initialization. Do not add empty encounter/town GameObjects or fake implementations. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

**When to use:** Use only when a future encounter/town domain has a real runtime owner. Phase 1 succeeds when none are declared. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

### Anti-Patterns to Avoid

- **A composition root that calls `FindAnyObjectByType`:** This merely centralizes the current nondeterministic selection and still accepts arbitrary substitutes. Discover only within the root's ownership boundary and reject duplicates/missing required roles. [VERIFIED: codebase grep]
- **Cross-scene serialized component wiring:** It makes scene composition fragile and contradicts the project architecture rule; serialize only the runtime gate/configuration and asset references. [VERIFIED: AGENTS.md]
- **Lifecycle work before composition:** `Awake`/`OnEnable` must not globally find or start cross-context work before the root has validated it. [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]
- **Fallback gameplay fabrication:** Remove the editor-only input load/code-created action map and fail clearly instead; also prevent required-core missing configs/prefabs from silently producing runtime substitutes. [VERIFIED: codebase grep]
- **Adding future-content placeholders:** Optional seams are interfaces/registration points, not Phase 1 encounter or town objects. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Keyboard/mouse binding definitions | A second hard-coded `InputAction` map | The existing `Assets/InputSystem_Actions.inputactions` asset and Input System map/action APIs | The authored asset already holds WASD/arrows and Space/left mouse bindings; a second map creates editor/build drift. [VERIFIED: codebase grep] |
| Action processing | Legacy polling or direct smoke calls into gameplay methods | Player-local Input System adapter and real action callbacks/reads | Enabled Input System actions are the required device-to-gameplay route. [CITED: https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionMap.html] |
| Cross-context locator | A singleton/service locator/global `Find...` fallback | Root-owned discovery plus explicit `Configure`/`Bind`/`SetTarget` calls | Existing `Configure` and `SetTarget` seams already fit the project convention without a framework replacement. [VERIFIED: codebase grep] |
| Test result format/runner | A custom test framework or hand-parsed console-only test outcome | Unity Test Framework with `-runTests`, `-testPlatform`, and `-testResults` | Unity supports separate platform runs and NUnit-format result paths. [CITED: https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html] |
| Development Player build | A hand-assembled player output | `BuildPipeline.BuildPlayer` with `BuildOptions.Development` and `BuildReport` | Unity reports build success/failure and can create a Development Player without another package. [CITED: https://docs.unity3d.com/kr/current/ScriptReference/BuildPipeline.BuildPlayer.html] [CITED: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/BuildOptions.Development.html] |

**Key insight:** The phase needs a narrow ownership boundary, not a global dependency container. The root coordinates dynamic relationships while each prefab continues to own its local components and authored data. [VERIFIED: AGENTS.md]

## Common Pitfalls

### Pitfall 1: Replacing One Startup Race with Another

**What goes wrong:** The root injects in `Start`, but active player/HUD/mob components have already performed discovery in `Awake` or `OnEnable`. [VERIFIED: codebase grep]

**Why it happens:** Unity does not guarantee which scene object's `Awake` runs first, and all `Awake` methods complete before `Start`. [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]

**How to avoid:** Make pre-composition lifecycle work local-only, place consumers behind the root's activation gate, and give cross-context components idempotent explicit initialization methods. [VERIFIED: AGENTS.md]

**Warning signs:** A test passes only when objects are enabled in a particular Inspector order, or a consumer logs/tag-searches before the root reports bootstrap success. [VERIFIED: codebase grep]

### Pitfall 2: Logging an Error but Continuing a Partial Slice

**What goes wrong:** A missing action, navigation source, player health, HUD, or camera logs a warning but the remaining scene still runs. [VERIFIED: codebase grep]

**Why it happens:** Current HUD and navigation paths frequently warn/return, while current player/mob combat code can fabricate fallback runtime objects. [VERIFIED: codebase grep]

**How to avoid:** Treat every required-core validation failure as one root result, log it with the offending component, and keep the owned runtime subtree inactive. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

**Warning signs:** A Development Build opens with no movement/action or an unbound HUD after an error instead of terminating the gameplay bootstrap. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

### Pitfall 3: Testing the Weapon Instead of the Input Route

**What goes wrong:** Tests invoke `TryAttack` or mutate player state directly, so the editor/build input split remains untested. [VERIFIED: codebase grep]

**Why it happens:** Existing tests exercise player weapon behavior and inspect the authored Space binding, but do not prove a Development Player's `InputActionAsset` route. [VERIFIED: codebase grep]

**How to avoid:** Add an adapter-observable action-route contract in PlayMode and require the interactive Development Build smoke to observe both physical movement and an attack action before emitting its pass sentinel. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

**Warning signs:** A smoke pass can occur without keyboard/mouse input, or deleting `Player/Attack` does not fail root validation. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

### Pitfall 4: Short-Circuiting Verification After the First Failure

**What goes wrong:** A script stops after EditMode fails, hiding PlayMode/build failures and violating the all-failures requirement. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

**Why it happens:** The current project has no repository-provided runner, and a recorded batch EditMode run previously exited before producing XML results. [VERIFIED: codebase grep]

**How to avoid:** Run all three subprocesses independently, record each exit code/report/log path, summarize all outcomes, then set the wrapper's final nonzero exit code if any failed. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

**Warning signs:** Only one report exists after a failed run, or a zero exit code is emitted when a result file/log sentinel is missing. [VERIFIED: codebase grep]

## Code Examples

Verified patterns from official sources and the existing codebase:

### Build a Development Player and inspect `BuildReport`

```csharp
// Source: https://docs.unity3d.com/kr/current/ScriptReference/BuildPipeline.BuildPlayer.html
BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
{
    scenes = new[] { "Assets/Scenes/SampleScene.unity" },
    locationPathName = outputPath,
    target = BuildTarget.StandaloneWindows64,
    options = BuildOptions.Development | BuildOptions.StrictMode
});

if (report.summary.result != BuildResult.Succeeded)
{
    throw new BuildFailedException("Phase 1 Development Build did not succeed.");
}
```

The exception is appropriate in an editor build command to make the build process fail; gameplay runtime validation should keep using guarded results and `Debug.LogError`. [VERIFIED: AGENTS.md]

### Aggregate independent verification processes

```powershell
# Source: Unity Test Framework command-line options and Phase 1 D-16.
$outcomes = @()
$outcomes += Invoke-UnityTestRun -Platform EditMode -ResultsPath $editResults
$outcomes += Invoke-UnityTestRun -Platform PlayMode -ResultsPath $playResults
$outcomes += Invoke-DevelopmentBuildSmoke -BuildPath $buildPath -LogPath $smokeLog

$outcomes | Format-Table Name, Passed, EvidencePath
if ($outcomes.Passed -contains $false) {
    exit 1
}
```

Each helper must wait for its own process, capture a missing report/log as failure, and never use `&&` or an early `exit` before all channels have run. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Editor-only `AssetDatabase` load plus code-created InputActions | Player-local authored `InputActionAsset` configuration validated by the composition root | Phase 1 | Editor and Development Player exercise the same asset-defined controls. [VERIFIED: codebase grep] |
| `FindAnyObjectByType`/`FindGameObjectWithTag` for required cross-context links | Root-owned discovery and explicit injection into existing `Configure`/`Bind`/`SetTarget` seams | Phase 1 | Missing/duplicate core dependencies become explicit startup errors rather than arbitrary selections. [VERIFIED: codebase grep] |
| Manual/partially captured test execution | Independent Unity Test Framework reports plus Development Player smoke log aggregation | Phase 1 | Verification has reproducible local evidence and an overall pass/fail result. [CITED: https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html] |

**Deprecated/outdated:**

- Editor-only input asset loading and automatic hard-coded fallback bindings: remove them from normal player startup because they produce different input definitions in editor and builds. [VERIFIED: codebase grep]
- Required cross-context tag/global discovery: remove it from the standard slice startup path because it cannot prove ownership or uniqueness. [VERIFIED: codebase grep]

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `[ASSUMED]` The Unity Test Framework `1.6.0` CLI accepts the same `-runTests`, `-testPlatform`, and `-testResults` behavior documented by Unity's current package reference. | Validation Architecture | The wrapper's test invocations could fail before writing XML; Wave 0 must prove the exact local command before relying on it. |

## Open Questions

1. **Can the installed Unity editor produce both test XML reports in this workspace?**
   - What we know: Unity `6000.4.2f1` is installed, but the repository concern audit records a previous headless test run failing before results due to licensing/read-only database state. [VERIFIED: environment probe] [VERIFIED: codebase grep]
   - What's unclear: Whether the local editor's current license/database state permits the intended batch invocations. [VERIFIED: codebase grep]
   - Recommendation: Make the first Wave 0 verification task run the exact commands and retain the generated XML/log paths; repair the local Unity environment before treating later tests as evidence. [ASSUMED]

2. **Which development-only smoke presentation best fits this local workflow?**
   - What we know: Phase 1 forbids a diagnostics UI but requires console/log-only pass/fail evidence after real keyboard/mouse actions. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]
   - What's unclear: Whether the developer prefers a short interactive prompt documented in the command output or a Development-only console instruction before acting. [ASSUMED]
   - Recommendation: Use a log-only `SMOKE-PASS` sentinel after the player-owned adapter reports movement and attack consequences; no new UI is needed. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|-------------|-----------|---------|----------|
| Unity Editor at `C:\Program Files\Unity\Hub\Editor\6000.4.2f1\Editor\Unity.exe` | Test execution and Development Build | ✓ | `6000.4.2f1` | Unity Hub-installed path can be passed explicitly to the wrapper. [VERIFIED: environment probe] |
| Unity Input System package | Player input contract | ✓ | `1.19.0` | None; it is a locked required-core dependency. [VERIFIED: codebase grep] |
| Unity Test Framework package | Focused test reports | ✓ | `1.6.0` | Unity Test Runner UI for local diagnosis only; the required reproducible command still needs batch proof. [VERIFIED: codebase grep] |
| Windows PowerShell | One verification entry point | ✓ | `5.1.26100.8875` | None needed for the Windows-local project. [VERIFIED: environment probe] |
| Interactive keyboard/mouse desktop | Development Build smoke action proof | Requires developer interaction | — | No automated fallback is required; the smoke runner must time out and fail without the required physical actions. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md] |

**Missing dependencies with no fallback:** None detected; test-runner license/database writability remains a Wave 0 proof obligation rather than an absent executable. [VERIFIED: environment probe] [VERIFIED: codebase grep]

**Missing dependencies with fallback:** None detected. [VERIFIED: environment probe]

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | Unity Test Framework `1.6.0` with NUnit. [VERIFIED: codebase grep] |
| Config file | `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`. [VERIFIED: codebase grep] |
| Quick run command | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode` — Wave 0 creates this wrapper. [ASSUMED] |
| Full suite command | `./scripts/Invoke-SliceVerification.ps1 -Mode All` — runs EditMode, PlayMode, and Development Build smoke independently. [ASSUMED] |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| RUNTIME-01 | Required core resolves uniquely, injects player/nav/HUD/camera links, and refuses missing/duplicate core roles without activating gameplay. | EditMode | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode -TestFilter RuntimeCompositionEditModeTests` | ❌ Wave 0 |
| RUNTIME-01 | `SampleScene` root initializes core in order and allows absent optional contexts without a placeholder. | PlayMode | `./scripts/Invoke-SliceVerification.ps1 -Mode PlayMode -TestFilter RuntimeCompositionPlayModeTests` | ❌ Wave 0 |
| RUNTIME-04 | Missing input asset/map/action fails the player input contract; authored `Player/Move` and `Player/Attack` bindings remain required. | EditMode | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode -TestFilter PlayerInputAdapterTests` | ❌ Wave 0 |
| RUNTIME-04 | The Input System route changes player movement and reports attack routing without a direct player/weapon method call. | PlayMode | `./scripts/Invoke-SliceVerification.ps1 -Mode PlayMode -TestFilter RuntimeCompositionPlayModeTests` | ❌ Wave 0 |
| RUNTIME-04 | Windows Development Player produces a build result plus log-only smoke pass after real movement and attack actions. | Interactive smoke | `./scripts/Invoke-SliceVerification.ps1 -Mode Smoke` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** Run the relevant focused EditMode or PlayMode filter, then inspect the generated result file. [VERIFIED: codebase grep]
- **Per wave merge:** Run `./scripts/Invoke-SliceVerification.ps1 -Mode All` and retain every local evidence path. [ASSUMED]
- **Phase gate:** All three channels pass, reports/log sentinel exist, and the overall wrapper exits zero before `$gsd-verify-work`. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md]

### Wave 0 Gaps

- [ ] `Assets/Tests/Editor/RuntimeCompositionEditModeTests.cs` — validates required-core, duplicate, input-contract, and fail-closed behavior for RUNTIME-01/RUNTIME-04.
- [ ] `Assets/Tests/PlayMode/RuntimeCompositionPlayModeTests.cs` — loads `SampleScene`, asserts explicit injected bindings, absent optional contexts, and Input System route behavior.
- [ ] `Assets/Editor/Verification/SliceVerificationCommands.cs` — builds the Development Player and checks `BuildReport`.
- [ ] `scripts/Invoke-SliceVerification.ps1` — independently runs tests/build/smoke and aggregates evidence.
- [ ] `.gitignore` entries for `TestResults/` and any chosen smoke artifact subdirectory not already beneath `Builds/`.
- [ ] Local batch-mode proof for the exact Unity Test Framework `1.6.0` flags and writable license/database state.

## Security Domain

Security enforcement is enabled in `.planning/config.json`; this local, offline gameplay phase has no authentication, session, authorization, network, credential, or persistence boundary in the scanned runtime code. [VERIFIED: codebase grep]

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|------------------|
| V2 Authentication | No | No identity boundary exists in Phase 1. [VERIFIED: codebase grep] |
| V3 Session Management | No | No user session/persistence exists in Phase 1. [VERIFIED: codebase grep] |
| V4 Access Control | No | Local single-player scene has no authorization boundary. [VERIFIED: codebase grep] |
| V5 Input Validation | Yes | Validate the local input asset, required map/actions, unique core roles, and build-command parameters; fail closed without creating substitutes. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md] |
| V6 Cryptography | No | No secrets, transport, or cryptographic data is introduced. [VERIFIED: codebase grep] |

### Known Threat Patterns for This Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Edited/missing `InputActionAsset` or required action | Tampering | Validate the asset/map/action names before activation, report the component context, and keep the runtime subtree inactive. [VERIFIED: .planning/phases/01-runtime-composition-guardrails/01-CONTEXT.md] |
| Duplicate or arbitrary scene actor chosen by global lookup | Spoofing / Tampering | Resolve only under the root-owned hierarchy and reject zero/multiple required candidates. [VERIFIED: codebase grep] |
| Wrapper writes outside local artifact paths | Tampering | Validate/quote the explicit Unity path and keep generated reports/builds below ignored project-owned output directories. [ASSUMED] |

## Sources

### Primary (HIGH confidence)

- None. The research seam routed documentation questions to Context7, but Context7 was unavailable in this runtime; its CLI fallback (`ctx7`) was also unavailable. [VERIFIED: environment probe]

### Secondary (MEDIUM confidence)

- [Unity 6 `MonoBehaviour.Awake`](https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html) - nondeterministic inter-object `Awake` ordering and `Start` sequencing.
- [Unity Input System `InputActionAsset`](https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html) and [InputActionMap](https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionMap.html) - asset/map/action lookup and enable/disable semantics.
- [Unity Test Framework command-line reference](https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/reference-command-line.html) - `runTests`, `testPlatform`, and `testResults`; exact 1.6 compatibility is logged as A1.
- [Unity `BuildPipeline.BuildPlayer`](https://docs.unity3d.com/kr/current/ScriptReference/BuildPipeline.BuildPlayer.html), [BuildOptions](https://docs.unity3d.com/kr/current/ScriptReference/BuildOptions.html), and [log-file reference](https://docs.unity3d.com/6000.0/Documentation/Manual/LogFiles.html) - build reports, Development Builds, and Windows player log location.

### Tertiary (LOW confidence)

- None. All external claims used in recommendations were cross-checked against Unity documentation and marked as MEDIUM because the available provider was WebSearch rather than Context7. [VERIFIED: research confidence seam]

## Metadata

**Confidence breakdown:**

- Standard stack: MEDIUM - versions and current project usage are verified locally; Input/Test API documentation was retrieved from official Unity pages through WebSearch. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html]
- Architecture: MEDIUM - the recommendation follows verified project seams and Unity's lifecycle documentation; exact refactor shape remains the planner's implementation choice. [VERIFIED: codebase grep] [CITED: https://docs.unity3d.com/ja/6000.0/ScriptReference/MonoBehaviour.Awake.html]
- Pitfalls: MEDIUM - current fallbacks/global discovery and prior test-run failure are verified internally; test CLI version compatibility needs Wave 0 proof. [VERIFIED: codebase grep] [ASSUMED]

**Research date:** 2026-07-24
**Valid until:** 2026-08-23 for project patterns; re-check Unity Test Framework CLI syntax before implementation because the local package version differs from the indexed command-line page. [ASSUMED]
