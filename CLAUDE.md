# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

**Local dev deploy** (build + copy DLLs into the game):
```powershell
.\install.ps1
# Optional override: .\install.ps1 -GameDir "D:\Steam\steamapps\common\Exploding Kittens 2"
```

**Build individual projects:**
```powershell
dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod
dotnet publish src/EKLobbyTray/EKLobbyTray.csproj -c Release -o out/EKLobbyTray --self-contained false -r win-x64 -p:PublishSingleFile=true
```

**Run tests:**
```powershell
dotnet test tests/EKLobbyMod.Tests/EKLobbyMod.Tests.csproj
dotnet test tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj
dotnet test tests/EKLobbyTray.Tests/EKLobbyTray.Tests.csproj
# Single test: dotnet test tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj --filter "FullyQualifiedName~ConfigStoreTests.Load_WhenFileAbsent"
```

Tests use `ConfigStore.OverridePath` / `SecretsStore.OverridePath` static fields (set to a temp directory in test setup) for file isolation — no mocking framework. `[Collection("...")]` attributes mark tests that must run serially.

**Package for distribution** (outputs `releases/EKLobbyMod-v<version>.zip`):
```powershell
.\package.ps1
```

**Cut a release** (bumps version in all files, commits, pushes, creates GitHub release):
```powershell
.\release.ps1 -Version X.Y.Z
```

## Architecture

Three projects with a shared library:

- **`src/EKLobbyShared`** (`netstandard2.0` / `net6.0`) — config and secrets persistence. No game or BepInEx dependencies. Used by both the BepInEx plugin and the tray app.
- **`src/EKLobbyMod`** (`net6.0`) — BepInEx 6 IL2CPP plugin. Harmony-patches the game; uses Photon for lobby control, Steamworks for friend invites. References `EKLobbyShared`.
- **`src/EKLobbyTray`** (`net8.0-windows`, WinForms) — standalone system-tray companion exe. Ships alongside the plugin but runs separately. References `EKLobbyShared`.

### Plugin internals (`src/EKLobbyMod/`)

- **`Plugin.cs`** — BepInEx entry point. Registers IL2CPP types, applies Harmony patches, wires Steamworks join callback, captures `+connect` cold-launch arg.
- **`LobbyManager.cs`** — Singleton created on first Photon room join. Owns room state, auto-queue countdown, peer version tracking, and rejoin logic.
- **`OverlayPanel.cs`** — `MonoBehaviour` injected into Unity scenes at load. Renders the minimized tab and expanded panel (lobby code, party size, version-drift warning, rejoin button).
- **`FriendPickerPopup.cs`** — `MonoBehaviour` for the in-game Steam friend invite popup.
- **`SteamInviter.cs`** — Steamworks rich-presence calls for Steam overlay invite flow.
- **`DiscordInviteClient.cs`** — HTTP client that posts invite to a private Discord bot endpoint (auth required; not publicly documented).
- **`PhotonClientFinder.cs`** — Harmony-patches `ExplodingKittensMultiplayer.InitNetworking` to capture the `IMultiplayerController` instance, initialize `LobbyManager`, and apply `PartyGamePatch`. This is the trigger for the entire plugin's initialization chain.
- **`PhotonControllerBridge.cs`** — Wraps `IMultiplayerController` into the testable `IPhotonBridge` interface. Translates IL2CPP Photon event delegates into plain C# events.
- **`IPhotonBridge.cs`** — The testability seam. `LobbyManager` depends only on this interface. `FakePhotonBridge` (in `tests/EKLobbyMod.Tests/`) is the test double; tests call `_manager.Handle*()` directly to simulate Photon callbacks without BepInEx or Unity.
- **`PartyGamePatch.cs`** — Applied at runtime (in `PhotonClientFinder`) once the concrete controller type is known. Prefixes `IMultiplayerController.CreateRoom` to replace the room name with `{homeLobby}-g` (deterministic, so party members can derive the target room without an out-of-band message) and calls `LobbyManager.OnPartyGameStarting()` to broadcast it via a room property before the host leaves.
- **`PhotonPropertyHelper.cs`** — Reads/writes Photon room and player custom properties, including `"ekmod_ver"` (peer mod version, key `LobbyManager.VersionPropertyKey`).
- **`NullablePolyfill.cs`** — Polyfill attributes required because `<Nullable>disable</Nullable>` is forced in the `.csproj` (IL2CPP interop assemblies hide `NullableAttribute`).

