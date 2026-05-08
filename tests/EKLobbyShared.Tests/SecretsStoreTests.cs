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
}
