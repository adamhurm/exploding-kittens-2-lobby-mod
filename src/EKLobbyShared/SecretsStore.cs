// src/EKLobbyShared/SecretsStore.cs
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EKLobbyShared;

public static class SecretsStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EKLobbyMod", "secrets.json");

    public static string? OverridePath { get; set; }
    private static string ResolvedPath => OverridePath ?? DefaultPath;

    public static LobbySecrets Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new LobbySecrets();
        return JsonSerializer.Deserialize<LobbySecrets>(File.ReadAllText(path), JsonOpts)
               ?? new LobbySecrets();
    }

    public static void Save(LobbySecrets secrets)
    {
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(secrets, JsonOpts));
        RestrictToCurrentUser(path);
    }

    /// <summary>
    /// Removes inherited ACEs and grants Read+Write only to the current user.
    /// No-ops on non-Windows platforms (BepInEx mod only runs on Windows).
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        // Remove all existing rules
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(NTAccount)))
            acl.RemoveAccessRule(rule);
        // Grant current user only
        var me = WindowsIdentity.GetCurrent().Name;
        acl.AddAccessRule(new FileSystemAccessRule(
            me,
            FileSystemRights.Read | FileSystemRights.Write,
            AccessControlType.Allow));
        fi.SetAccessControl(acl);
    }
}