**`LobbyManager` events** (subscribed by `OverlayPanel`): `RejoinAvailable`, `RejoinConfirmed`, `PlayerListChanged`, `AutoQueueCancelled`, `GameStarted`, `VersionMapChanged`.

Non-obvious runtime patterns in `LobbyManager.cs`:
- **Steam invite join**: `JoinRoomByInvite()` temporarily overwrites `Config.LobbyRoomName` with the friend's room code, then restores the original after joining. This avoids permanently replacing the user's home lobby name.
- **Failed join → create**: The `_joinOrCreatePending` flag causes a `OnJoinRoomFailed` callback to escalate to `CreateRoom()` instead of giving up.
- **Room state machine**: Three orthogonal flags — `_inHomeLobby` (currently in our own private lobby room), `_inGame` (inside a matchmaking/game room), and `AutoQueueActive` (post-game countdown running). Only game-room leave events trigger the auto-queue countdown; home-lobby leave events are ignored.
- **Photon abstraction**: All Photon calls go through the `IMultiplayerController` interface (from `MGS.Network`), not `PhotonNetwork` directly. `PhotonClientFinder` locates the concrete implementation at runtime by searching active `MonoBehaviour`s.

**Room naming convention**: Home lobby names are `EK-{8-char uppercase hex}`, generated once by `ConfigStore.GetOrCreateRoomName()`. Game rooms created via `PartyGamePatch` are `{homeLobby}-g`. `LobbyManager.IsValidRoomName()` validates this format to distinguish mod-managed connect strings from native Steam rich-presence joins.

`OverlayPanel.cs` builds the entire UI procedurally (no asset bundles, no UXML). All layout is code-driven with area-based responsive scaling: `_s = Mathf.Clamp(Mathf.Sqrt(Screen.width * Screen.height) / 1152f, 0.6f, 2.5f)`.

### SteamInviter invite strategy

`SteamInviter.InviteAll()` prefers `MGS.Platform.SteamPlatform.InviteFriend(string id, string extra)` over the raw `SteamFriends.InviteUserToGame()` fallback, because the game's own path sets up rich-presence correctly. The method is resolved once via reflection on first call (searching `MGS.Platform` assembly for a type with `InviteFriend(string,string)` and a static `Instance` property); the singleton and `MethodInfo` are cached. If the game's method throws, it falls back to `SteamFriends.InviteUserToGame` and stops retrying the cached method.

