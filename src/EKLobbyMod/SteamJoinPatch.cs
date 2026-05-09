using HarmonyLib;
using Steamworks;
using System;
using System.Reflection;

namespace EKLobbyMod;

// Applied manually (not via PatchAll) so a null result doesn't crash the plugin.
// Patches the game's SteamManager._OnRichPresenceJoinRequested to intercept Steam
// overlay join requests. Callback<GameRichPresenceJoinRequested_t>.Create() cannot
// be used because the struct is non-blittable under IL2CppInterop.
static class SteamJoinPatch
{
    internal static void TryApply(Harmony harmony)
    {
        MethodBase? method = null;
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
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (m == null) continue;
                    Plugin.Log.LogInfo($"[SteamJoinPatch] Found: {type.FullName}.{m.Name}");
                    method = m;
                }
                catch { }
                if (method != null) break;
            }
            if (method != null) break;
        }

        if (method == null)
        {
            Plugin.Log.LogWarning("[SteamJoinPatch] _OnRichPresenceJoinRequested not found; Steam overlay joins unavailable");
            return;
        }

        try
        {
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SteamJoinPatch), nameof(Postfix)));
            Plugin.Log.LogInfo("[SteamJoinPatch] Patch applied successfully");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SteamJoinPatch] Patch failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    static void Postfix(GameRichPresenceJoinRequested_t callback)
    {
        try
        {
            var connect = callback.m_rgchConnect;
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
