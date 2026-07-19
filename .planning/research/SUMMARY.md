# Project Research Summary

**Project:** Top-Down-RPG  
**Domain:** Solo, local Unity top-down action-RPG foundation and clearing-to-town vertical slice  
**Researched:** 2026-07-19  
**Confidence:** MEDIUM

## Executive Summary

Top-Down-RPG should be built as a small, continuously replayable clearing-to-town journey, not as a general RPG platform. Its success criterion is fast, fair, readable action combat with effects that make intent, impact, and space clearer than basic sprite rendering, followed by a town arrival that proves one thin quest, crafting, and progression hand-off. The existing Unity 6000.4.2f1, URP 2D, C#, Input System, Tilemap, ScriptableObject, prefab, HUD, AI/FSM, and test foundation is appropriate; replacing it with a new framework would slow the work without solving its actual risks.

The critical technical move is to make combat a single, data-authored timeline shared by player weapons/abilities and enemy attacks: wind-up, active danger, recovery, interruption/cancel policy, hit policy, and presentation signals originate from one gameplay-authoritative source. A scene-owned composition root injects dynamic scene relationships, while prefab-local helpers resolve owned parts. This directly removes the current dependency on global lookup, tag discovery, runtime fallback data, and serialized scene wiring without breaking the project's convention that serialized fields remain configuration, asset references, or intentional overrides.

The principal risks are unreadable telegraphs, VFX/sorting debt, allocation or pathfinding hitches under real encounter load, and scope creep into a miniature full game. Mitigate them by establishing the action/telegraph/render contracts and validators before content breadth; profiling a dense representative encounter in a Development Build before performance rewrites; and holding every phase to the five-minute equipped-clearing-to-town acceptance path. Automated tests protect contracts, but repeated Game-view capture and hands-on playable review decide combat feel.

## Key Findings

### Recommended Stack

Keep the project pinned to its established Unity 6000.4.2f1 / URP 17.4.0 / C# 9 / .NET Standard 2.1 platform. The most valuable additions are workflow practices and small editor-only diagnostics around the current stack, not runtime frameworks or services. Package compatibility must be confirmed in the installed 6000.4.2f1 editor before any manifest change, with `manifest.json` and `packages-lock.json` reviewed together.

**Core technologies:**

- **Unity 6000.4.2f1 + C#:** editor, runtime, and test platform — preserve the pinned baseline through this slice.
- **URP 17.4 2D Renderer:** Light 2D, particles, Shader Graph, sprite rendering, and restrained post-processing — the correct native path for expressive but readable 2D combat effects.
- **Unity Input System 1.19:** one authored `.inputactions` asset — serialize it as player-prefab configuration and remove normal-build hard-coded fallback bindings.
- **2D Tilemaps, Tilemap Extras, and DualGrid:** authored terrain and the existing navigation source — retain the current navigation seam and profile it before replacing it.
- **ScriptableObjects + prefabs + MonoBehaviours:** reusable immutable-ish definitions, authored content, and per-instance state — preserves the established component-oriented foundation.
- **UGUI + TextMesh Pro:** slice HUD and combat feedback — extend the working UI rather than add a second runtime UI stack.
- **Unity Test Framework, Profiler, Frame Debugger:** contract verification and Development Build measurement — first restore a local/batch test result artifact, then establish profiler baselines.
- **Built-in Particle System + Shader Graph:** semantic telegraphs, releases, hits, defeat, and reward cues — prefer these over VFX Graph, whose 2D limitations do not fit this slice.

Do not add Cinemachine, Addressables, ECS/DOTS, a DI container, a behavior-tree framework, UI Toolkit runtime UI, VFX Graph, persistence, a database, cloud services, multiplayer, or a navigation package during v1. They solve unproven problems while widening the iteration and ownership surface.

### Expected Features

The v1 product is one authored run: spawn equipped in a clearing, learn combat through three deliberately sequenced encounters, unlock the route, and arrive at a starting town. The first two encounters teach a close melee threat and a spatial/ranged threat separately; a final small mixed encounter tests whether their cues remain readable together. The route must become a tangible consequence of combat completion, rather than a decorative level transition.

**Must have (table stakes):**

