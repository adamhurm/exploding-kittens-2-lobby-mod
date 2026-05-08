// src/EKLobbyTray/AutoLaunchHelper.cs
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace EKLobbyTray;

public static class AutoLaunchHelper
{
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EKLobbyTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Enable()
    {
        var exePath = GetCurrentExePath();
        if (exePath == null)
        {
            throw new InvalidOperationException(
                "Cannot determine current executable path. " +
                "EKLobbyTray must be run as a self-contained .exe to use auto-launch.");
        }

        // Verify the path resolves to an actual .exe (not a temp dir, dotnet CLI, etc.)
        if (!File.Exists(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Auto-launch path '{exePath}' does not point to a .exe file. " +
                "Publish EKLobbyTray as a self-contained executable before enabling auto-launch.");
        }

        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key: {RegKey}");
        key.SetValue(ValueName, exePath);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Returns the current process executable path, or null if unavailable.</summary>
    public static string? GetCurrentExePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
