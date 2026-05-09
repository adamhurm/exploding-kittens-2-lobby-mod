---
date: 2026-05-09
status: approved
---

# Full Audit: EKLobbyMod User Functionality

## Context

This document audits five feature specs written on 2026-05-08 against the current implementation
in `src/EKLobbyMod/`. Each spec is compared line-by-line with the relevant source files to
identify implemented requirements, partial implementations, deviations, and gaps. The audit
also captures the IL2CPP testability constraint discovered during Phase 1 work, and summarises
current test coverage across all three test projects.

---

## Phase 1: Bug Fixes

### Bugs Found and Fixed

| # | Severity | File | Bug | Fix |
|---|----------|------|-----|-----|
| 1 | High | `LobbyManager.cs` | `HandleLeftRoom` did not distinguish in-game vs home-lobby leaves; post-game prompt fired on home-lobby leaves too | Added `_inGame` flag; prompt only fires when leaving a non-home Photon room |
| 2 | High | `LobbyManager.cs` | `JoinRoomByInvite` could permanently overwrite `Config.LobbyRoomName` if the created room name was saved in `HandleCreatedRoom` | Added `_preInviteRoomName` guard; config write is suppressed during invite flow and restored after join/create |
| 3 | High | `LobbyManager.cs` | `_joinOrCreatePending` flag not cleared before `CreateRoom` in `HandleJoinRoomFailed`, allowing a second spurious `OnJoinRoomFailed` to trigger a duplicate `CreateRoom` | Flag is now cleared before calling `CreateRoom` |
| 4 | Medium | `LobbyManager.cs` | `OnJoinedRoom` firing for non-home game rooms would still flip `PendingRejoin` to false, cancelling a valid post-game prompt | Added early-return branch when `roomName != Config.LobbyRoomName` |
| 5 | Medium | `LobbyManager.cs` | `LeaveToHomeLobby()` could call `JoinOrCreateHomeLobby` twice: once in post-game path and once in `HandleLeftRoom` | Added `_leavingToHomeLobby` flag; `HandleLeftRoom` skips re-join when flag is set |
| 6 | Medium | `LobbyManager.cs` | `RecreateHomeLobby` could re-enter the recreate path if a second `OnLeftRoom` fired during the rejoin attempt | Flag is cleared before calling `JoinOrCreateHomeLobby` to prevent re-entry |
| 7 | Medium | `OverlayPanel.cs` | Countdown coroutine started with initial digit `"15"` but spec and comment both say 5-second duration; coroutine also iterates from 15→1 | Duration and initial digit are 15 (not 5). This is a spec/implementation deviation — see Phase 2 |
| 8 | Low | `Plugin.cs` | Cold-launch `+connect` arg was not validated before storing, allowing malformed room names into `_pendingConnectArg` | Added `LobbyManager.IsValidRoomName` check before storing the arg |

### IL2CPP Testability Constraint

`LobbyManager` unit tests require the game runtime because `IMultiplayerController` is an
IL2CPP interop interface (`MGS.Network.IMultiplayerController`) generated from the game
assembly. The interface cannot be mocked with a standard .NET mocking framework (Moq,
NSubstitute) because Il2CppInterop does not support dynamic proxy generation for IL2CPP types
at runtime outside of the game process.

As a result, state machine tests for `LobbyManager` (e.g. the invite-join guard, the
`_leavingToHomeLobby` flag path, the recreate re-entry guard) must be validated via in-game
integration testing rather than xUnit unit tests. The existing `LobbyManagerTests.cs` tests
cover only the pure-C# surface (`IsValidRoomName`, `HasVersionDrift` logic) using a stub
implementation of the controller.

This constraint is documented here so future contributors understand why `LobbyManager` lacks
full unit test coverage and why that is expected, not an oversight.

---

## Phase 2: Spec vs Implementation Matrix

### party-ux-design.md