- **Responsive fixed starter loadout:** one weapon and two fixed active ability examples, without an inventory, equipment, or loadout screen.
- **Shared, readable action contracts:** each damaging player/enemy action has anticipation, active danger, recovery, hit shape, cooldown/cancel rules, and one authoritative timing owner.
- **Two enemy archetypes and staged encounters:** melee pursuer, spatial/ranged threat, then a small mixed encounter with a clear unlock condition.
- **Multi-channel but hierarchical feedback:** primary gameplay cue plus supporting animation/shape, VFX, sound hook, optional 2D light/shader accent, and minimal HUD/camera response.
- **Minimal combat HUD and goal guidance:** health, action state/cooldowns, objective, and interaction prompt; no journal, minimap, or menu suite.
- **Town arrival seams:** one NPC objective, one recipe at one station, and one progression reward/unlock, all connected to a real clearing reward.
- **Repeatable authoring and inspection:** data-authored actions, enemy timings, encounters, rewards, recipes, and objective steps with validators, gizmos/debug overlay, reset, and focused tests.

**Should have (competitive differentiators):**

- **Semantic VFX toolkit:** a small reusable cue catalogue for telegraph, release, impact, defeat, reward, and interaction, demonstrating particles, URP 2D light, and one shader/material treatment.
- **Self-teaching encounter flow:** encounter order teaches spacing, timing, and prioritisation through play, with text only reinforcing the lesson.
- **Combat-to-town reward chain:** one clearing reward updates a player-owned ledger that the NPC, station, and progression presentation can each consume.
- **Fast local tuning diagnostics:** phase/hit-area visualisation, encounter reset, cue log, and low-cost effect/GC counters make feel iterations comparable.

**Defer (v2+):**

- Additional weapon families, buildcraft, equipment, inventory UI, loot rarity, and broad enemy/boss rosters.
- Biomes, dungeons, world map, streaming, dynamic navigation at scale, and multiple towns.
- Dialogue trees, branching quests, town simulation, vendors, economy, crafting queues, skill trees, saving, and migration.
- Multiplayer, online/backend work, analytics, and release/distribution infrastructure.

### Architecture Approach

Evolve the existing component architecture rather than replace it. Use a scene-owned `SliceBootstrap`/runtime composition root to discover its owned services and inject dynamic dependencies into spawned or scene-placed actors before they begin behaviour. Keep asset references and numeric/configuration values serialized, resolve same-object and owned-child parts locally, and inject player target, navigation grid, HUD/camera context, presentation runtime, and town session state at runtime. This is the required reconciliation between extensibility and the project's explicit no-cross-scene-wiring convention.

**Major components:**

1. **SliceBootstrap and spawn services** — compose the active player, grid, encounters, presentation, HUD/camera, and town runtime; inject context explicitly and idempotently.
2. **CombatActionDefinition + CombatActionRunner** — own wind-up, active windows, recovery, cooldown/cancellation, hit volume, damage request/outcome, and action signals for both player and enemy actions.
3. **Player loadout/combat controller** — translate configured input slots into fixed weapon/ability definitions without owning raw input, hitbox timing, or transient VFX.
4. **EnemyController + EnemyActionController** — retain the inspectable FSM/navigation/perception split while replacing instantaneous damage ticks with the common action timeline.
5. **Actor presentation, feedback router, and PresentationRuntime** — observe committed action/health signals, select semantic recipes, enforce render rules, and pool only effects with a tested reset lifecycle.
6. **EncounterDirector and SliceFlowController** — own spawn definitions, actor injection, completion, reward, gate/route consequence, and resettable clearing progression.
7. **TownRuntime and interaction adapters** — hold session-only ledger, quest, recipe, and progression state; adapters consume injected runtime state and definition assets without a global manager.
8. **Editor validation and diagnostics** — reject invalid action timing, absent telegraphs/configuration, bad prefab-local topology, or sorting/layer violations before play/build; expose runtime phase and encounter state for tuning.

**Patterns to follow:**

- Gameplay is authoritative; presentation observes immutable action/combat signals and must never determine damage, cooldown, completion, or AI transitions.
- ScriptableObjects describe reusable definitions; per-actor/session state belongs to runtime components and plain runtime models, never shared definition assets.
- Pool only short-lived presentation objects after an explicit `Play`/`Stop`/`ResetForPool` contract is implemented and PlayMode-tested.
- Keep the first journey in one scene with explicit flow state. Add scene handoff only when a demonstrated need emerges.
- Use local prefab-part helpers, ownership-based lookup, and runtime injection; do not introduce broad `FindAnyObjectByType`, tag lookup, `Camera.main`, first-canvas fallback, or serialized dynamic scene links on normal runtime paths.

