# Version Maintenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Broadcast each player's mod version via Photon custom player properties when they join a room, detect version drift across peers, and show an amber warning band in the overlay with an "Update" button that opens the GitHub releases page.

**Architecture:** `PhotonPropertyHelper` is a new static class that writes the local player's mod version into `PhotonNetwork.LocalPlayer.CustomProperties` and reads it back from any `NetworkPlayer`. `LobbyManager` owns a `_peerVersions` dictionary, rebuilds it on every room event, fires a `VersionMapChanged` event, and exposes `HasVersionDrift`. `OverlayPanel` subscribes to `VersionMapChanged` and toggles an amber warning band and a dot on the minimised tab. Hot-reload is not implemented — the overlay guides users to restart after updating manually.

**Tech Stack:** C# / .NET 6, BepInEx 6 IL2CPP, HarmonyX, `PhotonUnityNetworking.dll` (already in interop dir), `MGS.Network.Photon.dll`, Unity uGUI (`Text`, `Button`, `Image`), `Application.OpenURL`.

---

## File Map

```
src/EKLobbyMod/
├── EKLobbyMod.csproj           MODIFY — add PhotonUnityNetworking reference
├── Plugin.cs                   MODIFY — add ReleasesUrl constant
├── PhotonPropertyHelper.cs     CREATE — SetLocalVersion / ReadPeerVersion
├── LobbyManager.cs             MODIFY — _peerVersions, VersionMapChanged,
│                                         HasVersionDrift, RebuildVersionMap,
│                                         OnPlayerPropertiesUpdate patch
└── OverlayPanel.cs             MODIFY — drift band, min-tab dot,
                                          OnVersionMapChanged, _restartTimer
```

No new test projects — the new code touches only in-game Photon and Unity APIs that require a running game to exercise. Manual smoke-test steps are included in each task.

---

## Task 1: Add PhotonUnityNetworking reference and ReleasesUrl constant

**Files:**
- Modify: `src/EKLobbyMod/EKLobbyMod.csproj`
- Modify: `src/EKLobbyMod/Plugin.cs`

`PhotonUnityNetworking.dll` is the assembly that exposes the static `PhotonNetwork` class (including `PhotonNetwork.LocalPlayer`). It is already present in the BepInEx interop directory alongside the other assemblies that `EKLobbyMod.csproj` already references, but it is not yet listed as a reference.

- [ ] **Step 1: Add the PhotonUnityNetworking reference to EKLobbyMod.csproj**

  Open `src/EKLobbyMod/EKLobbyMod.csproj`. After the existing `MGS.Network.Photon` `<Reference>` block (around line 49), add:

  ```xml
    <Reference Include="PhotonUnityNetworking">
      <HintPath>$(BepInExDir)\interop\PhotonUnityNetworking.dll</HintPath>
      <Private>False</Private>
    </Reference>
  ```

  The full `<ItemGroup>` containing game interop stubs will then look like:

  ```xml
  <ItemGroup>
    <!-- BepInEx core DLLs -->
    <Reference Include="BepInEx.Core">
      <HintPath>$(BepInExDir)\core\BepInEx.Core.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.IL2CPP">
      <HintPath>$(BepInExDir)\core\BepInEx.Unity.IL2CPP.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.Common">
      <HintPath>$(BepInExDir)\core\BepInEx.Unity.Common.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>$(BepInExDir)\core\Il2CppInterop.Runtime.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(BepInExDir)\core\0Harmony.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>$(BepInExDir)\interop\Il2Cppmscorlib.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="ExplodingKittens.Network">
      <HintPath>$(BepInExDir)\interop\ExplodingKittens.Network.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="MGS.Network">
      <HintPath>$(BepInExDir)\interop\MGS.Network.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="MGS.Network.Photon">
      <HintPath>$(BepInExDir)\interop\MGS.Network.Photon.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="PhotonUnityNetworking">
      <HintPath>$(BepInExDir)\interop\PhotonUnityNetworking.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="MGS.Platform">
      <HintPath>$(BepInExDir)\interop\MGS.Platform.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="com.rlabrecque.steamworks.net">
      <HintPath>$(BepInExDir)\interop\com.rlabrecque.steamworks.net.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(BepInExDir)\interop\UnityEngine.CoreModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.UI">
      <HintPath>$(BepInExDir)\interop\UnityEngine.UI.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.UIModule">
      <HintPath>$(BepInExDir)\interop\UnityEngine.UIModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.TextRenderingModule">
      <HintPath>$(BepInExDir)\interop\UnityEngine.TextRenderingModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(BepInExDir)\interop\UnityEngine.IMGUIModule.dll</HintPath>
      <Private>False</Private>
    </Reference>
    <Reference Include="UnityEngine.TextMeshPro">
      <HintPath>$(BepInExDir)\interop\Unity.TextMeshPro.dll</HintPath>
      <Private>False</Private>
    </Reference>
  </ItemGroup>
  ```

