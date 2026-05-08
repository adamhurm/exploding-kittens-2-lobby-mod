// src/EKLobbyTray/SteamUriInviter.cs
using System.Collections.Generic;
using System.Diagnostics;
using EKLobbyShared;

namespace EKLobbyTray;

public static class SteamUriInviter
{
    public static void InviteAll(IEnumerable<FriendEntry> friends)
    {
        foreach (var friend in friends)
            Invite(friend.Steam64Id);
    }

    public static void Invite(string steam64Id)
    {
        // Opens Steam's built-in invite dialog for this friend
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://friends/invite/{steam64Id}",
            UseShellExecute = true
        });
    }
}
