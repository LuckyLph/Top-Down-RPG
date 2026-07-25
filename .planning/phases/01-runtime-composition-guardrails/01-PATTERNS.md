# Phase 01: Runtime Composition & Guardrails - Pattern Map

**Mapped:** 2026-07-24  
**Files analyzed:** 20 expected new or modified files  
**Analogs found:** 18 / 20 (the PowerShell orchestrator has no repository analog; the composition root is a deliberate composition of several role-matched seams)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality | Scene / Prefab / Test Integration |
|---|---|---|---|---|---|
| `Assets/Scripts/Runtime/SliceCompositionRoot.cs` | component / composition root | event-driven bootstrap | `Assets/Scripts/AI/Core/MobController.cs` | role-match | New active scene root above an inactive owned runtime subtree in `SampleScene` |
| `Assets/Scripts/Runtime/ISliceOptionalContext.cs` | interface / extension contract | event-driven | `Assets/Scripts/AI/Core/IMobState.cs` | exact structural match | Implement only on real owned encounter/town runtime owners; none is required in Phase 1 |
| `Assets/Scripts/Player/PlayerInputAdapter.cs` | component / input adapter | event-driven | `Assets/Scripts/Player/PlayerController.cs` | role-match | Co-located on `Player.prefab`; its `InputActionAsset` is a prefab configuration reference |
| `Assets/Scripts/Player/PlayerController.cs` | component / player controller | event-driven + physics | itself, refactored around `PlayerInputAdapter` | modify-existing | Remains on `Player.prefab`; accepts adapter-owned action flow and writes velocity in `FixedUpdate` |
| `Assets/Scripts/UI/PlayerHudView.cs` | component / prefab-local view helper | transform / local lookup | `Assets/Scripts/UI/HudController.cs` | role-match | Co-located on the HUD prefab; owns `HealthFill`, `HealthText`, and `WeaponIcon` lookup |
| `Assets/Scripts/UI/HudController.cs` | component / presentation controller | event-driven | itself, with explicit bind seam | modify-existing | `PlayerHudCanvas.prefab`; receives `Health` and `PlayerWeaponController` from the root |
| `Assets/Scripts/Camera/CameraFollow2D.cs` | component / camera follower | request-response per frame | itself (`SetTarget`) | exact injection seam | `Main Camera` in `SampleScene`; root calls `SetTarget(player.transform)` before activation |
| `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` | component / navigation service | transform / bootstrap | itself (`BuildGrid`) | exact initialization seam | `Grid` hierarchy in `SampleScene`; root validates then builds before dependent runtime contexts |
| `Assets/Scripts/AI/Core/MobController.cs` | component / optional encounter owner | event-driven bootstrap | itself (`Configure`) | exact injection seam | Conditional only while a real Weasel/encounter is retained under the runtime subtree; no global fallback |
| `Assets/Scripts/AI/Core/MobTargetProvider.cs` | component / target-provider collaborator | request-response | itself (`SetTarget`) | exact injection seam | Co-located with its owned mob; root/encounter owner supplies the intended player target |
| `Assets/Scenes/SampleScene.unity` | scene configuration | event-driven bootstrap | current `Grid` / `Main Camera` scene composition | partial | Add the one composition root and inactive runtime subtree; preserve this as the sole build scene |
| `Assets/Prefabs/Player/Player.prefab` | prefab configuration | event-driven | current player local-component composition | exact | Add/authorize `PlayerInputAdapter` and assign the existing Input Actions asset; do not add scene-object links |
| `Assets/Prefabs/UI/PlayerHudCanvas.prefab` | prefab configuration | transform / event-driven | current owned-child HUD hierarchy | exact | Add `PlayerHudView`; no player component should be serialized here |
| `Assets/Prefabs/Mobs/Weasel.prefab` | optional prefab configuration | event-driven | current owner-local mob composition | role-match | Only if the existing mob remains part of the active slice: disable automatic global resolution and retain authored config |
| `Assets/Tests/Editor/RuntimeCompositionEditModeTests.cs` | test | request-response / bootstrap | `Assets/Tests/Editor/MobStateMachineTests.cs` | role-match | Minimal inactive root/player/nav/HUD/camera graphs; test unique/missing/duplicate core and Input Asset failures |
| `Assets/Tests/PlayMode/RuntimeCompositionPlayModeTests.cs` | test | event-driven / integration | `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs` | role-match | Load `SampleScene`, wait bounded frames, assert injected bindings and real Input System action route |
| `Assets/Editor/Verification/SliceVerificationCommands.cs` | editor utility | batch / file-I/O | `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs` | partial role-match | Explicit menu or `-executeMethod` Development Windows build using `BuildReport`; never an `[InitializeOnLoad]` writer |
| `scripts/Invoke-SliceVerification.ps1` | verification utility | batch / process + file-I/O | none | no analog | Owns EditMode, PlayMode, build, interactive smoke, report aggregation, and nonzero final exit |
| `.gitignore` | configuration | file-I/O | itself | exact | Add `TestResults/`; `Builds/` is already ignored |
| `Assets/InputSystem_Actions.inputactions` | authored configuration (verify, normally unchanged) | event-driven | current asset consumed by `PlayerController` | exact asset | Preserve `Player/Move` and `Player/Attack` bindings; Phase 1 assigns it rather than duplicating bindings in code |