### Critical Pitfalls

1. **Telegraphs become cosmetic rather than contractual** — derive both visible warning and active hit window from the same action phase; validate wind-up before active damage and test crowded encounters at the play camera.
2. **Effects undermine sorting and readability** — publish a render taxonomy for terrain, props, actors, actor effects, ground danger, UI, and debug overlays before expanding VFX; use a single Y-sort authority and prefab-local renderer helpers.
3. **Combat feedback creates frame-time/GC hitches** — profile a Development Build dense encounter before optimisation; set actor, path, particle, light, and draw-call budgets, then pool only resettable known hot-path effects.
4. **Incomplete content silently falls back** — favour explicit validation errors and user-invoked repair commands over runtime-created default content, hierarchy/name assumptions, or editor-startup asset mutation.
5. **The town proof expands into a full game** — allow exactly one observable objective, recipe, and unlock after arrival; defer simulation, economy, dialogue tooling, saving, and breadth until replay evidence supports them.

## Implications for Roadmap

The roadmap must establish the slice's runtime and authoring contracts before creating content with them. The feature research suggests tooling as a final outcome, but the architecture and pitfalls research is decisive: validation, debug visibility, and a reproducible test/profile baseline are early enabling work. Tuning polish remains ongoing and closes the slice, but guardrail tooling cannot wait until after new weapons, enemies, and effects have multiplied invalid wiring paths.

### Phase 1: Slice Runtime, Baseline, and Guardrails

**Rationale:** Existing global lookup, editor/build input drift, and unreliable batch-test evidence make every later combat or VFX result suspect. Establish deterministic composition and a repeatable evidence loop first.

**Delivers:** A one-scene `SliceBootstrap` composition root; explicit/idempotent actor initialization; player input asset as the build-safe configuration seam; context injection for player, camera/HUD, grid, and spawned enemies; a reproducible EditMode/PlayMode result artifact; Development Build smoke and baseline capture plan.

**Addresses:** Repeatable authoring/tuning loop, reliable local build verification, and the core one-scene slice boundary.

**Avoids:** Scene-global discovery, serialized scene-object wiring, editor/player input drift, and false confidence from tests that do not produce results.

### Phase 2: Data-Authored Combat and Content Validation

**Rationale:** A shared definition and validation layer must exist before a second ability or enemy turns the current weapon-specific paths into divergent code.

**Delivers:** `CombatActionDefinition`, loadout-facing weapon/ability definitions, damage/action signal contracts, authoring validation for timing/configuration/prefab topology, explicit repair tools, and runtime gizmos/overlay for phase and hit geometry.

**Addresses:** Fixed starter loadout foundation, data-authored enemy/encounter content, and fast inspection.

**Uses:** ScriptableObjects, existing test assemblies, prefab-local helpers, and the project-owned runtime injection seam.

**Avoids:** Silent fallback content, name/hierarchy contracts, editor-startup mutations, and premature generic RPG/action graph frameworks.

### Phase 3: Responsive Player Actions

**Rationale:** The player combat timeline is the immediate core-value proof and must be stable before enemy intent or encounter tuning can be judged.

**Delivers:** A player-owned common action runner migrating the starter weapon and implementing two fixed ability examples; phase/cooldown/cancel policy; hit volumes active only in valid windows; build-safe input and action HUD state.

**Addresses:** Responsive starter loadout, action-state feedback, and proof that a second ability is authoring work rather than a controller fork.

**Avoids:** Per-weapon timing/cooldown/hit-filter duplication and adding dodge/parry/block/stamina systems without evidence that positioning is insufficient.

### Phase 4: Enemy Intent and Readable Presentation Contract

**Rationale:** Fast combat is fair only after an enemy's warning, danger shape, active moment, and recovery come from the same shared timeline as damage. This must precede mixed encounters and spectacle.

**Delivers:** Enemy action controller integrated with the existing FSM; one melee and one spatial/ranged telegraphed attack; `TelegraphRecipe`/semantic cue contracts; render sorting/layer taxonomy; overlap test scene and Game-view readability review protocol.

**Addresses:** Two enemy archetypes, multi-channel feedback, and semantic telegraph/release/impact cues.

**Implements:** Gameplay-authoritative action/presentation-observer architecture and actor-local `TelegraphPresenter` ownership.

**Avoids:** VFX-driven collision, damage before warning, sorting-order folklore, and effects that obscure danger geometry.

