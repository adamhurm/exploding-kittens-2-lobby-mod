using System.Collections.Generic;
using EKLobbyShared;
using ExitGames.Client.Photon;
using HarmonyLib;
using MGS.Network.Photon;

namespace EKLobbyMod;

public class LobbyManager
{
    public static LobbyManager Instance { get; private set; }

    private readonly IPhotonBridge _bridge;
    public LobbyConfig Config { get; private set; }
    public bool PendingRejoin { get; private set; }

    // Set to true while we are attempting JoinOrCreate for our home room, so that
    // OnJoinRoomFailed knows to escalate to CreateRoom rather than ignoring the failure.
    private bool _joinOrCreatePending = false;
    private string _lastLoggedRoom = "";
    private ulong _localSteamId;

    // True only when we are inside our own home lobby room (not a game/matchmaking room).
    // Used to ignore OnJoinedRoom/OnLeftRoom events from internal Photon rooms.
    private bool _inHomeLobby = false;

    // Steam64 IDs of players currently connected to this Photon room.
    // Assumes PhotonNetwork.UserId == Steam64 ID string (standard for Steam+Photon games).
    private readonly HashSet<string> _roomSteamIds = new HashSet<string>();

    public event System.Action RejoinAvailable;
    public event System.Action RejoinConfirmed;
    public event System.Action PlayerListChanged;
    public event System.Action AutoQueueCancelled;
    public event System.Action GameStarted;

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

    // True while the player is inside a non-home-lobby Photon room (game in progress).
    // Used to trigger the auto-queue countdown only after a game ends, not on home-lobby leave.
    private bool _inGame = false;

    // True while an explicit mid-game leave is in progress (player clicked Leave button).
    // Suppresses the post-game prompt — HandleLeftRoom will immediately rejoin home lobby.
    private bool _leavingToHomeLobby = false;

    // True while the player has requested a home lobby recreate (Leave → immediately Create).
    private bool _recreatingHomeLobby = false;

    // Set true by OnPartyGameStarting so HandleRoomPropertiesUpdated skips the initiator.
    private bool _partyGameInitiatedByMe = false;

    // Non-null while we are in the middle of a Steam invite join.
    // Holds the room name to restore once the join completes (so the friend's own home lobby
    // is not permanently overwritten by the host's room code).
    private string? _preInviteRoomName = null;

    // True while the 15-second countdown is running in the overlay.
    // Reset to false on OnJoinedRoom or when the player explicitly clicks Leave mid-countdown.
    public bool AutoQueueActive { get; private set; }

    // Set to false to skip the auto-queue countdown entirely (reserved for future settings UI).
    public bool AutoQueueEnabled { get; set; } = true;

    // True while the player is inside a non-home-lobby Photon room (game in progress).
    public bool InGame => _inGame;

    // True while the player is in their own home lobby room.
    public bool InHomeLobby => _inHomeLobby;

    public IReadOnlyCollection<string> RoomSteamIds => _roomSteamIds;
    public bool IsMasterClient => _bridge.IsMasterClient();

    // Wired by Plugin.Load() to real BepInEx logging; default to no-ops so tests can
    // instantiate LobbyManager without BepInEx.Core in the output directory.
    internal static Action<string> LogInfo    = _ => { };
    internal static Action<string> LogWarning = _ => { };
    internal static Action<string> LogDebug   = _ => { };

    internal LobbyManager(IPhotonBridge bridge)
    {
        _bridge = bridge;
        Config = ConfigStore.Load();
        _bridge.PlayerEntered += HandlePlayerEntered;
        _bridge.PlayerLeft += HandlePlayerLeft;
        _bridge.PlayerPropertiesChanged += HandlePlayerPropertiesUpdate;
        LogInfo($"LobbyManager ready - home lobby: {Config.LobbyRoomName}");
    }

    public static void Initialize(IPhotonBridge bridge)
    {
        Instance = new LobbyManager(bridge);
        var steamId = SteamInviter.GetLocalSteamId();
        if (steamId != 0)
        {
            Instance._localSteamId = steamId;
            Instance.Config.LobbyRoomName = ConfigStore.GetOrCreateRoomName(steamId);
        }
        LogInfo($"LobbyManager initialized - room: {Instance.Config.LobbyRoomName}");

        // Apply cold-launch +connect arg if one was captured during Plugin.Load()
        if (Plugin.Instance?._pendingConnectArg is string pending)
        {
            Instance.JoinRoomByInvite(pending);
            Plugin.Instance._pendingConnectArg = null;
        }
    }