## Pattern Assignments

### `Assets/Scripts/Runtime/SliceCompositionRoot.cs` (composition root, event-driven bootstrap)

**Analog:** `Assets/Scripts/AI/Core/MobController.cs` (role-match), plus `NavigationGrid2D` for fail-closed initialization.

**Imports and component contract** — `MobController.cs` lines 1-17:

```csharp
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class MobController : MonoBehaviour
{
```

Copy the global namespace, ordinary `UnityEngine` import, four-space Allman style, and public operation / private lifecycle split. Do **not** copy the serialized `NavigationGrid2D` or provider links at `MobController.cs:19-22`: the Phase 1 root must discover required roles inside its owned hierarchy and reject zero or multiple candidates.

**Explicit configuration seam** — `MobController.cs` lines 81-89:

```csharp
public void Configure(MobConfig mobConfig, NavigationGrid2D navGrid, MobTargetProvider provider)
{
    config = mobConfig;
    navigationGrid = navGrid;
    targetProvider = provider;
    autoResolveDependencies = false;
    initialized = false;
    EnsureInitialized();
}
```

`SliceCompositionRoot` should follow this shape: resolve, assign/inject, reset initialization state where required, and explicitly initialize. Its own cross-context calls are `navigation.TryBuild(...)`/`BuildGrid()`, player-input initialization, `hud.Bind(...)`, and `camera.SetTarget(...)`; no participant discovers an arbitrary global substitute.

**Fail-closed error pattern** — `NavigationGrid2D.cs` lines 79-92:

```csharp
public void BuildGrid()
{
    cellsByPosition.Clear();
    resolvedSources.Clear();
    pathfinder = null;

    ResolveTerrainSources();
    if (resolvedSources.Count == 0)
    {
        Debug.LogError("NavigationGrid2D could not find any NavigationTerrainSource2D components.", this);
        return;
    }
```

For root validation, prefer a `TryResolve...(..., out string error)` / `TryInitialize(out string error)` result. Emit one `Debug.LogError(error, this)` and leave the owned runtime subtree inactive. Do not merely log and activate a partial slice; do not use exceptions for ordinary runtime setup errors.

**Required activation ordering:** resolve and validate unique core roles while the runtime subtree is inactive; build navigation; initialize the player-local input contract; bind HUD and camera; activate/initialize declared optional contexts. `Awake` in all participating components must cache only owner-local dependencies, because inter-object `Awake` order is not a composition mechanism.

**Integration:** add this one active component to `Assets/Scenes/SampleScene.unity` and place the current player, HUD, camera, navigation hierarchy, and any real optional context below a separate inactive `Runtime` child. A serialized `GameObject runtimeRoot` activation gate is an intentional configuration override; do not serialize player/grid/HUD/camera component links.

---

### `Assets/Scripts/Runtime/ISliceOptionalContext.cs` (interface, event-driven extension)

**Analog:** `Assets/Scripts/AI/Core/IMobState.cs`.

**Interface style** — `IMobState.cs` lines 1-9:

