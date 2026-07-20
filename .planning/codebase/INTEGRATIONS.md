# External Integrations

**Analysis Date:** 2026-07-18

## APIs & External Services

**Runtime game services:**
- Not detected - A scan of authored code in `Assets/Scripts/`, `Assets/Editor/`, and `Assets/Tests/` finds no `UnityWebRequest`, HTTP client, socket, WebSocket, Firebase, PlayFab, Steam, Discord, database, or external-service SDK use.
  - SDK/Client: Not applicable; gameplay code uses Unity APIs such as `UnityEngine.InputSystem` in `Assets/Scripts/Player/PlayerController.cs` and local `ScriptableObject` data in `Assets/Scripts/Combat/PlayerWeapon.cs`.
  - Auth: Not applicable; no runtime authentication configuration is present in `ProjectSettings/` or `Packages/manifest.json`.

**Unity cloud services:**
- Unity Connect/Analytics/Ads/Purchasing/Crash Reporting - Service settings exist but are disabled in `ProjectSettings/UnityConnectSettings.asset`; the analytics, ads, purchasing, diagnostics, and crash-reporting enablement flags are all `0`.
  - SDK/Client: Unity built-in service configuration in `ProjectSettings/UnityConnectSettings.asset`; no service package or authored client code is used under `Assets/Scripts/`.
  - Auth: No project/game identifiers are configured in `ProjectSettings/UnityConnectSettings.asset`; no environment-variable credential configuration is detected.

**Package acquisition:**
- Unity Package Registry - UPM resolves registry packages declared in `Packages/manifest.json`; the default registry URL is configured in `ProjectSettings/PackageManagerSettings.asset` and the resolved package graph is committed in `Packages/packages-lock.json`.
  - SDK/Client: Unity Package Manager configuration in `Packages/manifest.json`.
  - Auth: No scoped registry or package credential configuration is present in `ProjectSettings/PackageManagerSettings.asset`.
- GitHub - The development-time `com.skner.dualgrid` package is fetched from the Git URL specified in `Packages/manifest.json` and locked to a commit hash in `Packages/packages-lock.json`.
  - SDK/Client: Unity Package Manager Git dependency in `Packages/manifest.json`.
  - Auth: The configured URL is public HTTPS; no GitHub token or runtime GitHub client is present in project files.

**Local editor automation:**
- Unity MCP (`mcpforunityserver` 10.1.0) - A project-owner-provided, development-time MCP server exposes the active Unity Editor to an MCP client over local HTTP. It is separate from the game's runtime integrations and is not declared in `Packages/manifest.json`.
  - Endpoint: `http://127.0.0.1:8080`; loopback binding keeps the bridge local to the developer workstation.
  - Scope: `--project-scoped-tools` limits the server to this Unity project.
  - Start command:
    ```powershell
    C:\Users\lphud\scoop\shims\uvx.exe --from "mcpforunityserver==10.1.0" mcp-for-unity --transport http --http-url http://127.0.0.1:8080 --project-scoped-tools
    ```
  - Auth: No token or game credential is specified; do not change the endpoint to a non-loopback address or expose it through a tunnel without an explicit security review.

## Data Storage

**Databases:**
- Not detected - No database client, ORM, SQLite library, or database connection configuration appears in `Packages/manifest.json` or authored C# files under `Assets/Scripts/`.
  - Connection: Not applicable.
  - Client: Not applicable.

**File Storage:**
- Unity project assets only - Static gameplay configuration is stored as Unity assets in `Assets/Data/Weapons/Sword.asset` and `Assets/Settings/AI/`; types use `ScriptableObject` in `Assets/Scripts/Combat/PlayerWeapon.cs` and `Assets/Scripts/AI/Core/MobConfig.cs`.
- No runtime save system is detected - Authored runtime code under `Assets/Scripts/` has no `PlayerPrefs`, `Application.persistentDataPath`, `File`, or `Directory` usage. The editor-only generator `Assets/Editor/Combat/PlayerSlashPrefabBootstrap.cs` writes prefab assets beneath `Assets/Prefabs/Combat/`, not player data.

**Caching:**
- None detected - No remote cache, HTTP cache, or player-persistence cache is implemented in authored code under `Assets/Scripts/`.

## Authentication & Identity

**Auth Provider:**
- Not applicable - No authentication provider, identity SDK, sign-in UI, token handling, or credentials configuration is present in `Assets/Scripts/`, `Packages/manifest.json`, or `ProjectSettings/UnityConnectSettings.asset`.
  - Implementation: Local single-player runtime state only; player behaviour is implemented in `Assets/Scripts/Player/PlayerController.cs`.

## Monitoring & Observability

**Error Tracking:**
- No external error tracker is active - Unity diagnostics and crash reporting are disabled in `ProjectSettings/UnityConnectSettings.asset`; no Sentry, Bugsnag, Firebase Crashlytics, or equivalent client appears in `Assets/Scripts/` or `Packages/manifest.json`.

**Logs:**
- Unity console/player logging - Runtime diagnostics use `UnityEngine.Debug` calls in scripts such as `Assets/Scripts/AI/Navigation/NavigationGrid2D.cs`; player logging is enabled in `ProjectSettings/ProjectSettings.asset`.

## CI/CD & Deployment

**Hosting:**
- Not applicable - The repository contains a local Unity player configuration with the enabled build scene `Assets/Scenes/SampleScene.unity` in `ProjectSettings/EditorBuildSettings.asset`; no backend or web-hosting configuration is present.

**CI Pipeline:**
- Not detected - No GitHub Actions, Azure Pipelines, Jenkins, Docker, or other tracked CI/deployment descriptor was found alongside `Packages/`, `ProjectSettings/`, and `Assets/`.

## Environment Configuration

**Required env vars:**
- None detected - No tracked `.env` file name, environment-variable loader, service credential reference, or external endpoint configuration is present; settings are asset and project files such as `ProjectSettings/ProjectSettings.asset` and `Packages/manifest.json`.

**Secrets location:**
- No secret store is configured - The tracked project configuration in `ProjectSettings/` and `Packages/` contains no external-service credentials; if future services require secrets, keep them outside committed Unity assets and package manifests.

## Webhooks & Callbacks

**Incoming:**
- None - No HTTP listener, webhook handler, or server endpoint exists in authored code under `Assets/Scripts/`.

**Outgoing:**
- None - No runtime HTTP request, event callback to an external endpoint, or webhook sender exists in `Assets/Scripts/`; disabled Unity service endpoint templates in `ProjectSettings/UnityConnectSettings.asset` do not constitute active callbacks.

---

*Integration audit: 2026-07-18*
