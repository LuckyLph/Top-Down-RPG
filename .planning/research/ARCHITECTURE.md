# Architecture Patterns

**Domain:** Brownfield Unity top-down action RPG vertical-slice foundation
**Researched:** 2026-07-19
**Overall confidence:** HIGH for current-state evidence and recommended boundaries; MEDIUM for Unity-version-specific presentation and pooling guidance.

## Recommended Architecture

Evolve the current component-oriented project; do not replace it with an ECS, a global service locator, a behavior-tree framework, or a generic RPG framework. The present split between player ownership, reusable combat components, enemy state machines, navigation, prefabs, and ScriptableObject configuration is the right foundation. The missing piece is an explicit composition boundary that gives runtime-spawned actors their scene services deterministically.

Use one scene-owned `SliceBootstrap` as the composition root for the clearing-to-town slice. It owns the active player, navigation grid, encounter director, presentation runtime, camera/HUD binding, and minimal town runtime. It discovers components inside its own hierarchy, then injects runtime-only dependencies into actors when they are spawned. This removes the current `FindAnyObjectByType`, player-tag, and first-canvas fallback paths from normal gameplay without replacing them with serialized cross-scene component links.

```text
Authored assets (configuration, prefabs, visuals)                 Editor-only authoring tools
┌─────────────────────────────────────────────────────────────┐  ┌────────────────────────────┐
│ CombatAction / Weapon / Enemy / VFX / Encounter definitions  │  │ inspectors, validators,     │
│ Quest / Recipe / Item / Progression definitions               │  │ previews, gizmos, reports   │
└────────────────────────────┬────────────────────────────────┘  └────────────┬───────────────┘
                             │                                                  │ validates
                             ▼                                                  ▼
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ SliceBootstrap (scene-owned composition root)                                                  │
│ NavigationGrid2D · PlayerSpawner · EncounterDirector · PresentationRuntime · TownRuntime       │
└───────┬───────────────────────┬──────────────────────────┬───────────────────────────┬─────────┘
        │ injects               │ injects                  │ dispatches                │ owns session state
        ▼                       ▼                          ▼                           ▼
┌───────────────┐      ┌─────────────────┐       ┌────────────────────┐      ┌───────────────────┐
│ Player actor  │      │ Enemy actor     │       │ Presentation layer │      │ Town seams        │
│ input/motion  │      │ FSM/nav/intent  │       │ VFX pools, lights, │      │ inventory, quests,│
│ loadout       │      │ action runner   │       │ HUD/camera effects  │      │ crafting, XP      │
└──────┬────────┘      └──────┬──────────┘       └─────────▲──────────┘      └─────────▲─────────┘
       │ action signals              │ action signals                 │                         │ rewards/events
       └─────────────────────────────┴────────────────────────────────┴─────────────────────────┘
                                             Combat contracts
                             action timing · hit volumes · damage requests · health outcomes
```

### Composition and lifecycle rule

`Awake` may cache co-located or owned-child components only. It must not start behavior that requires a player, navigation grid, gameplay camera, or other scene participant. A scene bootstrapper or spawn owner performs `Initialize(context)` before that actor can make decisions. `Initialize` must be explicit and idempotent; absent required context is a configuration error, not a cue to create fallback gameplay data.

For an enemy, `EnemySpawnService` creates the prefab, resolves its local `MobController`/`MobTargetProvider`, assigns the player transform through `SetTarget`, passes the scene `NavigationGrid2D` and authored `MobConfig` to `Configure`, and only then enables the behavior loop. A scene-placed enemy uses a tiny local `SceneEnemyInitializer` owned by the bootstrapper, not a hand-wired reference to the player or grid. Retain existing `Configure(...)` seams for tests and expand them rather than adding singleton access.

### Serialization rule

The project rule remains strict:

- Serialize values, layer masks, colours, asset references, prefab templates, animation clips, effect recipes, and deliberate test/debug overrides.
- Resolve a mandatory component on the same object with `GetComponent`, and an owned presentation part with a local helper or `GetComponentInChildren`.
- Inject dynamic scene relationships at runtime through the spawning/composition owner. Player target, navigation grid, HUD subject, camera subject, active effect runtime, and town-session runtime are all dynamic relationships.
- Do not serialize a player `Transform` into every enemy, a grid into every prefab, a canvas into every popup, or controls on a scene HUD. Avoid `FindAnyObjectByType` and tag lookup on normal runtime paths; allow them only as a one-time development diagnostic with an actionable warning.