```csharp
public interface IMobState
{
    MobStateId StateId { get; }

    void Enter();
    void Tick();
    void FixedTick();
    void Exit();
}
```

Create a global-namespace interface in the same minimal style, for example an `Initialize(SliceCore core)` operation or a `TryInitialize(SliceCore core, out string error)` operation if optional-context validation must be reported. The root enumerates only real components in its owned subtree after core success. Do not create encounter/town placeholder objects or test doubles merely to exercise this interface.

---

### `Assets/Scripts/Player/PlayerInputAdapter.cs` and `Assets/Scripts/Player/PlayerController.cs` (player-local input, event-driven + physics)

**Analog:** `Assets/Scripts/Player/PlayerController.cs`.

**Current imports and local-component setup** — `PlayerController.cs` lines 1-18 and 35-45:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private Animator animator;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ResolveAnimator();
        ResolveWeaponController();

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }
}
```

Move the Input System asset/map/action ownership out of `PlayerController` into the co-located `PlayerInputAdapter`; retain the controller's local rigidbody, animator, weapon, `Update` presentation, and `FixedUpdate` velocity responsibilities. Preserve ownership lookups such as `GetComponent<PlayerWeaponController>()` at `PlayerController.cs:112-118` rather than adding serialized component wiring.

**Input action lookup and validation shape** — `PlayerController.cs` lines 120-139:

```csharp
InputActionAsset resolvedAsset = inputActionsAsset != null ? inputActionsAsset : LoadDefaultInputActionsAsset();
if (resolvedAsset != null)
{
    InputActionMap playerMap = resolvedAsset.FindActionMap(playerActionMapName, false);
    moveAction = playerMap?.FindAction(moveActionName, false);
    attackAction = playerMap?.FindAction(attackActionName, false);

    if (moveAction != null && attackAction != null)
    {
        usingFallbackActions = false;
        return;
    }
}
```

Reuse the `FindActionMap(..., false)` / `FindAction(..., false)` lookup, but turn it into `TryInitialize(out string error)` on the adapter. `inputActionsAsset`, `playerActionMapName`, `moveActionName`, and `attackActionName` are legitimate serialized configuration fields. A null asset or missing `Player`, `Move`, or `Attack` must return `false` with a contextual message.

**Enable/disable lifecycle** — `PlayerController.cs` lines 53-68:

```csharp
private void OnEnable()
{
    ResolveInputActions();
    moveAction?.Enable();
    attackAction?.Enable();
}

private void OnDisable()
{
    moveAction?.Disable();
    attackAction?.Disable();
}
```

The adapter should enable/disable the validated `Player` map (or the two actions) and publish the move/attack route to the controller. Keep initialization idempotent so the composition root can validate/initialize before subtree activation.

**Must remove, not copy** — `PlayerController.cs` lines 141-195:

```csharp
CreateFallbackActions();

private static InputActionAsset LoadDefaultInputActionsAsset()
{
#if UNITY_EDITOR
    return AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
#else
    return null;
#endif
}
```

Do not retain editor-only `AssetDatabase` loading, runtime-created bindings, legacy polling, or fallback disposal. They create the exact editor/Development Build drift this phase removes. The adapter must use the assigned `Assets/InputSystem_Actions.inputactions` asset and its existing `Player/Move` and `Player/Attack` definitions.

**Gameplay boundary** — `PlayerController.cs` lines 71-102:

```csharp
moveInput = moveAction != null ? moveAction.ReadValue<Vector2>().normalized : Vector2.zero;

if (attackAction != null && attackAction.WasPressedThisFrame())
{
    weaponController?.TryAttack();
}

