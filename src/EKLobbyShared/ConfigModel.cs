// src/EKLobbyShared/ConfigModel.cs
using System.Collections.Generic;

namespace EKLobbyShared;

public class FriendEntry
{
    public string Steam64Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class LobbyConfig
{
    public string LobbyRoomName { get; set; } = "";
    public List<FriendEntry> Friends { get; set; } = new();
    public bool AutoLaunchTray { get; set; } = false;
}
