# Party UX — Auto-Queue Countdown & Party Indicator Design

**Date:** 2026-05-08
**Status:** Approved (autonomous session — decisions documented inline)

---

## Summary

Two overlay UX improvements for EKLobbyMod targeting the post-game and minimized-state
experiences:

1. **Auto-queue countdown** — after a game ends, the overlay counts down 15→0 and
   automatically rejoins the home lobby. The player can cancel by clicking the LEAVE
   button shown directly on the countdown overlay.

2. **Party indicator** — the minimized tab shows "X in party" with a colored presence dot
   so the player can see room occupancy without opening the overlay.

Both features touch only `LobbyManager.cs` and `OverlayPanel.cs`. No new files are required
beyond the existing two.

---

## Background

### Relevant existing code

| Symbol | Location | Notes |
|---|---|---|
| `LobbyManager.HandleLeftRoom()` | `LobbyManager.cs:124` | Sets `PendingRejoin = true`, clears `_roomSteamIds`, fires `RejoinAvailable` |
| `LobbyManager.RoomSteamIds` | `LobbyManager.cs:31` | `IReadOnlyCollection<string>` of Steam64 IDs in current room |
| `LobbyManager.PlayerListChanged` | `LobbyManager.cs:29` | Fires on enter/leave; already wired to `OverlayPanel.OnPlayerListChanged` |
| `OverlayPanel.ShowRejoinPrompt()` | `OverlayPanel.cs:237` | Expands panel, turns Rejoin button green |
| `OverlayPanel._minTab` | `OverlayPanel.cs:21` | 220×40 bottom-left tab, currently shows only lobby code |
| `OverlayPanel._codeText` | `OverlayPanel.cs:18` | The `Text` component inside `_minTab` |
| `OverlayPanel.OnPlayerListChanged()` | `OverlayPanel.cs:250` | Already calls `RefreshFriendList()` when expanded |

### Constraints

- IL2CPP environment: coroutines must use `IEnumerator` + `StartCoroutine` (standard Unity
  pattern; works with BepInEx IL2CPP because `OverlayPanel` extends `MonoBehaviour` and is
  registered via `ClassInjector.RegisterTypeInIl2Cpp<OverlayPanel>()`).
- No new Harmony patches are introduced in this design (see "Leave detection" section below).
- uGUI only — no TextMeshPro, no custom sprites.

---

## Feature 1 — Auto-Queue Countdown

### Behavior

1. `OnLeftRoom` fires (game session has ended and the local player has left the Photon room).
2. If `LobbyManager.AutoQueueEnabled` is `true` (default), `LobbyManager` fires
   `RejoinAvailable` as it already does, but now also sets `AutoQueueActive = true`.
3. `OverlayPanel.ShowRejoinPrompt()` detects `AutoQueueActive` and starts a 5-second
   countdown coroutine instead of passively waiting.
4. The countdown is displayed as a label in the expanded panel: **"Rejoining in 3…"**
   (counts 5→4→3→2→1→0, then calls `JoinOrCreateHomeLobby()`).
5. The existing green REJOIN button remains clickable — clicking it rejoins immediately
   (skipping the rest of the countdown).
6. If the player explicitly leaves via the game's native Leave button, the countdown is
   cancelled and the overlay returns to idle state.

### Leave detection design

The LEAVE button is placed **inside the countdown overlay** so it remains reachable while
the semi-opaque overlay blocks the rest of the panel. Clicking it calls
`LobbyManager.LeaveToHomeLobby()`, which clears `AutoQueueActive`, fires `AutoQueueCancelled`
(stopping the coroutine), and either leaves the game room (mid-game) or rejoins the home
lobby directly (post-game).

The hint text "Click Leave to cancel" displayed below the countdown digit refers to this
on-overlay button.

**Why not expose the bottom-row LEAVE button:** The countdown overlay covers the full
300×356px interior below the header (intentional modal behaviour). Shrinking it to expose
the bottom row would leave an awkward partial-frame effect. Owning the cancel action inside
the modal is cleaner UX.

**Previous `OnLeftRoom` sequencing approach (superseded):** The original design detected an
explicit leave via a second `OnLeftRoom` event while `AutoQueueActive` was true. This is no
longer needed — `LeaveToHomeLobby()` cancels the countdown synchronously before any room
leave occurs.

