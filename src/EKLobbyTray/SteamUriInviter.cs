// src/EKLobbyTray/SteamUriInviter.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using EKLobbyShared;

namespace EKLobbyTray;

public static class SteamUriInviter
{
    /// <summary>
    /// Returns true only if the string is a valid decimal Steam64 ID
    /// (17-digit number in the documented SteamID64 range).
    /// </summary>
    public static bool IsValidSteam64Id(string steam64Id)
    {
        if (string.IsNullOrWhiteSpace(steam64Id)) return false;
        if (!ulong.TryParse(steam64Id, out _)) return false;
        // Steam64 IDs are in the range [76561193972207616, 76561202255233023]
        // A simple sanity check: must start with 7656119 (the Steam universe/type prefix)
        return steam64Id.Length == 17 && steam64Id.StartsWith("7656119", StringComparison.Ordinal);
    }

    public static void InviteAll(IEnumerable<FriendEntry> friends)
    {
        foreach (var friend in friends)
            Invite(friend.Steam64Id);
    }

    public static void Invite(string steam64Id)
    {
        if (!IsValidSteam64Id(steam64Id))
        {
            // Log and skip; do not pass to shell
            System.Diagnostics.Debug.WriteLine($"[SteamUriInviter] Skipping invalid Steam64 ID: {steam64Id}");
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://friends/invite/{steam64Id}",
            UseShellExecute = true
        });
    }
}
