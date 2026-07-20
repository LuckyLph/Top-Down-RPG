# Requirements: Top-Down-RPG

**Defined:** 2026-07-19
**Core Value:** Make fast, satisfying, highly readable top-down combat whose visual effects push sprite-based presentation beyond basic sprite rendering.

## v1 Requirements

Requirements for the reusable action-RPG foundation and its clearing-to-town vertical slice. Existing movement, basic melee combat, navigation, mob state machines, HUD, and test assemblies are validated baseline capabilities; the requirements below extend them into a coherent, repeatable experiment.

### Runtime & Tooling

- [ ] **RUNTIME-01**: Developer can start the vertical slice with a composition root that initializes the player, navigation, HUD/camera, encounters, and town runtime through explicit runtime ownership/injection rather than serialized cross-scene component links.
- [ ] **RUNTIME-02**: Developer can author and validate combat actions, enemy timings, encounter definitions, rewards, recipes, and objective steps as configuration/data, with actionable errors for invalid or missing required setup.
- [ ] **RUNTIME-03**: Developer can inspect and reset a local slice run using diagnostics for action phases, hit/telegraph geometry, encounter state, current objective, progression state, and active visual effects.
- [ ] **RUNTIME-04**: Developer can run focused Unity EditMode and PlayMode verification for the foundation contracts and reproduce a local development-build smoke check without relying on editor-only input fallbacks.

### Player Combat

- [ ] **COMBAT-01**: Player can use a configured starting weapon through an action timeline with visible activation, active hit, recovery, cooldown, and cancellation behavior.
- [ ] **COMBAT-02**: Player can use two configured active abilities from fixed starter slots through the same action/timing contract as the starting weapon.
- [ ] **COMBAT-03**: Player receives clear, responsive feedback for available actions, action phases, hit confirmation, damage, and defeat without presentation code determining combat outcomes.

### Enemy Intent

- [ ] **ENEMY-01**: Player can identify each harmful enemy action through an authoritative wind-up, active danger, recovery, target direction/area, and counterplay opportunity that match the actual damage window.
- [ ] **ENEMY-02**: Player can fight a close melee pursuer and a spatial or ranged threat whose attacks use distinct, readable telegraphs and do not damage before their warning phase.

### Visual Presentation

- [ ] **VFX-01**: Player can distinguish telegraph, attack release, impact, damage, defeat, reward, and interaction events through reusable semantic visual-cue profiles.
- [ ] **VFX-02**: Player experiences selected particle, URP 2D lighting, and shader/material effects that enhance combat feedback while preserving the readability of actors, danger areas, and objectives.
- [ ] **VFX-03**: Developer can profile the representative mixed encounter and keep high-frequency transient effects within a measured local performance envelope before expanding encounter density.

### Clearing Journey

- [ ] **JOURNEY-01**: Player spawns in a clearing with the fixed starter loadout and can replay a short authored combat path that teaches movement, action timing, and enemy-response rules through encounter order.
- [ ] **JOURNEY-02**: Player can complete the clearing encounters, receive an immediate combat reward, unlock or cross the route forward, and receive clear objective guidance to the town.

### Town Reward Loop

- [ ] **TOWN-01**: Player retains clearing rewards, completed objective state, crafted recipe state, and a progression/unlock state in a narrowly scoped runtime ledger that publishes change events to its consumers.
- [ ] **TOWN-02**: Player can reach town and interact through one shared prompt with an NPC that offers or completes one objective and a station that crafts one recipe from the clearing reward.
- [ ] **TOWN-03**: Player can see one progression threshold or reward unlock after the town interaction chain, proving that combat progress has a visible non-combat consequence.

## v2 Requirements

Deferred until the v1 foundation produces a stable, replayable loop and reveals what deserves expansion.

### Combat & Content

- **COMBAT-04**: Player can equip and compare multiple weapon families, extended ability builds, defensive mechanics, status effects, and progression-driven combat synergies.
- **ENEMY-03**: Player can face a broader enemy roster, bosses, group tactics, and advanced encounter patterns.
- **JOURNEY-03**: Player can explore additional biomes, dungeons, world-map routes, and dynamically changing navigation.

### RPG & Narrative Systems

- **TOWN-04**: Player can follow dialogue trees, branching/repeatable quest lines, broader crafting, vendors, economy, town simulation, inventory/equipment UI, and skill-tree progression.
- **TOWN-05**: Player progress persists across sessions through a designed save, migration, and content-identity model.

### Production Scope

- **PROD-01**: Developer can build multiplayer, online services, analytics, release automation, distribution, and commercial-ready content pipelines.

## Out of Scope

| Feature | Reason |
|---------|--------|
| A complete game, full campaign, or broad content production | The current milestone validates reusable foundations and the core loop first. |
| Multiplayer, online/backend systems, and release/distribution work | The project is currently a local single-player experiment for the developer. |
| Direct reuse of referenced games' mechanics, characters, art, or story | References establish desired genre feel only; the project must remain original. |
| Arbitrary numeric targets for subjective feel or visual quality | The developer will judge these through repeated playable review, captures, and iteration. |
| Inventory grids, vendors, economy, broad crafting, dialogue graphs, town simulation, or persistence | A single town reward chain proves the seam without turning the foundation into a complete RPG. |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| RUNTIME-01 | Phase 1 | Pending |
| RUNTIME-02 | Phase 2 | Pending |
| RUNTIME-03 | Phase 2 | Pending |
| RUNTIME-04 | Phase 1 | Pending |
| COMBAT-01 | Phase 3 | Pending |
| COMBAT-02 | Phase 3 | Pending |
| COMBAT-03 | Phase 3 | Pending |
| ENEMY-01 | Phase 4 | Pending |
| ENEMY-02 | Phase 4 | Pending |
| VFX-01 | Phase 5 | Pending |
| VFX-02 | Phase 5 | Pending |
| VFX-03 | Phase 5 | Pending |
| JOURNEY-01 | Phase 6 | Pending |
| JOURNEY-02 | Phase 6 | Pending |
| TOWN-01 | Phase 7 | Pending |
| TOWN-02 | Phase 7 | Pending |
| TOWN-03 | Phase 7 | Pending |

**Coverage:**
- v1 requirements: 17 total
- Mapped to phases: 17
- Unmapped: 0

---
*Requirements defined: 2026-07-19*
*Last updated: 2026-07-19 after roadmap creation*