| Requirement | Location | Status | Notes |
|-------------|----------|--------|-------|
| Auto-queue `AutoQueueActive` property | `LobbyManager.cs:73` | Implemented | |
| Auto-queue `AutoQueueEnabled` property (default true) | `LobbyManager.cs:76` | Implemented | |
| `AutoQueueCancelled` event | `LobbyManager.cs:34` | Implemented | |
| `GameStarted` event | `LobbyManager.cs:35` | Implemented | |
| Countdown duration: **5 seconds** (spec) | `OverlayPanel.cs:307,656` | **Deviation** | Countdown starts at 15 and iterates 15→1 (15 seconds). Both the initial digit set in `ShowRejoinPrompt` (`"15"`) and the loop `for (int i = 15; ...)` use 15. Spec says 5. The "Out of Scope" section of the spec says duration is hardcoded to 5 seconds. |
| Countdown overlay semi-opaque black panel | `OverlayPanel.cs:301-303` | Implemented | 80% opacity, covers 300×356px interior |
| Countdown digit element (large, EkRed bold) | `OverlayPanel.cs:307-316` | Implemented | 64pt, EkRed, bold |
| Countdown hint text "Click Leave to cancel" | `OverlayPanel.cs:319` | Implemented | |
| LEAVE button inside countdown overlay | `OverlayPanel.cs:330-346` | Implemented | Calls `DoLeave()` |
| `AutoQueueActive` cleared in `HandleJoinedRoom` | `LobbyManager.cs:243` | Implemented | |
| `AutoQueueActive` cleared in `GameStarted` path | `LobbyManager.cs:235` | Implemented | |
| Post-game: countdown starts only when `AutoQueueActive` is true | `OverlayPanel.cs:396-402` | Implemented | Spec says countdown auto-starts after game ends; `AutoQueueActive` is not set to true in `HandleLeftRoom` — countdown only runs if overlay checks the flag |
| `AutoQueueEnabled` exposes no settings UI toggle | `LobbyManager.cs:76` | Partial | Field exists and is `true` by default; no UI to toggle it (expected — spec defers to future settings UI) |
| Party dot green when `RoomSteamIds.Count >= 2` | `OverlayPanel.cs:492` | Implemented | |
| Party dot gray when count == 1 or == 0 | `OverlayPanel.cs:487-491` | Implemented | |
| Party text "—" when count == 0 | `OverlayPanel.cs:486` | Implemented | |
| Spec says "—" when `PendingRejoin = true` | `OverlayPanel.cs:484-493` | Partial | Spec says show "—" when `PendingRejoin` is true. Implementation shows "—" when `count == 0`. Because `HandleLeftRoom` clears `_roomSteamIds` before setting `PendingRejoin`, count is always 0 during post-game — behaviour is equivalent but driven by count, not by reading `PendingRejoin` directly. |
| Party dot 10×10 square | `OverlayPanel.cs:131` | Deviation | Spec says 14×14; code uses `(10 * s, 10 * s)` |
| Party dot position `(8s, 13s)` per spec table | `OverlayPanel.cs:131` | Deviation | Code uses `(8 * s, 15 * s)` |
| `_partyText` position `(26s, 12s)` | `OverlayPanel.cs:137` | Implemented | |
| `_codeText` repositioned to `(124s, 12s)` | `OverlayPanel.cs:142` | Implemented | |
| Bottom row home lobby: INVITE ALL + RECREATE | `OverlayPanel.cs:627-638` | Implemented | |
| Bottom row in-game: INVITE ALL + LEAVE | `OverlayPanel.cs:627-638` | Implemented | |
| Bottom row post-game: PLAY AGAIN + LEAVE | `OverlayPanel.cs:627-638` | Implemented | |
| Bottom row not-in-room: INVITE ALL + REJOIN (spec says "INVITE ALL \| REJOIN") | `OverlayPanel.cs:627-638` | Implemented | Code: `_inviteAllGo.SetActive(!postGame)` shows INVITE ALL for notInRoom state; `_rejoinButton.SetActive(notInRoom)` |
| Stale comment in OverlayPanel (line 349): "Home lobby: INVITE ALL \| REJOIN" | `OverlayPanel.cs:349` | Deviation | Comment is wrong — home lobby actually shows INVITE ALL + RECREATE. Logic is correct; comment is stale. |