private void FixedUpdate()
{
    rb.linearVelocity = moveInput * moveSpeed;
}
```

Keep movement application in `FixedUpdate`. Make the adapter's callback/event or an explicit player-owned method the single Input System-to-gameplay route. The Development Build smoke observer may observe adapter reception plus real movement/attack consequences; it must not call player or weapon methods to manufacture a pass.

**Integration:** attach `PlayerInputAdapter` to `Assets/Prefabs/Player/Player.prefab` and assign the existing Input Actions asset there. Do not modify `Assets/InputSystem_Actions.inputactions` unless the existing named actions/bindings are actually missing; Phase 1 is about consuming the authored asset.

---

### `Assets/Scripts/UI/PlayerHudView.cs` and `Assets/Scripts/UI/HudController.cs` (prefab-local view plus explicit player bind)

**Analog:** `Assets/Scripts/UI/HudController.cs`.

**Owner-local resolution pattern** — `HudController.cs` lines 212-263:

```csharp
private void ResolveLocalUiReferences()
{
    if (healthFillImage == null)
    {
        healthFillImage = FindNamedComponentInChildren<Image>(HealthFillName);
    }

    if (healthText == null)
    {
        healthText = FindNamedComponentInChildren<TextMeshProUGUI>(HealthTextName);
    }
}

private T FindNamedComponentInChildren<T>(string objectName) where T : Component
{
    Transform[] transforms = GetComponentsInChildren<Transform>(true);
    // Match the owned child by name and TryGetComponent.
}
```

Move this hierarchy knowledge to `PlayerHudView`, co-located on the HUD prefab. It may resolve its own children or retain intentional local inspector references, then expose read-only `Image` / `TextMeshProUGUI` properties. This fixes the fragile distributed child-name contract without adding cross-scene serialized links.

**Event subscription pattern** — `HudController.cs` lines 121-156:

```csharp
private void SubscribeToPlayerEvents()
{
    if (playerHealth != null)
    {
        playerHealth.Damaged += HandlePlayerDamaged;
        playerHealth.Died += HandlePlayerDied;
    }

    if (playerWeaponController != null)
    {
        playerWeaponController.EquippedWeaponChanged += HandleEquippedWeaponChanged;
    }

    subscribedToPlayer = true;
}

private void UnsubscribeFromPlayerEvents()
{
    if (!subscribedToPlayer)
    {
        return;
    }
    // Remove every subscription before clearing/disable.
}
```

Add a public `Bind(Health health, PlayerWeaponController weaponController)` (and, if useful, `Unbind`) that first unsubscribes, assigns the explicit collaborators, subscribes, and calls `UpdateAllDisplay`. Preserve `OnDisable`/`OnDestroy` cleanup.

**Must remove, not copy** — `HudController.cs` lines 31-55 and 82-114:

```csharp
private void Update()
{
    if (HasPlayerReferences())
    {
        return;
    }

    if (BindToPlayerIfAvailable())
    {
        UpdateAllDisplay();
    }
}

GameObject playerObject = GameObject.FindGameObjectWithTag(PlayerTag);
```

The Phase 1 standard path does not poll or use `FindGameObjectWithTag`. The root supplies the validated player collaborators before UI runtime begins. Missing local HUD view references are required-core failures for the root, not a warning that permits an unbound slice.

**Integration:** add `PlayerHudView` to `Assets/Prefabs/UI/PlayerHudCanvas.prefab`, retain the current owned hierarchy, and update the prefab/controller together. The root discovers the HUD beneath its own runtime subtree; neither HUD component serializes a `Player` reference.

---

### `Assets/Scripts/Camera/CameraFollow2D.cs` (camera follow, request-response)

**Analog:** existing explicit seam in `CameraFollow2D.cs`.

**Binding implementation** — `CameraFollow2D.cs` lines 55-60:

```csharp
public void SetTarget(Transform newTarget)
{
    target = newTarget;
    targetRigidbody = target != null ? target.GetComponent<Rigidbody2D>() : null;
    velocity = Vector3.zero;
}
```

Keep this public injection API; root code calls it after resolving the unique player. The `target` serialized field can remain only as an intentional authoring override if the planner retains one, but the standard slice must be root-bound.

**Must remove, not copy** — `CameraFollow2D.cs` lines 16-20 and 62-70:

```csharp
private void OnEnable()
{
    TryAssignTarget();
    velocity = Vector3.zero;
}

private void TryAssignTarget()
{
    GameObject player = GameObject.FindGameObjectWithTag("Player");
    // ...
}
```

Do not retry a tag lookup. `LateUpdate` should retain its existing null guard and follow/smoothing logic (`CameraFollow2D.cs:22-53`) so camera behavior stays unchanged after explicit binding.

---

### `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` (navigation service, transform/bootstrap)

**Analog:** existing `NavigationGrid2D.cs` build contract.

**Public state and initialization** — `NavigationGrid2D.cs` lines 51-59 and 79-92:

```csharp
public IPathfinder2D Pathfinder => pathfinder;
public bool IsBuilt => pathfinder != null;

