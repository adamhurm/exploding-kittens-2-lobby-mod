// src/EKLobbyTray/AutoLaunchHelper.cs
using Microsoft.Win32;

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
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)!;
        // NOTE: This only works correctly when EKLobbyTray is deployed as a self-contained
        // standalone .exe (via dotnet publish --self-contained). Running via 'dotnet run'
        // will register the dotnet runtime path instead of the app path.
        key.SetValue(ValueName, System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)!;
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
