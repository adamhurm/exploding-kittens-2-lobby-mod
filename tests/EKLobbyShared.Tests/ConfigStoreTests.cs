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

    // Bug 7: null-forgiving operator on GetDirectoryName — regression test
    [Fact]
    public void Save_WithBareFilenameOverridePath_DoesNotThrow()
    {
        // A bare filename has no directory separator, so Path.GetDirectoryName returns
        // null (on .NET) or empty string — this previously caused a NullReferenceException
        // because the null-forgiving operator ! was used without a guard.
        var bareFilename = "testconfig_regression.json";
        ConfigStore.OverridePath = bareFilename;
        try
        {
            var ex = Record.Exception(() => ConfigStore.Save(new LobbyConfig { LobbyRoomName = "EK-REGRESSION" }));
            Assert.Null(ex);
        }
        finally
        {
            ConfigStore.OverridePath = _tempPath;
            // Clean up files that may have been created in the working directory
            if (File.Exists(bareFilename)) File.Delete(bareFilename);
            if (File.Exists(bareFilename + ".hmac")) File.Delete(bareFilename + ".hmac");
        }
    }

    // Task 8: HMAC file corrupted — non-base64 garbage causes FormatException from
    // Convert.FromBase64String inside ConfigStore.Load(). The Load() method does NOT
    // swallow this exception; callers should be aware that a truly corrupted .hmac
    // file (non-base64) propagates a FormatException rather than silently returning defaults.
    [Fact]
    public void Load_WhenHmacFileCorrupted_ThrowsFormatException()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var cfg = new LobbyConfig { LobbyRoomName = "EK-TESTCORR" };
        ConfigStore.Save(cfg);

        // Overwrite the .hmac sidecar with non-base64 garbage bytes
        var hmacPath = ConfigStore.OverridePath + ".hmac";
        File.WriteAllBytes(hmacPath, new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03 });

        // Convert.FromBase64String throws FormatException on the garbage bytes
        Assert.Throws<FormatException>(() => ConfigStore.Load());
    }

    // Task 8: Round-trip preserves non-empty friends list
    [Fact]
    public void Save_And_Load_RoundTrip_PreservesFriends()
    {
        ConfigStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var config = new LobbyConfig
        {
            LobbyRoomName = "EK-FRIENDS1",
            Friends = new() { new FriendEntry { Steam64Id = "76561198000000001", DisplayName = "BobFriend" } }
        };
        ConfigStore.Save(config);
        var loaded = ConfigStore.Load();

        Assert.Single(loaded.Friends);
        Assert.Equal("76561198000000001", loaded.Friends[0].Steam64Id);
        Assert.Equal("BobFriend", loaded.Friends[0].DisplayName);
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
