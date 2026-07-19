# Feature Landscape

**Project:** Top-Down-RPG
**Domain:** Reusable top-down action-RPG foundation and clearing-to-town vertical slice
**Researched:** 2026-07-19
**Confidence:** MEDIUM — requirements are high-confidence project decisions; the combat-readability and VFX guidance is cross-checked, but the recommended exact slice size is intentionally a product judgement for this solo project.

## Scope Decision

Build one continuously playable, authored journey that proves the reusable contracts needed to make the next encounter, weapon, ability, effect, town interaction, quest, recipe, and progression reward cheaper to add. Do not try to prove a content volume, an open world, or a complete RPG.

The representative run should be short enough to replay after each tuning change: spawn in a clearing with a fixed starter loadout, clear a three-step combat path, cross an unlocked route, and arrive at a town where one NPC, one station, and one progression reward expose the future seams. The authored examples are deliberately small; the ability to author a second example without changing core code is the real success criterion.

## Table Stakes

These are the user-visible behaviours the vertical slice must have. They are table stakes for *this foundation*, not a claim that a full game needs only these features.

| Feature | Why Expected | Complexity | Prescriptive scope for this milestone |
|---------|--------------|------------|---------------------------------------|
| **A playable clearing-to-town run** | The project promise is a coherent combat-learning journey with an arrival payoff, not an isolated test arena. | Medium | One spawn, one linear route, one combat gate, and one town arrival trigger. Keep it in the current scene or use a single explicit transition; no world map or streaming. |
| **Responsive starter combat loadout** | Players need to move, face, attack, and use abilities immediately enough to judge the core loop. | High | Ship one fixed starting weapon and two fixed active ability slots. The slice needs two authored ability examples, but no inventory/loadout screen or equipment swapping yet. |
| **One learnable enemy attack contract** | Fast action is only fair when the player can identify intent, act, and see the outcome. | High | Every harmful enemy action must have `anticipation → active danger → recovery`, a consistent counterplay signal, and an authored hit area. Start with two archetypes: a close melee pursuer and a spatial/ranged threat. |
| **Readable encounter sequence** | Individual AI behaviour does not yet prove combat pacing, teaching, or target prioritisation. | High | Use three deliberate encounters: melee-only teaching, spatial-threat teaching, then a small mixed encounter. Clear condition unlocks the route. Do not build an endless-wave mode. |
| **Multi-channel combat feedback** | Attack, hit, damage, defeat, and objective completion must be legible even in a sprite-based presentation. | High | Each significant combat event needs a primary gameplay cue plus only supporting cues: animation/shape, VFX, sound hook, optional 2D light/shader treatment, and minimal camera/UI feedback. Effects must communicate event state before decorative detail. |
| **Minimal combat HUD and objective guidance** | The player needs health, available actions/cooldowns, and a current goal while learning the loop. | Medium | Extend the existing health/weapon HUD with fixed action-state indicators and a short objective/interaction prompt. Keep it passive; do not build menus, a quest journal, or minimap. |
| **Town interaction seams** | Reaching town must demonstrate that combat rewards lead into questing, crafting, and progression. | High | One NPC can offer/complete one objective, one station can make one recipe, and one progression presentation shows a reward/unlock. Interactions share a prompt and action affordance. |
| **Repeatable authoring and tuning loop** | The product is a local systems proving ground; tuning must be faster than hand-rewiring a scene. | High | Combat actions, enemy timings, encounter membership, rewards, recipes, and quest steps are authored data/configuration. Provide gizmos or debug text for attack phases, hit areas, encounter state, and current objective. |

### Readability Contract

Treat telegraphing as gameplay information, not a late VFX pass. An enemy action is eligible for the clearing only when a fresh player can answer all four questions from the playfield:

1. **Who is acting?** The actor has a distinct pose, facing, silhouette, or origin point.
2. **What is about to happen and where?** Wind-up/aiming, ground area, projectile origin, or movement path exposes the threat's shape and direction.
3. **What response works?** The enemy's cue family maps consistently to movement/positioning or the explicitly supported defensive/reposition action.
4. **When is it safe to respond or counterattack?** Active danger ends visibly and recovery/window-of-opportunity is real, not inferred from a hidden cooldown.

Do not add dodge, block, parry, stamina, and invulnerability-frame systems merely to satisfy genre convention. First make positioning and the existing responsive movement sufficient to evade the two starter archetypes. Add one defensive/reposition mechanic only if playtests show that a readable attack still leaves no satisfying response.

