using System;
using System.IO;
using EKLobbyShared;
using Xunit;

namespace EKLobbyShared.Tests;

[Collection("SecretsStoreTests")]
public class SecretsStoreTests : IDisposable
{
    public SecretsStoreTests()
    {
        SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    }

    public void Dispose() => SecretsStore.OverridePath = null;

    [Fact]
    public void Load_ReturnsEmpty_WhenFileAbsent()
    {
        SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var s = SecretsStore.Load();
        Assert.Equal("", s.DiscordBotSecret);
    }

    [Fact]
    public void Save_Load_RoundTrips_DiscordBotSecret()
    {
        SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        SecretsStore.Save(new LobbySecrets { DiscordBotSecret = "mysecret" });
        var loaded = SecretsStore.Load();
        Assert.Equal("mysecret", loaded.DiscordBotSecret);
    }

    // Task 8: Save_And_Load_RoundTrip_PreservesSecret
    [Fact]
    public void Save_And_Load_RoundTrip_PreservesSecret()
    {
        SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        SecretsStore.Save(new LobbySecrets { DiscordBotSecret = "test-secret-xyz" });
        var loaded = SecretsStore.Load();
        Assert.Equal("test-secret-xyz", loaded.DiscordBotSecret);
    }

    // Task 8: Load_WhenFileAbsent_ReturnsDefault
    [Fact]
    public void Load_WhenFileAbsent_ReturnsDefault()
    {
        // Point to a path that is guaranteed not to exist
        SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "_nonexistent.json");
        var secrets = SecretsStore.Load();
        Assert.NotNull(secrets);
        // LobbySecrets.DiscordBotSecret defaults to "" (empty string, not null)
        Assert.True(string.IsNullOrEmpty(secrets.DiscordBotSecret));
    }
}
