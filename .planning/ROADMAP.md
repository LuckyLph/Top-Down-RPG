# Roadmap v1.0: Top-Down-RPG

## Overview

This MVP turns the existing Unity component foundation into one replayable, local clearing-to-town action-RPG slice. It first makes scene composition, authored data, diagnostics, and build verification trustworthy; then proves a configured player loadout, fair enemy intent, readable effects within a measured local envelope, an authored clearing journey, and one thin town reward hand-off. The work extends the current prefabs, ScriptableObjects, HUD, navigation, health, and mob-state components rather than replacing them with a framework. This is not a full game: multiplayer, persistence, broad inventory/economy systems, extensive content, and copied reference-game material remain out of scope. Automated checks establish contracts, while target-camera playable-review captures remain required qualitative checkpoints for combat feel, visual readability, and performance decisions.

## Phases

**Phase Numbering:**

- Integer phases are planned MVP work.
- Decimal phases are urgent insertions and execute between their surrounding integer phases.

- [ ] **Phase 1: Runtime Composition & Guardrails** - Launch a build-safe, explicitly composed slice and restore trustworthy local verification.
- [ ] **Phase 2: Data Authoring & Slice Diagnostics** - Make slice content configuration-driven, validated, inspectable, and resettable.
- [ ] **Phase 3: Starter Loadout & Action Feel** - Deliver the responsive configured weapon and two fixed active abilities.
- [ ] **Phase 4: Enemy Intent & Telegraph Contracts** - Make two enemy threats fair, readable, and tied to authoritative damage timing.
- [ ] **Phase 5: Combat Presentation & Performance Envelope** - Reuse semantic VFX cues while measuring a representative mixed encounter.
- [ ] **Phase 6: Replayable Clearing Journey** - Compose the combat systems into a short, guided clearing-to-town route.
- [ ] **Phase 7: Town Reward Hand-off** - Prove one quest, crafting, and progression consequence from clearing rewards.

## Phase Details

### Phase 1: Runtime Composition & Guardrails

**Goal**: Developer can launch a build-safe local slice whose dynamic gameplay relationships are owned by a runtime composition root instead of serialized scene-object wiring.
**Mode:** mvp
**Depends on**: Nothing (first phase)
**Requirements**: RUNTIME-01, RUNTIME-04
**Success Criteria** (what must be TRUE):

  1. Developer can start the slice in the editor or a local Development Build and see its player, navigation, HUD/camera context, encounter context, and town context initialize without manually connecting dynamic scene components in the Inspector.
  2. Player can use the configured Input System controls in the local Development Build with the same intended movement and action behavior used for slice verification.
  3. Developer can run focused EditMode and PlayMode foundation checks and reproduce a local Development Build smoke check with a clear pass/fail result rather than relying on editor-only input fallbacks.

**Plans**: 5 plans

Plans:
**Wave 1**

- [ ] 01-01-PLAN.md — establish the local Unity verification wrapper and Wave 0 report path.

**Wave 2** *(blocked on Wave 1 completion)*

- [ ] 01-02-PLAN.md — prove the player-owned Input System contract without fallback actions.

**Wave 3** *(blocked on Wave 2 completion)*

- [ ] 01-03-PLAN.md — implement fail-closed core composition and optional-context seams.

**Wave 4** *(blocked on Wave 3 completion)*

- [ ] 01-04-PLAN.md — bind HUD/camera and author the single-scene runtime subtree.

**Wave 5** *(blocked on Wave 4 completion)*

- [ ] 01-05-PLAN.md — complete Development Build input smoke evidence and aggregate verification.

### Phase 2: Data Authoring & Slice Diagnostics

**Goal**: Developer can author the slice as validated gameplay data and inspect or reset its live state for rapid, reliable iteration.
**Mode:** mvp
**Depends on**: Phase 1
**Requirements**: RUNTIME-02, RUNTIME-03
**Success Criteria** (what must be TRUE):

  1. Developer can configure combat actions, enemy timings, encounters, rewards, recipes, and objective steps through authored definitions instead of hard-coded per-run setup.
  2. Developer receives an actionable validation error before play when a required definition, timing, prefab-local topology, or other required setup is invalid or absent.
  3. Developer can inspect action phases, hit and telegraph geometry, encounter state, current objective, progression state, and active effects during a run, then reset the local slice to a known replayable start.

**Plans**: TBD

### Phase 3: Starter Loadout & Action Feel

