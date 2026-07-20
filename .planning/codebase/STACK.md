# Technology Stack

**Analysis Date:** 2026-07-18

## Languages

**Primary:**
- C# 9.0 - Gameplay, UI, combat, AI, editor tooling, and tests under `Assets/Scripts/`, `Assets/Editor/`, and `Assets/Tests/`; Unity-generated project files set `LangVersion` to 9.0 in `TopDownRPG.Gameplay.csproj`.

**Secondary:**
- ShaderLab and HLSL - Rendering and TextMesh Pro shader assets under `Assets/TextMesh Pro/Shaders/`.
- YAML - Unity scenes, prefabs, ScriptableObject data, and project settings such as `Assets/Scenes/SampleScene.unity`, `Assets/Data/Weapons/Sword.asset`, and `ProjectSettings/ProjectSettings.asset`.
- JSON - Unity package/input configuration in `Packages/manifest.json`, `Packages/packages-lock.json`, and `Assets/InputSystem_Actions.inputactions`.

## Runtime

**Environment:**
- Unity Editor 6000.4.2f1 - Required engine version recorded in `ProjectSettings/ProjectVersion.txt`; the generated C# projects target `.NET Standard 2.1` in `TopDownRPG.Gameplay.csproj`.
- Unity's managed scripting runtime compiles the `TopDownRPG.Gameplay`, `TopDownRPG.EditModeTests`, and `TopDownRPG.PlayModeTests` assemblies defined in `Assets/Scripts/TopDownRPG.Gameplay.asmdef`, `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef`, and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`.

**Package Manager:**
- Unity Package Manager (UPM) - Direct dependencies are declared in `Packages/manifest.json`.
- Lockfile: present at `Packages/packages-lock.json`; use this file with `Packages/manifest.json` to reproduce resolved package versions.
- No Node, Python, Go, Rust, or other language-package manifest is present at the repository root; Unity/UPM is the build dependency authority.

## Frameworks

**Core:**
- Unity Engine 6000.4.2f1 - Game runtime and editor platform for all C# assemblies; scene entry is `Assets/Scenes/SampleScene.unity` and engine configuration is in `ProjectSettings/`.
- Universal Render Pipeline 17.4.0 (`com.unity.render-pipelines.universal`) - 2D rendering pipeline declared in `Packages/manifest.json` and configured by `Assets/Settings/UniversalRP.asset` and `Assets/Settings/Renderer2D.asset`.
- Unity 2D Tilemap 1.0.0 and Tilemap Extras 7.0.1 - Tilemap-based terrain and navigation used by `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs` and tile assets under `Assets/Tiles/`.
- Unity Input System 1.19.0 - Action-based player input declared in `Packages/manifest.json`, referenced by `Assets/Scripts/TopDownRPG.Gameplay.asmdef`, and configured in `Assets/InputSystem_Actions.inputactions`.
- Unity UI (UGUI) 2.0.0 and TextMesh Pro - HUD UI uses `UnityEngine.UI` and `TMPro` in `Assets/Scripts/UI/HudController.cs`; TextMesh Pro is referenced by `Assets/Scripts/TopDownRPG.Gameplay.asmdef` rather than separately pinned in `Packages/manifest.json`.

**Testing:**
- Unity Test Framework 1.6.0 - Edit-mode and play-mode tests are enabled by `Packages/manifest.json` and the test assembly definitions in `Assets/Tests/Editor/TopDownRPG.EditModeTests.asmdef` and `Assets/Tests/PlayMode/TopDownRPG.PlayModeTests.asmdef`.
- NUnit - Test source imports `NUnit.Framework` in `Assets/Tests/Editor/CombatComponentTests.cs` and `Assets/Tests/PlayMode/MobPlayModeBehaviorTests.cs`; the resolved dependency chain is locked in `Packages/packages-lock.json`.

**Build/Dev:**
- Unity Editor/UPM - Import, compile, test, and player build workflow configured by `ProjectSettings/`, `Packages/manifest.json`, and `ProjectSettings/EditorBuildSettings.asset`.
- Microsoft.NET.Sdk project generation - IDE project files `TopDownRPG.Gameplay.csproj`, `TopDownRPG.EditModeTests.csproj`, and `TopDownRPG.PlayModeTests.csproj` target `netstandard2.1`; Unity marks these generated files as non-authoritative.
- Visual Studio integration 2.0.27 and JetBrains Rider integration 3.0.39 - Editor support packages declared in `Packages/manifest.json`.
- Unity MCP (`mcpforunityserver` 10.1.0) - Developer-operated MCP bridge for project-scoped Unity Editor inspection, scene/object operations, script edits, console checks, and Unity Test Framework runs. It is local workstation tooling supplied by the project owner, not a UPM dependency or a runtime player feature.

## Key Dependencies

