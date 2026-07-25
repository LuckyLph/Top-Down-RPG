# Phase 1: Runtime Composition & Guardrails - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-07-24
**Phase:** 1-Runtime Composition & Guardrails
**Areas discussed:** Startup failure policy, Pre-content encounter/town contexts, Development Build input parity, Verification evidence, Cross-phase delivery workflow

---

## Startup failure policy

| Decision | Options considered | Selected |
|---|---|---|
| Required runtime failure | Block and name the fault; launch what can run; strict standard launch with an explicit sandbox mode | Block and name the fault |
| Failure visibility | Console plus minimal build failure screen; console/log only; failure screen plus report | Console/log only |
| Mandatory contexts | All five contexts; playable core only; encounter-ready core | Playable core only |
| Existing fallbacks | No automatic substitutes; retain development fallbacks; retain test-only fallbacks | No automatic gameplay substitutes |

**User's choice:** The core must fail closed and identify faults in output. Player, navigation, and HUD/camera are mandatory; encounter/town are optional. Do not fabricate missing gameplay dependencies.

---

## Pre-content encounter/town contexts

| Decision | Options considered | Selected |
|---|---|---|
| Absent future contexts | Valid absence with extension points; empty scene placeholders; minimal test doubles | Valid absence with extension points |
| Future context ownership | One composition root coordinates local domains; independent scene discovery; global registry | One composition root coordinates local domains |
| Initialization order | Core first then optional contexts; unordered startup; lazy self-start | Core first then optional contexts |
| Scene scope | One `SampleScene`; additive scenes; master-prefab migration | One `SampleScene` |

**User's choice:** Preserve a single-scene slice and establish extension seams without adding fake future content or global discovery.

---

## Development Build input parity

| Decision | Options considered | Selected |
|---|---|---|
| Required controls | Keyboard/mouse core controls; keyboard only; every existing device binding | Keyboard/mouse core controls |
| Parity proof | Real Input System action reaches gameplay; binding presence plus manual play; asset-only check | Real Input System action reaches gameplay |
| Missing input behavior | Fail startup; launch idle player; legacy keyboard fallback | Fail startup |
| Input ownership | Player-local adapter; composition-root ownership; self-discovery | Player-local adapter |

**User's choice:** Exercise real `Move` and `Attack` actions for the existing keyboard/mouse controls; missing input configuration is a blocking core fault with no fallback.

---

## Verification evidence

| Decision | Options considered | Selected |
|---|---|---|
| Required evidence | EditMode + PlayMode + Development Build smoke; tests plus manual build; build smoke only | All three channels |
| Reproduction | One documented local entry point; documented checklist; editor menus only | One documented local entry point |
| Retained output | Console plus ignored local artifacts; console only; commit artifacts | Console plus ignored local artifacts |
| Failure behavior | Run all then fail overall; fail fast; independent reports only | Run all then fail overall |

**User's choice:** Give developers a single local gate that collects every independent result and preserves reviewable, uncommitted evidence.

---

## Cross-phase delivery workflow

| Decision | Options considered | Selected |
|---|---|---|
| Branch lineage | User-specified phase branch workflow | One branch per phase: Phase 1 from `main`, each later phase from the preceding phase branch |

**User's choice:** Preserve a linear phase-branch lineage across the milestone.

---

## the agent's Discretion

None — no decision was delegated without a user preference.

## Deferred Ideas

None — discussion stayed within Phase 1's boundary.