- [ ] **Step 2: Add ReleasesUrl to Plugin.cs**

  Open `src/EKLobbyMod/Plugin.cs`. After the existing `PluginVersion` constant, add one new constant:

  ```csharp
  public const string ReleasesUrl = "https://github.com/adamhurm/exploding-kittens-mod/releases";
  ```

  The top of the class will then read:

  ```csharp
  public const string PluginGuid    = "com.eklobbymod.plugin";
  public const string PluginName    = "EKLobbyMod";
  public const string PluginVersion = "1.0.0";
  public const string ReleasesUrl   = "https://github.com/adamhurm/exploding-kittens-mod/releases";
  ```

- [ ] **Step 3: Build to confirm the reference resolves**

  ```
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: build succeeds with 0 errors. If the reference can't be resolved (interop DLL not yet generated), verify that `BepInEx/interop/PhotonUnityNetworking.dll` exists by checking `D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\interop\`. If it is missing, launch the game once with BepInEx installed to trigger interop generation, then retry.

- [ ] **Step 4: Commit**

  ```
  git add src/EKLobbyMod/EKLobbyMod.csproj src/EKLobbyMod/Plugin.cs
  git commit -m "chore: add PhotonUnityNetworking reference and ReleasesUrl constant"
  ```

---

## Task 2: PhotonPropertyHelper — write and read custom player properties

**Files:**
- Create: `src/EKLobbyMod/PhotonPropertyHelper.cs`

`PhotonPropertyHelper` is a static utility class — same pattern as `SteamInviter.cs`. It owns all Photon custom-property I/O so the rest of the codebase never touches `PhotonNetwork` or IL2CPP `Hashtable` directly.

`PhotonNetwork.LocalPlayer` is of type `Photon.Realtime.Player`. Its `SetCustomProperties` method accepts an `ExitGames.Client.Photon.Hashtable`. In the IL2CPP interop, that type is `Il2CppExitGames.Client.Photon.Hashtable`. `NetworkPlayer.CustomProperties` (from `MGS.Network`) wraps the same underlying object.

> **IL2CPP note:** If the interop exposes `CustomProperties` as a plain `Il2CppSystem.Collections.Hashtable`, use the `Il2CppSystem.Collections.Hashtable` type in the code below instead. The accessor and indexer syntax is identical — `ht[key]` returns an `Il2CppSystem.Object`, cast with `?.ToString()`. Check which namespace resolves if you get a type-not-found compiler error.

- [ ] **Step 1: Create PhotonPropertyHelper.cs**

  ```csharp
  using System;
  using ExitGames.Client.Photon;     // Hashtable — from PhotonRealtime interop
  using MGS.Network;                  // NetworkPlayer
  using Photon.Realtime;              // Player (LocalPlayer type)
  using Photon.Realtime.Extensions;   // may not be needed — remove if it causes CS0234
  
  namespace EKLobbyMod;
  
  /// <summary>
  /// Wraps Photon custom player property I/O.
  /// All other classes call this instead of touching PhotonNetwork or Hashtable directly.
  /// </summary>
  public static class PhotonPropertyHelper
  {
      // Matches LobbyManager.VersionPropertyKey — defined here too so this file
      // compiles independently even if read in isolation.
      private const string VersionKey = "ekmod_ver";
  
      /// <summary>
      /// Writes the local player's mod version into their Photon custom properties.
      /// Safe to call when not in a room — PhotonNetwork.LocalPlayer is always non-null.
      /// </summary>
      public static void SetLocalVersion(string version)
      {
          try
          {
              var props = new Hashtable { [VersionKey] = version };
              PhotonNetwork.LocalPlayer.SetCustomProperties(props);
              Plugin.Log.LogInfo($"[PhotonPropertyHelper] Set local ekmod_ver = {version}");
          }
          catch (Exception ex)
          {
              Plugin.Log.LogWarning($"[PhotonPropertyHelper] SetLocalVersion failed: {ex.Message}");
          }
      }
  
      /// <summary>
      /// Reads another player's mod version from their Photon custom properties.
      /// Returns null if the property is absent (player has no mod or pre-broadcast version).
      /// </summary>
      public static string ReadPeerVersion(NetworkPlayer player)
      {
          if (player == null) return null;
          try
          {
              var props = player.CustomProperties;
              if (props == null) return null;
              // CustomProperties is an ExitGames.Client.Photon.Hashtable.
              // The indexer returns object; cast to string via ToString().
              var val = props[VersionKey];
              return val?.ToString();
          }
          catch (Exception ex)
          {
              Plugin.Log.LogWarning($"[PhotonPropertyHelper] ReadPeerVersion failed: {ex.Message}");
              return null;
          }
      }
  }
  ```

  > **Compilation note — using directives:** If `ExitGames.Client.Photon.Hashtable` does not resolve, try `Il2CppExitGames.Client.Photon.Hashtable` (the IL2CPP-prefixed namespace). If `PhotonNetwork` is not found, add `using Photon.Realtime;` or check which namespace the interop exposes it under — open `BepInEx/interop/PhotonUnityNetworking.dll` in ILSpy/dnSpy and search for `PhotonNetwork` to find the correct namespace. The `Photon.Realtime.Player.SetCustomProperties` method takes a single `Hashtable` argument and is defined in `PhotonRealtime.dll`.

- [ ] **Step 2: Build**

  ```
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: 0 errors. Fix any unresolved namespace by checking the interop DLL (see note in Step 1). Do not proceed to Task 3 with a failing build.