private void Awake()
{
    BuildGrid();
}

public void BuildGrid()
{
    cellsByPosition.Clear();
    resolvedSources.Clear();
    pathfinder = null;
    // Resolve sources and return after contextual errors.
}
```

Keep the navigation hierarchy's ownership of terrain source discovery (`GetComponentsInChildren<NavigationTerrainSource2D>(true)` at `NavigationGrid2D.cs:363-389`) and its `IsBuilt` result. Refactor only enough to let the root call a fail-closed `TryBuild(out string error)` / validated `BuildGrid` before anything depends on it; do not introduce serialized references from the root to individual tilemaps.

**Existing local discovery** — `NavigationGrid2D.cs` lines 363-389:

```csharp
NavigationTerrainSource2D[] childSources = GetComponentsInChildren<NavigationTerrainSource2D>(true);
for (int i = 0; i < childSources.Length; i++)
{
    NavigationTerrainSource2D source = childSources[i];
    if (source != null && uniqueSources.Add(source))
    {
        resolvedSources.Add(source);
    }
}
```

This is the desired ownership-scoped discovery. The root validates the single grid role; the grid remains responsible for its owned terrain source topology.

---

### `Assets/Scripts/AI/Core/MobController.cs`, `Assets/Scripts/AI/Core/MobTargetProvider.cs`, and `Assets/Prefabs/Mobs/Weasel.prefab` (optional real encounter owner)

**Analog:** explicit injection seams in `MobController.cs` and `MobTargetProvider.cs`.

**Injection points** — `MobController.cs` lines 81-89 and `MobTargetProvider.cs` lines 31-34:

```csharp
public void Configure(MobConfig mobConfig, NavigationGrid2D navGrid, MobTargetProvider provider)
{
    config = mobConfig;
    navigationGrid = navGrid;
    targetProvider = provider;
    autoResolveDependencies = false;
    initialized = false;
    EnsureInitialized();
}

public void SetTarget(Transform newTarget)
{
    target = newTarget;
}
```

If the existing Weasel is activated in Phase 1, let the composition root or a real encounter owner call these explicit APIs with the root's validated player and navigation. The mob retains co-located `GetComponent` lookup for motor/perception/path/patrol/damage collaborators (`MobController.cs:177-208`) and serialized `MobConfig` authored data.

**Must remove, not copy** — `MobController.cs` lines 210-221, `MobController.cs` lines 157-170, and `MobTargetProvider.cs` lines 12-29:

```csharp
navigationGrid = FindAnyObjectByType<NavigationGrid2D>();
targetProvider = FindAnyObjectByType<MobTargetProvider>();

if (config == null)
{
    config = CreateRuntimeFallbackConfig();
}

GameObject found = GameObject.FindGameObjectWithTag(targetTag);
target = found != null ? found.transform : null;
```

The standard slice must not fabricate missing mob configuration or select arbitrary objects. Either an actual encounter owner receives explicit injection after core startup, or an absent optional context is valid. Do not add a placeholder encounter/town object merely to preserve the old fallback path.

---

### `Assets/Tests/Editor/RuntimeCompositionEditModeTests.cs` (test, request-response/bootstrap)

**Analog:** `Assets/Tests/Editor/MobStateMachineTests.cs`.

**Fixture and cleanup** — `MobStateMachineTests.cs` lines 5-44:

```csharp
public class MobStateMachineTests
{
    private GameObject root;
    private MobConfig config;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (config != null)
        {
            Object.DestroyImmediate(config);
        }
    }
}
```

Build real, minimal inactive GameObject trees. Track every created root/ScriptableObject in fixture fields and destroy them in `TearDown`; do not introduce a mocking framework.

**Explicit test seam** — `MobStateMachineTests.cs` lines 222-267:

```csharp
terrainSource.Configure(dataTilemap, collisionTilemap, null, groundTerrain);
navGrid = root.AddComponent<NavigationGrid2D>();
navGrid.BuildGrid();

