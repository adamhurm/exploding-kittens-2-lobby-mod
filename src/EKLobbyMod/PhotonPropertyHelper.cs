using ExitGames.Client.Photon;  // Hashtable — from Photon3Unity3D interop
using MGS.Network;               // NetworkPlayer

namespace EKLobbyMod;

/// <summary>
/// Wraps Photon custom player property I/O.
/// All other classes call this instead of touching PhotonNetwork or Hashtable directly.
/// </summary>
public static class PhotonPropertyHelper
{
    private const string VersionKey = "ekmod_ver";
    internal const string PartyGameKey = "ek_party_game";

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

    internal static void SetRoomGameProperty(string gameRoomName)
    {
        try
        {
            var props = new Hashtable();
            props[PartyGameKey] = gameRoomName;
            Photon.Pun.PhotonNetwork.CurrentRoom.SetCustomProperties(props);
            Plugin.Log.LogInfo($"[PhotonPropertyHelper] Set {PartyGameKey} = {gameRoomName}");
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[PhotonPropertyHelper] SetRoomGameProperty failed: {ex.Message}");
        }
    }

    internal static string GetRoomProperty(string key)
    {
        try
        {
            var props = Photon.Pun.PhotonNetwork.CurrentRoom?.CustomProperties;
            if (props == null || !props.ContainsKey(key)) return null;
            return props[key]?.ToString();
        }
        catch { return null; }
    }

    internal static void ClearRoomGameProperty()
    {
        try
        {
            var props = new Hashtable();
            props[PartyGameKey] = null;
            Photon.Pun.PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[PhotonPropertyHelper] ClearRoomGameProperty failed: {ex.Message}");
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
            if (props == null || !props.ContainsKey(VersionKey)) return null;
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
