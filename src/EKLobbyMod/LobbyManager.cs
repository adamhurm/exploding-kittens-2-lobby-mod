using System.Collections.Generic;
using EKLobbyShared;
using HarmonyLib;
using MGS.Network;
using MGS.Network.Photon;

namespace EKLobbyMod;

public class LobbyManager
{
    public static LobbyManager Instance { get; private set; }

    private readonly IMultiplayerController _controller;
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

    // Non-null while we are in the middle of a Steam invite join.
    // Holds the room name to restore once the join completes (so the friend's own home lobby
    // is not permanently overwritten by the host's room code).
    private string? _preInviteRoomName = null;

    // True while the 5-second countdown is running in the overlay.
    // Reset to false on OnJoinedRoom or when the player explicitly leaves mid-countdown.
    public bool AutoQueueActive { get; private set; }

    // Set to false to skip the auto-queue countdown entirely (reserved for future settings UI).
    public bool AutoQueueEnabled { get; set; } = true;

    public IReadOnlyCollection<string> RoomSteamIds => _roomSteamIds;
    public bool IsMasterClient => _controller.IsMasterClient();

    private LobbyManager(IMultiplayerController controller)
    {
        _controller = controller;
        Config = ConfigStore.Load();
        _controller.add_OnPlayerEnteredRoomEvent(new System.Action<NetworkPlayer>(HandlePlayerEntered));
        _controller.add_OnPlayerLeftRoomEvent(new System.Action<NetworkPlayer>(HandlePlayerLeft));
        _controller.add_OnPlayerPropertiesChangedEvent(
            new System.Action<NetworkPlayer, Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object>>(
                (p, _) => HandlePlayerPropertiesUpdate(p)));
        Plugin.Log.LogInfo($"LobbyManager ready — home lobby: {Config.LobbyRoomName}");
    }