- [ ] **Step 3: Commit**

  ```
  git add src/EKLobbyMod/PhotonPropertyHelper.cs
  git commit -m "feat: PhotonPropertyHelper — write/read ekmod_ver custom player property"
  ```

---

## Task 3: LobbyManager — version map, VersionMapChanged event, and OnPlayerPropertiesUpdate patch

**Files:**
- Modify: `src/EKLobbyMod/LobbyManager.cs`

This task adds the version tracking state to `LobbyManager` and wires all the triggers that rebuild it. There are four triggers: joined room, player entered, player left, and player properties updated.

The existing pattern (see bottom of `LobbyManager.cs`) is: Harmony postfix patches on `PhotonMatchMakingHandler` call instance methods on `LobbyManager.Instance`. We add a fifth patch for `OnPlayerPropertiesUpdate`.

`PhotonMatchMakingHandler.OnPlayerPropertiesUpdate` has the signature:

```csharp
void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
```

In the IL2CPP interop the parameters are wrapped types; the patch `Postfix` receives them as the IL2CPP interop versions. We only care about the player's `UserId` to look them up — no need to inspect `changedProps`.

- [ ] **Step 1: Add the version map fields and public surface to LobbyManager**

  Open `src/EKLobbyMod/LobbyManager.cs`. After the existing `public event System.Action PlayerListChanged;` line (around line 29), add:

  ```csharp
  public const string VersionPropertyKey = "ekmod_ver";

  // UserId → mod version string. Absent entry = no mod / pre-broadcast client.
  private readonly System.Collections.Generic.Dictionary<string, string> _peerVersions
      = new System.Collections.Generic.Dictionary<string, string>();

  public System.Collections.Generic.IReadOnlyDictionary<string, string> PeerVersions
      => _peerVersions;

  // True when at least one peer's version differs from Plugin.PluginVersion.
  // Peers with no ekmod_ver property are ignored (treated as non-mod clients).
  public bool HasVersionDrift =>
      System.Linq.Enumerable.Any(
          _peerVersions.Values,
          v => v != Plugin.PluginVersion);

  public event System.Action VersionMapChanged;
  ```

  > **`using` note:** `LobbyManager.cs` already has `using System.Collections.Generic;`. Confirm that is present (it is, on line 1). The `System.Linq` call above uses the fully-qualified form to avoid a new `using` directive — but you may add `using System.Linq;` at the top of the file instead if you prefer.