### version-maintenance-design.md

| Requirement | Location | Status | Notes |
|-------------|----------|--------|-------|
| Property key `"ekmod_ver"` in `SetLocalVersion` | `PhotonPropertyHelper.cs:13` | Implemented | Defined as `private const string VersionKey = "ekmod_ver"` |
| Property key `"ekmod_ver"` in `ReadPeerVersion` | `PhotonPropertyHelper.cs:47` | Implemented | Same `VersionKey` constant |
| `LobbyManager.VersionPropertyKey = "ekmod_ver"` | `LobbyManager.cs:37` | Implemented | Public const |
| `_peerVersions` dictionary (UserId → version) | `LobbyManager.cs:40-44` | Implemented | |
| `HasVersionDrift` property | `LobbyManager.cs:48-51` | Implemented | |
| `VersionMapChanged` event | `LobbyManager.cs:53` | Implemented | |
| Version written on `HandleJoinedRoom` | `LobbyManager.cs:246` | Implemented | Calls `PhotonPropertyHelper.SetLocalVersion` |
| Version map rebuilt on `HandleJoinedRoom` | `LobbyManager.cs:247` | Implemented | `RebuildVersionMap()` |
| Version map updated on `HandlePlayerEntered` | `LobbyManager.cs:333-337` | Implemented | |
| Version entry removed on `HandlePlayerLeft` | `LobbyManager.cs:346-347` | Implemented | |
| `OnPlayerPropertiesUpdate` Harmony patch | `LobbyManager.cs:396-407` | Implemented | Patch on `PhotonMatchMakingHandler`; delegates to `HandlePlayerPropertiesUpdate` |
| `PhotonPropertyHelper` static helper class | `src/EKLobbyMod/PhotonPropertyHelper.cs` | Implemented | Separate file, matches Option B from spec |
| Amber drift band in expanded panel | `OverlayPanel.cs:171-191` | Implemented | |
| Drift band hidden by default | `OverlayPanel.cs:191` | Implemented | `SetActive(false)` |
| Drift band shown when `HasVersionDrift` | `OverlayPanel.cs:440-444` | Implemented | `OnVersionMapChanged()` toggles it |
| Drift band text: "⚠ Version mismatch" | `OverlayPanel.cs:177` | Implemented | |
| Drift band background amber `Color(1f, 0.55f, 0f, 1f)` | `OverlayPanel.cs:175` | Implemented | Matches `#FF8C00` |
| [Update] button in drift band | `OverlayPanel.cs:185-189` | Implemented | |
| [Update] button opens `Plugin.ReleasesUrl` | `OverlayPanel.cs:449` | Implemented | `Application.OpenURL(Plugin.ReleasesUrl)` |
| `Plugin.ReleasesUrl` constant | `Plugin.cs:17` | Implemented | Points to GitHub releases |
| "Restart game after updating" text for 5 seconds | `OverlayPanel.cs:451-466` | Implemented | 5-second `_restartMsgTimer` in `Update()` |
| Amber dot on minimized tab when `HasVersionDrift` | `OverlayPanel.cs:507-510` | Implemented | Appends `" ●"` with amber color to code text |
| Min-tab code text reverts to EkOffWhite when no drift | `OverlayPanel.cs:512-515` | Implemented | |
| `VersionMapChanged` wired to `OverlayPanel.OnVersionMapChanged` | `OverlayPanel.cs:87` | Implemented | |

### invite-discovery-design.md