## Component Boundaries

| Component / area | Responsibility | Owns / resolves | Communicates with | Must not own |
|---|---|---|---|---|
| `SliceBootstrap` / `SliceRuntime` | Compose the current vertical slice and maintain its lifecycle. | Its child scene services and configuration assets. | Player spawner, encounter director, presentation runtime, HUD/camera binders, town runtime. | Combat rules, enemy decision logic, or prefab-internal references. |
| `PlayerSpawnService` | Spawn/configure the single player and publish the active player actor to slice-owned consumers. | Player prefab asset and spawn marker configuration. | Camera/HUD binders, encounter spawner. | Input polling, combat timing, or UI details. |
| `PlayerController` | Read the configured Input System actions, move the rigidbody, and provide facing. | Co-located rigidbody, animator, and player combat controller. | `PlayerCombatController`. | Weapon mechanics, hitboxes, VFX, quest/crafting state. |
| `PlayerLoadout` + `PlayerCombatController` | Equip definitions and translate requested slots into combat actions. | Co-located action runner and player presentation anchors. | Combat-action contracts and HUD event subscribers. | Raw input binding, scene discovery, transient effect allocation. |
| `CombatActionRunner` | Execute one authored action timeline: windup, active windows, recovery, cooldown/cancel rules, and action signals. | Local hit-volume emitters. | `DamageReceiver`, presentation dispatcher, animation adapter. | Enemy AI state transitions and UI. |
| `DamageReceiver` / `Health` | Apply a typed damage request, emit one outcome, and become dead exactly once. | Co-located health. | Death lifecycle, feedback router, progression/loot listeners. | Damage popup creation, pooling, effect selection, or source-side cooldowns. |
| `EnemyController` (the evolved `MobController`) | Coordinate existing perception, path agent, motor, patrol, and state machine with initialized context. | Required co-located collaborators. | A target provider supplied by spawn ownership, navigation grid, enemy action controller. | Global player discovery and visual telegraph implementation. |
| `EnemyActionController` | Start enemy actions from the attack state and expose their timing to AI. | Co-located combat action runner. | `AttackRangeState`, telegraph presenter, damage contracts. | Navigation, perception, and actual VFX pooling. |
| `TelegraphPresenter` / `ActorPresentation` | Turn action phase signals into body animation, ground indicator, sprite flash, light, particles, or sound recipes. | Its actor-local animator/renderers/anchor helpers. | `PresentationRuntime`. | Damage timing, hit detection, and AI transitions. |
| `PresentationRuntime` | Pool and reset transient effects, own the gameplay camera reference, and apply global presentation policies. | Pool roots and effect prefab assets. | Feedback router and actor presenters. | Actor health, encounter completion, or quest state. |
| `EncounterDirector` | Spawn a configured group, inject actor context, track defeat/exit conditions, and publish completion. | Encounter definition and spawned actors. | Slice flow/gates and presentation events. | Enemy prefab internals or town UI. |
| `TownRuntime` | Hold the vertical-slice inventory, quest, crafting, and progression runtime state. | Plain runtime state keyed by definition assets/IDs. | Interactables, reward applier, town HUD. | Combat actor components, scene lookup, or persistent saving. |
| `Interactable2D` adapters | Convert nearby-player interaction into a configured quest giver, crafting station, or town entry action. | Local collider/prompt view and definition asset. | Injected `TownRuntime`. | Global quest/crafting singletons or dialogue trees. |

### Combat action data model

Replace the weapon-specific `PlayerWeapon -> SwordSlashAttack` coupling with a small common action model. Convert the current sword asset into a weapon/loadout definition that points to one `CombatActionDefinition`; do not prematurely create an abstract graph editor or an inheritance hierarchy for every weapon class.