### Effects Rule

An effect profile must declare its **semantic job** before its implementation: `telegraph`, `attack release`, `hit confirmation`, `damage state`, `defeat`, `reward`, or `interaction available`. Give each profile one dominant readable signal (shape, motion, colour family, timing, or sound hook); particles, 2D lights, and shader treatment amplify it rather than competing with it.

This is where the project can differentiate visually: use URP 2D lights, particles, and shader-driven material changes in a controlled, reusable cue catalogue. The first slice should contain a few polished examples of each semantic job, not a general visual-effects laboratory.

## Foundation Requirements

These are reusable system seams. They may be invisible to the player, but content should not be expanded before they are sufficient to author the table-stakes examples.

| Foundation seam | Must provide now | Explicitly defer | Depends on / extends |
|-----------------|------------------|------------------|----------------------|
| **Combat action definition** | One data-authored definition for weapon attacks and abilities: activation, phase timings, movement/cancel policy, target/hit shape, cooldown/resource rule, and emitted cue IDs. | Animation-event framework, combo editor, complex targeting UI, elemental/stat formulae. | Extend `PlayerWeapon`, transient attack handling, `Health` events, and Input System actions. |
| **Action execution and cancellation policy** | A single player-owned executor that applies the definition consistently and publishes phase changes. The first abilities can be fixed in the starter loadout. | Arbitrary action graphs, universal command buffering, networking determinism. | Existing `PlayerController` and `PlayerWeaponController`; keep physics movement in `FixedUpdate`. |
| **Enemy action timeline** | An AI-owned component/state that exposes wind-up, active hit, recovery, cooldown, target direction, and a telegraph profile. Damage only occurs during the active phase. | Behaviour trees, boss phase framework, group tactics solver. | Current mob FSM, `MeleeDamageDealer`, path agent, and `MobConfig`. |
| **Combat cue dispatcher** | A local, event-driven bridge from action/health/encounter events to owned presentation helpers. Payload contains source, cue ID, world position/direction, and strength; consumers must not alter combat result. | Global event bus, cross-scene service locator, recording/replay system. | Existing `Health.Damaged` / `Died`, damage popup, slash, death animation, and prefab-local helpers. |
| **Semantic VFX profiles** | Data/configuration that binds a cue ID to an owned prefab/material/light/particle recipe and lifetime. Define performance budgets and pool short-lived high-frequency effects before adding mixed encounters. | Per-effect bespoke code, VFX Graph adoption, unrestricted dynamic lights/shadows. | URP 2D renderer, sprite sorting, existing transient combat effects. |
| **Encounter controller** | An authored list of spawn definitions, activation boundary, clear condition, completion reward, and one route/gate consequence. It owns spawned mobs and can reset for local iteration. | Procedural encounter director, infinite spawner, persistence across all maps. | Mob prefab/configuration, navigation grid, combat death event, route/town trigger. |
| **Player progress ledger** | A small runtime state object for resource counts, completed objective IDs, crafted recipe IDs, experience/progression amount, and unlocked action/reward IDs. It exposes queries and change events. | General inventory grid, equipment stat comparison, save migration, economy simulation. | Encounter rewards and town consumers. Keep it player-owned or scene-owned; do not make every UI/AI object discover it globally. |
| **Interaction boundary** | One interaction prompt, eligibility check, and activate command for NPC, station, and arrival marker. Targets expose local capability components or implement a narrow contract. | Dialogue trees, radial menus, universal use system, vendor UI. | Player input, HUD prompt, town prefab composition. |
| **Quest / recipe / progression data** | A minimum data shape that can express one objective, one completion condition/reward, one recipe with input/output, and one progress threshold/unlock. Completion must be event-driven from the ledger or encounter. | Branching story graph, repeatable quest board, crafting queues, skill-tree editor. | Progress ledger and interaction boundary. |
| **Debug and validation tools** | Editor validation for missing required assets/config; runtime debug overlay/gizmos for phase timing, hit/telegraph areas, encounter status, ledger state, and effect pool counts. Add focused EditMode/PlayMode tests around contracts. | Live balancing service, analytics pipeline, production CI infrastructure. | Existing test assemblies, editor tooling, and Unity MCP-assisted local verification loop. |

### Authoring Rules

