using System;
using EKLobbyMod;
using Xunit;

namespace EKLobbyMod.Tests;

public class DiscordInviteClientTests
{
    // --- Task C-2: HTTPS enforcement ---

    [Fact]
    public void BuildRequest_ThrowsOnHttpUrl()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DiscordInviteClient.ValidateBotUrl("http://bot.bring-us.com/ek-invite"));
        Assert.Contains("https", ex.Message);
    }

    [Fact]
    public void BuildRequest_AcceptsHttpsUrl()
    {
        // Should not throw
        DiscordInviteClient.ValidateBotUrl("https://bot.bring-us.com/ek-invite");
    }

    // --- Task M-3: BotUrl constant is https:// and static constructor fires ---

    [Fact]
    public void BotUrl_IsHttps()
    {
        // Accessing the constant exercises the static constructor;
        // if BotUrl were ever changed to http://, this would throw at class init.
        Assert.StartsWith("https://", DiscordInviteClient.BotUrl);
    }
}