| Definition or runtime type | Purpose | Authoring contents |
|---|---|---|
| `CombatActionDefinition` | Shared timing contract for a melee swing, projectile, dash strike, or enemy attack. | Windup/active/recovery durations, cooldown, hit-volume prefab or shape data, damage profile, movement/cancel policy, animation cue, telegraph recipe. |
| `WeaponDefinition` / `AbilityDefinition` | Loadout-facing identity and slot policy. | Name, icon, action reference(s), optional unlock requirement. |
| `DamageProfile` | Reusable gameplay-only description of a hit. | Base damage, damage type/tag, knockback/status hooks if later required. |
| `HitVolume2D` | Runtime-only hit detection during an action's active window. | Local collider(s), target layer mask, one-hit policy; resettable for pooling. |
| `DamageRequest` / `DamageOutcome` | Small C# structs for source, target, amount, hit position/direction, and killed/applied result. | Not Unity assets; do not make every subscriber parse a `GameObject` and an integer. |
| `ActionSignal` | Event emitted at action start, windup, active, hit, recovery, cancel, and complete. | Carries source actor, action definition, phase, position, and target/outcome when relevant. |

`CombatActionRunner` is the sole owner of an action timeline. A hit volume activates only during the definition's active window; it returns `DamageOutcome` through the receiver; visual effects only observe signals. This gives player combinations and enemy attacks the same experimentation surface while ensuring an enemy's warning always precedes its damaging window.

### Enemy readability and telegraph contract

Keep the explicit FSM. It is already appropriate for idle/patrol/chase/attack/return and is easier to inspect than a behavior-tree migration. Change only the attack path:

```text
AttackRangeState
  -> EnemyActionController.TryStart(EnemyAttackDefinition)
  -> CombatActionRunner emits WindupStarted
  -> TelegraphPresenter requests readable body + ground + optional light/particle effects
  -> CombatActionRunner opens hit volume at ActiveStarted
  -> DamageReceiver emits DamageOutcome
  -> CombatActionRunner emits Completed; AI may select the next action
```

Each `EnemyAttackDefinition` must declare a `TelegraphRecipe` and an authored minimum warning duration. Editor validation fails an attack that has a damage window before its telegraphable windup or has no readable cue. At runtime, player-readable cues use a limited palette: directional body pose/animation, ground-space indicator that matches the hit shape, and an optional short-lived accent effect. Never make a particle, shader, or animation event authoritative for collision damage.

`AttackRangeState` should ask the action controller whether an action can start, then wait for its result; it must stop calling `MeleeDamageDealer.TryDealDamage` as an instantaneous state-tick attack. Preserve `MeleeDamageDealer` only as a short-term adapter behind the first enemy action definition, then retire its duplicate cooldown/damage ownership once the action runner is proven.

### Presentation and visual-effects layer

The current `SwordSlashAttack`, `FloatingDamageText`, and `MobDeathAnimation` instantiate/destroy objects and individually create `PlayableGraph` work. Consolidate their lifecycle into a presentation layer without moving gameplay authority into presentation.

1. `CombatFeedbackRouter` subscribes to combat/action signals and chooses a recipe. It is a slice-owned consumer, not static state on `Health` or `DamageReceiver`.
2. `PresentationRuntime` owns effect pools. Use Unity's documented `IObjectPool<T>`/`ObjectPool<T>` API or an equivalent small wrapper, but require every pooled effect to implement explicit `Play`, `Stop`, and `ResetForPool` behavior before pooling it.
3. `EffectRecipe` assets select prefab, lifetime, local/world anchoring, sorting offset, tint, and optional URP 2D light. `ActorPresentation` resolves local anchors by helper components such as `ActorEffectAnchors`; it does not serialize cross-actor transforms.
4. `DamageReceiver` emits only a combat outcome. The router chooses floating number, hit flash, impact burst, and audio. Death handling similarly emits an outcome and lets a pooled death presentation play after the actor root becomes non-interactive.
5. `PresentationCameraContext` is injected by `SliceBootstrap` into screen-space consumers. Remove `Camera.main`, first-canvas, and static popup-canvas creation from normal paths.

