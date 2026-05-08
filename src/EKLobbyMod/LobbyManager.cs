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

    public event System.Action RejoinAvailable;
    public event System.Action RejoinConfirmed;

    private LobbyManager(IMultiplayerController controller)
    {
        _controller = controller;
        Config = ConfigStore.Load();
        Plugin.Log.LogInfo($"LobbyManager ready — home lobby: {Config.LobbyRoomName}");
    }

    public static void Initialize(IMultiplayerController controller)
    {
        Instance = new LobbyManager(controller);
        var steamId = SteamInviter.GetLocalSteamId();
        if (steamId != 0)
            Instance.Config.LobbyRoomName = ConfigStore.GetOrCreateRoomName(steamId);
        Plugin.Log.LogInfo($"LobbyManager initialized — room: {Instance.Config.LobbyRoomName}");
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

    // ── Photon event handlers (called from Harmony patches below) ─────────────

    internal void HandleCreatedRoom()
    {
        var name = _controller.GetRoomName();
        if (_joinOrCreatePending)
        {
            Config.LobbyRoomName = name;
            ConfigStore.Save(Config);
        }
        _joinOrCreatePending = false;
        Plugin.Log.LogInfo($"Room created: {name}");
    }

    internal void HandleJoinedRoom()
    {
        _joinOrCreatePending = false;
        PendingRejoin = false;
        RejoinConfirmed?.Invoke();
        Plugin.Log.LogInfo($"Joined room: {_controller.GetRoomName()}");
    }

    internal void HandleLeftRoom()
    {
        PendingRejoin = true;
        RejoinAvailable?.Invoke();
        Plugin.Log.LogInfo("Left room — rejoin prompt raised");
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
