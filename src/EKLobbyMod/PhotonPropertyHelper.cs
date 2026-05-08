using ExitGames.Client.Photon;  // Hashtable — from Photon3Unity3D interop
using MGS.Network;               // NetworkPlayer

namespace EKLobbyMod;

/// <summary>
/// Wraps Photon custom player property I/O.
/// All other classes call this instead of touching PhotonNetwork or Hashtable directly.
/// </summary>
public static class PhotonPropertyHelper
{
    // Matches LobbyManager.VersionPropertyKey — defined here for compile independence.
    private const string VersionKey = "ekmod_ver";

    /// <summary>
    /// Writes the local player's mod version into their Photon custom properties.
    /// Safe to call when not in a room — PhotonNetwork.LocalPlayer is always non-null.
    /// </summary>
    public static void SetLocalVersion(string version)
    {
        try
        {
            var props = new Hashtable();
            props[VersionKey] = version;
            Photon.Pun.PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            Plugin.Log.LogInfo($"[PhotonPropertyHelper] Set local ekmod_ver = {version}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[PhotonPropertyHelper] SetLocalVersion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads another player's mod version from their Photon custom properties.
    /// Returns null if the property is absent (player has no mod or pre-broadcast version).
    /// </summary>
    public static string ReadPeerVersion(NetworkPlayer player)
    {
        if (player == null) return null;
        try
        {
            var props = player.CustomProperties;
            if (props == null) return null;
            // CustomProperties is an ExitGames.Client.Photon.Hashtable in the interop.
            // The indexer returns object; cast via ToString().
            var val = props[VersionKey];
            return val?.ToString();
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[PhotonPropertyHelper] ReadPeerVersion failed: {ex.Message}");
            return null;
        }
    }
}
