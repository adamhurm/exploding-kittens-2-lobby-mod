# EKLobbyMod — Version Maintenance Design

**Date:** 2026-05-08
**Status:** Approved

---

## Summary

Three coordinated features that detect, display, and guide resolution of mod version mismatches across a private friend lobby:

1. **Version Broadcast** — each player writes their mod version into Photon custom player properties when joining a room; all clients observe the full version map in real time.
2. **Drift Indicator** — a visual warning in the overlay when any peer's version differs from the local version, with an "Update" button that opens the GitHub releases page.
3. **Hot-Reload** — not feasible in BepInEx 6 IL2CPP; replaced with an in-game notification instructing the user to restart.

Scope: private friends-only lobbies managed by EKLobbyMod. No server components required.

---

## Background & Constraints

- **Mod version source:** `Plugin.PluginVersion` (`"1.0.0"` semver constant in `Plugin.cs`). This is the only authoritative version string.
- **Photon layer:** The game wraps Photon Realtime behind `IMultiplayerController` (namespace `MGS.Network`). Harmony patches on `PhotonMatchMakingHandler` are the existing hook points. The existing code does not use `RaiseEvent` or custom properties.
- **BepInEx version:** 6 IL2CPP. Plugins are loaded once at game start by the doorstep proxy. DLL replacement without a restart is not possible (see Hot-Reload section).
- **UI layer:** `OverlayPanel.cs` — uGUI injected into the game's topmost Canvas, using legacy `Text` and `Button` primitives. All layout is absolute-positioned and scale-adjusted by `_s`.
- **No new dependencies:** All additions must compile against the assemblies already referenced in `EKLobbyMod.csproj`.

---

## Feature 1: Version Broadcast

### Mechanism: Photon Custom Player Properties

**Decision: custom player properties over RaiseEvent.**

Rationale:
- Custom player properties are stored in `PhotonNetwork.LocalPlayer.CustomProperties` and replicated to all current and future room members automatically. A late-joining player receives the full property set of every existing player in the `OnJoinedRoom` callback — no catch-up message needed.
- `RaiseEvent` is fire-and-forget. A player who joins after the initial event fires never receives it, requiring a separate "re-announce on join" mechanism. This adds complexity with no benefit for a small static value like a version string.
- The existing codebase uses `NetworkPlayer` objects tracked in `_roomSteamIds`. Custom player properties are the natural complement to `UserId` — they live on the same `NetworkPlayer` object.

**Property key:** `"ekmod_ver"` (short key to minimise Photon property overhead).

**Property value:** The local mod version string, e.g. `"1.0.0"`. Absent key means player has no mod (or an older version that predates this feature — treat as `"unmodded"`).

### Implementation

**Where to write (local player):**

In `LobbyManager.HandleJoinedRoom()`, after `RefreshRoomPlayers()`, call:

```csharp
_controller.SetLocalPlayerCustomProperty(VersionPropertyKey, Plugin.PluginVersion);
```

This requires `IMultiplayerController` to expose a `SetLocalPlayerCustomProperty(string key, string value)` method, or an equivalent. If the interface does not expose this directly, patch `PhotonMatchMakingHandler.OnJoinedRoom` to call it via the `PhotonNetwork.LocalPlayer` API surface. (See implementation notes below.)

**Where to read (all players):**

Add `IMultiplayerController.GetPlayerCustomProperty(NetworkPlayer player, string key): string` (returns null if absent), OR read directly from the `NetworkPlayer.CustomProperties` Il2Cpp hashtable.

**When to re-evaluate drift:**

`LobbyManager` fires `PlayerListChanged` whenever the room population changes. Version drift evaluation is triggered by the same event, plus a new `VersionMapChanged` event.

Specifically, the version map is rebuilt:
1. In `HandleJoinedRoom` — after writing local property, read all players.
2. In `HandlePlayerEntered` — check new player's property.
3. In `HandlePlayerLeft` — remove departed player from map.
4. On `OnPlayerPropertiesUpdate` (new Harmony patch on `PhotonMatchMakingHandler`) — re-read the updated player's property.

**Data model in LobbyManager:**