### Phase 5: Presentation Runtime and Performance Envelope

**Rationale:** Effects should strengthen the now-proven combat language, while a dense encounter exposes allocation, overdraw, light, and navigation limits before content is authored around them.

**Delivers:** Feedback router, recipe-based `PresentationRuntime`, resettable pools for validated slash/popup/impact/death/telegraph lifecycles, one shader/material family, restrained 2D light accents, and Development Build profiler baselines for the intended simultaneous load.

**Addresses:** Semantic visual-effects toolkit and fast local diagnostic counters.

**Uses:** URP 2D Renderer, built-in Particle System, Shader Graph, Unity pooling APIs where compatible, Profiler, and Frame Debugger.

**Avoids:** Decorative effect overload, unrestricted dynamic lights, premature pooled lifecycle bugs, and unmeasured ECS/pathfinding rewrites.

### Phase 6: Clearing Journey and Encounter Proof

**Rationale:** The project needs a repeatable playable combat lesson, not a collection of technically correct actors. This phase composes verified systems into the end-to-end action portion of the slice.

**Delivers:** Fixed clearing spawn/loadout; three authored encounters in teaching order; completion reward and route/gate unlock; resettable `EncounterDirector`; objective guidance; path/camera-scale captures and dense-mix verification.

**Addresses:** Playable clearing-to-town run, self-teaching encounter sequence, readable objective feedback, and combat completion consequence.

**Avoids:** Endless-wave/procedural content, dynamic terrain/navigation requirements, and scaling encounter density beyond the measured envelope.

### Phase 7: Town Arrival and Thin Reward Consumers

**Rationale:** Town systems are valuable only when they consume the real reward from the clearing; after the combat loop works, this phase proves forward-compatible seams without building a town simulator.

**Delivers:** Town arrival trigger, shared interaction prompt, player/session-owned progress ledger, one NPC offer/complete objective, one atomic recipe at one station, and one progression threshold/unlock displayed in the HUD.

**Addresses:** Combat-to-town reward chain, quest/crafting/progression foundations, and the coherent arrival payoff.

**Implements:** Definition assets plus `TownRuntime`/interaction adapters with injected runtime context.

**Avoids:** Inventory grids, vendors, economy, dialogue graphs, crafting queues, persistence, save migration, and a global quest/crafting manager.

### Phase Ordering Rationale

- Composition, build-safe input, validation, and test evidence come first because later encounters and pools otherwise deepen existing wiring and fallback debt.
- Shared player and enemy action timing precedes visuals and encounter breadth so a telegraph is guaranteed to match authoritative damage, rather than becoming a parallel animation/VFX path.
- Render taxonomy and presentation contracts are established before VFX catalogue work; only then should pooling/performance investment lock in lifecycles.
- The clearing validates combat under representative pressure before the town consumes its reward. Town work remains a thin terminal hand-off, not an independent systems programme.
- Playable review and profiling occur throughout: correctness is automated, but feel, occlusion, and actual capacity are judged in a stable target-camera encounter and Development Build.

### Research Flags

Phases likely needing deeper research during planning:

- **Phase 1:** Confirm the installed Unity 6000.4.2f1 test/build invocation and the exact current input/MCP package integration before committing migration details.
- **Phase 4:** Validate installed URP 2D transparency/sorting, Light 2D, Shader Graph, and renderer settings in the actual editor; define the visual language through playable capture rather than documents alone.
- **Phase 5:** Profile the real Development Build to set active-mob, path, light, particle, draw-call, and allocation budgets; package/API compatibility for pooling diagnostics may need verification.
- **Phase 7:** Design the narrow ledger/reward identity rules carefully so definitions remain immutable and town state does not turn into premature persistence architecture.

Phases with standard patterns (skip research-phase unless the codebase exposes a blocker):

- **Phase 2:** ScriptableObject definitions, explicit editor validation, local prefab helpers, and focused EditMode tests are well-established Unity patterns.
- **Phase 3:** A finite action timeline, active-only hit volumes, and PlayMode timing tests are direct extensions of the existing component foundation.
- **Phase 6:** Authored spawn/clear/gate flow in one static scene is intentionally conventional; use discovery time for tuning rather than framework selection.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM | Existing package/configuration evidence and official Unity guidance support retaining the stack; optional package/API compatibility must be confirmed in installed Unity 6000.4.2f1. |
| Features | MEDIUM | Product boundaries are high-confidence project decisions; exact encounter count, defensive mechanics, and visual intensity need repeated play review. |
| Architecture | HIGH | Current codebase maps and the project's ownership/wiring constraints strongly support component evolution, explicit runtime injection, asset definitions, and local helpers. |
| Pitfalls | MEDIUM | Current wiring/allocation/test risks are direct; rendering capacity and combat readability thresholds need measurement in this game. |

