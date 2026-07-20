# Testing Patterns

**Analysis Date:** 2026-07-18

## Test Framework

**Runner:**
- Unity Test Framework `1.6.0`, declared in `Packages/manifest.json`.
- EditMode assembly config: `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` references `TopDownRPG.Gameplay`, runs on the Editor platform, and declares `TestAssemblies`.
- PlayMode assembly config: `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef` references `TopDownRPG.Gameplay` and declares `TestAssemblies`.

**Assertion Library:**
- NUnit, imported as `NUnit.Framework` and used with constraint assertions such as `Assert.That(value, Is.EqualTo(...))` in `Assets/Tests/Editor/CombatComponentTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`.

**Run Commands:**
```text
Unity Editor: Window > General > Test Runner > EditMode > Run All
Unity Editor: Window > General > Test Runner > PlayMode > Run All
```

No repository-provided shell test script, CI pipeline, `.runsettings`, or test-result configuration is detected. For automation, invoke the locally installed Unity `6000.4.2f1` editor (`ProjectSettings/ProjectVersion.txt`) with Unity's `-runTests` option and select `EditMode` or `PlayMode`; keep the executable path outside repository configuration.

**MCP-assisted test workflow:**
- With the local project-scoped Unity MCP server running (see `.planning/codebase/INTEGRATIONS.md`), an MCP client may start Unity Test Framework EditMode or PlayMode runs and poll their results.
- Treat the Unity Test Runner and its results as the source of truth. Before a test run that follows an MCP-driven script change, wait for compilation/domain reload to finish and check the Unity Console for errors.
- The MCP server is developer tooling only; it does not add a CI runner, replace `-runTests`, or alter the test assemblies under `Assets/Tests/`.

## Test File Organization

**Location:**
- Tests are separated by Unity test mode rather than co-located with source:
  - `Assets/Tests/Editor/` contains five EditMode fixtures (32 `[Test]` cases).
  - `Assets/Tests/PlayMode/` contains one PlayMode fixture (six `[UnityTest]` cases).
- Production code is compiled by `Assets/Scripts/TopDownRPG.Gameplay.asmdef`; tests reference that assembly through their mode-specific `.asmdef` files.

**Naming:**
- Fixture classes use a domain name plus `Tests`: `CombatComponentTests`, `NavigationGridPathfindingTests`, and `MobPlayModeBehaviorTests`.
- Test methods use `Subject_ExpectedResult_WhenCondition` wording, for example `Health_ApplyDamage_ClampsAndDiesOnlyOnce` in `Assets/Tests/Editor/CombatComponentTests.cs`.

**Structure:**
```text
Assets/
├── Scripts/                         # Runtime gameplay assembly
│   └── TopDownRPG.Gameplay.asmdef
└── Tests/
    ├── Editor/                      # NUnit [Test] fixtures
    │   └── TopDownRPG.EditModeTests.asmdef
    └── PlayMode/                    # NUnit [UnityTest] IEnumerator fixtures
        └── TopDownRPG.PlayModeTests.asmdef
```

## Test Structure

**Suite Organization:**
```csharp
// Assets/Tests/Editor/CombatComponentTests.cs
public class CombatComponentTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void Health_ApplyDamage_ClampsAndDiesOnlyOnce()
    {
        root = new GameObject("HealthTest");
        Health health = root.AddComponent<Health>();

        int firstHit = health.ApplyDamage(4);

        Assert.That(firstHit, Is.EqualTo(4));
        Assert.That(health.CurrentHealth, Is.EqualTo(6));
    }
}
```

**Patterns:**
- Construct minimal `GameObject` hierarchies and attach real Unity components in each test. `Assets/Tests/Editor/MobStateMachineTests.cs` builds a grid, tilemaps, ScriptableObjects, player, and mob through a `SetupWorld` helper.
- Track roots and created `ScriptableObject` assets in fixture fields; destroy them in `[TearDown]` using `Object.DestroyImmediate`. The cleanup pattern appears in all five EditMode fixtures, especially `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`.
- Assert observable results with `Assert.That` and NUnit constraints (`Is.True`, `Is.False`, `Is.Null`, `Is.SameAs`, `Is.EqualTo(...).Within(...)`), rather than raw boolean assertions.
- Test event effects by subscribing local counters or callbacks, as in `Assets/Tests/Editor/CombatComponentTests.cs`; test state-machine behavior through public APIs such as `TickStateMachine` and `ChangeState` in `Assets/Tests/Editor/MobStateMachineTests.cs`.

## Mocking

**Framework:**
- Not used. No mocking package or mock/substitute usage is detected in `Packages/manifest.json` or `Assets/Tests/`.

**Patterns:**
```csharp
// Assets/Tests/Editor/MobStateMachineTests.cs
root = new GameObject("MobStateMachineTestRoot");
Grid grid = root.AddComponent<Grid>();

config = ScriptableObject.CreateInstance<MobConfig>();
config.detectionRadius = 8f;

brain = mob.AddComponent<MobController>();
brain.Configure(config, navGrid, provider);
```

