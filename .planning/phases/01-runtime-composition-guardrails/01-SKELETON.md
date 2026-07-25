# Walking Skeleton — Top-Down-RPG

**Phase:** 1
**Generated:** 2026-07-24

## Capability Proven End-to-End

A developer can launch the single local Unity slice, where a scene-owned composition root validates and activates player input, navigation, HUD, and camera relationships, then prove keyboard/mouse parity in a Development Build.

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime framework | Unity 6000.4.2f1, URP, C#, existing prefab/component/ScriptableObject foundation | The project is already a Unity local game slice; Phase 1 extends its established platform rather than introducing a runtime framework. |
| Scene composition | One `SliceCompositionRoot` owns discovery within a single inactive runtime subtree and supplies explicit cross-context injection | Satisfies D-01, D-03, D-04, D-06, and D-07 without serialized scene-object links or arbitrary global lookup. |
| Player input | Player-local `PlayerInputAdapter` configured with `Assets/InputSystem_Actions.inputactions` and the `Player/Move` and `Player/Attack` actions | Preserves the authored Input System asset as the sole input contract, including Development Build parity (D-09 through D-12). |
| Navigation | `NavigationGrid2D` continues to own its child terrain-source discovery and exposes a fail-closed build result to the root | The root validates the cross-context role while the grid retains its tilemap-local responsibilities. |
| HUD and camera | Owned local HUD view helper plus runtime `Bind(...)`; existing `CameraFollow2D.SetTarget(...)` | Presentation components retain prefab-local lookup and receive only the player relationship from the root. |
| Optional contexts | `ISliceOptionalContext` extension seam; encounter and town components may be absent | Absence is valid until their content phases; no synthetic gameplay contexts are added (D-05). |
| Verification | `scripts/Invoke-SliceVerification.ps1` orchestrates independent EditMode, PlayMode, and Development Build smoke channels | Each channel has a retained local result; the wrapper summarizes all outcomes and returns failure when any channel fails (D-13 through D-16). |
| Data layer | Out of scope | This local Unity phase has no database, persistence, or service boundary; adding one contradicts the Phase 1 scope. |
| Authentication | Out of scope | The slice is offline and has no account, credential, or network boundary. |
| Deployment target | Out of scope; local Windows Development Build smoke only | Production deployment is excluded. The documented local build-and-smoke entry point is the appropriate full-stack equivalent for this phase. |
| Directory layout | Keep `Assets/Scripts/<domain>/`, `Assets/Editor/Verification/`, existing test assemblies, and `scripts/` | Aligns with current Unity ownership, editor-only, and local tooling conventions. |
| Delivery workflow | One branch per phase: Phase 1 originates from `main`; each later phase originates from its immediate predecessor | Locked delivery decision D-17 prevents unrelated phase work from sharing a branch lineage. |

## Stack Touched in Phase 1

- [x] Existing Unity project, `SampleScene`, gameplay assembly, and Unity Test Framework foundations.
- [x] One enabled build scene and a runtime composition entry point; web-style routing is inapplicable to this Unity slice.
- [x] HUD and camera interactions are bound to the active player through the runtime root.
- [x] Configured Input System actions drive movement and attack routing.
- [x] Documented local Development Build run through `scripts/Invoke-SliceVerification.ps1`; hosted deployment is out of scope.
- [ ] Database read/write — inapplicable: no persistence or backend is permitted in Phase 1.
- [ ] Authentication — inapplicable: the local offline slice has no identity boundary.

## Out of Scope (Deferred to Later Slices)

- Encounter and town gameplay/content, including any authored dummy contexts. Phase 1 only provides their real optional extension seam (D-05).
- Additive scenes or a master-prefab architecture. `Assets/Scenes/SampleScene.unity` remains the single configured slice (D-08).
- Abilities and device bindings beyond the keyboard/mouse movement and attack parity surface (D-09).
- In-game diagnostics UI. Startup faults are reported in Unity logs only (D-02).
- Persistence, databases, web services, account/authentication flows, deployment, and new packages; all conflict with the locked local Unity scope.
- Committed test reports, smoke logs, and build output. These remain predictable ignored local evidence (D-15).

## Subsequent Slice Plan

Each later phase builds on this runtime-composition contract without renegotiating it:

- Phase 2: validated data authoring and slice diagnostics.
- Phase 3: configured starter weapon and abilities.
- Phase 4: enemy intent and telegraph contracts.
- Phase 5: readable combat presentation within a measured local envelope.
- Phase 6: authored replayable clearing journey.
- Phase 7: town reward hand-off.
