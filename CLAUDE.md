# Top-Down RPG

Unity 6 (URP 17.4, 2D) top-down RPG. Input System, VContainer for DI, DualGrid tilemaps, UnityMCP for editor automation.

## Project layout

- `Assets/Scripts/Core` (`TopDownRPG.Core`): boot, scene flow, input, clock, camera, shared services.
- `Assets/Scripts` (`TopDownRPG.Gameplay`): AI, combat, player, world, UI, effects, composition roots.
- `Assets/Scripts/Dev` (`TopDownRPG.DevTools`), `Assets/Editor` (`TopDownRPG.Editor`): dev-only and editor-only code.
- `Assets/Tests/Editor` (EditMode) and `Assets/Tests/PlayMode`: test assemblies.

`Docs/Architecture.md` is the code map (assemblies, boot/scene flow, DI scopes, one section per system, configs, events, tests, known gaps). Read it to orient before working on an unfamiliar system. Keep it current: any change that adds, removes, renames or rewires a type, system, scope registration, event, config asset or test suite must update the matching section in the same commit. Fixing an item listed under "Known gaps and oddities" means removing it from that list.

`Docs/Multiplayer.md` is the plan for online co-op (host-and-play, 2–4 players). Follow its "Rules for new code" in all new work, and tick off its phase checklist as steps land.

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
- Put tunable data in ScriptableObjects (e.g. `MobConfig`, `GameplaySettings`), not magic numbers in code. Treat ScriptableObject assets as immutable at runtime.
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
- Sprite sort order is handled by `YPositionSorter` and sorting layers; do not hack `z` positions.
- Use Pixel Perfect/consistent PPU settings for sprites; import via the Aseprite importer where sources exist.

### Logging and errors
- Use `Debug.Log*` sparingly and never in hot paths. Use `LogWarning`/`LogError` with context (`this`) so the console can ping the object. Remove temporary debug logging before committing.
- Fail loudly on missing inspector references (validate in `Awake` or `OnValidate`) rather than silently continuing.

## Testing

- Logic should be testable in EditMode with fakes (see `ManualClock`). Add or update tests in `Assets/Tests/Editor` when changing AI, combat, pathfinding, or input behaviour. PlayMode tests are for things that need the engine loop.
- Run tests through UnityMCP `run_tests`. After a compile, wait for it to finish and check `read_console` for errors before running.
- A change is not done until it compiles with no new console errors or warnings and the relevant tests pass.

## Working conventions

- Commit directly on `main`; no feature branch first. Use conventional-commit style messages (`feat(ai): ...`, `fix(ai): ...`, `tune(ai): ...`).
- Match surrounding code style: 4-space indent, Allman braces, `private` fields in camelCase without underscore prefix, PascalCase types/members.
- Avoid code comments. The only comments allowed are class and method descriptors (XML doc summaries). Design rationale, tuning notes, explanations of behaviour, and anything else belongs in a doc file under `Docs/`, not inline in code. Do not leave commented-out code or TODO notes in source.
- Prefer editing existing files over creating new ones; do not add speculative abstractions.
- Check the official Unity/package docs (`unity_docs` tool, or the web) before reverse-engineering behaviour.
- New `*LifetimeScope.cs` files can be overwritten by Unity's script template on first import; verify the contents after creating one.
