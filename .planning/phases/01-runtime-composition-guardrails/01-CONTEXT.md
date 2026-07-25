# Phase 1: Runtime Composition & Guardrails - Context

**Gathered:** 2026-07-24
**Status:** Ready for planning

<domain>
## Phase Boundary

Launch the existing `SampleScene` as a build-safe local slice whose required player, navigation, HUD, camera, and input relationships are explicitly composed at runtime. Restore reproducible local verification through focused Unity tests and a Development Build smoke check. This phase establishes extension seams for future encounter and town contexts; it does not add their gameplay, introduce additive scene loading, or replace the existing component/prefab foundation.

</domain>

<decisions>
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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Scope and project constraints
- `.planning/ROADMAP.md` — Phase 1 goal, dependencies, and the three success criteria.
- `.planning/REQUIREMENTS.md` — `RUNTIME-01` and `RUNTIME-04` define the required runtime-composition and verification outcomes.
- `.planning/PROJECT.md` — project core value, local-only constraints, and the requirement to extend rather than replace the current foundation.
- `.planning/STATE.md` — accumulated project decisions and known Phase 1 concerns.
- `AGENTS.md` — Unity architecture rule: dynamic relationships use runtime discovery/ownership/local helpers; serialized fields are for configuration, assets, and intentional overrides.

### Existing architecture and verification baseline
- `.planning/codebase/ARCHITECTURE.md` — current scene/prefab topology, data flows, initialization patterns, and composition integration points.
- `.planning/codebase/TESTING.md` — existing EditMode and PlayMode test structure and local test-running conventions.
- `.planning/codebase/CONCERNS.md` — known global-discovery, fallback, test-evidence, and Development Build input risks that this phase must address.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `Assets/Scripts/AI/Core/MobController.cs`: exposes owner-local initialization and `Configure(...)`-style seams that can inform explicit composition without new serialized scene links.
- `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`: current scene navigation service owned by the grid hierarchy.
- `Assets/Scripts/UI/HudController.cs` and `Assets/Scripts/Camera/CameraFollow2D.cs`: presentation components that currently discover gameplay collaborators and need an explicit runtime ownership path.
- `Assets/InputSystem_Actions.inputactions`: existing `Player/Move` and `Player/Attack` action definitions, including the keyboard-and-mouse bindings selected for the Phase 1 smoke contract.
- `Assets/Tests/Editor/` and `Assets/Tests/PlayMode/`: established Unity Test Framework assemblies to extend with focused foundation contracts.

### Established Patterns
- Keep Unity lifecycle methods thin; resolve/cache local dependencies in `Awake`, subscribe in `OnEnable`, and use named initialization helpers.
- Prefer co-located component lookup, owned-child discovery, and explicit runtime configuration over serialized references that merely wire scene objects together.
- ScriptableObjects and serialized fields remain appropriate for authored assets/configuration, not dynamic cross-context links.
- Unity MCP is a local editor-assistance tool only; it is not a runtime dependency.

### Integration Points
- `Assets/Scenes/SampleScene.unity` is the sole enabled build scene and the Phase 1 composition-root home.
- `Assets/Scripts/Player/PlayerController.cs` and `Assets/Scripts/Player/PlayerWeaponController.cs` form the player-facing endpoint for the Input System parity proof.
- `ProjectSettings/EditorBuildSettings.asset` and the Unity test assemblies are the build/test integration boundary for the reproducible local verification entry point.

</code_context>

<specifics>
## Specific Ideas

- Phase 1 should establish a trustworthy, single-scene core rather than simulate future encounter/town content.
- Startup problems should be obvious in Unity output but should not introduce a new diagnostics UI ahead of Phase 2.
- Local verification artifacts belong in a predictable ignored directory, preserving the working tree while making failures reviewable.
- Branch lineage is deliberate: `main` → Phase 1 branch → Phase 2 branch → subsequent phase branches.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within the phase scope. Encounter and town gameplay, additive loading, broader device controls, action abilities, diagnostics UI, and persistent/committed verification evidence are intentionally outside Phase 1.

</deferred>

---

*Phase: 1-Runtime Composition & Guardrails*
*Context gathered: 2026-07-24*
