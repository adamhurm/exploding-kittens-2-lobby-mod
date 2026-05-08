# Party UX — Auto-Queue Countdown & Party Indicator Design

**Date:** 2026-05-08
**Status:** Approved (autonomous session — decisions documented inline)

---

## Summary

Two overlay UX improvements for EKLobbyMod targeting the post-game and minimized-state
experiences:

1. **Auto-queue countdown** — after a game ends, the overlay counts down 5→0 and
   automatically rejoins the home lobby. The player can cancel by clicking the game's
   native red Leave button.

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

**Decision: flag + `OnLeftRoom` + `OnJoinedRoom` sequencing.**

The game's native Leave button triggers `PhotonMatchMakingHandler.OnLeftRoom` just like a
natural game-end. There is no clean pre-leave hook available without inspecting the
`MGS.Network` assembly for a "LeaveRoom" call — that inspection is out of scope here.

Instead, the design distinguishes between two `OnLeftRoom` scenarios:

| Scenario | How it presents |
|---|---|
| Game ended naturally | `OnLeftRoom` fires while `PendingRejoin` is `false` (first leave after a joined room) |
| Explicit leave during countdown | `OnLeftRoom` fires while `AutoQueueActive` is `true` (countdown was running) |

When `OnLeftRoom` fires while `AutoQueueActive` is already `true`, that means the player
joined a room, a countdown started, and now `OnLeftRoom` fired again — this is the explicit
leave. The overlay cancels the countdown.

**Why this works cleanly:** `OnJoinedRoom` clears `AutoQueueActive`. So the sequence is:

```
Game session ends → OnLeftRoom (first) → countdown starts (AutoQueueActive = true)
                 → OnJoinedRoom → AutoQueueActive = false (back to home lobby)

Player leaves mid-countdown → OnLeftRoom (second, AutoQueueActive still true) → cancel
```

**Known gap:** If the player completes a game, declines to rejoin (countdown expires without
auto-fire), and then leaves a second game naturally, the second `OnLeftRoom` would not be
ambiguous because `AutoQueueActive` was already reset to `false` when the countdown expired
or when `OnJoinedRoom` fired. This gap does not apply in practice because the countdown
either fires `JoinOrCreateHomeLobby` (→ `OnJoinedRoom` → reset) or is explicitly cancelled
(reset). The only edge case is if the player somehow leaves two rooms in rapid succession
with no join in between — acceptable for a private friends-only lobby scenario.

**Simpler alternative considered and rejected:** A dedicated Harmony pre-patch on a
`LeaveRoom()` call. This would require finding the correct `MGS.Network` method name, which
needs additional IL2CPP inspection. Deferring this to a follow-up if the flag approach
proves unreliable.

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

New fields:

```csharp
private Text _countdownLabel = null!;
private Coroutine _countdownCoroutine;
```

`_countdownLabel` is built in `BuildExpandedPanel()` near `_rejoinPromptLabel`, same
vertical position (they swap visibility — prompt shown during countdown, hidden after):

```
Position: new Vector2(6 * s, 26 * s), size: new Vector2(200 * s, 18 * s)
Font size: (int)(12 * s)
Color: EkOffWhite
Initially hidden
```

`ShowRejoinPrompt()` is updated:

```csharp
public void ShowRejoinPrompt()
{
    SetExpanded(true);
    _rejoinPromptLabel.gameObject.SetActive(true);
    _rejoinBtnImage.color = new Color(0.12f, 0.48f, 0.12f, 1f);

    if (_manager.AutoQueueActive)
    {
        if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = StartCoroutine(RunCountdown());
    }
}
```

Countdown coroutine:

```csharp
private System.Collections.IEnumerator RunCountdown()
{
    for (int i = 5; i >= 1; i--)
    {
        _countdownLabel.gameObject.SetActive(true);
        _countdownLabel.text = $"Rejoining in {i}…";
        yield return new WaitForSeconds(1f);

        if (!_manager.AutoQueueActive)   // cancelled externally
        {
            _countdownLabel.gameObject.SetActive(false);
            yield break;
        }
    }
    _countdownLabel.gameObject.SetActive(false);
    _manager.JoinOrCreateHomeLobby();
}
```

`HideRejoinPrompt()` (called on `RejoinConfirmed`) also cleans up:

```csharp
public void HideRejoinPrompt()
{
    if (_countdownCoroutine != null)
    {
        StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = null;
    }
    _countdownLabel.gameObject.SetActive(false);
    _rejoinPromptLabel.gameObject.SetActive(false);
    _rejoinBtnImage.color = EkDark;
}
```

Wire `AutoQueueCancelled` in `Inject()`:

```csharp
manager.AutoQueueCancelled += (System.Action)panel.OnAutoQueueCancelled;
```

`OnAutoQueueCancelled` in `OverlayPanel`:

```csharp
public void OnAutoQueueCancelled()
{
    if (_countdownCoroutine != null)
    {
        StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = null;
    }
    _countdownLabel.gameObject.SetActive(false);
    _rejoinPromptLabel.gameObject.SetActive(false);
    _rejoinBtnImage.color = EkDark;
    // Stay expanded (user explicitly left — show idle state)
}
```

### Immediate Rejoin (REJOIN button clicked during countdown)

`DoRejoin()` already calls `_manager.JoinOrCreateHomeLobby()`. No change needed. The
coroutine is cleaned up by `HideRejoinPrompt()` which fires on `RejoinConfirmed`.

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
