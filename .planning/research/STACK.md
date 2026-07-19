# Technology Stack

**Project:** Top-Down-RPG
**Researched:** 2026-07-19
**Confidence:** MEDIUM — core recommendations are grounded in the checked-in project and Unity primary documentation. The optional package versions below are documented as released for Unity 6000.0; confirm their availability in the Unity 6000.4.2f1 Package Manager before changing the manifest.

## Recommended Stack

Keep the project on its existing Unity 6000.4 / URP / C# / component-and-ScriptableObject stack. The immediate need is not a replacement framework: it is a repeatable authoring, profiling, and test loop around the existing clearing-to-town slice.

### Core Framework

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Unity Editor | 6000.4.2f1 (project pin) | Editor, player build, 2D tooling | This is the committed project platform. Stay on this patch through the current vertical-slice baseline; evaluate a Unity 6000.4 patch update only as a deliberate upgrade with tests and a player smoke test. |
| C# / Unity scripting runtime | C# 9 / .NET Standard 2.1 | Gameplay, editor tools, tests | Matches the generated gameplay and test projects. Keep code in the existing assembly-definition boundary; do not introduce a second runtime or package manager. |
| Universal Render Pipeline | 17.4.0 | 2D Renderer, lighting, Shader Graph, post-processing | Already configured with 'UniversalRP.asset' and 'Renderer2D.asset'. Unity’s 2D workflow explicitly supports Light 2D, Shadow Caster 2D, Particle System, Shader Graph, and URP post-processing, which directly serve the visual-effects goal. |
| Unity Input System | 1.19.0 | Player action maps and control schemes | Preserve the existing '.inputactions' asset as the one source of truth. Assign it as a Player-prefab configuration reference so Editor and player builds use identical maps; cache 'FindAction' results at initialization. |
| Unity 2D Tilemap + Tilemap Extras + DualGrid | 1.0.0 / 7.0.1 / existing Git pin | Authored terrain and navigation source | The current navigation grid and tile authoring already depend on this chain. Improve the project’s navigation implementation and asset validation rather than replacing tilemaps. |
| UGUI + TextMesh Pro | 2.0.0 / bundled | In-game HUD and combat feedback | Existing HUD, damage text, and tests use these APIs. Keep UGUI for the slice rather than adding a second runtime UI system. |

### Database

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Unity assets and local runtime state | Existing | ScriptableObject configuration and scene/prefab content | This is a local single-player vertical slice with no save or remote-service requirement. Do not add SQLite, a backend, cloud save, or an ORM until a concrete persistence feature needs them. |

### Infrastructure

| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Unity Package Manager + committed lockfile | Existing | Dependency resolution | Treat 'Packages/manifest.json' and 'Packages/packages-lock.json' as the authority. Review both together on every package change. |
| Git | Existing | Source, asset, and configuration history | Unity YAML assets, the package lockfile, and configuration are all part of a reproducible playable slice. Keep generated IDE projects and 'Library/' out of version control. |
| Windows standalone player | Existing target | Local playtest target | Establish one repeatable development-player build for performance and input verification before considering additional platforms. |
| Coplay Unity MCP | Git dependency resolved to 'c14de1e...' | Local editor inspection, scene operations, test assistance | It is development tooling only: never reference it from runtime gameplay code. The manifest currently tracks '#main'; pin a reviewed release tag or commit hash before the next package resolution, then retain the lockfile. |
| Visual Studio integration / Rider integration | 2.0.27 / 3.0.39 | C# editing and debugging | Both are already installed. Use one primary IDE; no additional language-tool chain is justified. |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Unity Test Framework + NUnit | 1.6.0 / resolved transitive | Existing EditMode and PlayMode tests | Keep the two current test assemblies. First repair the local headless invocation so it produces '-testResults' XML; do not replace the runner or add a mocking library. |
| Unity Code Coverage | 1.2.7 (verify in Package Manager) | Coverage reports and manual coverage recording | Add after a successful batch test command exists. It complements the current tests and turns refactor safety into evidence; it does not fix the licensing/database failure currently preventing the test process from writing results. |
| Unity Profiler + Frame Debugger | Built into Editor | CPU, GPU/rendering, GC-allocation investigation | Use from the first representative combat encounter. Profile a development player, not only the Editor, and capture a baseline before pooling or rendering optimizations. |
| Memory Profiler | 1.1.9 (verify in Package Manager) | Detailed allocation snapshots | Add as an editor-only diagnostic once pooled slashes, popups, particles, and lighting experiments are active. Compare snapshots before/after an encounter rather than guessing at memory pressure. |
| Particle System | Built into Unity | Sprite-based combat effects | Use for hits, trails, dust, telegraph accents, and short-lived ambiance. Pool effect roots through a project-owned lifecycle contract before encounter count increases. |
| Shader Graph | Included with URP 17.4 | Sprite-lit visual treatments and impact shaders | Use small, reusable graph families—telegraph, hit flash, dissolve, and environment mood—with one readability owner per effect. Validate effects on the play camera, not only in isolation. |

