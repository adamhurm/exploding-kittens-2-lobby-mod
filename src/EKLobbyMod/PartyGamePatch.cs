using System;
using System.Reflection;
using HarmonyLib;
using MGS.Network;

namespace EKLobbyMod;

// Applied at runtime in PhotonClientFinder once the concrete IMultiplayerController type
// is known. Intercepts the game's CreateRoom call so we can:
//   1. Replace the room name with a deterministic EK-...-g name (so party members can
//      derive the target without an out-of-band message).
//   2. Announce the upcoming game room via a Photon room property before A leaves the
//      mod lobby, giving B time to receive the update and auto-join.
static class PartyGamePatch
{
    internal static void TryApply(Harmony harmony, IMultiplayerController controller)
    {
        if (controller == null) return;

        MethodBase? method = null;
        try
        {
            foreach (var m in controller.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == "CreateRoom" && m.GetParameters().Length == 3 && !m.IsAbstract)
                { method = m; break; }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PartyGamePatch] Method search failed: {ex.Message}");
        }

        if (method == null)
        {
            Plugin.Log.LogWarning("[PartyGamePatch] CreateRoom not found; party auto-join unavailable");
            return;
        }
        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(PartyGamePatch), nameof(Prefix)));
            Plugin.Log.LogInfo($"[PartyGamePatch] Patched {controller.GetType().Name}.CreateRoom");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PartyGamePatch] Patch failed: {ex.Message}");
        }
    }

    // HarmonyX passes the first string parameter by ref so we can replace it.
    // IL2CPP note: if ref mutation is not applied, the room will retain the original
    // name but ek_party_game property still fires — party members will join whatever
    // name the game chose rather than the deterministic one. Not ideal but functional.
    static void Prefix(ref string name)
    {
        try
        {
            var mgr = LobbyManager.Instance;
            if (mgr == null || !mgr.InHomeLobby) return;
            if (string.IsNullOrEmpty(name) || name == mgr.Config.LobbyRoomName) return;

            var deterministic = mgr.Config.LobbyRoomName + "-g";
            name = deterministic;
            mgr.OnPartyGameStarting(deterministic);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PartyGamePatch] Prefix error: {ex.Message}");
        }
    }
}