**Overall confidence:** MEDIUM-HIGH. The recommended direction is well-supported and low-risk for the stated scope; exact tuning values and performance ceilings are deliberately empirical.

### Gaps to Address

- **Test and player-build evidence:** Restore a writable local/batch Unity Test Runner path that emits XML, then run EditMode, PlayMode, and a standalone input/slice smoke test before broad refactors.
- **Installed-version verification:** Check final URP 2D, Light 2D, Shader Graph, pooling, Code Coverage, and Memory Profiler compatibility in Unity 6000.4.2f1 rather than assuming Unity 6.0/6.1 documentation maps exactly.
- **Combat feel contract:** Establish a fixed clearing review protocol with one-enemy and mixed-enemy recordings at target resolution; record action timing, cancellation, telegraph clarity, hit confirmation, and occlusion before/after changes.
- **Capacity budgets:** Set thresholds from Development Build captures for simultaneous mobs/effects, GC allocations, paths, lights, particles, and draw calls before expanding the map or encounter density.
- **Navigation change boundary:** The first route should remain static. Treat dynamic terrain, destructible blockers, or walkability changes as a future design/research phase with explicit grid refresh ownership.
- **Tooling integration:** Reconcile the documented MCP server/package discrepancy and keep Unity MCP loopback, project-scoped, mutation-narrow, and editor-only; it must not become a gameplay dependency.

## Sources

### Primary (HIGH confidence)

- [.planning/PROJECT.md](../PROJECT.md) — product goal, scope, platform, visual/readability constraints, and ownership/wiring decision.
- [.planning/research/STACK.md](STACK.md) — established Unity package/runtime stack and deliberately excluded alternatives.
- [.planning/research/FEATURES.md](FEATURES.md) — vertical-slice behaviours, differentiators, dependency chain, and explicit deferred content.
- [.planning/research/ARCHITECTURE.md](ARCHITECTURE.md) — current-codebase-informed component boundaries, initialization/data-flow patterns, and build order.
- [.planning/research/PITFALLS.md](PITFALLS.md) — current-codebase-informed failure modes, phase warnings, and mitigations.
- Codebase maps cited by the research: `.planning/codebase/ARCHITECTURE.md`, `CONVENTIONS.md`, `CONCERNS.md`, `STRUCTURE.md`, and `TESTING.md` — direct evidence for present component seams, wiring debt, and test status.

### Secondary (MEDIUM confidence)

- [Unity 2D game creation workflow](https://docs.unity3d.com/6000.1/Documentation/Manual/2d-game-creation-wokflow.html) — URP 2D lights, particles, Shader Graph, post-processing, and player profiling guidance.
- [Unity ScriptableObject manual](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html) and [Unity `IObjectPool<T>` API](https://docs.unity3d.com/ja/current/ScriptReference/Pool.IObjectPool_1.html) — asset-backed definitions and pooling lifecycle support.
- [Unity 2D sorting](https://docs.unity.cn/Manual/2DSorting.html), [optimizing 2D lights](https://docs.unity3d.com/kr/current/Manual/urp/2d-lights-optimize-methods.html), and [profiling applications](https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html) — rendering and performance guardrails.
- [Unity Test Framework](https://docs.unity3d.com/ja/current/Manual/com.unity.test-framework.html) and [performance testing API](https://docs.unity.cn/6000.1/Documentation/Manual/com.unity.test-framework.performance.html) — local verification and benchmark approach.
- [Designing for Difficulty: Readability in ARPGs](https://www.gamedeveloper.com/game-platforms/designing-for-difficulty-readability-in-arpgs), [Enemy Attacks and Telegraphing](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing), and [Designing Game Feel: A Survey](https://arxiv.org/abs/2011.09201) — readability, staged pattern learning, and feedback principles.

### Tertiary (LOW confidence)

- Exact wind-up lengths, cancellation windows, particle/light counts, camera response, active-mob budgets, and the need for a defensive mechanic — validate through the fixed playable-review protocol and Development Build profiling, not generic benchmarks.

---
*Research completed: 2026-07-19*  
*Ready for roadmap: yes*