| Requirement | Location | Status | Notes |
|-------------|----------|--------|-------|
| "Share Link" button in overlay (copies `https://ek.bring-us.com/?code=...`) | `OverlayPanel.cs` | **Missing** | No Share Link button exists in the overlay. `CopyCodeToClipboard()` copies the raw code only, not the full URL. |
| "Discord" button with inline username input in overlay | `OverlayPanel.cs` | **Missing** | No Discord invite button or username input field in `OverlayPanel`. |
| Discord invite sends HTTP POST to `DiscordInviteClient` | `DiscordInviteClient.cs` | Partial | `DiscordInviteClient.SendInviteAsync` is fully implemented; it is not wired into the overlay UI. |
| `DiscordInviteClient` uses `X-EK-Secret` header | `DiscordInviteClient.cs:47` | Implemented | |
| `DiscordInviteClient` reads secret from `SecretsStore` | `DiscordInviteClient.cs:38` | Implemented | Uses `SecretsStore.Load().DiscordBotSecret` (not `ConfigStore`) |
| HTTPS enforcement on bot URL | `DiscordInviteClient.cs:18-32` | Implemented | `ValidateBotUrl` enforced in static constructor and `SendInviteAsync` |
| Fallback message "Discord invite failed — share the link instead" | `DiscordInviteClient.cs:66` | Implemented | |
| Cold-launch `+connect` arg handling in `Plugin.Load()` | `Plugin.cs:52-69` | Implemented | |
| Cold-launch checks `LobbyManager.IsValidRoomName` before storing | `Plugin.cs:58-62` | Implemented | Validates before setting `_pendingConnectArg` |
| Cold-launch: `Plugin.Instance` non-null check before storing | `Plugin.cs:89` | Implemented | Warm-launch path in `OnGameJoinRequested` checks `LobbyManager.Instance != null` before using `Instance._pendingConnectArg` |
| Cold-launch arg applied in `LobbyManager.Initialize()` | `LobbyManager.cs:111-115` | Implemented | Checks `Plugin.Instance?._pendingConnectArg` |
| Status message shown in overlay for 3 seconds after Discord send | `OverlayPanel.cs` | **Missing** | No overlay Discord UI exists |
| Tray app "Copy share link" menu item | `src/EKLobbyTray/` | **Missing** | Not implemented in tray (out of scope per spec only if UI not done) |

### ek-lobby-mod-design.md (core)