- [ ] **Step 2: Add RebuildVersionMap private method**

  Still in `LobbyManager.cs`, add this method after the `RefreshRoomPlayers()` method (around line 159):

  ```csharp
  private void RebuildVersionMap()
  {
      _peerVersions.Clear();
      var players = _controller.GetRoomPlayers();
      if (players == null)
      {
          VersionMapChanged?.Invoke();
          return;
      }
      foreach (var p in players)
      {
          if (p == null || string.IsNullOrEmpty(p.UserId)) continue;
          var ver = PhotonPropertyHelper.ReadPeerVersion(p);
          if (ver != null)
              _peerVersions[p.UserId] = ver;
      }
      VersionMapChanged?.Invoke();
      Plugin.Log.LogInfo(
          $"[LobbyManager] Version map rebuilt: {_peerVersions.Count} modded peer(s), drift={HasVersionDrift}");
  }
  ```

- [ ] **Step 3: Call SetLocalVersion + RebuildVersionMap in HandleJoinedRoom**

  Find the existing `HandleJoinedRoom` method (around line 109). After `RefreshRoomPlayers();`, add two calls:

  ```csharp
  internal void HandleJoinedRoom()
  {
      _joinOrCreatePending = false;
      PendingRejoin = false;
      RejoinConfirmed?.Invoke();
      RefreshRoomPlayers();
      PhotonPropertyHelper.SetLocalVersion(Plugin.PluginVersion);   // ← add
      RebuildVersionMap();                                           // ← add
      var roomName = _controller.GetRoomName();
      if (!string.IsNullOrEmpty(roomName) && roomName != _lastLoggedRoom)
      {
          Plugin.Log.LogInfo($"Joined room: {roomName}");
          _lastLoggedRoom = roomName;
      }
  }
  ```

- [ ] **Step 4: Update HandlePlayerEntered to update the version map entry**

  Find `HandlePlayerEntered` (around line 132). After `PlayerListChanged?.Invoke();`, add:

  ```csharp
  private void HandlePlayerEntered(NetworkPlayer player)
  {
      if (player == null) return;
      var uid = player.UserId;
      if (!string.IsNullOrEmpty(uid))
      {
          _roomSteamIds.Add(uid);
          PlayerListChanged?.Invoke();
          // New player may already have their version property set
          var ver = PhotonPropertyHelper.ReadPeerVersion(player);
          if (ver != null)
          {
              _peerVersions[uid] = ver;
              VersionMapChanged?.Invoke();
          }
      }
  }
  ```

