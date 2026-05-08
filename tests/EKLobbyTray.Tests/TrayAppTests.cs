using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    [Fact]
    public void BuildMenuItems_TruncatesLongDisplayName()
    {
        var config = new LobbyConfig
        {
            LobbyRoomName = "EK-TESTCODE",
            Friends = new List<FriendEntry>
            {
                new FriendEntry { Steam64Id = "76561198000000001", DisplayName = new string('A', 200) }
            }
        };
        var items = TrayApp.BuildMenuItems(config);
        var friendItem = items.First(i => i.StartsWith(new string('A', 10)));
        Assert.True(friendItem.Length <= 35 + 3, "DisplayName should be truncated to 35 chars + '...'");
    }
}
