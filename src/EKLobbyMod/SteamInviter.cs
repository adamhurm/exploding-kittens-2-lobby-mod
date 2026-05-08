using System.Collections.Generic;
using EKLobbyShared;
using Steamworks;

namespace EKLobbyMod;

// Wraps the in-process Steamworks.NET API loaded by the game (MGS.Platform.SteamManager).
// SteamFriends/SteamUser calls are available because the game already initialised Steam before
// our plugin code runs.
public static class SteamInviter
{
    public static ulong GetLocalSteamId()
    {
        if (!SteamManager.Instance || !SteamManager.Initialized) return 0;
        return SteamUser.GetSteamID().m_SteamID;
    }

    public static IEnumerable<FriendEntry> GetAllSteamFriends()
    {
        if (!SteamManager.Instance || !SteamManager.Initialized) yield break;
        int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
        for (int i = 0; i < count; i++)
        {
            var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            yield return new FriendEntry
            {
                Steam64Id = id.m_SteamID.ToString(),
                DisplayName = SteamFriends.GetFriendPersonaName(id),
            };
        }
    }

    public static bool IsOnline(string steam64Id)
    {
        if (!SteamManager.Instance || !SteamManager.Initialized) return false;
        if (!ulong.TryParse(steam64Id, out var raw)) return false;
        var state = SteamFriends.GetFriendPersonaState(new CSteamID(raw));
        return state != EPersonaState.k_EPersonaStateOffline;
    }

    public static void InviteAll(IEnumerable<string> steam64Ids)
    {
        if (!SteamManager.Instance || !SteamManager.Initialized) return;
        foreach (var idStr in steam64Ids)
        {
            if (!ulong.TryParse(idStr, out var raw)) continue;
            // Empty connect string — the invitee uses their own Rejoin button to enter our room
            SteamFriends.InviteUserToGame(new CSteamID(raw), "");
        }
    }
}