- [ ] **Step 5: Update HandlePlayerLeft to remove from version map**

  Find `HandlePlayerLeft` (around line 143). After `PlayerListChanged?.Invoke();`, add:

  ```csharp
  private void HandlePlayerLeft(NetworkPlayer player)
  {
      if (player == null) return;
      var uid = player.UserId ?? "";
      _roomSteamIds.Remove(uid);
      PlayerListChanged?.Invoke();
      if (_peerVersions.Remove(uid))
          VersionMapChanged?.Invoke();
  }
  ```

- [ ] **Step 6: Add the OnPlayerPropertiesUpdate Harmony patch**

  At the bottom of `LobbyManager.cs`, in the Harmony patch region (after the existing `Patch_OnJoinRoomFailed` class), add a new patch class and a new handler method:

  ```csharp
  internal void HandlePlayerPropertiesUpdate(NetworkPlayer player)
  {
      if (player == null || string.IsNullOrEmpty(player.UserId)) return;
      var ver = PhotonPropertyHelper.ReadPeerVersion(player);
      if (ver != null)
      {
          _peerVersions[player.UserId] = ver;
          VersionMapChanged?.Invoke();
          Plugin.Log.LogInfo(
              $"[LobbyManager] Player {player.UserId} updated ekmod_ver to {ver}");
      }
  }

  [HarmonyPatch(typeof(PhotonMatchMakingHandler),
      nameof(PhotonMatchMakingHandler.OnPlayerPropertiesUpdate))]
  class Patch_OnPlayerPropertiesUpdate
  {
      // Photon signature: OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
      // In IL2CPP interop the first parameter becomes the interop-wrapped Player type,
      // which MGS wraps as NetworkPlayer — cast via the controller if needed.
      // The simplest approach: re-read all properties for that player via GetRoomPlayers().
      static void Postfix(object targetPlayer)
      {
          if (LobbyManager.Instance == null) return;
          // targetPlayer is the IL2CPP Player; find the matching NetworkPlayer by UserId.
          // We cast to INetworkPlayer (if available) or use dynamic dispatch via the
          // Photon.Realtime.Player interop type to get UserId.
          try
          {
              var photonPlayer = targetPlayer as Photon.Realtime.Player;
              if (photonPlayer == null) return;
              var players = LobbyManager.Instance._controller.GetRoomPlayers();
              if (players == null) return;
              foreach (var p in players)
              {
                  if (p != null && p.UserId == photonPlayer.UserId)
                  {
                      LobbyManager.Instance.HandlePlayerPropertiesUpdate(p);
                      return;
                  }
              }
          }
          catch (System.Exception ex)
          {
              Plugin.Log.LogWarning(
                  $"[Patch_OnPlayerPropertiesUpdate] {ex.Message}");
          }
      }
  }
  ```

  > **Type note:** `PhotonMatchMakingHandler.OnPlayerPropertiesUpdate`'s first parameter in the IL2CPP interop may be typed as `Photon.Realtime.Player` or as a raw `Il2CppSystem.Object`. If `as Photon.Realtime.Player` returns null at runtime, replace the cast with:
  > ```csharp
  > var userId = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToCSharpObject(
  >     (Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)targetPlayer
  > )?.ToString();
  > ```
  > But try the simple cast first — it works in most BepInEx IL2CPP mods. Check `BepInEx.log` after the first in-game test.

- [ ] **Step 7: Also clear _peerVersions in HandleLeftRoom**

  Find `HandleLeftRoom` (around line 123). Add one line to clear the version map when the local player leaves the room:

  ```csharp
  internal void HandleLeftRoom()
  {
      PendingRejoin = true;
      _roomSteamIds.Clear();
      _peerVersions.Clear();          // ← add
      VersionMapChanged?.Invoke();    // ← add
      PlayerListChanged?.Invoke();
      RejoinAvailable?.Invoke();
      Plugin.Log.LogInfo("Left room — rejoin prompt raised");
  }
  ```