### LobbyManager changes

Add two properties and update `HandleLeftRoom` and `HandleJoinedRoom`:

```csharp
public bool AutoQueueActive { get; private set; }  // countdown is running
public bool AutoQueueEnabled { get; set; } = true;  // can be disabled in future settings
```

`HandleLeftRoom` becomes:

```csharp
internal void HandleLeftRoom()
{
    if (AutoQueueActive)
    {
        // Second OnLeftRoom while countdown running = explicit leave
        AutoQueueActive = false;
        PendingRejoin = false;
        _roomSteamIds.Clear();
        PlayerListChanged?.Invoke();
        AutoQueueCancelled?.Invoke();   // new event
        Plugin.Log.LogInfo("Left room during countdown — auto-queue cancelled");
        return;
    }

    PendingRejoin = true;
    if (AutoQueueEnabled)
        AutoQueueActive = true;
    _roomSteamIds.Clear();
    PlayerListChanged?.Invoke();
    RejoinAvailable?.Invoke();
    Plugin.Log.LogInfo("Left room — rejoin prompt raised");
}
```

`HandleJoinedRoom` clears `AutoQueueActive`:

```csharp
internal void HandleJoinedRoom()
{
    _joinOrCreatePending = false;
    PendingRejoin = false;
    AutoQueueActive = false;
    RejoinConfirmed?.Invoke();
    // ... rest unchanged
}
```

New event on `LobbyManager`:

```csharp
public event System.Action AutoQueueCancelled;
```

### OverlayPanel changes

The countdown is rendered as a full-panel semi-opaque overlay (`_countdownOverlay`, 300×356px)
containing three child elements:

| Element | Content | Position (from overlay center) |
|---|---|---|
| `_countdownDigit` (`Text`) | Large digit 15→1, EkRed bold 64pt | `(0, +40s)` |
| `_countdownHint` (`Text`) | "Click Leave to cancel" | `(0, -20s)` |
| `Btn_Leave_Countdown` (`Button`) | "LEAVE", EkRedDark, 130×32px | `(0, -70s)` |

The LEAVE button inside the overlay calls `DoLeave()` (same as the bottom-row LEAVE).

`ShowRejoinPrompt()` activates the overlay and starts the coroutine:

```csharp
public void ShowRejoinPrompt()
{
    SetExpanded(true);
    _rejoinPromptLabel.gameObject.SetActive(true);
    RefreshBottomRow();
    if (_manager.AutoQueueActive)
    {
        if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
        _countdownOverlay.SetActive(true);
        _countdownDigit.text = "15";
        _countdownCoroutine = StartCoroutine("RunCountdown");
    }
}
```

Coroutine counts 15→1, then fires `JoinOrCreateHomeLobby()`. It exits early if
`AutoQueueActive` is cleared externally.

`HideRejoinPrompt()` (on `RejoinConfirmed`) and `OnAutoQueueCancelled()` (on
`AutoQueueCancelled`) both stop the coroutine and deactivate `_countdownOverlay`.

### Bottom-row button states

| State | Left | Right |
|---|---|---|
| Home lobby (`InHomeLobby`) | INVITE ALL | RECREATE |
| In-game (`InGame`) | INVITE ALL | LEAVE |
| Post-game (`PendingRejoin`) | PLAY AGAIN | LEAVE |
| Not in any room | INVITE ALL | REJOIN |

RECREATE leaves the current Photon room and immediately recreates it with the same name.
REJOIN is only shown in the rare "not in any room" state (e.g. after a disconnect).

---

## Feature 2 — Party Indicator in Minimized State

### Behavior

The minimized tab (`_minTab`, 220×40) shows:

```
[●] 3 in party    EK-A3F9C12B
```

- Left side: colored dot (`●`) + "X in party" count
- Right side: lobby code (existing)
- Dot is **green** (`#1E7A1E` = `EkGreen`) when `RoomSteamIds.Count >= 2` (others are present)
- Dot is **gray** (`Color(0.45f, 0.45f, 0.45f, 1f)`) when `Count == 1` (solo) or `Count == 0`