brain = mob.AddComponent<MobController>();
brain.Configure(config, navGrid, provider);
```

Use the same minimal-real-graph approach to test: exactly-one role success; zero/duplicate player, grid, HUD, or camera failure; inactive runtime retained on error; no Input Asset/map/action fallback; and explicit input adapter validation. Assert public observable results and active state, not private fields/reflection.

**Auth/error equivalent:** there is no auth layer. The relevant guard is failure of input/configuration validation and `Debug.LogError` with the composition root as context. Unity `LogAssert` may be used for expected logged core failures where that is clearer than inferring behavior.

---

### `Assets/Tests/PlayMode/RuntimeCompositionPlayModeTests.cs` (integration test, event-driven)

**Analog:** `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`.

**Scene load and bounded wait** — `MobPlayModeBehaviorTests.cs` lines 12-42:

```csharp
[UnityTest]
public IEnumerator MobInSampleScene_DetectsAndLosesTarget()
{
    yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
    yield return null;

    MobController brain = Object.FindAnyObjectByType<MobController>();
    Assert.That(brain, Is.Not.Null);

    yield return WaitFrames(6);
    Assert.That(brain.CurrentStateId == MobStateId.Chase || brain.CurrentStateId == MobStateId.AttackRange, Is.True);
}
```

Follow the fixture's `[UnityTest]` / `IEnumerator` style, load the actual `SampleScene`, wait one or bounded named helper frames, and assert visible root-injected state. The new tests should prove that missing optional contexts do not block core startup, whereas a missing/duplicate required core role does.

**Bounded helper** — `MobPlayModeBehaviorTests.cs` lines 248-262:

```csharp
private static IEnumerator WaitFrames(int frameCount)
{
    for (int i = 0; i < frameCount; i++)
    {
        yield return null;
    }
}
```

For input parity, drive real Input System actions/devices through the adapter and assert player movement plus attack-route observation. Do not call `PlayerController` or `PlayerWeaponController` attack APIs directly; that would bypass the requirement being tested.

---

### `Assets/Editor/Verification/SliceVerificationCommands.cs` (editor command, batch/file-I/O)

**Analog:** explicit menu utility shape in `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs` lines 17-22:

```csharp
[MenuItem("Tools/TopDownRPG/Rebuild Player Slash Prefab")]
public static void RebuildPrefab()
{
    GameObject slashPrefab = CreateOrUpdatePrefab();
    LinkSwordWeapon(slashPrefab);
}
```

Use a public static editor entry point suitable for a Tools menu and Unity `-executeMethod`. It builds exactly `Assets/Scenes/SampleScene.unity` to an ignored `Builds/Phase01/...` Windows path with `BuildOptions.Development | BuildOptions.StrictMode`, inspects `BuildReport.summary.result`, and returns a machine-readable success/failure outcome to the caller.

**Important negative pattern:** do **not** copy `[InitializeOnLoad]`, `EditorApplication.delayCall`, or automatic asset writes from `PlayerSlashPrefabBootstrap.cs:5-15` and `:24-38`. This verification command is explicitly invoked and only writes requested build artifacts. Exceptions are acceptable here only to make an editor batch build fail; gameplay setup still uses guarded results and `Debug.LogError`.

---

### `scripts/Invoke-SliceVerification.ps1` and `.gitignore` (verification batch/process and artifact configuration)

**PowerShell analog:** none exists in the repository. Follow the Phase 1 research process shape rather than inventing a test framework:

```powershell
$outcomes = @()
$outcomes += Invoke-UnityTestRun -Platform EditMode -ResultsPath $editResults
$outcomes += Invoke-UnityTestRun -Platform PlayMode -ResultsPath $playResults
$outcomes += Invoke-DevelopmentBuildSmoke -BuildPath $buildPath -LogPath $smokeLog

$outcomes | Format-Table Name, Passed, EvidencePath
if ($outcomes.Passed -contains $false) {
    exit 1
}
```

Each helper must complete independently, retain its own result/log path, treat missing XML/log/smoke sentinel as failure, then aggregate only after every channel has run. The `Smoke` path is interactive: it must time out/fail without physical WASD/arrows and Space/left-mouse input; it is not a unit-test substitute.

**Ignore-file analog** — `.gitignore` lines 1-13:

```gitignore
# Unity generated folders
[Bb]uilds/
[Ll]ogs/

