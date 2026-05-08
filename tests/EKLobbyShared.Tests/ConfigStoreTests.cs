using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using EKLobbyShared;
using Xunit;

namespace EKLobbyShared.Tests;

[Collection("ConfigStoreTests")]
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
    public void GetOrCreateRoomName_WhenNoExisting_GeneratesRoomName()
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

    // H-1: ACL restriction test
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Save_RestrictsConfigFileToCurrentUser()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        ConfigStore.Save(new LobbyConfig { LobbyRoomName = "EK-TEST" });

        var fi = new FileInfo(ConfigStore.OverridePath);
        var acl = fi.GetAccessControl();
        var rules = acl.GetAccessRules(true, false, typeof(NTAccount)); // false = no inherited
        var me = WindowsIdentity.GetCurrent().Name;
        bool onlyMe = rules.Cast<FileSystemAccessRule>().All(r => r.IdentityReference.Value == me);
        Assert.True(onlyMe, "config.json should have no inherited ACEs and grant access only to current user");
    }

    // M-1: HMAC integrity test
    [Fact]
    public void Load_ReturnsFreshConfig_WhenFileIsTampered()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var original = new LobbyConfig { LobbyRoomName = "EK-AABBCCDD" };
        ConfigStore.Save(original);

        // Tamper with the file directly
        var raw = File.ReadAllText(ConfigStore.OverridePath);
        raw = raw.Replace("EK-AABBCCDD", "EK-EVIL1234");
        File.WriteAllText(ConfigStore.OverridePath, raw);

        var loaded = ConfigStore.Load();
        // Should reject the tampered file and return defaults
        Assert.NotEqual("EK-EVIL1234", loaded.LobbyRoomName);
    }

    // M-4: Random room code test
    [Fact]
    public void GetOrCreateRoomName_GeneratesRandomSuffix_NotSteamIdDerived()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        // Use a known Steam64 ID
        ulong steamId = 76561198000000000UL;
        var name = ConfigStore.GetOrCreateRoomName(steamId);

        Assert.StartsWith("EK-", name);
        Assert.Equal(11, name.Length); // "EK-" + 8 hex chars

        // The last 8 hex of steamId 76561198000000000 = "02FAF080"
        // A random code must NOT match this derived value
        Assert.NotEqual("EK-02FAF080", name);
    }
}