    public void JoinOrCreateHomeLobby()
    {
        if (string.IsNullOrEmpty(Config.LobbyRoomName))
        {
            LogWarning("Home lobby room name not set - cannot rejoin");
            return;
        }
        LogInfo($"Attempting to join room: {Config.LobbyRoomName}");
        _joinOrCreatePending = true;
        _bridge.JoinRoom(Config.LobbyRoomName);
    }

    public void AddFriend(FriendEntry friend)
    {
        Config.Friends.Add(friend);
        ConfigStore.Save(Config);
    }

    public void RemoveFriend(string steam64Id)
    {
        Config.Friends.RemoveAll(f => f.Steam64Id == steam64Id);
        ConfigStore.Save(Config);
    }

    public void UpdateRoomName(string newName)
    {
        Config.LobbyRoomName = newName;
        ConfigStore.Save(Config);
        LogInfo($"Room name updated to: {newName}");
    }

    /// <summary>
    /// A valid room name is 1–64 printable ASCII characters with no control characters,
    /// null bytes, or path-separator characters. This matches the Photon room name limits.
    /// </summary>
    public static bool IsValidRoomName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > 64) return false;
        foreach (char c in name)
        {
            if (c < 0x20 || c > 0x7E) return false;  // non-printable or non-ASCII
            if (c == '/' || c == '\\' || c == '.') return false;  // path chars
        }
        return true;
    }

    public void JoinSpecificRoom(string roomName)
    {
        if (!IsValidRoomName(roomName))
        {
            LogWarning($"JoinSpecificRoom: rejected invalid room name (length={roomName?.Length})");
            return;
        }
        UpdateRoomName(roomName);
        JoinOrCreateHomeLobby();
    }

    // Like JoinSpecificRoom but used for Steam invite acceptance.
    // Swaps the room name in-memory only (no config write) so HandleJoinedRoom/HandleCreatedRoom
    // recognise the room, then restores the friend's own home lobby after the join completes.
    public void JoinRoomByInvite(string roomName)
    {
        if (!IsValidRoomName(roomName))
        {
            LogWarning($"JoinRoomByInvite: rejected invalid room name (length={roomName?.Length})");
            return;
        }
        // Guard against a second invite arriving before the first join completes.
        // Photon only allows one active JoinRoom call at a time, so accepting a second
        // invite while one is pending would overwrite _preInviteRoomName and corrupt the
        // home lobby name on restore. Reject the second invite to preserve the first.
        if (_preInviteRoomName != null)
        {
            LogWarning($"JoinRoomByInvite: invite join already pending, ignoring {roomName}");
            return;
        }
        LogInfo($"Invite join: {roomName} (home lobby will be restored after join)");
        _preInviteRoomName = Config.LobbyRoomName;
        Config.LobbyRoomName = roomName; // in-memory only — no ConfigStore.Save
        JoinOrCreateHomeLobby();
    }

    // ── Photon event handlers (called from Harmony patches below) ─────────────

    internal void HandleCreatedRoom()
    {
        var name = _bridge.GetRoomName() ?? string.Empty;
        if (_preInviteRoomName != null)
        {
            // Invite join created the room (host not there yet) — persist party room
            ConfigStore.Save(Config);
            _preInviteRoomName = null;
        }
        else if (_joinOrCreatePending)
        {
            Config.LobbyRoomName = name;
            ConfigStore.Save(Config);
        }
        _joinOrCreatePending = false;
        _inGame = false;
        _inHomeLobby = true;
        _bridge.AllowKickPlayers(true);
        RefreshRoomPlayers();
        if (_bridge.IsMasterClient())
            _bridge.ClearPartyGameRoom();
        LogInfo($"Room created: {name}");
    }

    internal void HandleJoinedRoom()
    {
        var roomName = _bridge.GetRoomName() ?? string.Empty;
        if (roomName != Config.LobbyRoomName)
        {
            LogDebug($"OnJoinedRoom: game room '{roomName}'");
            _joinOrCreatePending = false;
            _inGame = true;
            PendingRejoin = false; // clear post-game state — we're in a new game room
            AutoQueueActive = false; // cancel any countdown — game started
            GameStarted?.Invoke();
            return;
        }
        _inGame = false;
        _inHomeLobby = true;
        _joinOrCreatePending = false;
        PendingRejoin = false;
        AutoQueueActive = false;
        RejoinConfirmed?.Invoke();
        RefreshRoomPlayers();
        _bridge.SetLocalVersion(Plugin.PluginVersion);
        RebuildVersionMap();
        if (_bridge.IsMasterClient())
            _bridge.ClearPartyGameRoom();
        if (roomName != _lastLoggedRoom)
        {
            LogInfo($"Joined room: {roomName}");
            _lastLoggedRoom = roomName;
        }
        if (_preInviteRoomName != null)
        {
            // Persist party room as home lobby after successfully joining
            ConfigStore.Save(Config);
            _preInviteRoomName = null;
        }
    }

    internal void HandleLeftRoom()
    {
        if (_inGame)
        {
            _inGame = false;
            _roomSteamIds.Clear();
            _peerVersions.Clear();
            VersionMapChanged?.Invoke();
            PlayerListChanged?.Invoke();

            if (_leavingToHomeLobby)
            {
                // Player explicitly clicked Leave mid-game — skip post-game prompt, go straight home
                _leavingToHomeLobby = false;
                LogInfo("Mid-game leave: rejoining home lobby directly");
                JoinOrCreateHomeLobby();
                return;
            }

            // Game ended normally — show post-game prompt; countdown only starts if Play Again is clicked
            PendingRejoin = true;
            RejoinAvailable?.Invoke();
            LogInfo("Game ended - showing post-game prompt");
            return;
        }

        if (!_inHomeLobby)
        {
            // Neither _inGame nor _inHomeLobby — unknown state (e.g. a game room we never
            // tracked). Clear PendingRejoin so the overlay does not keep showing a stale
            // post-game prompt that can never be resolved from this state.
            PendingRejoin = false;
            return;
        }
        _inHomeLobby = false;
        _roomSteamIds.Clear();
        _peerVersions.Clear();
        VersionMapChanged?.Invoke();
        PlayerListChanged?.Invoke();

        if (_recreatingHomeLobby)
        {
            // Invariant: flag is cleared BEFORE calling JoinOrCreateHomeLobby so that
            // if another left-room event fires during the rejoin attempt, the recreate
            // path is not re-entered (which would trigger an infinite recreate loop).
            _recreatingHomeLobby = false;
            LogInfo("Recreate: rejoining home lobby");
            JoinOrCreateHomeLobby();
            return;
        }

        if (_preInviteRoomName != null)
        {
            LogInfo("Left room for invite join - auto-queue suppressed");
            return;
        }

        LogInfo("Left home lobby - game starting");
    }

    private void HandlePlayerEntered(PlayerInfo player)
    {
        if (!_inHomeLobby || player == null) return;
        if (!string.IsNullOrEmpty(player.UserId))
        {
            _roomSteamIds.Add(player.UserId);
            PlayerListChanged?.Invoke();
            if (player.Version != null)
            {
                _peerVersions[player.UserId] = player.Version;
                VersionMapChanged?.Invoke();
            }
        }
    }

    private void HandlePlayerLeft(PlayerInfo player)
    {
        if (!_inHomeLobby || player == null) return;
        var uid = player.UserId ?? "";
        _roomSteamIds.Remove(uid);
        PlayerListChanged?.Invoke();
        if (_peerVersions.Remove(uid))
            VersionMapChanged?.Invoke();
    }

    private void RefreshRoomPlayers()
    {
        _roomSteamIds.Clear();
        var players = _bridge.GetRoomPlayers();
        foreach (var p in players)
            if (!string.IsNullOrEmpty(p.UserId))
                _roomSteamIds.Add(p.UserId);
        PlayerListChanged?.Invoke();
    }

    private void RebuildVersionMap()
    {
        _peerVersions.Clear();
        var players = _bridge.GetRoomPlayers();
        foreach (var p in players)
        {
            if (string.IsNullOrEmpty(p.UserId)) continue;
            if (p.Version != null)
                _peerVersions[p.UserId] = p.Version;
        }
        VersionMapChanged?.Invoke();
        LogInfo(
            $"[LobbyManager] Version map rebuilt: {_peerVersions.Count} modded peer(s), drift={HasVersionDrift}");
    }

    internal void HandlePlayerPropertiesUpdate(PlayerInfo player)
    {
        if (player == null || string.IsNullOrEmpty(player.UserId)) return;
        if (player.Version != null)
        {
            _peerVersions[player.UserId] = player.Version;
            VersionMapChanged?.Invoke();
            LogInfo(
                $"[LobbyManager] Player {player.UserId} updated ekmod_ver to {player.Version}");
        }
    }

    // Called when the player clicks "Play Again" in the overlay.
    // Clears the post-game UI and queues a new random game match — no countdown shown.
    public void RequestPlayAgain()
    {
        PendingRejoin = false;
        AutoQueueActive = false;
        RejoinConfirmed?.Invoke(); // clears post-game UI silently (no countdown)
        _bridge.JoinRandomRoom();
        LogInfo("Play Again: queueing for matchmaking");
    }

    // Leaves the home lobby and immediately recreates it (fresh room, same name).
    // Useful when no one has joined and the player wants to reset or force a new room.
    public void RecreateHomeLobby()
    {
        _recreatingHomeLobby = true;
        _bridge.LeaveRoom();
        LogInfo("Recreating home lobby");
    }

    // Called when the player clicks "Leave" in the overlay (mid-game or post-game).
    // Cancels any running countdown and returns to the home lobby.
    public void LeaveToHomeLobby()
    {
        AutoQueueActive = false;
        AutoQueueCancelled?.Invoke(); // stops countdown coroutine in overlay

        if (_inGame)
        {
            // Mid-game: leave game room first; HandleLeftRoom will then call JoinOrCreateHomeLobby
            _leavingToHomeLobby = true;
            _bridge.LeaveRoom();
        }
        else
        {
            // Post-game: already left game room, just rejoin home lobby
            PendingRejoin = false;
            JoinOrCreateHomeLobby();
        }
    }

    public void KickPlayer(string steam64Id)
    {
        _bridge.AllowKickPlayers(true);
        _bridge.KickPlayer(steam64Id);
        LogDebug($"Kicked player (Steam64 ID redacted from LogInfo)");
    }

    internal void HandleJoinRoomFailed(short returnCode, string message)
    {
        if (!_joinOrCreatePending) return;
        // Clear before calling CreateRoom so a spurious second OnJoinRoomFailed during
        // the create flight cannot trigger a second CreateRoom call.
        _joinOrCreatePending = false;

        // Room not found (Photon code 32758) or similar — create it instead
        LogInfo($"JoinRoom failed ({returnCode}): {message} - creating room");
        _bridge.CreateRoom(Config.LobbyRoomName);
    }

    // ── Party game launch ──────────────────────────────────────────────────────

    // Called by PartyGamePatch.Prefix before the CreateRoom Photon operation fires.
    // Writes ek_party_game to the current lobby room so all party members auto-join.
    internal void OnPartyGameStarting(string gameRoomName)
    {
        _partyGameInitiatedByMe = true;
        PhotonPropertyHelper.SetRoomGameProperty(gameRoomName);
        LogInfo($"[Party] Routing game to {gameRoomName} - notifying party via room property");
    }

    internal void HandleRoomPropertiesUpdated(Hashtable props)
    {
        if (_partyGameInitiatedByMe) { _partyGameInitiatedByMe = false; return; }
        if (!_inHomeLobby || props == null) return;
        if (!props.ContainsKey(PhotonPropertyHelper.PartyGameKey)) return;
        var gameRoom = props[PhotonPropertyHelper.PartyGameKey]?.ToString();
        if (!IsValidRoomName(gameRoom)) return;
        LogInfo($"[Party] Leader started game in {gameRoom} - auto-joining");
        _bridge.JoinRoom(gameRoom);
    }

    // ── Harmony patches ────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(PhotonMatchMakingHandler), nameof(PhotonMatchMakingHandler.OnCreatedRoom))]
    class Patch_OnCreatedRoom
    {
        static void Postfix() => Instance?.HandleCreatedRoom();
    }

    [HarmonyPatch(typeof(PhotonMatchMakingHandler), nameof(PhotonMatchMakingHandler.OnJoinedRoom))]
    class Patch_OnJoinedRoom
    {
        static void Postfix() => Instance?.HandleJoinedRoom();
    }

    [HarmonyPatch(typeof(PhotonMatchMakingHandler), nameof(PhotonMatchMakingHandler.OnLeftRoom))]
    class Patch_OnLeftRoom
    {
        static void Postfix() => Instance?.HandleLeftRoom();
    }

    [HarmonyPatch(typeof(PhotonMatchMakingHandler), nameof(PhotonMatchMakingHandler.OnJoinRoomFailed))]
    class Patch_OnJoinRoomFailed
    {
        static void Postfix(short returnCode, string message) =>
            Instance?.HandleJoinRoomFailed(returnCode, message);
    }

}
