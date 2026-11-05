# Top-Down RPG

Unity 6 (URP 17.4, 2D) top-down RPG. Input System, VContainer for DI, DualGrid tilemaps, UnityMCP for editor automation.

## Project layout

- `Assets/Scripts/Core` (`TopDownRPG.Core`): boot, scene flow, input, clock, camera, shared services.
- `Assets/Scripts` (`TopDownRPG.Gameplay`): AI, combat, player, world, UI, effects, composition roots.
- `Assets/Scripts/Dev` (`TopDownRPG.DevTools`), `Assets/Editor` (`TopDownRPG.Editor`): dev-only and editor-only code.
- `Assets/Tests/Editor` (EditMode) and `Assets/Tests/PlayMode`: test assemblies.

`Docs/Architecture.md` is the code map (assemblies, boot/scene flow, DI scopes, one section per system, configs, events, tests, known gaps). Read it to orient before working on an unfamiliar system. Keep it current: any change that adds, removes, renames or rewires a type, system, scope registration, event, config asset or test suite must update the matching section in the same commit. Fixing an item listed under "Known gaps and oddities" means removing it from that list.

`Docs/Roadmap.md` lists every plan and where it stands, what comes next, and the features planned for later that have no plan of their own yet (multiplayer testing among them). Keep it current: update it when a plan is added, a phase lands, a branch merges into `main`, or work moves to later.

`Docs/Multiplayer.md` is the plan for online co-op (host-and-play, 2–4 players). Follow its "Rules for new code" in all new work, and tick off its phase checklist as steps land.

`Docs/PlayerControls.md` is the spec for the click-to-move controls and ability rework. Follow it when working on player input, movement, auto attacks or abilities, and tick off its phase checklist as steps land.

`Docs/DamageAndStatusEffects.md` is the spec for typed damage, resistances, immunities and status effects. Follow it when working on damage, healing, statuses or crowd control, and tick off its phase checklist as steps land.

`Docs/KeyBindings.md` is the spec for remappable controls, the options menu, the in-game menu and the local settings file. Follow it when working on bindings, menus or player preferences, and tick off its phase checklist as steps land.

`Docs/Animation.md` is the spec for character animation: the character rig and the baker that generates its sprite sheets, the runtime animator, gaits by speed, the attack timeline with its cancel rules, and weapon movesets. Follow it when working on character art, character animation, attack timing or weapon visuals, and tick off its phase checklist as steps land.

`Docs/AsepriteCharacters.md` is the plan for characters, effects and projectiles drawn by hand in Aseprite and imported into the same animation sets and sprite sets the baker and the effect and weapon sprite writers write: the file and tag conventions, the contact markers, the lantern's layers, the enforced import settings and the import recipes. Follow it when working on Aseprite files, their import or hand-drawn characters, effects or projectiles, and tick off its phase checklist as steps land.

`Docs/TerrainBlending.md` is the spec for drawing the ground with a blending shader instead of dual-grid tilesets. Follow it when working on ground rendering, terrain materials, the terrain brush or the ground and wall tilesets, and tick off its phase checklist as steps land.

`Docs/TerrainTypes.md` is the plan for the next ground materials (dirt, dry grass, meadow, tall grass, planks, plaza, shallows, ocean, lava, snow, ice, raised ground), 16 ground layers per area and per-area layer sets. Follow it when adding a terrain type, changing how many or which layers an area uses, and tick off its phase checklist as steps land.

`Docs/TerrainLayers32.md` is the record of raising a terrain renderer's layers from 16 to 32, done and on `main`: the wide control map and the shader's `_TERRAIN_WIDE` variant past 16 layers, the stress scene and the area template. Follow it when working on the control map, the layer cap, the stress scene or the area template.

`Docs/WorldStreaming.md` is the plan for seamless worlds that players travel between on their own: chunk scenes streamed around the players, worlds at separate origins, navigation and terrain across chunk borders, per-chunk networking and world persistence. Follow it when working on scene flow, areas or worlds, chunk loading, world navigation or world saves, and tick off its phase checklist as steps land.

`Docs/WorldGeneration.md` is the plan for generating worlds in the editor: a world generator that composes a world from the area generators' layouts as regions, with wilds, roads and the sea between them, and writes it into the world's chunk scenes, starting with the Overworld, the first world. Follow it when working on world generation, the regions, the Overworld, or regenerating a world without breaking saves or hand work, and tick off its phase checklist as steps land.

`Docs/PropGeneration.md` is the plan for natural props generated in the editor (trees, bushes, rocks, stumps, logs, plants) in looks for each terrain, written as sprites and prefabs and placed by the area and world generators. Follow it when working on props, their recipes, the prop canvas, prop prefabs or prop placement, and tick off its phase checklist as steps land.