**Count includes the local player.** `RoomSteamIds` tracks all players in the room; when
the local player is alone, count = 1 (gray dot). When at least one friend has joined, count
≥ 2 (green dot).

**Decision on "0 in party":** When `PendingRejoin` is true (between games), `_roomSteamIds`
is empty. Show "—" instead of "0 in party" to avoid confusion. Implementation: if count is
0, label shows "—" and dot is gray.

### Min-tab layout rework

Current tab (220×40): single `_codeText` centered.

New layout — three child elements:

| Element | Content | Position | Size |
|---|---|---|---|
| `_partyDot` (`Image`) | Colored circle (solid square, rounded by small size) | `(8s, 13s)` | `(14s, 14s)` |
| `_partyText` (`Text`) | "X in party" or "—" | `(26s, 12s)` | `(90s, 16s)` |
| `_codeText` (`Text`) | Lobby code | `(124s, 12s)` | `(88s, 16s)` |

The existing `_codeText` is repositioned (currently centered across the full 220px width).
`_partyDot` is a plain `Image` component — no sprite needed, uGUI renders it as a solid
square at small size. For a dot appearance, size it to `(14s, 14s)` which at 1× screen
scale is 14×14 pixels — visually dot-like.

### LobbyManager — no changes needed

`RoomSteamIds` and `PlayerListChanged` already exist and already fire on every player
enter/leave.

### OverlayPanel changes

New fields:

```csharp
private Image _partyDot = null!;
private Text _partyText = null!;
```

New colors:

```csharp
private static readonly Color EkGreen = new Color(0.12f, 0.48f, 0.12f, 1f);
private static readonly Color EkGray  = new Color(0.45f, 0.45f, 0.45f, 1f);
```

`BuildMinTab()` is updated to add the two new elements and reposition `_codeText`.

New private method called from `BuildMinTab()` and wired to `PlayerListChanged`:

```csharp
private void RefreshPartyIndicator()
{
    if (_partyDot == null || _partyText == null) return;
    int count = _manager.RoomSteamIds.Count;

    if (count == 0)
    {
        _partyText.text = "—";
        _partyDot.color = EkGray;
    }
    else
    {
        _partyText.text = $"{count} in party";
        _partyDot.color = count >= 2 ? EkGreen : EkGray;
    }
}
```

Wire in `Inject()` (already wired via `PlayerListChanged → OnPlayerListChanged`, which only
calls `RefreshFriendList` when expanded). Update `OnPlayerListChanged`:

```csharp
public void OnPlayerListChanged()
{
    RefreshPartyIndicator();          // always refresh min-tab
    if (_expanded) RefreshFriendList();
}
```

`RefreshPartyIndicator()` is also called at end of `Build()` for the initial state.

---

## Event Wiring Summary (Inject method additions)

```csharp
manager.AutoQueueCancelled += (System.Action)panel.OnAutoQueueCancelled;
```

All existing wires remain:

```csharp
manager.RejoinAvailable  += (System.Action)panel.ShowRejoinPrompt;
manager.RejoinConfirmed  += (System.Action)panel.HideRejoinPrompt;
manager.PlayerListChanged += (System.Action)panel.OnPlayerListChanged;
```

---

## What Is Not Changed

- `LobbyManager.HandleCreatedRoom()` — no change
- `LobbyManager.HandleJoinRoomFailed()` — no change
- `OverlayPanel.BuildExpandedPanel()` — minimal additions only (countdown label)
- `Plugin.cs` — no change
- No new files, no new classes

---

## Mock HTML placeholder

`docs/superpowers/specs/2026-05-08-dev-tooling-design.md` already notes a countdown
placeholder state in `mock/overlay.html`. The party indicator needs a new panel state (or
an update to panel state #1 in the mock). This is a nice-to-have update and is not a
blocking dependency for the C# implementation.

---

## Out of Scope

- Configurable countdown duration (hardcoded to 5 seconds)
- Audio/haptic feedback for countdown ticks
- Disabling auto-queue per-session via the overlay UI (could be added later via a checkbox)
- Hooking the game's Leave button via Harmony for clean pre-leave detection (deferred)
- Party indicator in the expanded panel (redundant — the friend list already shows who is in room)