- Keep shared numbers and asset references in ScriptableObjects/configuration assets; keep per-instance runtime state on the player, enemy, encounter, or town owner.
- Resolve scene collaborators through ownership and explicit runtime configuration. Local child lookups are appropriate for a prefab's own view/effect pieces; cross-scene serialized component wiring and broad `FindAnyObjectByType` discovery are not.
- The cue dispatcher is one-way: presentation may observe a committed gameplay event, but visual timing must not decide hit registration, damage, quest completion, or gate unlock.
- A content addition is successful only if it is an asset/configuration/prefab composition change plus a test, not a new special-case path through the core loop.

## Differentiators

These are valuable because they make this project a stronger reusable foundation. Build only the thin form described below; they are not excuses to expand content breadth.

| Feature | Value proposition | Complexity | Thin implementation |
|---------|-------------------|------------|---------------------|
| **Semantic visual-effects toolkit** | Makes sprite combat expressive while preserving the information hierarchy that fast action requires. | High | A cue catalogue with reusable profiles for telegraph, release, impact, defeat, and reward; demonstrate particles, URP 2D light, and one shader/material treatment in the slice. |
| **Fight teaches itself through encounter order** | Uses authored sequence rather than tutorial text to teach spacing, cues, and target priority. | Medium | Two single-archetype encounters followed by a mixed test; objective text only reinforces the observed rule. |
| **One combat-to-town reward chain** | Proves that fighting has a legible purpose beyond deleting enemies and lets town systems consume gameplay outcomes. | High | Clearing grants one resource/progress change; the town NPC recognises it, the station consumes it in one recipe, and a visible unlock/result follows. |
| **Fast local tuning diagnostics** | Lets the developer compare feel changes without guessing whether timing, hitbox, pathing, effects, or routing caused a result. | Medium | Toggle phase labels and geometry, encounter reset, cue log, and cheap allocation/effect-count counters in development/editor builds. |
| **Content-shaped data rather than class-shaped features** | A new enemy/action/recipe/quest should primarily be authoring work, preserving experimentation speed. | High | Use small data definitions and owner-local executors; prove the seam by creating a second enemy and second ability without duplicating the control loop. |

## Future Content — Deliberately Deferred

These should have a compatible seam, but no milestone work should implement them beyond what the three-step clearing and one town interaction need.

| Future content | Foundation proof required now | Why it is not current scope |
|----------------|-------------------------------|-----------------------------|
| Additional weapons, ability families, gear rarity, and builds | A fixed starter loadout and action definitions can support another data-authored action. | Content volume hides whether the combat contract itself is satisfying. |
| Enemy roster, elites, bosses, and faction behaviours | Two archetypes and a mixed encounter prove the telegraph and encounter contracts. | More enemies multiply art, balance, navigation, and performance work before the core responses are validated. |
| Biomes, dungeons, world map, and multiple towns | One route/gate and one arrival marker prove a journey transition. | No open-world or multi-scene architecture is necessary to test the foundation. |
| Story, dialogue, branching quests, and quest journal | One offer/complete objective proves event-driven quest data. | Narrative tools require content decisions that cannot validate combat feel. |
| Crafting catalogue, inventory UX, vendors, and economy | One resource input and one recipe output prove the ledger and recipe seam. | A full inventory/economy would dominate the milestone and pre-commit the UI. |
| Levels, skill tree, stat buildcraft, and save progression | One progress threshold/unlock proves a progression consumer. | Persistence, migration, and balance are separate product decisions. |

## Anti-Features

