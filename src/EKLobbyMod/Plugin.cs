using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace EKLobbyMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.eklobbymod.plugin";
    public const string PluginName = "EKLobbyMod";
    public const string PluginVersion = "1.0.0";

    // 'new' shadows BasePlugin.Log (instance) with a static field accessible by other classes
    internal static new ManualLogSource Log = null!;

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"{PluginName} v{PluginVersion} loaded");
        new Harmony(PluginGuid).PatchAll();
        SceneManager.sceneLoaded += new System.Action<Scene, LoadSceneMode>(OnSceneLoaded);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Log.LogInfo($"Scene loaded: {scene.name}");
        var manager = LobbyManager.Instance;
        if (manager != null)
            OverlayPanel.Inject(manager);
    }
}