    public static void Initialize(IMultiplayerController controller)
    {
        Instance = new LobbyManager(controller);
        var steamId = SteamInviter.GetLocalSteamId();
        if (steamId != 0)
        {
            Instance._localSteamId = steamId;
            Instance.Config.LobbyRoomName = ConfigStore.GetOrCreateRoomName(steamId);
        }
        Plugin.Log.LogInfo($"LobbyManager initialized — room: {Instance.Config.LobbyRoomName}");

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
            Plugin.Log.LogWarning("Home lobby room name not set — cannot rejoin");
            return;
        }
        Plugin.Log.LogInfo($"Attempting to join room: {Config.LobbyRoomName}");
        _joinOrCreatePending = true;
        _controller.JoinRoom(Config.LobbyRoomName);
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
        Plugin.Log.LogInfo($"Room name updated to: {newName}");
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
            Plugin.Log.LogWarning($"JoinSpecificRoom: rejected invalid room name (length={roomName?.Length})");
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
            Plugin.Log.LogWarning($"JoinRoomByInvite: rejected invalid room name (length={roomName?.Length})");
            return;
        }
        Plugin.Log.LogInfo($"Invite join: {roomName} (home lobby will be restored after join)");
        _preInviteRoomName = Config.LobbyRoomName;
        Config.LobbyRoomName = roomName; // in-memory only — no ConfigStore.Save
        JoinOrCreateHomeLobby();
    }

    // ── Photon event handlers (called from Harmony patches below) ─────────────

    internal void HandleCreatedRoom()
    {
        var name = _controller.GetRoomName();
        if (_preInviteRoomName != null)
        {
            // Invite join created the room (host not there yet) — restore own lobby name
            Config.LobbyRoomName = _preInviteRoomName;
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
        _controller.AllowKickPlayers(true);
        RefreshRoomPlayers();
        Plugin.Log.LogInfo($"Room created: {name}");
    }

    internal void HandleJoinedRoom()
    {
        var roomName = _controller.GetRoomName();
        if (roomName != Config.LobbyRoomName)
        {
            Plugin.Log.LogDebug($"OnJoinedRoom: game room '{roomName}'");
            _joinOrCreatePending = false;
            _inGame = true;
            AutoQueueActive = false; // cancel any countdown — game started
            return;
        }
        _inGame = false;
        _inHomeLobby = true;
        _joinOrCreatePending = false;
        PendingRejoin = false;
        AutoQueueActive = false;
        RejoinConfirmed?.Invoke();
        RefreshRoomPlayers();
        PhotonPropertyHelper.SetLocalVersion(Plugin.PluginVersion);
        RebuildVersionMap();
        if (roomName != _lastLoggedRoom)
        {
            Plugin.Log.LogInfo($"Joined room: {roomName}");
            _lastLoggedRoom = roomName;
        }
        if (_preInviteRoomName != null)
        {
            // Restore friend's own home lobby after successfully joining the invite room
            Config.LobbyRoomName = _preInviteRoomName;
            ConfigStore.Save(Config);
            _preInviteRoomName = null;
        }
    }

    internal void HandleLeftRoom()
    {
        if (_inGame)
        {
            // Left a game room — game just ended, trigger countdown back to home lobby
            _inGame = false;
            _roomSteamIds.Clear();
            _peerVersions.Clear();
            VersionMapChanged?.Invoke();
            PlayerListChanged?.Invoke();
            PendingRejoin = true;
            if (AutoQueueEnabled)
                AutoQueueActive = true;
            RejoinAvailable?.Invoke();
            Plugin.Log.LogInfo("Game ended — auto-queue started");
            return;
        }

        if (!_inHomeLobby) return;
        _inHomeLobby = false;

        if (_preInviteRoomName != null)
        {
            // Left current room intentionally to join a Steam invite — don't trigger auto-queue
            _roomSteamIds.Clear();
            _peerVersions.Clear();
            VersionMapChanged?.Invoke();
            PlayerListChanged?.Invoke();
            Plugin.Log.LogInfo("Left room for invite join — auto-queue suppressed");
            return;
        }

        // Left home lobby because a game is starting — clear state, no countdown
        // (countdown fires when the game ends and we leave the game room)
        _roomSteamIds.Clear();
        _peerVersions.Clear();
        VersionMapChanged?.Invoke();
        PlayerListChanged?.Invoke();
        Plugin.Log.LogInfo("Left home lobby — game starting");
    }

    private void HandlePlayerEntered(NetworkPlayer player)
    {
        if (!_inHomeLobby || player == null) return;
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

    private void HandlePlayerLeft(NetworkPlayer player)
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
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NetworkPlayer> players;
        try { players = _controller.GetRoomPlayers(); }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[LobbyManager] GetRoomPlayers threw (room not ready yet): {ex.Message}");
            PlayerListChanged?.Invoke();
            return;
        }
        if (players != null)
            foreach (var p in players)
                if (p != null && !string.IsNullOrEmpty(p.UserId))
                    _roomSteamIds.Add(p.UserId);
        PlayerListChanged?.Invoke();
    }

    private void RebuildVersionMap()
    {
        _peerVersions.Clear();
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NetworkPlayer> players;
        try { players = _controller.GetRoomPlayers(); }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[LobbyManager] GetRoomPlayers threw in RebuildVersionMap: {ex.Message}");
            VersionMapChanged?.Invoke();
            return;
        }
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

    public void KickPlayer(string steam64Id)
    {
        _controller.AllowKickPlayers(true);
        _controller.KickPlayer(steam64Id);
        Plugin.Log.LogDebug($"Kicked player (Steam64 ID redacted from LogInfo)");
    }

    internal void HandleJoinRoomFailed(short returnCode, string message)
    {
        if (!_joinOrCreatePending) return;

        // Room not found (Photon code 32758) or similar — create it instead
        Plugin.Log.LogInfo($"JoinRoom failed ({returnCode}): {message} — creating room");
        // ctor: publishUserId, isOpen, isVisible, maxPlayers, playerTtl, emptyRoomTtl
        var opts = new NetworkRoomOptions(false, true, false, 5, 60000, 0);
        _controller.CreateRoom(Config.LobbyRoomName, opts, null);
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