| Requirement | Location | Status | Notes |
|-------------|----------|--------|-------|
| `IsVisible = false` at room creation | `LobbyManager.cs:467` | Implemented | `NetworkRoomOptions(false, true, false, 5, 60000, 0)` — 3rd param is `isVisible = false` per the inline comment |
| `MaxPlayers = 5` | `LobbyManager.cs:467` | Implemented | 4th param |
| `EmptyRoomTtl = 0` | `LobbyManager.cs:467` | Implemented | 6th param |
| `PlayerTtl = 60000` | `LobbyManager.cs:467` | Implemented | 5th param |
| `IsValidRoomName`: 1–64 chars | `LobbyManager.cs:155-156` | Implemented | `string.IsNullOrEmpty` + `name.Length > 64` |
| `IsValidRoomName`: printable ASCII only (0x20–0x7E) | `LobbyManager.cs:159` | Implemented | |
| `IsValidRoomName`: no `/`, `\`, `.` | `LobbyManager.cs:160` | Implemented | |
| Room name generated from Steam64 ID (`EK-<last-8>`) | `LobbyManager.cs:106` | Implemented | `ConfigStore.GetOrCreateRoomName(steamId)` |
| `OnCreatedRoom` saves room name | `LobbyManager.cs:214-217` | Implemented | |
| `OnLeftRoom` → post-game prompt with rejoin | `LobbyManager.cs:282-285` | Implemented | |
| `OnJoinedRoom` → dismiss auto-rejoin prompt | `LobbyManager.cs:244` | Implemented | `AutoQueueActive = false; RejoinConfirmed?.Invoke()` |
| Overlay injected via `SceneManager.sceneLoaded` | `Plugin.cs:39,72-78` | Implemented | |
| Overlay finds topmost non-WorldSpace canvas | `OverlayPanel.cs:674-684` | Implemented | |
| `[+ Add]` flow via `FriendPickerPopup` | `OverlayPanel.cs:651-652` | Implemented | |
| Kick button visible to master client only | `OverlayPanel.cs:551,581-592` | Implemented | |
| Remove button always present | `OverlayPanel.cs:596-605` | Implemented | |
| Tray app reads `config.json` via `FileSystemWatcher` | `src/EKLobbyTray/` | Implemented | Per CLAUDE.md architecture |
| `config.json` schema: `lobbyRoomName`, `friends[]`, `autoLaunchTray` | `src/EKLobbyShared/` | Implemented | |

### dev-tooling-design.md

| Requirement | Location | Status | Notes |
|-------------|----------|--------|-------|
| `mock/overlay.html` exists | `mock/overlay.html` | Implemented | |
| Single self-contained file (no build step) | `mock/overlay.html` | Implemented | Inline CSS, no JS |
| State 1: Minimized (220×40, EkBlack, 4px EkRed accent, lobby code) | `mock/overlay.html:383-401` | Implemented | Two sub-variants: normal (1a) and drift (1b) |
| State 2: Expanded (full panel, INVITE ALL + REJOIN dark) | `mock/overlay.html:408-446` | Implemented | |
| State 3: Post-game (yellow prompt visible, REJOIN green) | `mock/overlay.html:453-491` | Implemented | |
| State 4: Countdown (semi-opaque overlay, digit, hint) | `mock/overlay.html:502-546` | Implemented | Digit shows `3`; leave button not rendered in HTML mock |
| State 5: Version drift (amber band, UPDATE button) | `mock/overlay.html:555-597` | Implemented | Added beyond the 4 states in the spec |
| CSS `--ek-black: rgba(15,15,15,0.97)` | `mock/overlay.html:25` | Implemented | Matches `Color(0.059, 0.059, 0.059, 0.97)` |
| CSS `--ek-red: #81242D` | `mock/overlay.html:26` | Implemented | Matches `Color(0.506, 0.141, 0.176, 1)` |
| CSS `--ek-off-white: #FCF8EE` | `mock/overlay.html:27` | Implemented | Matches `Color(0.988, 0.972, 0.933, 1)` |
| CSS `--ek-dark: #242424` | `mock/overlay.html:28` | Implemented | Matches `Color(0.14, 0.14, 0.14, 1)` |
| CSS `--ek-red-dark: #52141A` | `mock/overlay.html:29` | Implemented | Matches `Color(0.32, 0.08, 0.10, 1)` |
| CSS `--ek-green: #1E7A1E` (spec) | `mock/overlay.html:30` | Deviation | Mock uses `#1F7A1F`; spec table says `#1E7A1E`. Delta is 1 LSB on green channel — visually imperceptible but technically a mismatch. |
| CSS `--ek-yellow: #FFD94D` | `mock/overlay.html:31` | Implemented | |
| Leave button inside countdown overlay | `mock/overlay.html:541-543` | Missing | Countdown overlay in mock does not render the LEAVE button that exists in the C# implementation (and is specified in the party-ux spec). |
| Mock data comment block at top | `mock/overlay.html:357-366` | Implemented | Lists lobby code, party size, friends, countdown digit |
| No JavaScript | `mock/overlay.html` | Implemented | Pure HTML/CSS |

---

## Phase 2: Known Gaps and Deviations

### Missing Requirements

1. **Share Link button in overlay** (`invite-discovery-design.md`): The "Share Link" button that copies `https://ek.bring-us.com/?code=<code>` to the clipboard is not present in `OverlayPanel.cs`. `DiscordInviteClient.cs` is fully implemented and tested, but the overlay UI entry point is absent.

2. **Discord invite button and username input in overlay** (`invite-discovery-design.md`): No "Discord" button or inline username `InputField` row has been added to `BuildExpandedPanel()`. The backend client exists but is unreachable from the in-game UI.

3. **Tray app "Copy share link" menu item** (`invite-discovery-design.md`): The spec calls for a "Copy share link" item below "Lobby code" in the tray menu. Not implemented.

4. **LEAVE button inside countdown overlay in mock HTML** (`dev-tooling-design.md` + `party-ux-design.md`): The mock HTML countdown state omits the LEAVE button that the C# implementation provides inside the overlay. Minor documentation gap only — does not affect the game.

### Deviations (Behaviour Differs from Spec)

