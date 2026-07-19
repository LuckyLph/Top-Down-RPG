# Domain Pitfalls

**Domain:** Solo/local Unity URP top-down action-RPG foundation
**Researched:** 2026-07-19
**Overall confidence:** MEDIUM — the current-code findings are direct (HIGH); Unity/MCP guidance was verified against primary documentation, but combat readability still needs player observation in this specific game.

## Critical Pitfalls

### Pitfall 1: Combat telegraphs become an effect rather than a gameplay contract

**Confidence:** HIGH for the project risk; MEDIUM for the proposed design contract.

**What goes wrong:** A new weapon or enemy gains a wind-up animation, a slash particle, and a hitbox through separate ad-hoc paths. Timing drifts: an attack can deal damage before the player can read it, danger zones disappear beneath bursts, or a cancelled attack leaves an active hitbox/effect. With several enemies, the player cannot identify *who* is attacking, *where*, or *when it becomes dangerous*.

**Why it happens:** The current system has reusable hit and feedback pieces but no explicit shared attack-intent/phase contract. Fast iteration makes it tempting to attach each new visual directly to animation events or weapon-specific code.

**Consequences:** Combat feels unfair rather than demanding. Later cancellation, dodge, stagger, weapon-combo, and ability work becomes a web of exceptions; balancing numbers cannot fix ambiguous state.

**Prevention:** Before adding a second combat archetype, define a small, data-authored attack contract: owner/faction, facing or target geometry, wind-up, active, recovery, interruption rule, hit policy, and presentation hooks. The collision window and visible telegraph must be driven from that same phase source. Reserve a stable visual channel for player-danger geometry, use directionally meaningful silhouettes/ground shapes, and make hit confirmation visibly distinct from anticipation. Test every attack both alone and in a deliberately crowded encounter at the intended camera scale.

**Detection:** A tester asks why they took damage; the first response is to make an effect brighter; a hitbox needs a unique timing workaround; recordings show ground danger hidden by damage popups or slash sprites.

**Phase placement:** Combat-contract and enemy-telegraph phase, before weapon/ability breadth or visual-polish tooling.

### Pitfall 2: 2D effects fight sorting, lighting, and each other

**Confidence:** HIGH for the existing sorting exposure; MEDIUM for URP-specific performance implications.

**What goes wrong:** Gameplay-critical decals, sprites, particles, hit flashes, and light are authored without a visual-order taxonomy. A transient effect renders behind a wall or in front of a character at the wrong time; an additive burst hides a red warning; lighting makes two enemy states visually indistinguishable. Fixes accumulate as arbitrary `sortingOrder` offsets.

**Why it happens:** The project already applies runtime Y sorting, while URP 2D transparent rendering also resolves sorting layer/order, queue, camera distance, sorting group, and material. Unity documents that equal priorities fall back to an internal tiebreaker, so implicit or duplicated priorities are fragile.

**Consequences:** Art changes can create apparent combat bugs. Prefab variants become difficult to inspect, and visual regressions appear only in particular overlaps/camera positions.

**Prevention:** Publish a compact render contract before building the effect library: terrain/backdrop, world props, actors, actor-attached action effects, ground-danger indicators, screen/UI feedback, and debug overlays. Give each a documented sorting layer/order range and ownership rule; use a local `PrefabParts`/effect-root helper to own renderers instead of searching by child name or serializing scene links. Treat `YPositionSorter` as one authority, configure the URP 2D Renderer transparency axis deliberately, and validate the contract with a scene containing overlapping actors, tall props, decals, and lights.

**Detection:** Fixing draw order requires changing a magic number in more than one prefab; "first child renderer" is relied upon; an effect works in isolation but not beside foliage, a tilemap, or a second enemy.

**Phase placement:** Rendering/presentation foundation, before an effect catalogue or town art pass.

### Pitfall 3: Polished feedback produces a frame-time and GC cliff

**Confidence:** HIGH for current hot-path risk; MEDIUM for the exact capacity limit until profiled.

