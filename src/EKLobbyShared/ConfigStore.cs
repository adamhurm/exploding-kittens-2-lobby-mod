// src/EKLobbyShared/ConfigStore.cs
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EKLobbyShared;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EKLobbyMod", "config.json");

    // Set in tests to redirect file I/O to a temp path
    public static string? OverridePath { get; set; }

    private static string ResolvedPath => OverridePath ?? DefaultPath;

    public static LobbyConfig Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path))
            return new LobbyConfig();
        return JsonSerializer.Deserialize<LobbyConfig>(File.ReadAllText(path), JsonOpts)
               ?? new LobbyConfig();
    }

    public static void Save(LobbyConfig config)
    {
        var path = ResolvedPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOpts));
    }

    public static string GetOrCreateRoomName(ulong steam64Id)
    {
        var config = Load();
        if (!string.IsNullOrEmpty(config.LobbyRoomName))
            return config.LobbyRoomName;
        var hex = steam64Id.ToString("X16");
        config.LobbyRoomName = $"EK-{hex.Substring(hex.Length - 8)}";
        Save(config);
        return config.LobbyRoomName;
    }
}
