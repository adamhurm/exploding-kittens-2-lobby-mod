using System;
using System.Collections.Generic;

namespace EKLobbyMod;

public record PlayerInfo(string UserId, string Version = null);

public interface IPhotonBridge
{
    void JoinRoom(string roomName);
    void CreateRoom(string name);
    void LeaveRoom();
    void JoinRandomRoom();
    void AllowKickPlayers(bool allow);
    void KickPlayer(string userId);
    void SetLocalVersion(string version);
    void ClearPartyGameRoom();

    string GetRoomName();
    string GetRoomProperty(string key);
    IReadOnlyList<PlayerInfo> GetRoomPlayers();
    bool IsMasterClient();

    event Action<PlayerInfo> PlayerEntered;
    event Action<PlayerInfo> PlayerLeft;
    event Action<PlayerInfo> PlayerPropertiesChanged;
    event Action RoomPropertiesChanged;
}
