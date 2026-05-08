# Exploding Kittens 2 — Lobby Mod Design

**Date:** 2026-05-08
**Status:** Approved

## Summary

A BepInEx IL2CPP mod for Exploding Kittens 2 (Steam) that gives a fixed group of friends a persistent private lobby code. After each game the overlay prompts everyone to rejoin; a system tray companion app lets any member summon the group even when the game isn't open.

---

## Background & Constraints

- **Game:** Exploding Kittens 2, `ExplodingKittens.exe`, Unity + IL2CPP, `GameAssembly.dll`
- **Multiplayer SDK:** Photon Realtime (`PhotonRealtime` assembly, `Photon.Realtime.LoadBalancingClient`)
- **Game session layer:** AWS GameLift Realtime (`MGS.GameLift.Realtime`)
- **Steam SDK:** Steamworks.NET already loaded in-process (`MGS.Platform.SteamManager`)
- **No official mod API** exists; no existing community mods
- **Server components are not controllable** — all persistence must be client-side illusions (recreate same room name on demand)

---

## Architecture

Two artifacts shipped together, installed into the game directory:

| Artifact | Type | Runs in |
|---|---|---|
| `EKLobbyMod.dll` | BepInEx IL2CPP plugin | Game process |
| `EKLobbyTray.exe` | .NET 8 WinForms tray app | Windows tray |

**Shared state:** `%AppData%\EKLobbyMod\config.json`
- Saved friend list (Steam64 IDs + display names)
- Home lobby room name (`EK-<suffix>`)
- No IPC needed — both components read/write the same file; tray app uses `FileSystemWatcher` to pick up in-game changes immediately

**Toolchain prerequisites (one-time setup):**
1. BepInEx 6.x IL2CPP build installed into game directory
2. Il2CppDumper or Cpp2IL run against `GameAssembly.dll` to produce `DummyDll/` type stubs — enables strongly-typed references to game classes at compile time

---

## Component 1: EKLobbyMod.dll (BepInEx Plugin)

### Photon Room Management

Hooks `Photon.Realtime.LoadBalancingClient` via Harmony/Il2CppInterop:

| Callback | Action |
|---|---|
| `OnCreatedRoom` | Save room name to `config.json` as home lobby code |
| `OnLeftRoom` | If scene is returning to menu, expand overlay and highlight Rejoin |
| `OnJoinedRoom` | Confirm rejoin succeeded, dismiss auto-rejoin prompt |

**Persistent room name:** Generated once as `EK-<last-8-chars-of-Steam64ID>`, stored in `config.json`. All `CreateRoom()` / `JoinOrCreateRoom()` calls use this name. Photon closes empty rooms but allows immediate reuse of the same name — "persistence" is achieved by always recreating on demand.

**Room creation options:**
- `IsVisible = false` — private, excluded from matchmaking
- `MaxPlayers = 5` — Exploding Kittens 2's documented maximum player count
- `EmptyRoomTtl = 0` — default; room closes when empty (expected, we recreate)

### In-Game Overlay UI

Injected into the game's Unity canvas at scene load:

**Hook:** `SceneManager.sceneLoaded` → walk `Canvas` hierarchy → append panel `GameObject` as child of topmost canvas.

**Expanded panel:**
```
┌─ EK Lobby ──────────────────── [_] ┐
│ Your code: EK-A7F3          [Copy] │
│                                    │
│ Friends                   [+ Add]  │
│  ● Alice (in lobby)                │
│  ○ Bob (offline)                   │
│  ○ Carol (online)                  │
│                                    │
│        [Invite All]  [Rejoin]      │
└────────────────────────────────────┘
```

**Minimized state:** Small tab pinned to screen corner showing only the lobby code; expands on click.

**[+ Add] flow:** Clicking the button opens a scrollable popup listing all Steam friends (fetched via in-process `SteamFriends.GetFriendCount` / `GetFriendByIndex`). Each entry shows the friend's Steam avatar and display name. Clicking a name adds them to the list and writes `config.json`; the popup closes. Friends already on the list are grayed out.

**Friend status:** Polled from `SteamFriends.GetFriendPersonaState` (in-process, no API key needed).

**Invite All:** Sends Steam invites to all friends on the list not already in the lobby.

**Rejoin:** Calls `JoinOrCreateRoom()` with the saved room name.

**Styling:** Unity default uGUI primitives (Image, Text, Button) with colors matched to the game's palette. No custom asset bundles.

### Post-Game Auto-Rejoin Flow

1. `OnLeftRoom` fires as the game session ends
2. Scene transitions back to main menu / lobby screen
3. Overlay automatically expands, Rejoin button is highlighted
4. Prompt displayed: *"Game over — return to your lobby?"*
5. Player clicks Rejoin → `JoinOrCreateRoom()` called with saved room name
6. On `OnJoinedRoom` → overlay returns to normal state

---

## Component 2: EKLobbyTray.exe (System Tray Companion)

Minimal .NET 8 WinForms app, runs hidden (no main window), system tray icon only.

**Tray menu:**
```
[EK Lobby icon]
  ├─ Lobby code: EK-A7F3  (click to copy)
  ├─ ─────────────────────────────────
  ├─ Invite All Friends
  ├─ Alice  (submenu: Remove from list)
  ├─ Bob
  ├─ Carol
  ├─ ─────────────────────────────────
  ├─ Launch Game
  └─ Quit
```

The tray app runs outside the game process and has no Steamworks.NET integration — it shows friend names only (no online/offline status). Live status is only available in the in-game overlay, which runs inside the process where Steam is already initialized.

**Invite mechanism:** Opens `steam://friends/invite/<steamid>` URI per friend via `Process.Start`. Uses the Steam client's built-in invite handler — no Steam Web API key required.

**Auto-launch (opt-in):** Tray menu checkbox writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\EKLobbyTray`. Off by default.

**Live sync with in-game changes:** `FileSystemWatcher` on `config.json` — tray menu rebuilds when the file changes, no restart needed.

---

## Data: config.json Schema

```json
{
  "lobbyRoomName": "EK-A7F3C2B1",
  "friends": [
    { "steam64Id": "76561198000000001", "displayName": "Alice" },
    { "steam64Id": "76561198000000002", "displayName": "Bob" }
  ],
  "autoLaunchTray": false
}
```

---

## Installation

1. User installs BepInEx 6.x IL2CPP into the game directory (standard BepInEx install)
2. User runs the mod installer script, which drops:
   - `BepInEx/plugins/EKLobbyMod.dll`
   - `EKLobbyTray.exe` (alongside the game exe)
3. User launches the game — BepInEx loads the plugin automatically
4. On first launch, mod generates the home lobby room name and creates `config.json`

---

## Out of Scope

- Cross-platform support (Steam PC only)
- Voice/chat features
- Matchmaking with strangers
- Any modification to game rules, cards, or balance
- Server-side persistence (Photon server not controllable)