URP's 2D Renderer is already present, so add Light 2D, Shader Graph materials, particles, and restrained post-processing as recipe capabilities, not independent gameplay systems. Use a small set of layer/sorting conventions and profile a representative simultaneous-hit scene before authoring high-density effects. Light, bloom, and shader effects are accents; they cannot obscure telegraph colour, silhouette, ground area, or target position.

### Authoring tools and validation

Make experiments fast through focused editor tools, not automatic asset mutation on editor startup.

| Tool | Location | Outcome |
|---|---|---|
| Combat action inspector + preview | `Assets/Editor/Combat/` | Displays total duration, phase ranges, hit shape, telegraph relationship, and test-play preview. |
| Enemy attack/telegraph validator | `Assets/Editor/Combat/` | Reports missing action/telegraph/presentation links and invalid warning-to-active timing. |
| Encounter authoring inspector | `Assets/Editor/World/` | Shows spawn count, enemy definitions, completion condition, and navigation placement checks. |
| Slice content validator | `Assets/Editor/Validation/` | Explicit menu/pre-build validation for player input asset, required definitions, prefab-local helpers, and slice bootstrap configuration. |
| Debug gizmos and runtime overlay | Owning runtime domains | Shows action ranges, windup/active phase, selected target, encounter state, and pool counts in development builds. |

Use `CreateAssetMenu`, `OnValidate`, custom inspectors, and explicit validation commands. `OnValidate` may clamp values and report invalid authoring contracts, but must not generate or overwrite prefabs/assets. Convert the current editor-startup slash-prefab bootstrap into an explicit repair command plus a validator. This avoids source-control churn and ensures a missing effect link is visible before play/build.

## Data Flow

### Player action to combat and presentation

```text
Input System asset
  -> PlayerController (input + facing + Rigidbody2D motion)
  -> PlayerCombatController / PlayerLoadout (requested slot)
  -> CombatActionRunner (timeline and cooldown)
  -> HitVolume2D (active window only)
  -> DamageReceiver -> Health
  -> DamageOutcome + ActionSignals
       ├-> CombatFeedbackRouter -> PresentationRuntime -> pooled effects / HUD / camera response
       ├-> DeathLifecycle -> actor deactivation + pooled death effect
       └-> EncounterDirector / reward listeners when the target dies
```

The `InputActionAsset` is a serialized player-prefab configuration asset. Eliminate the current editor-only `AssetDatabase` lookup and divergent hard-coded player-build bindings as part of this work; a code-created fallback may remain only as an explicit debug/test mode, never as normal release behavior.

### Enemy action and telegraph flow

```text
EnemySpawnService
  -> injects EnemyRuntimeContext (player target, grid, presentation runtime, authored definition)
  -> EnemyController ticks perception/FSM
  -> AttackRangeState requests EnemyActionController
  -> CombatActionRunner: WindupStarted -> ActiveStarted -> Hit/Outcome -> Recovery -> Completed
       ├-> TelegraphPresenter emits recipe effects during windup
       └-> HitVolume2D applies DamageRequest only during active
```

Enemy pathfinding remains behind the present `IPathfinder2D`/`PathRequest`/`PathResult` seam. Do not change A* as part of combat readability work. First remove grid/target global discovery and preserve partial-path semantics; then profile the clearing's intended active enemy count. Pool/schedule path work only when the profile requires it.

### Clearing-to-town flow

```text
SliceBootstrap
  -> clearing player spawn
  -> EncounterDirector activates EncounterDefinition
  -> spawned enemies receive injected context and combat definitions
  -> encounter completion publishes event
  -> SliceFlowController opens exit / advances journey state
  -> TownArrivalTrigger activates town-facing interactables
  -> QuestGiver / CraftingStation / Progression reward adapters call TownRuntime
  -> Town HUD reflects quest, inventory, recipe, and level events
```

Keep this first journey in one scene while the systems are being proven. Use authored encounter volumes/markers and a simple `SliceJourneyState` rather than additive loading, a general world-streaming framework, or a global game manager. Scene separation can come later only when a tested `SliceRuntime` handoff is required.

### Minimal town-system seams

Definitions are shared assets; runtime progress is plain per-session state. Never mutate a `QuestDefinition`, `RecipeDefinition`, or `ProgressionDefinition` to record player progress.

