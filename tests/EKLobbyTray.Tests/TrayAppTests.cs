using System;
using System.IO;
using EKLobbyShared;
using EKLobbyTray;
using Xunit;

namespace EKLobbyTray.Tests;

[Collection("TrayAppTests")]
public class TrayAppTests : IDisposable
{
    private readonly string _tempPath;

    public TrayAppTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        ConfigStore.OverridePath = _tempPath;
    }

    public void Dispose() => ConfigStore.OverridePath = null;

    [Fact]
    public void BuildMenuItems_WithNoFriends_ShowsOnlyStaticItems()
    {
        ConfigStore.Save(new LobbyConfig { LobbyRoomName = "EK-TEST0000" });
        var items = TrayApp.BuildMenuItems(ConfigStore.Load());
        Assert.Contains(items, i => i.Contains("EK-TEST0000"));
        Assert.Contains(items, i => i.Contains("Launch Game"));
        Assert.Contains(items, i => i.Contains("Quit"));
    }

    [Fact]
    public void BuildMenuItems_WithFriends_IncludesFriendNames()
    {
        ConfigStore.Save(new LobbyConfig
        {
            LobbyRoomName = "EK-TEST0000",
            Friends = new() { new FriendEntry { Steam64Id = "123", DisplayName = "Bob" } }
        });
        var items = TrayApp.BuildMenuItems(ConfigStore.Load());
        Assert.Contains(items, i => i.Contains("Bob"));
    }
}