**What goes wrong:** A single sword slash, popup, and death look cheap in the sample scene; a clearing encounter makes them all happen together. The current attack/effect path creates and destroys objects, calls `Physics2D.SyncTransforms`, allocates overlap results, runs popup updates, and creates animation resources. Navigation can simultaneously perform synchronous, allocation-heavy A* searches.

**Why it happens:** Correctness was established for a minimal scene, with no mob-count/map-cell/effect budget or target-build benchmark. Editor play-mode performance is mistaken for player performance.

**Consequences:** Hitches land precisely on attacks and deaths, making responsive combat feel sluggish. Prematurely expanding a town/map turns a bounded optimization into a rewrite.

**Prevention:** Establish a representative stress scene early: the maximum intended active mobs for the slice, simultaneous melee hits/deaths/popups, lights, and a worst-case tilemap area. Capture baseline CPU/GPU frame time, GC allocation, draw calls, and path requests in a Development Build; Unity notes that Editor and player allocation/performance data differ. Pool short-lived slash/popup/death-effect instances only after defining their reset contract; change physics queries to bounded non-alloc forms; and put a node/time budget on path work before scaling encounter count or map size. Use one or two URP 2D light blend styles by default and profile any exception; Unity states each blend style uses a render texture.

**Detection:** A one-frame spike follows every multi-kill; `GC.Alloc`, `Physics2D.SyncTransforms`, or path search dominates a capture; new VFX requires "just a few more" lights; test frame time differs materially from the standalone Development Build.

**Phase placement:** Performance baseline during the combat/presentation foundation; enforce budgets again before the vertical-slice content pass.

### Pitfall 4: Content data and prefab wiring fail silently

**Confidence:** HIGH.

**What goes wrong:** A weapon/mob/effect prefab is incomplete but a runtime fallback makes it look partly functional. A renamed HUD/effect child, reordered renderer, missing ScriptableObject, or altered prefab hierarchy silently binds the wrong collaborator. In the current codebase, name/hierarchy discovery, empty configured references, global fallback lookup, and in-memory fallback assets can conceal these errors.

**Why it happens:** Unity permits many valid-but-unintended object graphs. Convenience fallbacks and generic `GetComponentInChildren` calls speed the first demo, while serialized scene references would create a different brittle dependency problem.

**Consequences:** Designers cannot trust authored assets, bugs reproduce only in builds or a particular prefab variant, and each content item needs programmer inspection.

**Prevention:** Keep ScriptableObjects as configuration/asset references, but make required gameplay data explicit and validate it. Add local helper components that expose owned prefab parts (visual, hitbox, UI slots) through narrow APIs; discover co-located owner components at runtime and allow a serialized field only for an intentional asset/configuration override. Use `OnValidate` only to constrain an asset's own fields—Unity documents it as editor-only and not a place to alter other scripts. Add an explicit menu/build validation that reports missing configuration, invalid timing, topology, sorting range, layers, and content IDs without mutating assets. Replace editor-startup regeneration with a user-invoked repair command that records Undo.

**Detection:** Opening Unity dirties assets; a missing asset produces a generic object instead of a precise error; a prefab restructure breaks HUD/VFX without a compile error; content authors ask which hidden child name/order is required.

**Phase placement:** Authoring/tooling foundation before multiplying weapons, mobs, town interactables, or effect prefabs.

### Pitfall 5: The small clearing-to-town proof becomes a miniature full game

**Confidence:** HIGH.

**What goes wrong:** Every town-facing idea—NPC schedules, inventories, economy, save/load, multiple biomes, quest graph, crafting tree, dialogue tooling, progression UI—enters the first slice. Combat, visuals, and arrival never reach a reviewable state.

**Why it happens:** Town systems are inherently connected and the project deliberately values reusable foundations, which can be mistaken for permission to build a general-purpose RPG platform.

**Consequences:** The slice loses its teaching arc, foundations are designed without play evidence, and local solo iteration becomes dominated by integration and content chores.