**What to Mock:**
- Do not add a mocking framework for existing component tests. Build the smallest real Unity object graph, then configure it through public test seams such as `MobController.Configure`, `NavigationTerrainSource2D.Configure`, `TerrainMovementProfile2D.Configure`, and `PlayerWeapon.Create`.
- Use a small private fixture helper when multiple tests need the same object graph, following `SetupNavigationGrid` and `CreateTestWeapon` in `Assets/Tests/Editor/NavigationGridPathfindingTests.cs` and `Assets/Tests/Editor/PlayerWeaponSystemTests.cs`.

**What NOT to Mock:**
- Do not mock Unity's `GameObject`, components, physics objects, ScriptableObjects, or assets that are simple to create in the test. Existing tests exercise the actual Unity component relationships.
- Avoid testing a private implementation field through reflection when a public behavior or `Configure` seam can be added. Reflection is present only for narrow checks in `Assets/Tests/Editor/MobMotor2DTests.cs`, `Assets/Tests/Editor/CombatComponentTests.cs`, and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`; it is more brittle than the public-API pattern.

## Fixtures and Factories

**Test Data:**
```csharp
// Assets/Tests/Editor/NavigationGridPathfindingTests.cs
fillTile = ScriptableObject.CreateInstance<Tile>();
groundTerrain = CreateTerrain("ground");

for (int x = 0; x < 5; x++)
{
    for (int y = 0; y < 5; y++)
    {
        dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
    }
}

navigationGrid = root.AddComponent<NavigationGrid2D>();
navigationGrid.BuildGrid();
```

**Location:**
- No shared fixtures or factory directory is present. Keep test-specific builders private to the fixture that owns them, as in `Assets/Tests/Editor/PlayerWeaponSystemTests.cs` and `Assets/Tests/Editor/NavigationGridPathfindingTests.cs`.
- Use `AssetDatabase.LoadAssetAtPath` only in EditMode tests when verifying authored assets, such as the input action asset and animation clip in `Assets/Tests/Editor/PlayerWeaponSystemTests.cs`.

## Coverage

**Requirements:**
- None enforced. `Packages/manifest.json` has the Unity Test Framework but no Unity Code Coverage package, and no coverage report, threshold, or CI enforcement file is detected.

**View Coverage:**
```text
Not available from committed project configuration.
```

## Test Types

**Unit Tests:**
- EditMode tests cover focused gameplay behavior without loading the scene: health/damage (`Assets/Tests/Editor/CombatComponentTests.cs`), pathfinding (`Assets/Tests/Editor/NavigationGridPathfindingTests.cs`), AI transitions (`Assets/Tests/Editor/MobStateMachineTests.cs`), motor state (`Assets/Tests/Editor/MobMotor2DTests.cs`), and weapon/UI behavior (`Assets/Tests/Editor/PlayerWeaponSystemTests.cs`).
- These are Unity component tests rather than pure CLR unit tests: they create real `GameObject` graphs and frequently exercise Unity APIs.

**Integration Tests:**
- EditMode tests combine multiple runtime components and authored assets. `Assets/Tests/Editor/PlayerWeaponSystemTests.cs` verifies weapon spawning, collision damage, UI state, animations, and input bindings together.

**E2E Tests:**
- PlayMode uses Unity integration tests, not a separate E2E framework. `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs` loads `SampleScene`, discovers scene objects, drives movement/combat state, and verifies HUD, popups, death animation, and cleanup.

## Common Patterns

**Async Testing:**
```csharp
// Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs
[UnityTest]
public IEnumerator MobInSampleScene_AttackRespectsConfiguredInterval()
{
    yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
    yield return null;

    yield return WaitForHealthChange(health, startingHealth - 1, 30);
    yield return new WaitForSeconds(0.2f);

    Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - 1));
}
```
- Use `[UnityTest]` plus `IEnumerator` for scene loading, frame waits, and elapsed-time assertions. Reuse bounded helpers such as `WaitFrames` and `WaitForHealthChange` from `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs` instead of unbounded loops.

**Error Testing:**
```csharp
// Assets/Tests/Editor/CombatComponentTests.cs
Assert.That(receiver.ReceiveDamage(0), Is.EqualTo(0));
Assert.That(receiver.ReceiveDamage(-5), Is.EqualTo(0));
Assert.That(health.CurrentHealth, Is.EqualTo(10));
```
- Test invalid inputs and unavailable paths as safe result values (`0`, `false`, or a failed `PathResult`), not exception throws. `Assets/Tests/Editor/NavigationGridPathfindingTests.cs` checks unreachable goals with `result.Success` constraints.
- No exception-assertion tests are present; match the runtime guard-clause error strategy unless a new API explicitly documents an exception contract.

---

*Testing analysis: 2026-07-18*
