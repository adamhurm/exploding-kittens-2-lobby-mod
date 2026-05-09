using HarmonyLib;
using Steamworks;
using System;
using System.Reflection;

namespace EKLobbyMod;

// Harmony-patches SteamManager._OnRichPresenceJoinRequested to intercept Steam overlay
// join requests. Callback<GameRichPresenceJoinRequested_t>.Create() cannot be used because
// the struct is non-blittable under IL2CppInterop (m_rgchConnect is a string property).
[HarmonyPatch]
static class SteamJoinPatch
{
    static MethodBase? TargetMethod()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, t => t != null);
            }
            catch { continue; }

            foreach (var type in types)
            {
                if (type == null || type.Name != "SteamManager") continue;
                var method = type.GetMethod("_OnRichPresenceJoinRequested",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null) continue;
                Plugin.Log.LogInfo($"[SteamJoinPatch] Target: {type.FullName}.{method.Name}");
                return method;
            }
        }
        Plugin.Log.LogWarning("[SteamJoinPatch] SteamManager._OnRichPresenceJoinRequested not found; Steam overlay joins unavailable");
        return null;
    }

    static void Postfix(GameRichPresenceJoinRequested_t param)
    {
        try
        {
            var connect = param.m_rgchConnect;
            Plugin.Log.LogInfo($"[SteamJoinPatch] Rich presence join ({connect?.Length ?? 0} chars)");
            if (string.IsNullOrEmpty(connect)) return;
            if (!LobbyManager.IsValidRoomName(connect))
            {
                Plugin.Log.LogWarning("[SteamJoinPatch] Connect string failed validation, ignoring");
                return;
            }
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.JoinRoomByInvite(connect);
            else if (Plugin.Instance != null)
            {
                Plugin.Instance._pendingConnectArg = connect;
                Plugin.Log.LogInfo("[SteamJoinPatch] LobbyManager not ready, stored as pending");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SteamJoinPatch] Postfix error: {ex.Message}");
        }
    }
}
