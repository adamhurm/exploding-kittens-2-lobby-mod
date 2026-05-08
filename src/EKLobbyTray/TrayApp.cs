// src/EKLobbyTray/TrayApp.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using EKLobbyShared;

namespace EKLobbyTray;

public class TrayApp : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly FileSystemWatcher _watcher;
    private LobbyConfig _config;
    private DateTime _lastConfigReload = DateTime.MinValue;

    public TrayApp()
    {
        _config = ConfigStore.Load();
        _icon = new NotifyIcon
        {
            Text = "EK Lobby",
            Icon = SystemIcons.Application,
            Visible = true
        };
        _icon.ContextMenuStrip = BuildMenu();

        var configDir = Path.GetDirectoryName(ConfigStore.OverridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EKLobbyMod", "config.json"))!;
        Directory.CreateDirectory(configDir);

        _watcher = new FileSystemWatcher(configDir, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnConfigChanged;
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        // FSW fires twice on Windows for a single save; ignore events within 500ms of the last reload
        var now = DateTime.UtcNow;
        if ((now - _lastConfigReload).TotalMilliseconds < 500) return;
        _lastConfigReload = now;

        _config = ConfigStore.Load();
        _icon.ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var codeItem = new ToolStripMenuItem($"Lobby code: {_config.LobbyRoomName}");
        codeItem.Click += (_, _) => Clipboard.SetText(_config.LobbyRoomName);
        menu.Items.Add(codeItem);
        menu.Items.Add(new ToolStripSeparator());

        var inviteAll = new ToolStripMenuItem("Invite All Friends");
        inviteAll.Click += (_, _) => SteamUriInviter.InviteAll(_config.Friends);
        menu.Items.Add(inviteAll);

        foreach (var friend in _config.Friends)
        {
            var f = friend;
            var friendItem = new ToolStripMenuItem(f.DisplayName);
            var removeItem = new ToolStripMenuItem("Remove from list");
            removeItem.Click += (_, _) =>
            {
                _config.Friends.Remove(f);
                ConfigStore.Save(_config);
            };
            friendItem.DropDownItems.Add(removeItem);
            menu.Items.Add(friendItem);
        }

        menu.Items.Add(new ToolStripSeparator());

        var launchItem = new ToolStripMenuItem("Launch Game");
        launchItem.Click += (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "steam://rungameid/2999030",
                UseShellExecute = true
            });
        menu.Items.Add(launchItem);

        var autoLaunch = new ToolStripMenuItem("Start with Windows")
            { Checked = AutoLaunchHelper.IsEnabled() };
        autoLaunch.Click += (_, _) =>
        {
            if (AutoLaunchHelper.IsEnabled()) AutoLaunchHelper.Disable();
            else AutoLaunchHelper.Enable();
            autoLaunch.Checked = AutoLaunchHelper.IsEnabled();
        };
        menu.Items.Add(autoLaunch);

        menu.Items.Add(new ToolStripSeparator());
        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => Application.Exit();
        menu.Items.Add(quit);

        return menu;
    }

    // Testable helper — returns descriptions of menu items without instantiating WinForms UI
    public static List<string> BuildMenuItems(LobbyConfig config)
    {
        var items = new List<string>();
        items.Add($"Lobby code: {config.LobbyRoomName}");
        items.Add("Invite All Friends");
        foreach (var f in config.Friends) items.Add(f.DisplayName);
        items.Add("Launch Game");
        items.Add("Start with Windows");
        items.Add("Quit");
        return items;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _icon.Dispose();
    }
}