`SteamInviter` is only safe to call while `SteamManager.Instance && SteamManager.Initialized` (the game's Steam singleton). It wraps the in-process Steamworks.NET loaded by `MGS.Platform.SteamManager` — not a separate Steam init.

### EKLobbyTray internals

`TrayApp` watches `%AppData%\EKLobbyMod\config.json` via `FileSystemWatcher` and rebuilds the context menu on changes. Windows fires the `Changed` event twice per save; a 500ms debounce suppresses the duplicate.

`SteamUriInviter` (tray) is distinct from `SteamInviter` (plugin). Running outside the game process, it cannot call Steamworks directly — it opens `steam://` protocol URIs instead.

`AutoLaunchHelper` manages a Windows registry key under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` to enable/disable start-with-Windows.

### Harmony patching

`Plugin.Load()` calls `harmony.PatchAll()` to apply all `[HarmonyPatch]`-attributed classes, then calls `SteamJoinPatch.TryApply(harmony)` manually. `SteamJoinPatch` is manual because it searches for the target method by reflection at runtime and gracefully logs a warning rather than crashing if the method is not found.

**IL2CPP postfix parameter names must exactly match the target method's IL2CPP parameter names.** When patching an IL2CPP method whose parameter is named `callback`, the Harmony postfix must also declare `GameRichPresenceJoinRequested_t callback` — using any other name causes a compile-time `Parameter "x" not found` error at patch application. Check `BepInEx.log` for the HarmonyX error line; it shows the actual parameter names from the IL2CPP signature.

`MonoBehaviour` subclasses (`OverlayPanel`, `FriendPickerPopup`) must be registered before Unity can instantiate them: `ClassInjector.RegisterTypeInIl2Cpp<T>()` is called in `Plugin.Load()`.

### Secrets and config security

`EKLobbyShared` maintains two separate stores:
- **`ConfigStore`** → `%AppData%\EKLobbyMod\config.json` — lobby name, friends list. Protected with Windows owner-only ACL and an HMAC sidecar (`config.json.hmac`) for tamper detection. A tampered file causes `Load()` to return defaults and log a warning.
- **`SecretsStore`** → `%AppData%\EKLobbyMod\secrets.json` — Discord bot shared secret. Owner-only ACL set on write. Never put secrets in `ConfigStore`.

### BepInEx DLL references

`EKLobbyMod.csproj` uses `$(BepInExDir)` which resolves to `libs/BepInEx` at build time:
- `libs/BepInEx/core/` — BepInEx core DLLs (downloaded by CI from the BepInEx nightly build)
- `libs/BepInEx/interop/` — IL2CPP interop stubs (committed; generated once by BepInEx from the game)

The `libs/BepInEx/interop/` stubs are generated from `ExplodingKittens.exe` and committed. If the game updates, they must be regenerated by running BepInEx's IL2CPP dump tool against the new binary.

`libs/BepInEx/core/` is populated automatically by `install.ps1` and `package.ps1` on first run if missing. No manual step required.

## E2E tests

`tests/e2e/` contains a Python E2E harness that drives the game via an MCP server.

### Single-machine

**Setup:**
```powershell
pip install -r tests/e2e/requirements.txt
cp tests/e2e/ek_test_config.json.example tests/e2e/ek_test_config.json
# Edit ek_test_config.json: set game_dir, steam_app_id, steam_friend_name
```

**Run:**
```powershell
python tests/e2e/test_new_room.py
```

### Two-machine (single Claude session)

Machine B runs the MCP server as an HTTP service. A single Claude Code session
on Machine A controls both machines' games via two MCP servers.

**Machine B setup** (one-time):
```powershell
.\tests\e2e\bootstrap_machine_b.ps1 `
    -GameDir "D:\Steam\steamapps\common\Exploding Kittens 2" `
    -SteamAppId 2999030 `
    -FriendSteamName "HostSteamName"
```
The bootstrap creates a Windows Scheduled Task that starts
`ek_test_server.py --transport http --port 8080` at logon.

**Machine A setup:**
Edit `.mcp.json` — replace `<machine-b-hostname>` with Machine B's actual
hostname or IP. Enable the server in `.claude/settings.local.json` by adding
`"ek-test-b"` to `enabledMcpjsonServers`, or enable it at runtime via `/mcp`.

**Run the two-machine test:**
Give Claude the prompt in `tests/e2e/prompts/unified.md`. A single session
controls both machines via `mcp__ek-test__*` (host) and `mcp__ek-test-b__*`
(joiner) — no file-based coordination needed.

The older `host.md` / `joiner.md` prompts are still available for two-session
manual coordination.

`ek_test_server.py` is a `FastMCP` server exposing 10 game control tools
(`launch_game`, `wait_for_game`, `focus_game`, `click`, `screenshot`,
`read_logs`, etc.). It supports two transports:

```
python tests/e2e/ek_test_server.py                           # stdio (default)
python tests/e2e/ek_test_server.py --transport http --port 8080  # HTTP (remote)
```

**Resolution dependency**: Click coordinates in `test_new_room.py` (`CLICKS`
dict) assume 1392×878. Update them if running at a different resolution.
Pixel-based screen detection uses `WAIT_PIXELS` coordinates for the same reason.

`ek_test_config.json` is git-ignored (not committed). The `.example` file shows
the required fields including optional `mcp_port` for HTTP mode.

## CI/CD

`.github/workflows/release.yml` triggers on `release: published`:
1. **`package` job** (windows-latest) — downloads BepInEx core to `libs/`, runs `package.ps1`, uploads the ZIP to the GitHub release. Requires `permissions: contents: write`.
2. **`deploy-pages` job** (ubuntu-latest) — deploys `hosting/ek.bring-us.com/public/` to Cloudflare Pages with `--branch main` (required for production vs. preview classification).

## Version source of truth

`Plugin.PluginVersion` in `src/EKLobbyMod/Plugin.cs` is the canonical version string. `package.ps1` and `release.ps1` both read it via regex. When bumping, run `.\release.ps1 -Version X.Y.Z` — it updates four files atomically: `Plugin.cs`, `docs/index.html`, `hosting/ek.bring-us.com/public/index.html`, and `hosting/ek.bring-us.com/public/get` (the bootstrap script).
