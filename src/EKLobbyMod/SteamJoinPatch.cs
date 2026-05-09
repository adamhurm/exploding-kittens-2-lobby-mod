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
