using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine.SceneManagement;

namespace EKLobbyMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.eklobbymod.plugin";
    public const string PluginName = "EKLobbyMod";
    public const string PluginVersion = "1.1.16";
    public const string ReleasesUrl   = "https://github.com/adamhurm/exploding-kittens-2-lobby-mod/releases";

    // 'new' shadows BasePlugin.Log (instance) with a static field accessible by other classes
    internal static new ManualLogSource Log = null!;

    // Singleton instance — set in Load() so LobbyManager can read pending args
    internal static Plugin? Instance { get; private set; }

    // Harmony instance stored statically so PhotonClientFinder can apply runtime patches
    internal static Harmony HarmonyInstance { get; private set; } = null!;

    // Pending +connect arg from cold-launch command line; applied in LobbyManager.Initialize()
    internal string? _pendingConnectArg;

    public override void Load()
    {
        Log = base.Log;
        Instance = this;
        LobbyManager.LogInfo    = msg => Log.LogInfo(msg);
        LobbyManager.LogWarning = msg => Log.LogWarning(msg);
        LobbyManager.LogDebug   = msg => Log.LogDebug(msg);
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded");
        ClassInjector.RegisterTypeInIl2Cpp<OverlayPanel>();
        ClassInjector.RegisterTypeInIl2Cpp<FriendPickerPopup>();
        HarmonyInstance = new Harmony(PluginGuid);
        HarmonyInstance.PatchAll();
        SteamJoinPatch.TryApply(HarmonyInstance);
        SceneManager.sceneLoaded += new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);

        // Cold-launch: Steam may pass the room code as a command-line arg
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "+connect")
            {
                var connectArg = args[i + 1];
                if (LobbyManager.IsValidRoomName(connectArg))
                {
                    Log.LogInfo($"Cold launch: valid +connect arg received ({connectArg.Length} chars)");
                    _pendingConnectArg = connectArg;
                }
                else
                {
                    Log.LogWarning($"Cold launch: +connect arg failed validation (length={connectArg?.Length ?? 0}) - ignored");
                }
                break;
            }
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log.LogInfo($"Scene loaded: {scene.name}");
        var manager = LobbyManager.Instance;
        if (manager != null)
            OverlayPanel.Inject(manager);
    }

}
