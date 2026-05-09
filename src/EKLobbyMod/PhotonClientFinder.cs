using EK.Network;
using HarmonyLib;
using MGS.Network;

namespace EKLobbyMod;

// Captures the game's IMultiplayerController when ExplodingKittensMultiplayer initialises.
// The game uses a custom MGS abstraction (IMultiplayerController) over Photon, so we hook
// there rather than digging into LoadBalancingClient directly.
public static class PhotonClientFinder
{
    public static IMultiplayerController Controller { get; private set; }

    [HarmonyPatch(typeof(ExplodingKittensMultiplayer), nameof(ExplodingKittensMultiplayer.InitNetworking))]
    class Patch_InitNetworking
    {
        static void Postfix(ExplodingKittensMultiplayer __instance)
        {
            Controller = __instance.Controller;
            Plugin.Log.LogInfo($"IMultiplayerController captured: {Controller?.GetType()?.Name ?? "null"}");

            if (LobbyManager.Instance == null && Controller != null)
                LobbyManager.Initialize(new PhotonControllerBridge(Controller));

            PartyGamePatch.TryApply(Plugin.HarmonyInstance, Controller);
        }
    }
}