| Seam | Minimal vertical-slice implementation | Intentionally deferred |
|---|---|---|
| Inventory | `InventoryRuntime` holds item-definition counts and emits changes. | Equipment grids, loot rarity, vendors, persistence. |
| Quest | `QuestRuntime` tracks accepted/completed states and a small objective counter keyed by definition. | Branching dialogue, quest graphs, time limits, campaign state. |
| Crafting | `CraftingRuntime.TryCraft(recipe)` checks ingredient definitions and atomically changes inventory. | Stations with production queues, economy, recipe discovery UI. |
| Progression | `ProgressionRuntime` grants XP, recalculates level, and publishes unlocked action/recipe IDs. | Skill trees, respec, save migration. |
| Rewards | A narrow reward adapter grants item, XP, and quest progress from combat/interaction events. | A universal event bus or generic effect-language framework. |

The town adapters are configured with definition assets and get their `TownRuntime` from the bootstrapper at activation. They are not direct references from world NPCs to UI or player components.

## Patterns to Follow

### Pattern 1: Scene-owned runtime injection

**What:** A composition root discovers its owned scene services, creates/spawns actors, and calls their public initialization seam with a small runtime context.

**When:** Every actor needs a player target, navigation grid, presentation runtime, session runtime, or camera that cannot be expressed as prefab-local ownership.

**Example:**

```csharp
public void SpawnEnemy(EnemyDefinition definition, Vector3 position)
{
    GameObject enemyObject = Instantiate(definition.Prefab, position, Quaternion.identity, activeEnemiesRoot);
    MobController controller = enemyObject.GetComponent<MobController>();
    MobTargetProvider provider = enemyObject.GetComponent<MobTargetProvider>();

    provider.SetTarget(activePlayer.transform);
    controller.Configure(definition.MobConfig, navigationGrid, provider);
    controller.InitializeCombat(definition.PrimaryAttack);
}
```

The production implementation should make controller initialization idempotent and prevent `Awake` from creating fallback configuration before `Configure` is called. Tests can construct a small real GameObject graph and call the same seams.

### Pattern 2: Gameplay-authoritative action, presentation-observing effects

**What:** The combat runner owns timing and hit activation; all visuals consume immutable action/combat signals.

**When:** Every player attack, ability, enemy attack, hit, death, and damage popup.

**Example:**

```csharp
private void EnterActiveWindow()
{
    activeHitVolume.EnableFor(currentAction.DamageProfile, owner);
    ActionPhaseChanged?.Invoke(new ActionSignal(owner, currentAction, ActionPhase.Active));
}
```

`TelegraphPresenter` can respond to `Windup`, but cannot call `ReceiveDamage`, change cooldowns, or advance an AI state. A missing effect degrades presentation and logs validation failure; it never changes whether an attack hits.

### Pattern 3: Definitions for reuse, runtime state for progress

**What:** ScriptableObjects describe reusable immutable-ish content; MonoBehaviours and small C# runtime models hold instance and session state.

**When:** Weapons, actions, enemies, effects, encounters, quests, recipes, items, and progression thresholds.

**Example:**

```text
CombatActionDefinition (asset) -> CombatActionRunner (per actor state)
QuestDefinition (asset)        -> QuestRuntime (one session's accepted/completed state)
RecipeDefinition (asset)       -> CraftingRuntime (one session's inventory mutation)
```

This matches the existing `PlayerWeapon`, `MobConfig`, and terrain-profile pattern while preventing shared assets from becoming accidental save data.

### Pattern 4: Pool only resettable presentation objects

**What:** A pooled effect has one explicit reset contract covering lifetime, transforms, parent/anchor, sorting, colliders, particles, animation/playable state, subscriptions, and visibility.

**When:** Slashes, damage popups, impact bursts, death presentations, telegraph indicators, and temporary 2D lights after their lifecycle is stable.

**Example:**

```csharp
public void ResetForPool()
{
    StopAllCoroutines();
    activeLifetime = 0f;
    ownerAnchor = null;
    hitReceivers.Clear();
    gameObject.SetActive(false);
}
```