## Compatibility and Integration Notes

1. **Keep Unity package versions coherent.** URP 17.4.0, Input System 1.19.0, UGUI 2.0.0, and Test Framework 1.6.0 are the established 6000.4 project set. Do not independently upgrade direct Unity packages while holding the editor version fixed unless Unity’s Package Manager explicitly resolves the combination. Upgrade one reviewed package group, commit both manifest and lockfile, then run EditMode, PlayMode, and a Windows-player smoke test.

2. **Make the input asset the build-safe configuration seam.** 'PlayerController' currently uses 'AssetDatabase' in the Editor and hard-coded fallback actions in player builds. 'InputActionAsset' is an imported '.inputactions' ScriptableObject; serialize that asset on the player prefab as configuration (which fits the project’s scene-wiring rule), resolve actions once at runtime, and test the same asset in a standalone player. Do not use a serialized cross-scene actor reference to solve this.

3. **Use the existing URP 2D Renderer for the VFX toolkit.** Light 2D, Shadow Caster 2D, Particle System, Shader Graph, and URP volume/full-screen effects compose with current SpriteRenderers and tilemaps. Set explicit sorting layers, 2D light blend styles, layer masks, and a per-encounter particle budget. The visual target is readable intent: hostile telegraphs and hit confirmation must remain visible above mood lighting and particles.

4. **Do not use VFX Graph for this slice.** Unity’s feature comparison describes URP VFX Graph as 3D-particle oriented and lacking support for 2D physics, 2D sprite emitters, and 2D Lights. It would create a second, poorly aligned effects path; start with the built-in Particle System and revisit only if measured effect scale exceeds it.

5. **Treat MCP as an editor boundary, not gameplay architecture.** The current manifest and lockfile show 'com.coplaydev.unity-mcp' as a UPM Git dependency, despite the 2026-07-18 map describing a separately launched 'mcpforunityserver'. Reconcile that documentation before relying on the tool. Review/commit-pin the package URL, keep it loopback and project-scoped, wait for compilation after mutations, and use the Unity Test Runner as the authority.

6. **Instrumentation must precede performance rewrites.** Current concerns identify allocations in slash/popups/death effects and synchronous navigation as likely hot spots. Add a repeatable development-player profiler capture and a representative combat scene first; add pooling, non-alloc physics, or algorithm changes only against that baseline.

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Camera | Existing 'CameraFollow2D' with explicit runtime target seam | Cinemachine 3.1.5 | Unity documents it as released for Unity 6000, but the slice has one simple follow camera and no blends, cutscenes, multi-target framing, or camera-impulse requirement. Adding it now creates a migration without validating a need. Reassess when a camera behavior cannot stay small and local. |
| Asset loading | Direct prefab/ScriptableObject references | Addressables | Addressables solve asynchronous location/dependency loading across local or remote content. One local scene with no streaming or downloadable content does not warrant catalogs, groups, handles, and lifecycle rules. Add only when multiple independently loaded scenes or large optional content make direct references a measured problem. |
| Effects | Particle System + URP 2D + Shader Graph | VFX Graph | VFX Graph’s documented 2D limitations conflict with sprite-lit, Light 2D-based combat presentation. |
| UI | Existing UGUI/TMP HUD | UI Toolkit runtime migration | The HUD already has working UGUI/TMP behavior and test coverage. A second runtime UI stack adds style and event-system seams with no slice requirement. Consider UI Toolkit later for editor-only content-authoring tools, not the current HUD. |
| Gameplay architecture | Focused MonoBehaviours, ScriptableObject data, owner-local helpers | ECS/DOTS, third-party DI container, generic behavior-tree framework | The current bottlenecks are unprofiled component allocation/pathfinding costs, not proven architecture limits. These additions would widen the learning and debugging surface and conflict with the project’s preference for ownership-based discovery and minimal serialized scene links. |
| Navigation | Existing 2D grid/A* seams | Third-party pathfinding package | Navigation already has explicit contracts and tests. First add budgets, pooling, and profiling to its current implementation; replace it only if the vertical-slice map/crowd evidence shows the current model cannot meet its target. |
| Persistence/services | None | SQLite, cloud save, analytics, multiplayer backend | Explicitly out of scope for a solo, local, single-player foundation. These systems would create security, configuration, and support work before gameplay proves its needs. |