# Unity / IDE generated files
*.log
```

Add a focused `TestResults/` entry near these generated outputs. Do not add report XML broadly to ignore rules if unrelated XML artifacts might later be intentionally versioned; `Builds/` already covers the requested Development Build/smoke tree.

## Shared Patterns

### Runtime ownership and injection

**Sources:** `Assets/Scripts/AI/Core/MobController.cs:81-89`, `Assets/Scripts/AI/Core/MobTargetProvider.cs:31-34`, `Assets/Scripts/Camera/CameraFollow2D.cs:55-60`  
**Apply to:** composition root, camera, HUD, player input, and any real optional encounter/town owner.

```csharp
public void SetTarget(Transform newTarget)
{
    target = newTarget;
    targetRigidbody = target != null ? target.GetComponent<Rigidbody2D>() : null;
    velocity = Vector3.zero;
}
```

Use public runtime bind/configure methods for cross-context relationships; use `GetComponent` or `GetComponentsInChildren` only for owned local topology. Never restore `FindAnyObjectByType`, `FindGameObjectWithTag`, or serialized scene-object references for required-core wiring.

### Fail-closed validation and logging

**Source:** `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs:79-149`  
**Apply to:** root resolution, input adapter, navigation build, HUD view validation, and optional-context initialization.

```csharp
if (resolvedSources.Count == 0)
{
    Debug.LogError("NavigationGrid2D could not find any NavigationTerrainSource2D components.", this);
    return;
}
```

Prefer `Try...` results plus one actionable `Debug.LogError(..., this)` for expected startup faults. The root must keep gameplay inactive after any required-core fault; ordinary runtime validation does not throw or fabricate substitutes.

### Lifecycle and event cleanup

**Sources:** `Assets/Scripts/UI/HudController.cs:26-65`, `:121-156`; `Assets/Scripts/Player/PlayerController.cs:53-68`  
**Apply to:** input adapter, HUD controller, smoke observer, and optional contexts.

```csharp
private void OnDisable()
{
    UnsubscribeFromPlayerEvents();
}

private void OnDestroy()
{
    UnsubscribeFromPlayerEvents();
}
```

Cache local dependencies in `Awake`; subscribe/enable only after initialization; unsubscribe/disable in both `OnDisable` and `OnDestroy` where applicable. Keep repeated initialization safe because the root gates runtime activation.

### Tests use real Unity graphs

**Sources:** `Assets/Tests/Editor/MobStateMachineTests.cs:186-268`, `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs:12-42`  
**Apply to:** both new fixtures.

Create the smallest real GameObject/component graph, configure it through public seams, clean it up, and use bounded `UnityTest` waits. Do not use a mock package or reflection where a new public composition/input contract can be asserted directly.

## No Analog Found

| File | Role | Data Flow | Reason / Planner Direction |
|---|---|---|---|
| `scripts/Invoke-SliceVerification.ps1` | verification utility | batch/process + file-I/O | No PowerShell runner exists. Implement the independent-all-channels aggregation specified in research; run every channel even after failures. |
| `Assets/Scripts/Runtime/SliceCompositionRoot.cs` | composition root | event-driven bootstrap | No existing scene-wide composition owner exists. Compose the local lookup, `Configure`, `Bind`, `SetTarget`, and fail-closed patterns above; do not create a service locator. |

## Metadata

**Analog search scope:** `Assets/Scripts/Player`, `Assets/Scripts/UI`, `Assets/Scripts/Camera`, `Assets/Scripts/AI/Core`, `Assets/Scripts/AI/Navigation`, `Assets/Editor`, `Assets/Tests`, `Assets/Prefabs`, `Assets/Scenes`, `.gitignore`, and `scripts/`  
**Strong analogs read:** 10 — `MobController`, `NavigationGrid2D`, `PlayerController`, `HudController`, `CameraFollow2D`, `PlayerWeaponController`, `IMobState`, `MobTargetProvider`, `MobStateMachineTests`, and `MobPlayModeBehaviorTests` (plus focused asset/editor/config files)  
**Pattern extraction date:** 2026-07-24