**Critical:**
- `com.unity.render-pipelines.universal` 17.4.0 - Supplies the active URP/2D renderer configured in `Assets/Settings/UniversalRP.asset` and `Assets/Settings/Renderer2D.asset`.
- `com.unity.inputsystem` 1.19.0 - Supplies action maps consumed by `Assets/Scripts/Player/PlayerController.cs` and stored in `Assets/InputSystem_Actions.inputactions`.
- `com.unity.ugui` 2.0.0 and Unity TextMesh Pro - Supply the `Image` and TMP UI types used by `Assets/Scripts/UI/HudController.cs` and `Assets/Scripts/Combat/FloatingDamageText.cs`.
- `com.unity.2d.tilemap` 1.0.0 plus `com.unity.2d.tilemap.extras` 7.0.1 - Support authored tile assets in `Assets/Tiles/` and the navigation grid implementation in `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`.
- `com.skner.dualgrid` (Git dependency pinned by hash `493500b6ac59f5713b7d9d9bdff6d6a37016da78`) - Provides DualGrid tile authoring assets referenced under `Assets/Tiles/tilesets/`; the source URL and resolved hash are in `Packages/manifest.json` and `Packages/packages-lock.json`.

**Infrastructure:**
- `com.unity.2d.animation` 14.0.4, `com.unity.2d.aseprite` 4.0.1, and `com.unity.2d.psdimporter` 13.0.2 - 2D animation and art import tooling declared in `Packages/manifest.json`; related animation assets live in `Assets/Animations/`.
- `com.unity.timeline` 1.8.12 and `com.unity.visualscripting` 1.9.11 - Installed Unity feature packages declared in `Packages/manifest.json`; no authored runtime source imports these APIs under `Assets/Scripts/`.
- `com.unity.collab-proxy` 2.12.4 and `com.unity.multiplayer.center` 1.0.1 - Installed editor-oriented Unity packages in `Packages/manifest.json`; no authored multiplayer or collaboration client is present under `Assets/Scripts/`.

## Configuration

**Environment:**
- Engine and player settings are versioned Unity YAML in `ProjectSettings/ProjectVersion.txt` and `ProjectSettings/ProjectSettings.asset`; the configured product is `Top-Down-RPG` and the active editor build defines target standalone Windows.
- Input mappings are versioned in `Assets/InputSystem_Actions.inputactions`; use this asset when adding player actions rather than hard-coding input bindings in scripts.
- Gameplay data is asset-backed through ScriptableObjects, with weapon data in `Assets/Data/Weapons/Sword.asset` and AI data in `Assets/Settings/AI/`; their types are defined in `Assets/Scripts/Combat/PlayerWeapon.cs`, `Assets/Scripts/AI/Core/MobConfig.cs`, and `Assets/Scripts/AI/Navigation/TerrainMovementProfile2D.cs`.
- No `.env` files or environment-variable configuration were detected in the repository root; `Packages/manifest.json` and `ProjectSettings/` contain the project's tracked configuration.

**Build:**
- Build settings are in `ProjectSettings/EditorBuildSettings.asset`, which enables `Assets/Scenes/SampleScene.unity` as the sole configured scene.
- Player build options, platform identifiers, Android/WebGL settings, and scripting backend configuration are in `ProjectSettings/ProjectSettings.asset`.
- Package sources and resolved dependency versions are controlled by `Packages/manifest.json` and `Packages/packages-lock.json`; do not hand-edit generated IDE projects such as `TopDownRPG.Gameplay.csproj`.

## Platform Requirements

**Development:**
- Install Unity Editor 6000.4.2f1 (normally through Unity Hub) to match `ProjectSettings/ProjectVersion.txt` and restore UPM dependencies from `Packages/manifest.json`.
- Use a C# IDE supported by the installed UPM integrations in `Packages/manifest.json` (Visual Studio or Rider); generated projects require .NET Standard 2.1 support as shown in `TopDownRPG.Gameplay.csproj`.
- The Unity editor needs access to `Packages/packages-lock.json` and the Git-hosted `com.skner.dualgrid` source specified in `Packages/manifest.json` when restoring dependencies.
- For MCP-assisted editor work, start the project-scoped Unity MCP server on the developer machine after opening this project in Unity:
  ```powershell
  C:\Users\lphud\scoop\shims\uvx.exe --from "mcpforunityserver==10.1.0" mcp-for-unity --transport http --http-url http://127.0.0.1:8080 --project-scoped-tools
  ```
  The loopback endpoint is intended for the local MCP client only; it is not required to build or run the game.

**Production:**
- The current editor-generated compilation symbols target standalone Windows in `TopDownRPG.Gameplay.csproj`; player platform settings also contain Android and WebGL options in `ProjectSettings/ProjectSettings.asset`.
- Production content is a local Unity player built from `Assets/Scenes/SampleScene.unity`; no hosting, backend deployment, or custom native plugin artifact is configured under `Assets/`.

---

*Stack analysis: 2026-07-18*
