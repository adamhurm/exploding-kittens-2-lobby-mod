using System;
using System.IO;
using EKLobbyShared;
using Xunit;

namespace EKLobbyShared.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempPath;

    public ConfigStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "config.json");
        ConfigStore.OverridePath = _tempPath;
    }

    public void Dispose() => ConfigStore.OverridePath = null;

    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefaultConfig()
    {
        var config = ConfigStore.Load();
        Assert.NotNull(config);
        Assert.Empty(config.Friends);
        Assert.Equal("", config.LobbyRoomName);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var config = new LobbyConfig
        {
            LobbyRoomName = "EK-TESTCODE",
            Friends = new() { new FriendEntry { Steam64Id = "123", DisplayName = "Alice" } }
        };
        ConfigStore.Save(config);
        var loaded = ConfigStore.Load();
        Assert.Equal("EK-TESTCODE", loaded.LobbyRoomName);
        Assert.Single(loaded.Friends);
        Assert.Equal("Alice", loaded.Friends[0].DisplayName);
    }

    [Fact]
    public void GetOrCreateRoomName_WhenNoExisting_GeneratesFromSteamId()
    {
        var name = ConfigStore.GetOrCreateRoomName(76561198000000001UL);
        Assert.StartsWith("EK-", name);
        Assert.Equal(11, name.Length); // "EK-" + 8 hex chars
    }

    [Fact]
    public void GetOrCreateRoomName_WhenExisting_ReturnsExisting()
    {
        ConfigStore.Save(new LobbyConfig { LobbyRoomName = "EK-EXISTING" });
        var name = ConfigStore.GetOrCreateRoomName(76561198000000001UL);
        Assert.Equal("EK-EXISTING", name);
    }
}