## Installation

Use Unity’s Package Manager, or add the exact packages to 'Packages/manifest.json' and let Unity regenerate 'Packages/packages-lock.json'. Do not install packages through npm.

~~~json
{
  "dependencies": {
    "com.unity.testtools.codecoverage": "1.2.7",
    "com.unity.memoryprofiler": "1.1.9"
  }
}
~~~

Install neither package until the project can run its current tests in a writable local environment and produce result XML. At installation time, accept only the versions the Unity 6000.4.2f1 Package Manager marks compatible; this research verifies their Unity 6000.0 release status, not a 6000.4.2f1 package-resolution run.

For the already-present Git packages, replace moving branch references with a reviewed immutable revision and commit the lockfile:

~~~text
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#<reviewed-commit-or-release-tag>
https://github.com/skner-dev/DualGrid.git#<reviewed-commit>
~~~

## What Not to Add Yet

- **Cinemachine:** wait for a proven camera-composition or impulse requirement.
- **Addressables:** wait for multiple independently loaded scenes, content streaming, or delivery needs.
- **VFX Graph:** its documented 2D limitations are mismatched to Light 2D and sprite-based effects.
- **ECS/DOTS, behavior-tree, DI, or generic event-bus frameworks:** profile and improve the existing component seams first; avoid adding abstractions that obscure ownership.
- **Runtime UI migration:** preserve the UGUI/TMP HUD until the product needs a new UI class that cannot be served incrementally.
- **Backend, database, analytics, multiplayer, cloud save, or CI service:** all are outside the present local vertical-slice goal. A local batch test/build script is the next automation step, not hosted infrastructure.
- **Unpinned Git dependencies:** do not keep '#main' or another moving reference once the current baseline is confirmed.

## Sources

- [Unity 2D game creation workflow — URP 2D lights, particles, Shader Graph, post-processing, and profile-the-player guidance](https://docs.unity3d.com/6000.1/Documentation/Manual/2d-game-creation-wokflow.html) — MEDIUM (official documentation; Unity 6.1 page, applied cautiously to the project’s 6000.4 URP package).
- [Unity Test Framework package reference](https://docs.unity3d.com/ja/current/Manual/com.unity.test-framework.html) and [test command-line workflow](https://docs.unity3d.com/kr/Packages/com.unity.test-framework%402.0/manual/workflow-run-test.html) — MEDIUM (official documentation; command-line concepts cross-checked, while the project remains on Test Framework 1.6.0).
- [Unity Code Coverage package reference](https://docs.unity3d.com/ja/current/Manual/com.unity.testtools.codecoverage.html) — MEDIUM (official documentation; 1.2.7 released for Unity 6000.0, exact 6000.4.2f1 resolution remains an install-time check).
- [Unity Memory Profiler package reference](https://docs.unity3d.com/ja/current/Manual/com.unity.memoryprofiler.html) and [tracking GC allocations](https://docs.unity3d.com/jp/current/Manual/performance-track-garbage-collection.html) — MEDIUM (official Unity 6 documentation).
- [Unity InputActionAsset API](https://docs.unity3d.com/ja/Packages/com.unity.inputsystem%401.4/api/UnityEngine.InputSystem.InputActionAsset.html) — MEDIUM (official API documentation; the project’s installed Input System is newer, but the asset/action-map model is unchanged in the observed project).
- [Unity render-pipeline feature comparison](https://docs.unity3d.com/kr/current/Manual/render-pipelines-feature-comparison.html) — MEDIUM (official documentation, including the cited VFX Graph 2D limitations).
- [Cinemachine package reference](https://docs.unity3d.com/jp/current/Manual/com.unity.cinemachine.html) and [Unity Addressables overview](https://docs.unity3d.com/cn/2020.1/Manual/com.unity.addressables.html) — MEDIUM (official docs; used for scope fit, not as a mandate to install).
- [Unity Git URL dependency documentation](https://docs.unity3d.com/es/current/Manual/upm-git.html) and [Coplay Unity MCP repository](https://github.com/CoplayDev/unity-mcp) — MEDIUM (primary vendor sources; the repository’s own '#main' installation guidance is deliberately overridden here for reproducibility).