**Prevention:** Make the vertical-slice acceptance path non-negotiable: start equipped in a clearing, learn one or two readable combat decisions along a linear path, arrive in town, and expose only a single thin quest/crafting/progression hand-off. A town system may prove an interface and one authored example; it must not simulate a town. Defer save systems, economy, large dialogue tooling, multiple regions, and broad content until the arrival loop has repeated playtest evidence.

**Detection:** A phase cannot be demonstrated as a five-minute start-to-town journey; a new town feature has no dependency on the arrival experience; the number of authored content types grows faster than playable encounters.

**Phase placement:** Slice definition and roadmap guardrails first; revisit scope after each playable review.

## Moderate Pitfalls

### Pitfall 1: Weapon and ability variants fork the combat implementation

**Confidence:** HIGH.

**What goes wrong:** Each weapon gains its own controller, cooldown path, hit filter, animation trigger, and exception rules. Player responsiveness and enemy reaction differ by weapon accidentally.

**Prevention:** Keep player ownership/input separate from a reusable attack execution contract. Add an ability only when it can use an existing phase, targeting, damage, interruption, and feedback seam; first strengthen the seam with a second example rather than building a broad abstraction from one sword.

### Pitfall 2: Navigation assumptions leak into the authored path

**Confidence:** HIGH.

**What goes wrong:** The linear slice later gains blocked tiles, props, or dynamic obstacles, but the eager, static navigation grid and synchronous pathfinder either stall or navigate stale data.

**Prevention:** Keep the first path simple and static, profile the expected grid and repath rate, and define an explicit tile-change/refresh boundary before authoring mechanics that alter walkability. Preserve current `PathResult` semantics in tests whenever navigation changes.

### Pitfall 3: Tests give false confidence while the player build remains unverified

**Confidence:** HIGH.

**What goes wrong:** EditMode tests pass against `AssetDatabase` content and the single SampleScene while the build uses a different input path, effect/pool reset is untested, and batch runs do not produce results. The recorded headless test run already exits before writing XML.

**Prevention:** First repair a reproducible local/batch test invocation that writes results. Use EditMode tests for data validation, phase timing rules, and configuration errors; use PlayMode tests for physics boundaries, input, scene composition, attack-to-telegraph synchronization, pooled lifecycle, and arrival-path smoke tests. Prefer observable behaviour to private reflection/name/hierarchy assertions. Add a compact performance suite once the baseline scene exists; Unity's extension supports versioned tests and frame-based samples.

### Pitfall 4: Feel iteration is treated as a unit-test problem

**Confidence:** MEDIUM.

**What goes wrong:** The project has many correctness tests but no repeatable playable review. Timing changes are made by intuition from an unrepresentative quiet scene, so an attack is technically correct but feels unreadable or sluggish under pressure.

**Prevention:** Pair automated checks with a fixed review protocol: record the same clearing encounter at target resolution, play it with one and several enemies, score response/cancellation/telegraph/hit confirmation/occlusion, and retain before/after clips plus the changed data values. Automate invariants; judge feel through a stable encounter.

### Pitfall 5: Local Unity MCP is used as a bulk-authoring shortcut

**Confidence:** HIGH for the local operational risk; MEDIUM for general MCP guidance.

**What goes wrong:** The editor bridge applies broad scene/prefab changes against an uninspected or wrong active Unity instance, triggers a recompilation, then further mutations happen before errors are visible. Tooling becomes an unreviewed source of asset churn or is accidentally exposed beyond the local project.

**Prevention:** Keep `127.0.0.1` and `--project-scoped-tools`; MCP guidance favours least privilege and explicit capabilities. Before any mutation, inspect the active editor/scene and exact target, then perform one focused, reversible operation. Wait for compilation/domain reload, read Console errors and warnings, run the relevant Unity tests, and inspect the version-control diff before the next mutation. Never make MCP a runtime dependency, expose/tunnel it, run broad generated changes without a review checkpoint, or use it to bypass the project's prefab/ScriptableObject authoring model.

## Minor Pitfalls