```csharp
public const string VersionPropertyKey = "ekmod_ver";

// Maps UserId -> mod version string. Null/absent = unmodded or pre-broadcast version.
private readonly Dictionary<string, string> _peerVersions = new();

public IReadOnlyDictionary<string, string> PeerVersions => _peerVersions;

public bool HasVersionDrift =>
    _peerVersions.Values.Any(v => v != Plugin.PluginVersion);

public event System.Action VersionMapChanged;
```

### IMultiplayerController Gap Analysis

The existing `IMultiplayerController` is used for: `JoinRoom`, `CreateRoom`, `GetRoomName`, `GetRoomPlayers`, `IsMasterClient`, `AllowKickPlayers`, `KickPlayer`, and event subscriptions. Custom property access is not currently in scope.

Two options:

**Option A:** Harmony-patch `PhotonMatchMakingHandler.OnPlayerPropertiesUpdate` to intercept the callback inline, and call `PhotonNetwork.LocalPlayer.SetCustomProperties(...)` directly by casting via Il2CppInterop. Avoids a helper class but scatters Photon API calls across patch classes.

**Option B (preferred):** Add a `PhotonPropertyHelper` static class that wraps `PhotonNetwork.LocalPlayer.SetCustomProperties` and `NetworkPlayer.CustomProperties` reads. Concentrates all property I/O in one place, consistent with the existing `SteamInviter` pattern (static helper over an external API).

**Decision: Option B** — add a `PhotonPropertyHelper` static class that accesses `PhotonNetwork.LocalPlayer` directly via `PhotonUnityNetworking.dll` (already present in the interop directory). Add the reference to `EKLobbyMod.csproj`. A separate Harmony patch class intercepts `OnPlayerPropertiesUpdate` and delegates to `LobbyManager`. This is self-contained and does not require interface changes to `IMultiplayerController`.

---

## Feature 2: Drift Indicator

### Visual Design

The indicator has two parts:

**Minimized tab:** A small amber dot (`●`) appended to the lobby code text when drift is detected. The dot disappears when all versions match.

```
[ EK-A7F3 ● ]   ← amber dot when drift present
[ EK-A7F3   ]   ← no dot when all match
```

**Expanded panel:** A warning band injected between the header strip and the code row, visible only when drift is detected. Height: `20 * _s` px. Background: amber (`#FF8C00`, `new Color(1f, 0.55f, 0f, 1f)`).

```
┌─ MY LOBBY ─────────────────────── [_] ┐
│ ⚠ Version mismatch detected  [Update] │  ← amber band, hidden when no drift
│ Code: EK-A7F3  [Copy] [✏]             │
│ ...                                   │
└───────────────────────────────────────┘
```

The warning band contains:
- Left-aligned text: `"⚠ Version mismatch"` (font size `(int)(11 * _s)`, color black for legibility on amber).
- Right-aligned "Update" button (`52 * _s` wide, `18 * _s` tall), dark red background, white text.

**"Update" button behavior:** Calls `Application.OpenURL(Plugin.ReleasesUrl)`. `ReleasesUrl` is a `const string` in `Plugin.cs` set to `"https://github.com/adamhurm/exploding-kittens-mod/releases"` — a single point of maintenance.

The band is hidden by default (`SetActive(false)`). `OverlayPanel` subscribes to `LobbyManager.VersionMapChanged` and calls `SetVersionDriftVisible(bool)` to toggle it.

### Restart Notification (hot-reload substitute)

When the user clicks "Update", after opening the browser, a secondary amber text line replaces the warning band:

```
│ ⚠ Restart game after updating         │
```

This text fades out (via coroutine or a 5-second timer via `Update()`) and the band reverts to showing the version mismatch warning.

**Rationale:** The user may click "Update" to see the releases page. We remind them that updating requires restarting the game. The band stays visible because the mismatch still exists until they restart.

### Overlay Layout Change

Current `BuildExpandedPanel()` builds the panel at `300 * _s` wide, `400 * _s` tall. The drift band takes `20 * _s` height from the reserved space between the header (`y = 356*_s`) and the code row (`y = 322*_s`). There is `34 * _s` of space there (header bottom to code row top). Inserting a `20 * _s` band starting at `y = 338 * _s` fits without layout shifts. The band is hidden when not needed, so the visual space gap when no drift is present is acceptable given the existing generous spacing.

