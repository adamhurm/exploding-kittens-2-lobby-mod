using System;
using System.Collections.Generic;
using System.Reflection;
using EKLobbyShared;
using Steamworks;

namespace EKLobbyMod;

// Wraps the in-process Steamworks.NET API loaded by the game (MGS.Platform.SteamManager).
// SteamFriends/SteamUser calls are available because the game already initialised Steam before
// our plugin code runs.
public static class SteamInviter
{
    // Resolved once on first InviteAll call — the game's platform InviteFriendImmediately method
    // and its singleton. Prefer this over SteamFriends.InviteUserToGame so the invite goes
    // through the game's existing invite system (proper rich-presence setup, matching accept flow).
    private static bool _platformResolved;
    private static object _platformSingleton;
    private static MethodInfo _platformInviteMethod;

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

    public static void InviteAll(IEnumerable<string> steam64Ids, string connectString = "")
    {
        if (!SteamManager.Instance || !SteamManager.Initialized) return;
        if (!_platformResolved) ResolvePlatformInvite();

        foreach (var idStr in steam64Ids)
        {
            if (_platformSingleton != null && _platformInviteMethod != null)
            {
                try
                {
                    _platformInviteMethod.Invoke(_platformSingleton, new object[] { idStr, connectString });
                    continue;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[SteamInviter] Platform invite threw: {ex.Message}; falling back");
                    _platformSingleton = null; // don't retry broken method
                }
            }
            if (!ulong.TryParse(idStr, out var raw)) continue;
            SteamFriends.InviteUserToGame(new CSteamID(raw), connectString);
        }
    }

    // Searches the MGS.Platform assembly for a type with InviteFriendImmediately(string,string)
    // and a static Instance property. Called once; result is cached in static fields.
    private static void ResolvePlatformInvite()
    {
        _platformResolved = true;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "MGS.Platform") continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types, t => t != null); }
            catch { break; }

            foreach (var type in types)
            {
                if (type == null) continue;
                var m = type.GetMethod("InviteFriendImmediately",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(string), typeof(string) }, null);
                if (m == null) continue;
                var instanceProp = type.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public);
                if (instanceProp == null) continue;
                try
                {
                    var inst = instanceProp.GetValue(null);
                    if (inst == null) continue;
                    _platformSingleton = inst;
                    _platformInviteMethod = m;
                    Plugin.Log?.LogInfo($"[SteamInviter] Platform invite resolved: {type.FullName}.InviteFriendImmediately");
                    return;
                }
                catch { continue; }
            }
            break;
        }
        Plugin.Log?.LogInfo("[SteamInviter] Platform invite not found; using SteamFriends.InviteUserToGame");
    }
}