- [ ] **Step 8: Build**

  ```
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: 0 errors. The most likely error is an unresolved `Photon.Realtime.Player` type — if so, check which namespace `PhotonUnityNetworking.dll` uses (open in ILSpy and look for the `Player` class, then adjust the `using` in the patch).

- [ ] **Step 9: Commit**

  ```
  git add src/EKLobbyMod/LobbyManager.cs
  git commit -m "feat: LobbyManager version map with VersionMapChanged event and OnPlayerPropertiesUpdate patch"
  ```

---

## Task 4: OverlayPanel — drift band, min-tab amber dot, and Update button

**Files:**
- Modify: `src/EKLobbyMod/OverlayPanel.cs`

This task adds the visible version-mismatch indicators. Everything lives in `OverlayPanel` — no new files. Follow the existing `_rejoinPromptLabel` / `ShowRejoinPrompt` pattern exactly.

The expanded panel is `300 * _s` wide and `400 * _s` tall. The header strip occupies `y = 356*_s` to `y = 400*_s` (44 px tall). The code row is at `y = 322*_s`. The drift band sits at `y = 338*_s`, height `20 * _s`, between those two elements.

The minimised tab code text (`_codeText`) currently shows just the room name. When drift is detected we append `" ●"` (space + bullet) in amber; when not, we show only the room name.

The "Update" button opens `Plugin.ReleasesUrl` in the default browser. After clicking, the band text changes to `"⚠ Restart game after updating"` for 5 seconds, then reverts.

- [ ] **Step 1: Add new private fields to OverlayPanel**

  Open `src/EKLobbyMod/OverlayPanel.cs`. After the existing `private Text _rejoinPromptLabel = null!;` field (around line 21), add:

  ```csharp
  private GameObject _driftBand = null!;
  private Text _driftBandText = null!;
  private bool _showingRestartMsg = false;
  private float _restartMsgTimer = 0f;
  ```

- [ ] **Step 2: Build the drift band in BuildExpandedPanel**

  In `BuildExpandedPanel()`, find the line where `_codeLabelText` is built (around line 127):

  ```csharp
  _codeLabelText = CreateText(_expandedPanel.transform,
      $"Code: {_manager.Config.LobbyRoomName}", (int)(13 * _s));
  ```

  Insert the drift band construction immediately BEFORE that line:

  ```csharp
  // Drift band — amber strip between header and code row, hidden by default
  _driftBand = new GameObject("DriftBand");
  _driftBand.transform.SetParent(_expandedPanel.transform, false);
  var driftRt = _driftBand.AddComponent<RectTransform>();
  PositionRect(driftRt, new Vector2(0, 338 * _s), new Vector2(300 * _s, 20 * _s));
  _driftBand.AddComponent<Image>().color = new Color(1f, 0.55f, 0f, 1f); // amber

  _driftBandText = CreateText(_driftBand.transform, "⚠ Version mismatch", (int)(11 * _s));
  _driftBandText.color = Color.black;
  var driftTextRt = _driftBandText.rectTransform;
  driftTextRt.anchorMin = Vector2.zero;
  driftTextRt.anchorMax = Vector2.one;
  driftTextRt.offsetMin = new Vector2(6 * _s, 0);
  driftTextRt.offsetMax = new Vector2(-(58 * _s), 0); // leave room for Update button

  var updateBtn = CreateButton(_driftBand.transform, "Update", (int)(10 * _s),
      new Vector2(244 * _s, 1 * _s), new Vector2(52 * _s, 18 * _s));
  updateBtn.GetComponent<Image>().color = new Color(0.32f, 0.08f, 0.10f, 1f); // EkRedDark
  System.Action onUpdateClick = OnUpdateClicked;
  updateBtn.onClick.AddListener((UnityEngine.Events.UnityAction)onUpdateClick);

  _driftBand.SetActive(false); // hidden until drift detected
  ```

- [ ] **Step 3: Subscribe to VersionMapChanged in Inject**

  In the static `Inject` method, after the existing event subscriptions (the three lines around line 63–65 that wire `RejoinAvailable`, `RejoinConfirmed`, and `PlayerListChanged`), add:

  ```csharp
  manager.VersionMapChanged += (System.Action)panel.OnVersionMapChanged;
  ```

  The four-line block will then read:

  ```csharp
  manager.RejoinAvailable    += (System.Action)panel.ShowRejoinPrompt;
  manager.RejoinConfirmed    += (System.Action)panel.HideRejoinPrompt;
  manager.PlayerListChanged  += (System.Action)panel.OnPlayerListChanged;
  manager.VersionMapChanged  += (System.Action)panel.OnVersionMapChanged;
  ```

- [ ] **Step 4: Add OnVersionMapChanged method**

  After the existing `public void OnPlayerListChanged()` method, add:

  ```csharp
  public void OnVersionMapChanged()
  {
      var drift = _manager.HasVersionDrift;
      if (_driftBand != null)
          _driftBand.SetActive(drift);
      RefreshMinTabLabel();
  }
  ```

- [ ] **Step 5: Add RefreshMinTabLabel helper and update existing RefreshCodeLabel**

  The existing `RefreshCodeLabel()` sets `_codeText.text` to the room name only. We now need to also append the drift dot. Replace `RefreshCodeLabel()` with:

  ```csharp
  private void RefreshCodeLabel()
  {
      if (_codeLabelText != null && !_codeEditing)
          _codeLabelText.text = $"Code: {_manager.Config.LobbyRoomName}";
      RefreshMinTabLabel();
  }

  private void RefreshMinTabLabel()
  {
      if (_codeText == null) return;
      if (_manager.HasVersionDrift)
      {
          _codeText.text = $"{_manager.Config.LobbyRoomName} ●";
          _codeText.color = new Color(1f, 0.55f, 0f, 1f); // amber
      }
      else
      {
          _codeText.text = _manager.Config.LobbyRoomName;
          _codeText.color = EkOffWhite;
      }
  }
  ```

  > The original `RefreshCodeLabel` set both `_codeText` and `_codeLabelText`. We now split them: `_codeLabelText` (expanded panel code row) stays in `RefreshCodeLabel`, and `_codeText` (min tab) is handled by `RefreshMinTabLabel`. No behaviour is lost.

- [ ] **Step 6: Add OnUpdateClicked method**

  After `OnVersionMapChanged`, add:

  ```csharp
  private void OnUpdateClicked()
  {
      UnityEngine.Application.OpenURL(Plugin.ReleasesUrl);
      _showingRestartMsg = true;
      _restartMsgTimer = 5f;
      if (_driftBandText != null)
          _driftBandText.text = "⚠ Restart game after updating";
  }
  ```

- [ ] **Step 7: Add Update() to handle the restart-message timer**

  `OverlayPanel` is a `MonoBehaviour`, so Unity calls `Update()` each frame. Add:

  ```csharp
  private void Update()
  {
      if (!_showingRestartMsg) return;
      _restartMsgTimer -= UnityEngine.Time.deltaTime;
      if (_restartMsgTimer <= 0f)
      {
          _showingRestartMsg = false;
          if (_driftBandText != null)
              _driftBandText.text = "⚠ Version mismatch";
      }
  }
  ```

- [ ] **Step 8: Build**

  ```
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: 0 errors. If `UnityEngine.Application.OpenURL` is not found, it lives in `UnityEngine.CoreModule` (already referenced). If `Color.black` is not resolved, use `new Color(0f, 0f, 0f, 1f)`.