**Goal**: Player can fight with one configured starting weapon and two fixed active abilities that share a responsive, gameplay-authoritative action timeline.
**Mode:** mvp
**Depends on**: Phase 2
**Requirements**: COMBAT-01, COMBAT-02, COMBAT-03
**Success Criteria** (what must be TRUE):

  1. Player can use the configured starting weapon and observe its activation, active hit, recovery, cooldown, and configured cancellation behavior.
  2. Player can trigger two configured active abilities from fixed starter slots, with the same timing and hit-resolution contract as the starting weapon.
  3. Player receives responsive screen and combat feedback for action availability, current phase, hit confirmation, damage, and defeat, while the underlying combat outcome remains correct if presentation is unavailable.

**Plans**: TBD
**UI hint**: yes

### Phase 4: Enemy Intent & Telegraph Contracts

**Goal**: Player can make informed positioning choices because enemy warning, danger geometry, recovery, and damage use one authoritative action timeline.
**Mode:** mvp
**Depends on**: Phase 3
**Requirements**: ENEMY-01, ENEMY-02
**Success Criteria** (what must be TRUE):

  1. Player can identify every harmful enemy action's wind-up, target direction or area, active danger, recovery, and counterplay opportunity before its actual damage window opens.
  2. Player can fight both a close melee pursuer and a spatial or ranged threat whose distinct telegraphs match their respective damage areas and never deal damage before their warning phase.
  3. Developer can replay the paired-threat combat review at the intended camera scale and judge whether warning shapes, actors, and safe responses remain visually legible.

**Plans**: TBD

### Phase 5: Combat Presentation & Performance Envelope

**Goal**: Player experiences reusable effects that clarify the established combat language without exceeding a measured local mixed-encounter envelope.
**Mode:** mvp
**Depends on**: Phase 4
**Requirements**: VFX-01, VFX-02, VFX-03
**Success Criteria** (what must be TRUE):

  1. Player can distinguish telegraph, attack release, impact, damage, defeat, reward, and interaction events through reusable semantic visual-cue profiles.
  2. Player experiences selected particles, URP 2D lighting, and shader/material effects that enhance feedback while actors, danger areas, and objectives remain readable at the intended play camera.
  3. Developer can profile the representative mixed encounter in a local Development Build, compare its effect and frame-time evidence against the adopted envelope, and review a matching playable capture before increasing encounter density.

**Plans**: TBD

### Phase 6: Replayable Clearing Journey

**Goal**: Player can learn the combat loop through a short, repeatable authored clearing path and reach a visibly unlocked route toward town.
**Mode:** mvp
**Depends on**: Phase 5
**Requirements**: JOURNEY-01, JOURNEY-02
**Success Criteria** (what must be TRUE):

  1. Player spawns in the clearing with the fixed starter loadout and can replay an encounter order that teaches movement, action timing, and enemy-response rules through play.
  2. Player can clear the authored encounters, receive an immediate combat reward, and cause a tangible route or gate consequence that makes the path toward town available.
  3. Player receives clear objective guidance from clearing completion through town arrival without a broad journal, minimap, or world-map system.
  4. Developer can reset and replay the full clearing path, using target-camera playable review alongside automated encounter-flow checks to assess combat feel and cue readability.

**Plans**: TBD
**UI hint**: yes

### Phase 7: Town Reward Hand-off

**Goal**: Player can consume one real clearing reward in town through a minimal objective, recipe, and progression chain with visible non-combat payoff.
**Mode:** mvp
**Depends on**: Phase 6
**Requirements**: TOWN-01, TOWN-02, TOWN-03
**Success Criteria** (what must be TRUE):

  1. Player retains clearing rewards, completed objective state, crafted-recipe state, and progression or unlock state for the active slice run, with changes published to town consumers.
  2. Player can reach town and use one shared interaction UI prompt to engage an NPC that offers or completes one objective and a station that crafts one recipe using the clearing reward.
  3. Player can see one progression threshold or reward unlock after completing the town interaction chain, demonstrating a visible non-combat consequence of clearing progress.
  4. Developer can reset the slice and replay the clearing-to-town reward chain without introducing persistence, inventory grids, vendors, economy, dialogue graphs, or town simulation.

**Plans**: TBD
**UI hint**: yes

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Runtime Composition & Guardrails | 0/TBD | Not started | - |
| 2. Data Authoring & Slice Diagnostics | 0/TBD | Not started | - |
| 3. Starter Loadout & Action Feel | 0/TBD | Not started | - |
| 4. Enemy Intent & Telegraph Contracts | 0/TBD | Not started | - |
| 5. Combat Presentation & Performance Envelope | 0/TBD | Not started | - |
| 6. Replayable Clearing Journey | 0/TBD | Not started | - |
| 7. Town Reward Hand-off | 0/TBD | Not started | - |
