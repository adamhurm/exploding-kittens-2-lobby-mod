# Party Invite Flow Design
**Date:** 2026-05-09

## Context

Two bugs and one missing feature prevent the lobby mod from delivering a working invite-and-play experience:

1. **Invite accept does nothing.** `SteamJoinPatch` finds `_OnRichPresenceJoinRequested` on the derived class `EK.ExplodingKittensSteamPlatform`, which declares the method with C#'s `new` keyword (shadow, not override). Steamworks registered its callback pointer against the base class `MGS.Platforms.SteamPlatform`, so the mod's postfix never fires. HarmonyX logs this warning explicitly but the patch is still reported as "applied."

2. **Lobby does not persist across invite accept.** `JoinRoomByInvite` saves B's original room name and restores it after B joins A's room. This means B's home lobby immediately reverts to their own room. "Update both user's lobby id" means B should adopt A's room as their own after accepting.

3. **Party not bridged to game.** The mod's lobby rooms (`EK-…`) and the game's "Play with Friends" rooms (short numeric names) are separate Photon rooms. When A creates a game, B has no mechanism to join the same room automatically.

## Intended Outcome

- B accepts A's invite → B joins A's mod-lobby Photon room → B's `Config.LobbyRoomName` is updated to A's room (both show the same room in the overlay)
- A navigates to Play → Play with Friends → Create Game → B is automatically pulled into the same game room
- After the game, both return to A's mod-lobby room (persistent party)

---

## Design

### Part 1 — Fix SteamJoinPatch

**File:** `src/EKLobbyMod/SteamJoinPatch.cs`

