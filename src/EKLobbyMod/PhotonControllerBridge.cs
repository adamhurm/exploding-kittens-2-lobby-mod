using System;
using System.Collections.Generic;
using MGS.Network;
using MGS.Network.Photon;

namespace EKLobbyMod;

public sealed class PhotonControllerBridge : IPhotonBridge
{
    private readonly IMultiplayerController _controller;

    public event Action<PlayerInfo> PlayerEntered;
    public event Action<PlayerInfo> PlayerLeft;
    public event Action<PlayerInfo> PlayerPropertiesChanged;
    public event Action RoomPropertiesChanged;

    public PhotonControllerBridge(IMultiplayerController controller)
    {
        _controller = controller;
        _controller.add_OnPlayerEnteredRoomEvent(new System.Action<NetworkPlayer>(p =>
            PlayerEntered?.Invoke(new PlayerInfo(p?.UserId ?? "", PhotonPropertyHelper.ReadPeerVersion(p)))));
        _controller.add_OnPlayerLeftRoomEvent(new System.Action<NetworkPlayer>(p =>
            PlayerLeft?.Invoke(new PlayerInfo(p?.UserId ?? ""))));
        _controller.add_OnPlayerPropertiesChangedEvent(
            new System.Action<NetworkPlayer, Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object>>(
                (p, _) => PlayerPropertiesChanged?.Invoke(
                    new PlayerInfo(p?.UserId ?? "", PhotonPropertyHelper.ReadPeerVersion(p)))));
        _controller.add_OnRoomPropertiesChangedEvent(
            new System.Action<NetworkRoom, Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object>>(
                (_, _) => RoomPropertiesChanged?.Invoke()));
    }

    public void JoinRoom(string roomName) => _controller.JoinRoom(roomName);

    public void CreateRoom(string name) =>
        _controller.CreateRoom(name, new NetworkRoomOptions(false, true, false, 5, 60000, 0), null);

    public void LeaveRoom() => _controller.LeaveRoom();
    public void JoinRandomRoom() => _controller.JoinRandomRoom();
    public string GetRoomName() => _controller.GetRoomName();
    public bool IsMasterClient() => _controller.IsMasterClient();
    public void AllowKickPlayers(bool allow) => _controller.AllowKickPlayers(allow);
    public void KickPlayer(string userId) => _controller.KickPlayer(userId);
    public void SetLocalVersion(string version) => PhotonPropertyHelper.SetLocalVersion(version);
    public void ClearPartyGameRoom() => PhotonPropertyHelper.ClearRoomGameProperty();
    public string GetRoomProperty(string key) => PhotonPropertyHelper.GetRoomProperty(key);

    public IReadOnlyList<PlayerInfo> GetRoomPlayers()
    {
        var result = new List<PlayerInfo>();
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<NetworkPlayer> players;
        try { players = _controller.GetRoomPlayers(); }
        catch (System.Exception ex)
        {
            Plugin.Log?.LogWarning($"[PhotonControllerBridge] GetRoomPlayers threw: {ex.Message}");
            return result;
        }
        if (players != null)
            foreach (var p in players)
                if (p != null && !string.IsNullOrEmpty(p.UserId))
                    result.Add(new PlayerInfo(p.UserId, PhotonPropertyHelper.ReadPeerVersion(p)));
        return result;
    }
}