| Anti-feature | Why avoid | Do instead |
|--------------|-----------|------------|
| **A full game loop or campaign** | The milestone is a systems and feel proving ground; broad content gives false confidence and locks in weak contracts. | Keep one polished, repeatable clearing-to-town journey. |
| **Multiplayer, online services, or backend work** | They do not improve the local combat/town proof and would force ownership, authority, persistence, and testing decisions far outside scope. | Preserve single-player local ownership boundaries. |
| **An open world, procedural generation, or dynamic navigation at scale** | Current synchronous pathfinding and eager navigation-grid construction have no capacity budget; scale would mask combat work with performance debt. | Use an authored linear path and profile a representative mixed encounter first. |
| **A generic inventory/loot/equipment UI** | It is high-surface-area content and UI work with little evidence that its rules are needed yet. | Use a small player ledger and fixed starter loadout; show only the resource/reward required by the town proof. |
| **A full dialogue, quest-graph, or town-simulation framework** | It commits to story and economy semantics before the interaction boundary is proven. | Implement one NPC objective using narrow data and an interaction prompt. |
| **Every action-game defense at once** | Dodge, parry, block, stamina, i-frames, and cancelling multiply tuning states and can conceal unclear enemy cues. | Start with spatial avoidance plus at most one tested reposition/defensive response. |
| **Effects that obscure decisions** | Heavy particles, camera motion, light flashes, and shader noise can make a fast fight less readable—the opposite of the core value. | Maintain a cue hierarchy, cap simultaneous high-intensity effects, and test mixed encounters at normal play distance. |
| **Per-feature global managers and serialized scene links** | They make a solo scene work quickly but conflict with the current ownership/discovery conventions and complicate future scene changes. | Use player/enemy/encounter/town owners, local helper components, and explicit runtime setup. |
| **A premature performance rewrite** | Jobs, ECS, procedural directors, and broad pooling frameworks add complexity before a measured representative load exists. | Add budgets and instrumentation now; pool the known short-lived combat effects before the mixed encounter exposes allocation pressure. |

## Feature Dependencies

```text
Starter input + player locomotion
  → combat action definition + executor
  → player attack/ability phases
  → combat cue dispatcher → semantic VFX profiles → readable player feedback

Mob FSM + navigation
  → enemy action timeline + telegraph profile
  → readable enemy attacks
  → authored encounters
  → clear reward + route/gate unlock
  → town arrival

Encounter reward
  → player progress ledger
  → interaction boundary
  ├→ one NPC objective (quest seam)
  ├→ one station recipe (crafting seam)
  └→ one threshold/unlock (progression seam)

Debug/validation tools → all authoring seams and every iteration loop
```

### Dependency Notes

- Do **not** build mixed encounters until the player action phases, one melee telegraph, one spatial telegraph, and their effect profiles all work in isolation.
- Do **not** add town content before a clearing completion produces a real reward/objective event; otherwise quests and crafting are disconnected mock UI.
- Do **not** broaden the action catalogue until the same action definition/executor can author the second starter ability without a code fork.
- Pool or otherwise budget slash, popup, death, and new cue effects before the mixed encounter; the existing code allocates transient combat objects per event.

## MVP Recommendation

Prioritize:

1. **Combat action and enemy action contracts** — make one weapon and two starter abilities, plus two telegraphed enemy attacks, genuinely readable and responsive.
2. **Combat presentation and encounter proof** — route committed combat events into semantic effects, then author the three-encounter clearing and a completion gate.
3. **Town seams and reward chain** — connect the clear reward to one objective, one recipe, and one progression unlock through a small player ledger and shared interaction boundary.
4. **Tooling, validation, and replayability** — make the slice easy to reset, inspect, test, and tune before expanding any content.

Defer: all content breadth listed in **Future Content** and all system breadth listed in **Anti-Features**. A second weapon, ability, enemy, recipe, or quest should be a proof that the foundation generalises—not the start of a content-production phase.

## Sources

- Project requirements and scope: `.planning/PROJECT.md` (primary project evidence).
- Existing gameplay capabilities, component/event boundaries, and authoring patterns: `.planning/codebase/ARCHITECTURE.md`, `.planning/codebase/STRUCTURE.md`, and `.planning/codebase/CONCERNS.md` (primary codebase evidence).
- [Unity Manual: 2D lighting in URP](https://docs.unity3d.com/Manual/urp/2d-index.html) — MEDIUM confidence from the research confidence seam; official documentation confirms the available 2D light, shader/VFX compatibility, and batching surfaces.
- [Designing for Difficulty: Readability in ARPGs](https://www.gamedeveloper.com/game-platforms/designing-for-difficulty-readability-in-arpgs) — MEDIUM confidence; cross-checked design-practitioner guidance on telegraphs, consistent visual language, threat expectation, and staged pattern learning.
- [Enemy Attacks and Telegraphing](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing) — MEDIUM confidence; cross-checked design-practitioner guidance on pre-attack delay and layered animation, sound, and VFX cues.
- [Designing Game Feel: A Survey](https://arxiv.org/abs/2011.09201) — MEDIUM confidence; academic synthesis supporting feedback amplification/"juice" as both empowerment and event clarity.