- [ ] **Step 9: Commit**

  ```
  git add src/EKLobbyMod/OverlayPanel.cs
  git commit -m "feat: overlay drift band, amber min-tab dot, and Update button"
  ```

---

## Task 5: Deploy and smoke-test in-game

This is a manual verification task. There is no automated test for Photon/Unity integration — we verify by running two game instances.

**Files:** none changed in this task.

- [ ] **Step 1: Publish and install the updated DLL**

  ```
  dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod
  copy out\EKLobbyMod\EKLobbyMod.dll "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\plugins\EKLobbyMod.dll"
  ```

- [ ] **Step 2: Smoke-test 1 — single player, no drift**

  Launch the game. Open the overlay. Join or create the home lobby room (click Rejoin if needed).

  Open `BepInEx/logs/BepInEx.log` in a text editor. Look for:

  ```
  [Info   :EKLobbyMod] [PhotonPropertyHelper] Set local ekmod_ver = 1.0.0
  [Info   :EKLobbyMod] [LobbyManager] Version map rebuilt: 1 modded peer(s), drift=False
  ```

  Expected in the overlay:
  - Min tab shows room code **without** the amber dot.
  - Expanded panel: no amber drift band visible.

- [ ] **Step 3: Smoke-test 2 — force drift (edit Plugin.cs temporarily)**

  To test the drift indicator without a second machine, temporarily change `PluginVersion` in `Plugin.cs` to a different value, rebuild, and install. Then restore it afterward.

  Edit `Plugin.cs`:
  ```csharp
  public const string PluginVersion = "1.0.0-test-drift";
  ```

  Rebuild and install (same commands as Step 1). Launch game and join the room.

  The local player will broadcast `"1.0.0-test-drift"`. The version map will contain `"1.0.0-test-drift"` for the local player. Since all peers have the same version, `HasVersionDrift` will be `false` — so this test simulates the "all same version" case with the new version string.

  To force a visible drift without a second machine: after joining the room, open `BepInEx.log` and confirm version was set. Then manually verify via log output — it is not practical to trigger a true mismatch solo. The code path will be exercised in the two-player test below.

  **Restore Plugin.cs:**
  ```csharp
  public const string PluginVersion = "1.0.0";
  ```

  Rebuild and reinstall.

