using System;
using System.Collections.Generic;
using EKLobbyMod;

namespace EKLobbyMod.Tests;

public class FakePhotonBridge : IPhotonBridge
{
    public string CurrentRoomName { get; set; }
    public bool MasterClient { get; set; } = true;
    public List<PlayerInfo> RoomPlayers { get; } = new List<PlayerInfo>();

    public event Action<PlayerInfo> PlayerEntered;
    public event Action<PlayerInfo> PlayerLeft;
    public event Action<PlayerInfo> PlayerPropertiesChanged;

    public string GetRoomName() => CurrentRoomName;
    public IReadOnlyList<PlayerInfo> GetRoomPlayers() => RoomPlayers;
    public bool IsMasterClient() => MasterClient;

    public void JoinRoom(string roomName) { }
    public void CreateRoom(string name) { }
    public void LeaveRoom() { }
    public void JoinRandomRoom() { }
    public void AllowKickPlayers(bool allow) { }
    public void KickPlayer(string userId) { }
    public void SetLocalVersion(string version) { }
    public void ClearPartyGameRoom() { }

    public void FirePlayerEntered(PlayerInfo p) => PlayerEntered?.Invoke(p);
    public void FirePlayerLeft(PlayerInfo p) => PlayerLeft?.Invoke(p);
    public void FirePlayerPropertiesChanged(PlayerInfo p) => PlayerPropertiesChanged?.Invoke(p);
}
