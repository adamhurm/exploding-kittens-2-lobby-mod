using HarmonyLib;
using Steamworks;
using System;
using System.Reflection;

namespace EKLobbyMod;

// Applied manually (not via PatchAll) so a null result doesn't crash the plugin.
// Patches the game's SteamManager._OnRichPresenceJoinRequested to intercept Steam
// overlay join requests. Callback<GameRichPresenceJoinRequested_t>.Create() cannot
// be used because the struct is non-blittable under IL2CppInterop.
//
// This is a PREFIX (not postfix) so we run BEFORE the game's handler. The game's
// handler calls JoinMatch → JoinRoom via Photon; if we ran after it as a postfix,
// our JoinRoomByInvite would issue a second JoinRoom, causing a double-join that
// triggers OnJoinRoomFailed → CreateRoom (catastrophic). Returning false skips the
// game's handler entirely, letting our join path own the operation.
static class SteamJoinPatch
{
    internal static void TryApply(Harmony harmony)
    {
        // Collect every type that DECLARES _OnRichPresenceJoinRequested (DeclaredOnly excludes
        // types that merely inherit the method). When both a base class and a derived class
        // declare the method (derived uses C# 'new' to shadow), Steamworks registers its
        // callback against the base class, so we must patch the base-most candidate.
        var candidates = new System.Collections.Generic.List<MethodBase>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types!, t => t != null); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null) continue;
                try
                {
                    var m = type.GetMethod("_OnRichPresenceJoinRequested",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                        | BindingFlags.DeclaredOnly);
                    if (m == null) continue;
                    Plugin.Log.LogInfo($"[SteamJoinPatch] Candidate: {type.FullName}.{m.Name}");
                    candidates.Add(m);
                }
                catch { }
            }
        }

        if (candidates.Count == 0)
        {
            Plugin.Log.LogWarning("[SteamJoinPatch] _OnRichPresenceJoinRequested not found; Steam overlay joins unavailable");
            return;
        }

        // Among candidates, pick the base-most declaring type.
        // Any candidate whose DeclaringType is a subclass of another candidate's DeclaringType
        // is a shadow (new) override — prefer the ancestor.
        MethodBase? method = null;
        foreach (var candidate in candidates)
        {
            var dt = candidate.DeclaringType!;
            bool isBase = true;
            foreach (var other in candidates)
            {
                if (other == candidate) continue;
                if (dt.IsSubclassOf(other.DeclaringType!)) { isBase = false; break; }
            }
            if (isBase) { method = candidate; break; }
        }
        method ??= candidates[0];
        Plugin.Log.LogInfo($"[SteamJoinPatch] Selected: {method.DeclaringType?.FullName}.{method.Name}");

        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(SteamJoinPatch), nameof(Prefix)));
            Plugin.Log.LogInfo("[SteamJoinPatch] Patch applied successfully");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SteamJoinPatch] Patch failed ({ex.GetType().Name}): {ex.Message}");
        }

        TryApplyLobbyProbe(harmony);
    }

    // Probe: find _OnLobbyJoinRequested to determine if the game uses Steam Lobby callbacks.
    // Pure logging postfix — never returns false, never interferes with the game.
    private static void TryApplyLobbyProbe(Harmony harmony)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types!, t => t != null); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null) continue;
                try
                {
                    var m = type.GetMethod("_OnLobbyJoinRequested",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                        | BindingFlags.DeclaredOnly);
                    if (m == null) continue;
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(SteamJoinPatch), nameof(LobbyProbePostfix)));
                    Plugin.Log.LogInfo($"[SteamJoinPatch] Lobby probe → {type.FullName}._OnLobbyJoinRequested");
                    return;
                }
                catch { }
            }
        }
        Plugin.Log.LogInfo("[SteamJoinPatch] _OnLobbyJoinRequested not found in any assembly");
    }

    static void LobbyProbePostfix(GameLobbyJoinRequested_t callback)
    {
        Plugin.Log.LogInfo($"[SteamJoinPatch] *** LOBBY JOIN REQUESTED fired — lobbyId={callback.m_steamIDLobby.m_SteamID} friend={callback.m_steamIDFriend.m_SteamID} ***");
        if (!SteamManager.Instance || !SteamManager.Initialized) return;
        int count = SteamMatchmaking.GetLobbyDataCount(callback.m_steamIDLobby);
        Plugin.Log.LogInfo($"[SteamJoinPatch] Lobby data entries: {count}");
        for (int i = 0; i < count; i++)
        {
            if (SteamMatchmaking.GetLobbyDataByIndex(callback.m_steamIDLobby, i,
                out string key, 256, out string val, 256))
                Plugin.Log.LogInfo($"[SteamJoinPatch]   lobby['{key}'] = '{val}'");
        }
    }

    // Returns false to skip the game's handler when we own this invite, preventing
    // the game's JoinMatch from racing our JoinRoomByInvite on the same Photon room.
    static bool Prefix(GameRichPresenceJoinRequested_t callback)
    {
        try
        {
            var connect = callback.m_rgchConnect;
            Plugin.Log.LogInfo($"[SteamJoinPatch] Rich presence join ({connect?.Length ?? 0} chars)");
            if (string.IsNullOrEmpty(connect)) return true;
            if (!LobbyManager.IsValidRoomName(connect))
            {
                Plugin.Log.LogWarning("[SteamJoinPatch] Connect string not our format, deferring to game handler");
                return true;
            }
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.JoinRoomByInvite(connect);
            else if (Plugin.Instance != null)
            {
                Plugin.Instance._pendingConnectArg = connect;
                Plugin.Log.LogInfo("[SteamJoinPatch] LobbyManager not ready, stored as pending");
            }
            return false; // skip game's JoinMatch — we own this join
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SteamJoinPatch] Prefix error: {ex.Message}");
            return true; // on error let game attempt its own handler
        }
    }
}