Do not pool the current objects before replacing `Destroy`-driven lifetime and `PlayableGraph` ownership with a tested reset path. A broken pool leaks state between attacks and is worse than a short-lived allocation during early experimentation.

## Anti-Patterns to Avoid

### Ambient scene lookup as normal wiring

**What:** `FindAnyObjectByType`, `Camera.main`, player-tag discovery, first-canvas lookup, and tag-driven HUD binding determine essential relationships.

**Why bad:** These choices are nondeterministic with more than one mob, camera, canvas, navigation grid, or future scene transition. They also obscure a missing composition step.

**Instead:** Let the slice owner inject dynamic context. Keep any fallback lookup only for development diagnostics, and make it single-use/actionable.

### Serialized component fields for co-located or scene wiring

**What:** Prefab components hold serialized references to another component on the same root or a scene actor merely to connect behavior.

**Why bad:** They duplicate prefab topology, break on routine hierarchy edits, and violate the project convention.

**Instead:** Use `[RequireComponent]` plus `GetComponent` for mandatory siblings, local helper components for owned children, and runtime injection for non-owned participants. The existing `PlayerController.weaponController`, `PlayerWeaponController.playerController`, `MobController.navigationGrid`, `MobController.targetProvider`, and `MobPerception2D.targetProvider` are migration candidates; retain only explicit debug/test override fields where genuinely needed.

### VFX-driven collision and opaque attacks

**What:** Animation events, particles, or a shader decide when an attack damages, while a state tick applies damage without an authored windup.

**Why bad:** Timing drifts when presentation changes, cannot be easily tested, and gives the player no reliable response cue.

**Instead:** Define action timing once in gameplay data. Visuals mirror that data; enemy active windows cannot begin before a validated telegraph window.

### Runtime fallback content concealing invalid assets

**What:** Missing weapon/effect/mob configuration silently creates a default object or configuration at runtime.

**Why bad:** Experiments appear to work while authored content is invalid, and builds differ from editor behavior.

**Instead:** Validator plus actionable warning/error, disabled dependent feature in release behavior, and explicit repair tools. Retain fallbacks only behind a deliberate debug-mode setting.

### Prematurely broad town or world framework

**What:** Build dialogue trees, persistence, a generic event bus, an economy, streaming, or a universal interaction graph before the clearing-to-town loop proves its seams.

**Why bad:** It delays combat iteration and produces abstractions unsupported by real content.

**Instead:** One encounter chain, one quest interaction, one crafting recipe, and one progression reward, all using definition/runtime-state boundaries that can grow after playtesting.

## Dependent Build Order

| Order | Deliverable | Depends on | Verification gate |
|---|---|---|---|
| 1 | Slice composition root and explicit actor initialization | Existing prefabs, `NavigationGrid2D`, `MobController.Configure` test seam. | PlayMode: player, camera, HUD, and two spawned enemies bind to the intended context without `FindAnyObjectByType`/tag fallback. |
| 2 | Authoring contracts and validators | Slice bootstrap; existing ScriptableObject workflow. | EditMode: action/enemy/encounter definitions reject missing assets, invalid phase ordering, and invalid prefab-local topology; editor startup does not mutate assets. |
| 3 | Combat action runner and player loadout migration | Action definitions and actor initialization. | EditMode: windup/active/recovery/cooldown order, one-hit policy, cancellation rules; PlayMode: sword action has unchanged responsive baseline. |
| 4 | Enemy action controller and readable telegraphs | Combat action signals; existing FSM and perception/navigation. | PlayMode: attack warning starts before active hit; moving out of the declared shape avoids damage; AI preserves chase/return behavior. |
| 5 | Presentation runtime and effect pooling | Combat and telegraph signals; validated effect recipes. | PlayMode/profiler scene: repeated slash, hit, death, popup, and telegraph effects reset correctly with bounded allocations; visuals do not alter hit timing. |
| 6 | Clearing encounter flow and tuning tools | Spawning/injection, combat, telegraphs, presentation. | A playable path teaches one action/telegraph response, completes an encounter, opens the route, and reaches town. |
| 7 | Minimal town quest/crafting/progression seams | Slice flow, inventory/session runtime, interaction adapters. | PlayMode: town arrival accepts/completes one quest, crafts one recipe with atomic inventory changes, and grants one XP/unlock reward. |