Change `TryApply` to:
1. Add `BindingFlags.DeclaredOnly` to the `GetMethod` call — only types that **declare** the method are collected, not types that just inherit it.
2. Collect **all** candidates (don't break on first match).
3. Among candidates, pick the base-most type: any candidate whose `DeclaringType` is a subclass of another candidate's `DeclaringType` is discarded. Fall back to the first candidate if no clear base is found.

Expected result: the patch targets `MGS.Platforms.SteamPlatform._OnRichPresenceJoinRequested`, eliminating the HarmonyX warning and ensuring the postfix fires when Steam delivers the callback.

---

### Part 2 — Persist Party Membership

**File:** `src/EKLobbyMod/LobbyManager.cs`

In both `HandleJoinedRoom` and `HandleCreatedRoom`, when `_preInviteRoomName != null` (an invite join completed), **remove** the line `Config.LobbyRoomName = _preInviteRoomName`. Keep the `ConfigStore.Save(Config)` call — this now persists A's room code (already in `Config.LobbyRoomName`) instead of restoring B's original code.

Effect: after B joins A's lobby, B's home lobby is permanently A's room. Both overlays show the same room code. When B returns from a game, they return to A's room, not their own. B can always change their room back via the overlay's edit feature.

---

### Part 3 — Party Game Launch

#### 3a. Add room property support to PhotonPropertyHelper

**File:** `src/EKLobbyMod/PhotonPropertyHelper.cs`

Add:
- `internal const string PartyGameKey = "ek_party_game"` — key used for the game-room announcement
- `SetRoomGameProperty(string gameRoomName)` — writes `ek_party_game` to `PhotonNetwork.CurrentRoom.CustomProperties`
- `ClearRoomGameProperty()` — sets `ek_party_game = null` to clean up after returning to the lobby

#### 3b. Store Harmony instance statically

**File:** `src/EKLobbyMod/Plugin.cs`

Add `internal static Harmony HarmonyInstance { get; private set; }` and assign in `Load()`. This makes the Harmony instance accessible from `PhotonClientFinder` without threading the instance through every call.

#### 3c. New PartyGamePatch

**File:** `src/EKLobbyMod/PartyGamePatch.cs`

Applied at runtime from `PhotonClientFinder.Patch_InitNetworking.Postfix` once the concrete `IMultiplayerController` type is known (same pattern as `SteamJoinPatch.TryApply`):

```
TryApply(Harmony harmony, IMultiplayerController controller):
  Find CreateRoom(string, NetworkRoomOptions, ...) on controller.GetType()
  If not found: log warning, return
  Apply PREFIX: PartyGamePatch.Prefix(ref string name)
```

**Prefix logic:**
```
if LobbyManager.Instance == null: return
if !LobbyManager.Instance.InHomeLobby: return
if name == LobbyManager.Instance.Config.LobbyRoomName: return  // mod's own home lobby create

deterministic = LobbyManager.Instance.Config.LobbyRoomName + "-g"
name = deterministic                                      // mutate the parameter (ref string)
LobbyManager.Instance.OnPartyGameStarting(deterministic)  // sets flag + writes room property
```

`LobbyManager.OnPartyGameStarting(string gameRoomName)` (new internal method):
```
_partyGameInitiatedByMe = true
PhotonPropertyHelper.SetRoomGameProperty(gameRoomName)
Log: "[Party] Routing game to {gameRoomName} - notifying party via room property"
```

Keeping this logic in `LobbyManager` means `PartyGamePatch` only needs to read `InHomeLobby`/`Config.LobbyRoomName` (already public) and call one method.

Using a deterministic name (`EK-03771D16-g`) means B can derive the target room independently — no round-trip needed to discover it. The room property is set **before** the CreateRoom operation is sent to Photon, so B's `OnRoomPropertiesUpdated` fires while B is still in the lobby room.

> **IL2CPP note:** Mutating a method parameter via `ref string name` in a HarmonyX prefix is supported in BepInEx 6 / IL2CPP. Verify during implementation by checking BepInEx.log; if the ref mutation doesn't take, fall back to cancelling the original call (return `false` from prefix) and calling `_controller.CreateRoom(deterministic, options, null)` directly via `PhotonClientFinder.Controller`.

#### 3d. Handle room property updates in LobbyManager

**File:** `src/EKLobbyMod/LobbyManager.cs`

New Harmony patch (inner class):
```csharp
[HarmonyPatch(typeof(PhotonMatchMakingHandler), "OnRoomPropertiesUpdated")]
class Patch_OnRoomPropertiesUpdated {
    static void Postfix(ExitGames.Client.Photon.Hashtable propertiesThatChanged) =>
        Instance?.HandleRoomPropertiesUpdated(propertiesThatChanged);
}
```

New fields on `LobbyManager`:
- `private bool _partyGameInitiatedByMe = false` — set by `OnPartyGameStarting` before the Photon property is written; prevents the party-game initiator from acting on their own room property update.

New handler:
```
HandleRoomPropertiesUpdated(Hashtable props):
  if _partyGameInitiatedByMe:
    _partyGameInitiatedByMe = false   // clear; don't self-join
    return
  if !_inHomeLobby: return
  if props doesn't contain PartyGameKey: return
  gameRoom = props[PartyGameKey]?.ToString()
  if !IsValidRoomName(gameRoom): return
  Log: "[Party] Leader started game in {gameRoom} - auto-joining"
  _bridge.JoinRoom(gameRoom)
```

`OnPartyGameStarting` sets `_partyGameInitiatedByMe = true` before writing the property, ensuring the initiator skips the auto-join handler (they are already joining via the game's own CreateRoom flow) while all other party members correctly auto-join.

In `HandleJoinedRoom` and `HandleCreatedRoom`, when entering the **home lobby as master client**, also call `PhotonPropertyHelper.ClearRoomGameProperty()` to prevent stale `ek_party_game` values from persisting across sessions.

---

## File Summary

| File | Change |
|------|--------|
| `src/EKLobbyMod/SteamJoinPatch.cs` | Fix base-class search with `DeclaredOnly` + base-preference logic |
| `src/EKLobbyMod/Plugin.cs` | Store `HarmonyInstance` statically |
| `src/EKLobbyMod/PhotonPropertyHelper.cs` | Add `PartyGameKey`, `SetRoomGameProperty`, `ClearRoomGameProperty` |
| `src/EKLobbyMod/PhotonClientFinder.cs` | Call `PartyGamePatch.TryApply` after controller capture |
| `src/EKLobbyMod/LobbyManager.cs` | Remove invite-restore lines; add `HandleRoomPropertiesUpdated`; add `Patch_OnRoomPropertiesUpdated`; clear party prop on home lobby enter |
| `src/EKLobbyMod/PartyGamePatch.cs` | **New** — runtime prefix on `IMultiplayerController.CreateRoom` |

---

## Verification

1. Build and deploy with `.\install.ps1`
2. Check `BepInEx\LogOutput.log`:
   - `[SteamJoinPatch] Selected: MGS.Platforms.SteamPlatform` (no longer `EK.ExplodingKittensSteamPlatform`)
   - `[PartyGamePatch] Patched <ConcreteControllerType>.CreateRoom`
3. Player A opens game, opens overlay → shares invite
4. Player B (game running) accepts invite via Steam overlay:
   - Log shows `[SteamJoinPatch] Rich presence join (N chars)` followed by join success
   - B's overlay shows A's room code; A's overlay shows B in party list
5. Both restart game:
   - Both auto-rejoin A's room on startup (B's `Config.LobbyRoomName` = A's code)
6. A navigates to Play → Play with Friends → Create Game:
   - Log shows `[PartyGamePatch] Routing party game to EK-XXXXXXXX-g`
   - B's log shows `[Party] Leader started game in EK-XXXXXXXX-g - auto-joining`
   - Both appear in the same game lobby in-game
7. After game ends, both return to A's mod lobby automatically
