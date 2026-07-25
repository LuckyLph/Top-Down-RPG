---
phase: "01"
slug: runtime-composition-guardrails
# status lifecycle: draft (seeded by plan-phase) -> validated (set by validate-phase)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-07-24
---

# Phase 01 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | Unity Test Framework 1.6.0 with NUnit |
| **Config file** | `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef` |
| **Quick run command** | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode` |
| **Full suite command** | `./scripts/Invoke-SliceVerification.ps1 -Mode All` |
| **Estimated runtime** | Unknown until Wave 0 validates the local Unity batch-test path |

## Sampling Rate

- **After every task commit:** Run the relevant focused EditMode or PlayMode filter through `Invoke-SliceVerification.ps1`, then inspect its generated result file.
- **After every plan wave:** Run `./scripts/Invoke-SliceVerification.ps1 -Mode All`.
- **Before `$gsd-verify-work`:** EditMode, PlayMode, and the Development Build smoke channel must all pass.
- **Max feedback latency:** Establish during Wave 0; do not rely on unverified batch-mode timing.

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| W0-composition | TBD | 0 | RUNTIME-01 | T-01-01 | Required player, navigation, HUD, and camera resolve uniquely; invalid core remains inactive and logs a contextual error. | EditMode | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode -TestFilter RuntimeCompositionEditModeTests` | ❌ Wave 0 | ⬜ pending |
| W0-input | TBD | 0 | RUNTIME-04 | T-01-02 | Missing input asset, Player map, Move, or Attack fails the player-owned contract without legacy or generated fallback actions. | EditMode | `./scripts/Invoke-SliceVerification.ps1 -Mode EditMode -TestFilter PlayerInputAdapterTests` | ❌ Wave 0 | ⬜ pending |
| runtime-flow | TBD | 1+ | RUNTIME-01, RUNTIME-04 | T-01-01 / T-01-02 | The loaded scene receives explicit core bindings, optional contexts may be absent, and genuine Input System actions reach movement and attack routing. | PlayMode | `./scripts/Invoke-SliceVerification.ps1 -Mode PlayMode -TestFilter RuntimeCompositionPlayModeTests` | ❌ Wave 0 | ⬜ pending |
| development-smoke | TBD | final | RUNTIME-04 | T-01-03 | A Development Build yields a build result and log-only smoke pass after real keyboard/mouse movement and attack input; missing sentinel or timeout fails. | Interactive smoke | `./scripts/Invoke-SliceVerification.ps1 -Mode Smoke` | ❌ Wave 0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

## Wave 0 Requirements

- [ ] `Assets/Tests/Editor/RuntimeCompositionEditModeTests.cs` — required-core uniqueness, fail-closed startup, and input-contract tests.
- [ ] `Assets/Tests/PlayMode/RuntimeCompositionPlayModeTests.cs` — loaded-scene injection, absent optional-context, and action-routing tests.
- [ ] `Assets/Editor/Verification/SliceVerificationCommands.cs` — Windows Development Build command with `BuildReport` result handling.
- [ ] `scripts/Invoke-SliceVerification.ps1` — independent EditMode, PlayMode, and smoke steps; aggregate all results after every channel completes.
- [ ] `.gitignore` entries for predictable local `TestResults/` output and any smoke output not already under `Builds/`.
- [ ] A local proof that Unity Test Framework 1.6.0 accepts the selected batch commands and can write XML reports with the current license/database state.

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Keyboard/mouse Development Build parity | RUNTIME-04 | The contract requires physical WASD/arrows plus Space/left-mouse input in the built player; Unity Test Framework cannot prove the developer's real-device interaction. | Run `./scripts/Invoke-SliceVerification.ps1 -Mode Smoke`, launch the generated Development Build, move with WASD or arrows, then attack with Space or left mouse. Confirm the wrapper reports the smoke sentinel and its log path. |

## Validation Sign-Off

- [ ] All implementation tasks reference an automated command or this Wave 0 dependency.
- [ ] Sampling continuity: no three consecutive implementation tasks lack an automated verification step.
- [ ] Wave 0 establishes the exact Unity 6000.4.2f1 / Test Framework 1.6.0 batch command behavior and writable report paths.
- [ ] The wrapper waits for every channel, preserves each result/log path, and exits nonzero if any channel fails.
- [ ] No watch-mode flags are used.
- [ ] `nyquist_compliant: true` is set only after the verification map is implemented and run successfully.

**Approval:** pending