**Ordering rationale:** Injection must precede more spawned enemies, pooled presentation, and town bindings because those systems otherwise deepen the existing nondeterministic lookup debt. Definitions/validation come before the action runner so combat experimentation is asset-driven from its first implementation. Player and enemy actions share the same timing core, so telegraphs should immediately follow action execution rather than become a parallel system. The clearing proves the full combat loop before the town adds only the smallest forward-compatible session-state seams.

## Scalability Considerations

The immediate product is a local vertical slice, not a user-scaled service. Treat concurrent actors, grid cells, and transient effects as the relevant capacity axes; no capacity is currently profiled.

| Concern | Clearing slice baseline | Before increasing encounter density | Out of current scope |
|---|---|---|---|
| Navigation | Preserve existing grid/A* contracts and profile representative cells/mobs. | Add a path expansion budget/collection reuse only if profiling identifies stalls. | Large dynamic worlds, jobs/ECS navigation, or streaming. |
| Transient VFX | Pool validated slash/popup/death/telegraph lifecycles and set a per-recipe cap. | Profile simultaneous hits, lights, overdraw, and allocation rate. | Unlimited particle/light spectacle or per-hit canvas creation. |
| Scene wiring | One `SliceBootstrap` and one active player context. | Add a tested context handoff before additive scenes. | Multiplayer, split-screen, or arbitrary multi-world routing. |
| Town state | In-memory session state with clean definition/runtime separation. | Add persistence only after a real save/load decision and migration design. | Backend/cloud services or account systems. |

## Sources

### Codebase evidence — HIGH confidence

- [.planning/PROJECT.md](../PROJECT.md) — product goals, constraints, and clearing-to-town scope.
- [.planning/codebase/ARCHITECTURE.md](../codebase/ARCHITECTURE.md), [.planning/codebase/CONVENTIONS.md](../codebase/CONVENTIONS.md), and [.planning/codebase/CONCERNS.md](../codebase/CONCERNS.md) — current boundaries, wiring convention, and known debt.
- `Assets/Scripts/AI/Core/MobController.cs`, `MobPerception2D.cs`, `MobPathAgent2D.cs`, and `MobTargetProvider.cs` — explicit FSM/injection seam plus current global fallback wiring.
- `Assets/Scripts/Combat/Health.cs`, `DamageReceiver.cs`, `SwordSlashAttack.cs`, `FloatingDamageText.cs`, and `MobDeathAnimation.cs` — current combat events and transient-presentation lifecycle.
- `Assets/Scripts/Player/PlayerController.cs` and `PlayerWeaponController.cs` — input, movement, current weapon coupling, and serialized sibling fields.

### Unity documentation — MEDIUM confidence

- [Unity 6 ScriptableObject manual](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html) — asset-backed shared data and custom Inspector support.
- [Unity 6 2D game creation workflow](https://docs.unity3d.com/6000.1/Documentation/Manual/2d-game-creation-wokflow.html) — URP 2D lighting, particles, and post-processing as 2D presentation capabilities.
- [Unity `IObjectPool<T>` API](https://docs.unity3d.com/ja/current/ScriptReference/Pool.IObjectPool_1.html) — supported pooling interface and lifecycle operations.

## Research Gaps and Phase Flags

- The current test suite has no verified recent batch result. Before modifying composition, combat timing, or pooling, run the Unity Test Runner in a writable Unity environment and capture EditMode/PlayMode results.
- Unity documentation found through the fallback search is Unity 6.0/6.1, while this project uses 6000.4.2f1. Validate final 2D Renderer, Light 2D, Shader Graph, and pooling API details in the installed editor before implementation; these are presentation enhancements, not a reason to add packages now.
- Exact feel targets—windup length, cancel windows, visual intensity, camera response, and active-mob budget—require playable clearing review and profiling, not architecture research alone.
- Dynamic navigation updates are not needed for the first static path, but become a dedicated research/design task before crafting/world interactions can alter traversable terrain.