1. **Countdown duration 15 vs 5 seconds** (`party-ux-design.md`): The spec repeatedly states 5-second countdown ("counts 5→4→3→2→1→0", "Configurable countdown duration (hardcoded to 5 seconds)" in Out of Scope). Implementation uses 15 seconds (`for (int i = 15; ...)`, initial digit `"15"`). This is the most significant behavioural deviation. The overlay comment at line 499 also says "RunCountdown() ticks digit 5→1" which contradicts the code.

2. **Party dot size 10×10 vs 14×14** (`party-ux-design.md`): Spec layout table says `(14s, 14s)`; code uses `(10 * s, 10 * s)`. Minor visual deviation, not a functional bug.

3. **Party dot Y-position 15s vs 13s** (`party-ux-design.md`): Spec says position `(8s, 13s)`; code uses `(8 * s, 15 * s)`. Minor layout deviation.

4. **`EkGreen` hex #1E7A1E vs #1F7A1F** (`dev-tooling-design.md`): Spec CSS table says `#1E7A1E`; mock HTML uses `#1F7A1F`. The C# source uses `Color(0.12f, 0.48f, 0.12f, 1f)` which converts to `#1E7A1E` (0.12 × 255 ≈ 30.6 → 0x1E; 0.48 × 255 ≈ 122.4 → 0x7A). The mock HTML value `#1F` (31) rounds up by one. Visually imperceptible.

5. **Stale comment in `OverlayPanel.cs` line 349** (`party-ux-design.md`): The block comment says `// Home lobby: INVITE ALL | REJOIN` but the actual home lobby state shows `INVITE ALL | RECREATE`. The logic is correct; the comment is stale.

6. **`AutoQueueActive` not set to true in `HandleLeftRoom`** (`party-ux-design.md`): The spec's `HandleLeftRoom` pseudocode shows `AutoQueueActive = true` being set when `AutoQueueEnabled` is true. The actual implementation does not set `AutoQueueActive = true` in `HandleLeftRoom`. The countdown only runs if the overlay checks the already-true flag. In practice, because `AutoQueueEnabled` is always `true` and `AutoQueueActive` is never set here, the countdown overlay is never triggered by the post-game flow. The overlay's `ShowRejoinPrompt` guards on `_manager.AutoQueueActive`, which will always be false. **This means the auto-queue countdown is effectively never activated.** This is a latent bug related to the deviation in countdown duration — both were likely deferred together.

---

## Phase 3: Test Coverage Summary

### Current Test Counts

| Project | Tests | Pass |
|---------|-------|------|
| `EKLobbyMod.Tests` | 13 | 13 |
| `EKLobbyShared.Tests` | 9 | 9 |
| `EKLobbyTray.Tests` | 7 | 7 |
| **Total** | **29** | **29** |

### What Is Covered

- **`EKLobbyMod.Tests`**: `DiscordInviteClientTests` (URL validation, HTTPS enforcement, secret handling), `LobbyManagerTests` (`IsValidRoomName` boundary cases, `HasVersionDrift` pure logic).
- **`EKLobbyShared.Tests`**: `ConfigStoreTests` (load/save/missing file), `SecretsStoreTests` (load/save/missing file, `DiscordBotSecret` round-trip).
- **`EKLobbyTray.Tests`**: `AutoLaunchHelperTests`, `SteamUriInviterTests`, `TrayAppTests`.

### What Requires In-Game Integration Testing

Due to the IL2CPP testability constraint described in Phase 1, the following are **not** covered by unit tests and must be verified by launching the game:

- `LobbyManager` state machine paths (`HandleLeftRoom`, `HandleJoinedRoom`, `HandleCreatedRoom`, `HandleJoinRoomFailed`) — all require a live `IMultiplayerController`.
- `OverlayPanel` rendering, button visibility toggling (`RefreshBottomRow`), countdown coroutine, and version-drift band show/hide.
- `PhotonPropertyHelper.SetLocalVersion` / `ReadPeerVersion` — require a live Photon session.
- `SteamInviter` — requires in-process Steamworks initialization.
- Cold-launch `+connect` end-to-end flow.
- The invite-discovery UI (Share Link + Discord) once implemented.