- [ ] **Step 4: Smoke-test 3 — two players, one with old version**

  With a friend or a second Steam account / PC:

  1. Player A installs the current build (version `1.0.0`). Player B installs a build with `PluginVersion = "0.9.0"` (edit, rebuild, install on Player B's machine).
  2. Both join the same room (use the Rejoin button or Steam invite).
  3. Player A checks their overlay — the min tab should show `"EK-XXXX ●"` in amber. The expanded panel should show the amber drift band with `"⚠ Version mismatch"` and an `[Update]` button.
  4. Player A clicks `[Update]` — the default browser should open to the GitHub releases URL. The band text should change to `"⚠ Restart game after updating"` for 5 seconds, then revert.
  5. Player B's overlay should show the drift band too (from their perspective, Player A is on version `1.0.0` and they are on `0.9.0` — still a mismatch).
  6. Check `BepInEx.log` on both machines for `drift=True`.

  If a second machine is unavailable, skip to Step 5 and rely on the log output from Step 2 to confirm code correctness.

- [ ] **Step 5: Commit smoke-test results as a comment in BepInEx.log (optional)**

  No file change needed. If the smoke-tests pass, proceed to Task 6. If drift is `True` when it should be `False` (e.g., old version cached in room properties), verify that `HandleLeftRoom` clears `_peerVersions` correctly.

---

## Task 6: Final cleanup and version bump

**Files:**
- Modify: `src/EKLobbyMod/Plugin.cs` (version bump to 1.1.0)

- [ ] **Step 1: Bump PluginVersion to 1.1.0**

  The version maintenance feature is now live. Bump the version to signal the new capability:

  ```csharp
  public const string PluginVersion = "1.1.0";
  ```

- [ ] **Step 2: Build final**

  ```
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj -c Release
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Final install**

  ```
  dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod
  copy out\EKLobbyMod\EKLobbyMod.dll "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\plugins\EKLobbyMod.dll"
  ```

- [ ] **Step 4: Commit**

  ```
  git add src/EKLobbyMod/Plugin.cs
  git commit -m "chore: bump version to 1.1.0 — version broadcast and drift indicator shipped"
  ```
