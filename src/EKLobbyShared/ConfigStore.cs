// src/EKLobbyShared/ConfigStore.cs
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
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

    // --- HMAC helpers (M-1) ---

    private static byte[] DeriveHmacKey()
    {
        // Machine-local key: not secret from the current user, but opaque to other users/processes
        var salt = "EKLobbyMod-v1-integrity";
        var identity = Environment.UserName + Environment.MachineName;
        return SHA256.HashData(Encoding.UTF8.GetBytes(identity + salt));
    }

    private static string HmacPath(string configPath) => configPath + ".hmac";

    private static string ComputeHmac(string json)
    {
        var key = DeriveHmacKey();
        var tag = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(tag);
    }

    // --- Public API ---

    public static LobbyConfig Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new LobbyConfig();

        var json = File.ReadAllText(path);
        var hmacPath = HmacPath(path);

        if (File.Exists(hmacPath))
        {
            var stored = File.ReadAllText(hmacPath).Trim();
            var expected = ComputeHmac(json);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(stored),
                    Convert.FromBase64String(expected)))
            {
                // Log and return safe default rather than potentially malicious config
                System.Diagnostics.Debug.WriteLine(
                    "[ConfigStore] HMAC mismatch — config.json may be tampered. Returning defaults.");
                return new LobbyConfig();
            }
        }

        return JsonSerializer.Deserialize<LobbyConfig>(json, JsonOpts) ?? new LobbyConfig();
    }

    public static void Save(LobbyConfig config)
    {
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(path, json);
        File.WriteAllText(HmacPath(path), ComputeHmac(json));
        RestrictToCurrentUser(path);
        RestrictToCurrentUser(HmacPath(path));
    }

    public static string GetOrCreateRoomName(ulong steam64Id)
    {
        var config = Load();
        if (!string.IsNullOrEmpty(config.LobbyRoomName))
            return config.LobbyRoomName;

        // Generate a cryptographically random 8-char hex suffix, independent of Steam ID
        var bytes = RandomNumberGenerator.GetBytes(4);
        var suffix = Convert.ToHexString(bytes); // uppercase 8-char hex
        config.LobbyRoomName = $"EK-{suffix}";
        Save(config);
        return config.LobbyRoomName;
    }

    /// <summary>
    /// Removes inherited ACEs and grants Read+Write only to the current user.
    /// No-ops on non-Windows platforms.
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(NTAccount)))
            acl.RemoveAccessRule(rule);
        var me = WindowsIdentity.GetCurrent().Name;
        acl.AddAccessRule(new FileSystemAccessRule(
            me,
            FileSystemRights.Read | FileSystemRights.Write,
            AccessControlType.Allow));
        fi.SetAccessControl(acl);
    }
}
