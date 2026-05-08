using EKLobbyTray;
using Xunit;

namespace EKLobbyTray.Tests;

public class SteamUriInviterTests
{
    [Fact]
    public void Invite_RejectsNonNumericId()
    {
        // Should not throw, but should not call Process.Start
        // We verify by asserting the validation method returns false
        Assert.False(SteamUriInviter.IsValidSteam64Id("not-a-number"));
        Assert.False(SteamUriInviter.IsValidSteam64Id("../etc/passwd"));
        Assert.False(SteamUriInviter.IsValidSteam64Id("76561198000000000; cmd.exe"));
    }

    [Fact]
    public void Invite_AcceptsValidSteam64Id()
    {
        Assert.True(SteamUriInviter.IsValidSteam64Id("76561198000000000"));
        Assert.True(SteamUriInviter.IsValidSteam64Id("76561199088685507"));
    }
}