`Docs/WorldScale.md` is the plan for worlds up to the 16 x 16 chunk limit: sprites sorted by the 2D renderer's custom axis instead of a 16-bit sorting order that runs out past 327 units, a chunk's cost measured in a player build with the streaming benchmark, and a chunk load made lighter if those numbers call for it. Follow it when working on sprite sorting, the streaming benchmark, player builds or chunk load costs, and tick off its phase checklist as steps land.

`Docs/Lighting.md` is the plan for lighting: light sampled once per art pixel through one lit sprite shader and the terrain graph, spells that light the world around them, and a day and night with a sun that moves and the shadows of props, units and cliffs that follow it, over the painted top-left light; then more light props, light that walls and props stop, clouds and a sky per region, a lantern in hand, reflections on water, and relief with shadows that know the ground. Follow it when working on lights, light props, lit shaders, the time of day, sun shadows, clouds, reflections or relief, and tick off its phase checklist as steps land.

`Docs/KnownGaps.md` is the plan for closing the known gaps no other plan covers (Architecture.md's Known gaps and oddities), each fixed or recorded as accepted: the code within CLAUDE.md's rules, orders and the save, waystones and exits, what each machine shows of the others, worlds and saves, the terrain and its brush, and light and animation. Follow it when working on any of those gaps, and tick off its phase checklist as steps land.

`Docs/LightingGaps.md` is the plan for closing the known gaps the lighting left, and the ocean's waves along coasts that run north to south: waves measured along the coast, lamps' shadows on units and props, sun shadows across a caster, light shadows at the smallest zoom, the lava glow map and the first caster's wake off the load, the night previewed in the editor, the editor's shader oddities, and a camera on the art grid. Follow it when working on those, and tick off its phase checklist as steps land.

Use only art generated in this project (recipes and editor tools) or drawn in Aseprite for it, by hand or by Claude through the Aseprite MCP server (`Docs/AsepriteCharacters.md`'s Drawing with Claude), and imported through an Aseprite recipe (character, effect or projectile; `Docs/AsepriteCharacters.md`); import no third-party art.

Respect the assembly dependency direction: Core must not reference Gameplay; runtime code must not reference Editor or DevTools. Put new code in the assembly that matches its role rather than adding asmdef references to make something compile.

## Unity good practices

### Lifecycle and performance
- Do not do per-frame work you can avoid. No `Find*`, `GetComponent*`, `Camera.main`, LINQ, string building, or allocations in `Update`/`FixedUpdate`/`LateUpdate`. Cache references in `Awake`/`Start`.
- Use `Awake` for self-initialisation, `Start` for anything that depends on other objects. Never rely on cross-object `Awake` ordering.
- Physics and Rigidbody2D movement belongs in `FixedUpdate`; input sampling and visuals in `Update`/`LateUpdate`. Use `Time.deltaTime` in `Update` and `Time.fixedDeltaTime` in `FixedUpdate`, or go through `IClock`.
- Subscribe in `OnEnable`, unsubscribe in `OnDisable`. Every event/`UnityAction`/C# event subscription needs a matching unsubscription.
- Pool frequently spawned objects (slashes, damage popups, VFX) instead of `Instantiate`/`Destroy` churn.
- Prefer non-allocating APIs (`OverlapCircleNonAlloc`/`ContactFilter2D` overloads, reused `List<T>`/arrays, `TryGetComponent`).
- Compare tags with `CompareTag`, never `== "Tag"`. Use layer masks configured in the inspector over hard-coded layer numbers.
- Never use `==`/`??`/`?.` semantics on `UnityEngine.Object` carelessly: destroyed objects are "fake null". Use `== null` / implicit bool for Unity objects, not `?.` or `??`.

### Serialization and inspector
- Expose fields as `[SerializeField] private`, not `public`. Expose read access via properties.
- Use `[Header]`, `[Tooltip]`, `[Min]`/`[Range]` so inspector data is self-documenting and validated. Add `[RequireComponent]` for hard dependencies and `[DisallowMultipleComponent]` where appropriate.
- Put tunable data in ScriptableObjects (e.g. `MobConfig`, `GameplaySettings`), not magic numbers in code.
- Runtime code never writes to an asset: ScriptableObjects, prefabs, input action assets, materials, settings. Expose assets read-only (private serialized fields, getters, no public setters or mutating methods outside `#if UNITY_EDITOR`). When runtime state has to diverge from the asset, work on a copy the owning scope creates with `Object.Instantiate` and destroys on dispose, as `RuntimeInputActions` does for the input asset. Change prefabs only on their instances. Tests build their own instances (`CreateInstance` or a static `Create` factory) instead of changing project assets.
- Never rename or move a serialized field, class, or script file without `[FormerlySerializedAs]` or moving the `.meta` with it: GUIDs and field names are what scenes and prefabs reference.
- Use `OnValidate` for cheap inspector validation only; no scene lookups or asset writes there.
- Keep one `MonoBehaviour` per file, and the file name must match the class name.

### Architecture
- Prefer composition: small single-purpose components over large ones. Plain C# classes (no `MonoBehaviour`) for logic that does not need the engine, so it is unit-testable.
- Use VContainer for dependencies: constructor injection for plain classes, `[Inject]` method injection on components, registration in the `*LifetimeScope` classes under `Composition`. Do not add singletons, static mutable state, or `FindObjectOfType` service lookups.
- Depend on interfaces at seams (`IClock`, `IRandom`, `IPlayerInput`, `IPlayerRegistry`) so tests can substitute fakes.
- Communicate between systems with events/interfaces, not by reaching into other objects' internals. Avoid `SendMessage`, string-based lookups, and `Invoke("name")`.
- Keep the AI state machine states small and free of engine lookups; share behaviour via the mob's components.
- Use coroutines sparingly; prefer explicit state/timers driven by the clock so behaviour is deterministic and testable.

### Scenes, prefabs, assets
- Reusable things are prefabs. Configure instances with overrides sparingly; prefer prefab variants for real variation.
- Keep scenes merge-friendly: do not reorder or churn serialized data incidentally. Do not commit unrelated scene/asset diffs (e.g. TextMesh Pro font asset regeneration).
- Never hand-edit `.meta` files, `.unity`, `.prefab` or `.asset` YAML unless there is no editor-based alternative. Prefer UnityMCP (or an editor script) so Unity serializes the change.
- Always commit `.meta` files together with their assets; never delete a `.meta` on its own.
- Use the `Assets/Scripts/...` folder structure by feature. Do not put runtime code under `Editor` folders or use `UnityEditor` APIs in runtime assemblies; if unavoidable, wrap with `#if UNITY_EDITOR`.

### 2D specifics
- Use `Rigidbody2D`/`Collider2D` APIs (not 3D ones). Move bodies through the Rigidbody, not `transform.position`, when physics is involved.
- Sprites sort by the 2D renderer's custom axis within their sorting layer, a lower one in front (Docs/WorldScale.md). Keep every order on the `Entities` layer at 0: sort a unit, projectile or effect as a `SortingGroup` on its root, and a prop by its sprite's pivot (Sprite Sort Point Pivot). Parts inside a unit's sorting group may order themselves (a carried lantern's layer at 1 over its body), the group staying at 0. Never write sorting orders from code, and do not hack `z` positions.
- Use Pixel Perfect/consistent PPU settings for sprites; import via the Aseprite importer where sources exist.

### Logging and errors
- Use `Debug.Log*` sparingly and never in hot paths. Use `LogWarning`/`LogError` with context (`this`) so the console can ping the object. Remove temporary debug logging before committing.
- Fail loudly on missing inspector references (validate in `Awake` or `OnValidate`) rather than silently continuing.

## Testing

- Logic should be testable in EditMode with fakes (see `ManualClock`). Add or update tests in `Assets/Tests/Editor` when changing AI, combat, pathfinding, or input behaviour. PlayMode tests are for things that need the engine loop.
- Run tests through `ProjectTestRun.Start(mode, fixtures, categories, tests)` from UnityMCP `execute_code`, not `run_tests`, and wait for `Temp/TestRunSummary.txt` with one background shell loop instead of polling (see the Tests section of `Docs/Architecture.md`). After a compile, wait for it to finish and check `read_console` for errors before running.
- While working, run only the test classes the change touches. Run the full EditMode and PlayMode suites only when a feature is done, before merging it into `main`. Run the `ShaderCompile` category (explicit, nearly four minutes) after changing a shader, and before merging one.
- A change is not done until it compiles with no new console errors or warnings and the relevant tests pass.

## Working conventions

- Ask which branch to work on (`main` or a feature branch) before committing. Use conventional-commit style messages (`feat(ai): ...`, `fix(ai): ...`, `tune(ai): ...`).
- The game is pre-alpha (Player Settings version `0.1.0-prealpha`). Never change the build version unless the user asks for it, even when the network protocol or a world in the build changes, and keep it a pre-alpha number when they do. Testing multiplayer by hand (Multiplayer Play Mode, a second machine) is a later feature of its own (`Docs/Roadmap.md`) and out of scope until then: design every feature for multiplayer as the docs say and cover it with the PlayMode tests that run in the editor (the in-process host and client), but give feature plans no Multiplayer Play Mode checks or checkboxes.
- Match surrounding code style: 4-space indent, Allman braces, `private` fields in camelCase without underscore prefix, PascalCase types/members.
- Avoid code comments. The only comments allowed are class and method descriptors (XML doc summaries). Design rationale, tuning notes, explanations of behaviour, and anything else belongs in a doc file under `Docs/`, not inline in code. Do not leave commented-out code or TODO notes in source.
- Prefer editing existing files over creating new ones; do not add speculative abstractions.
- Check the official Unity/package docs (`unity_docs` tool, or the web) before reverse-engineering behaviour.
- New `*LifetimeScope.cs` files can be overwritten by Unity's script template on first import; verify the contents after creating one.
- Never publish an artifact (a claude.ai page). Share previews, images and reports as files in the scratchpad or the repo, with their paths in the reply.