### Pitfall 1: Editor and player input drift

**Confidence:** HIGH.

**What goes wrong:** The current player resolves an authored Input System asset in the Editor but creates a separate hard-coded map outside it, so a binding change can succeed in play mode and fail in a player build.

**Prevention:** Make the InputActionAsset an intentional prefab configuration reference, give fallback behaviour an explicit supported purpose, and add a player-build input smoke test before control expansion.

### Pitfall 2: Scene-global discovery becomes the default dependency mechanism

**Confidence:** HIGH.

**What goes wrong:** `FindAnyObjectByType`/player tags choose an arbitrary grid, target provider, HUD, or camera when the slice gets spawned objects or additive scenes.

**Prevention:** Use an owned gameplay context/spawn setup to provide scene services, discover co-located components from their owner, and keep serialized fields for configuration, assets, and deliberate overrides—not routine scene links.

### Pitfall 3: Profiled quality changes are made from the Scene view alone

**Confidence:** MEDIUM.

**What goes wrong:** An effect looks good in isolation but clips, sorts, scales, or overdraws badly in Game view at the actual camera and target resolution.

**Prevention:** Make Game-view capture, frame-debug/overdraw inspection, and the dense encounter part of the definition of done for each reusable effect prefab.

## Phase-Specific Warnings

| Phase topic | Likely pitfall | Mitigation |
|-------------|----------------|------------|
| Combat seams and enemy telegraphs | Visuals, hitboxes, and interrupts evolve independently. | Establish the shared attack phase/intent contract and test its timing before adding variants. |
| Presentation toolkit | Sorting-order folklore, additive overload, too many light blend styles. | Publish visual channels and URP configuration; approve each effect in the overlap/stress scene. |
| Authoring tools and data | Silent fallback, name-based hierarchy contracts, auto-mutating editor startup. | Add local prefab-part helpers plus explicit validation/repair commands; no implicit asset writes. |
| Clearing encounter and path | Navigation stalls or becomes stale after level authoring. | Keep initial terrain static; profile the worst grid/repath mix and define refresh ownership before dynamic tiles. |
| Town arrival foundations | Reusable foundations expand into untested full-system architecture. | Build one arrival interaction per system and defer simulation, economy, saving, and breadth. |
| Test/performance verification | Tests remain green only in Editor or do not execute headlessly. | Produce XML from a writable run; add player-build smoke, stress, and performance baselines. |
| Any MCP-assisted editor change | Wrong target, incomplete compilation, or unreviewed asset churn. | Inspect first; mutate narrowly; compile, inspect Console, test, and diff before continuing. |

## Sources

- Current-project evidence: [.planning/codebase/CONCERNS.md](../codebase/CONCERNS.md) and [.planning/codebase/TESTING.md](../codebase/TESTING.md) — **HIGH** confidence.
- [Unity: Optimize 2D lights](https://docs.unity3d.com/kr/current/Manual/urp/2d-lights-optimize-methods.html) and [Light Blend Styles](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal%4014.0/manual/LightBlendStyles) — **MEDIUM** confidence; current/manual guidance, with exact trade-offs to be measured in this project.
- [Unity: 2D Sorting](https://docs.unity.cn/Manual/2DSorting.html) — **MEDIUM** confidence.
- [Unity: Tracking garbage collection allocations](https://docs.unity3d.com/jp/current/Manual/performance-track-garbage-collection.html), [Profiling your application](https://docs.unity3d.com/2022.2/Documentation/Manual/profiler-profiling-applications.html), and [Performance testing API](https://docs.unity.cn/6000.1/Documentation/Manual/com.unity.test-framework.performance.html) — **MEDIUM** confidence.
- [Unity: ScriptableObject.OnValidate](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ScriptableObject.OnValidate.html) and [Unity Test Framework API](https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/api/UnityEngine.TestTools.html) — **MEDIUM** confidence.
- [MCP security best practices](https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices) — **MEDIUM** confidence; local-project specifics are corroborated by the current integration map.