---

## Feature 3: Hot-Reload Feasibility

### Verdict: Not Feasible

**Reason 1 — BepInEx IL2CPP plugin loading model.** BepInEx 6 IL2CPP loads plugins via its chainloader during the doorstep/preloader phase, before `UnityEngine.Application` is active. Plugins are loaded as managed .NET assemblies via `Assembly.LoadFrom`. Once loaded, the CLR holds a lock on the file (on Windows). There is no `Unload` path in `BasePlugin` or BepInEx's plugin infrastructure.

**Reason 2 — IL2CPP interop registration is permanent.** `ClassInjector.RegisterTypeInIl2Cpp<T>()` registers the managed type in the IL2CPP runtime's internal type table. This registration cannot be undone without killing the process. The mod registers `OverlayPanel` and `FriendPickerPopup` in `Plugin.Load()`. Re-registering after a reload would cause undefined behavior or crash.

**Reason 3 — Harmony patches are process-global.** HarmonyX patches are applied to the underlying method via native code hooks. Unpatching and re-patching while the game runs could leave the patched methods in an inconsistent state, particularly if a patched method is on the call stack during the operation.

**Reason 4 — Unity scene objects.** The `OverlayPanel` `MonoBehaviour` is injected into the live scene hierarchy. Destroying and recreating it on reload is theoretically possible but would require tracking and cleaning up every `UnityEngine.Object` the mod created, including event subscriptions on game-owned objects.

**Reason 5 — No BepInEx API for it.** BepInEx 6 does not expose a `PluginManager.Reload()` or similar API. The only supported lifecycle is load-once-per-process.

### Alternative: In-Game Update Notification

Rather than hot-reload, the mod notifies the user through the overlay (Feature 2) that a version mismatch exists. Clicking "Update" opens the GitHub releases page. After the user downloads the new DLL and replaces it in `BepInEx/plugins/`, they must restart the game. This is the standard BepInEx mod update flow and is the correct approach.

A future quality-of-life enhancement could auto-download the new DLL to a staging directory and show a "Restart to finish updating" prompt, but this is explicitly out of scope for this iteration. Auto-download is not designed here because: (a) it requires GitHub API calls from within the game process, (b) file replacement while the DLL is loaded is impossible on Windows, and (c) it adds meaningful risk with limited payoff for a small private-use mod.

---

## Data Flow Summary

```
Player joins room
       │
       ▼
LobbyManager.HandleJoinedRoom()
       │
       ├─► PhotonPropertyHelper.SetLocalVersion(Plugin.PluginVersion)
       │       (writes "ekmod_ver" to LocalPlayer.CustomProperties)
       │
       └─► RebuildVersionMap()
               │
               ▼
       LobbyManager._peerVersions updated
               │
               ▼
       VersionMapChanged event fires
               │
               ▼
       OverlayPanel.OnVersionMapChanged()
               │
               ├─► if HasVersionDrift → show amber band + min-tab dot
               └─► if !HasVersionDrift → hide amber band + min-tab dot
```

---

## New Files

| File | Purpose |
|---|---|
| `src/EKLobbyMod/PhotonPropertyHelper.cs` | Static helper: SetLocalVersion, ReadPeerVersion. Accesses PhotonNetwork.LocalPlayer via PhotonUnityNetworking interop. |

## Changed Files

| File | Change |
|---|---|
| `EKLobbyMod.csproj` | Add `PhotonUnityNetworking` reference (already in interop dir) |
| `LobbyManager.cs` | Add `_peerVersions`, `VersionMapChanged` event, `HasVersionDrift`, `RebuildVersionMap()`, patch hook for `OnPlayerPropertiesUpdate` |
| `OverlayPanel.cs` | Add drift band, amber dot on min-tab, `OnVersionMapChanged()` subscription |
| `Plugin.cs` | Add `ReleasesUrl` constant |

---

## Out of Scope

- Auto-downloading or auto-installing the updated DLL
- Showing which specific peer has a mismatched version (the warning is binary: drift / no drift)
- Handling the case where one player has no mod installed (unmodded players cannot send the property; treat as unknown, not a mismatch, to avoid false positives)
- Version broadcast for the EKLobbyTray companion app (tray does not connect to Photon)
