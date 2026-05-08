// src/EKLobbyMod/DiscordInviteClient.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EKLobbyShared;

namespace EKLobbyMod;

public static class DiscordInviteClient
{
    // The bot URL is a compile-time constant. Changing it to http:// will throw at startup.
    public const string BotUrl = "https://bot.bring-us.com/ek-invite";

    // Static constructor validates the constant at class initialization time (caught in tests)
    static DiscordInviteClient()
    {
        ValidateBotUrl(BotUrl);
    }

    // One shared instance; do not allow HTTP redirects that could downgrade to plaintext
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

    public static void ValidateBotUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Bot URL must use https://. Got: {url}", nameof(url));
    }

    public static async Task<(bool ok, string message)> SendInviteAsync(
        string botUrl, string roomCode, string discordUsername)
    {
        ValidateBotUrl(botUrl);
        var secret = SecretsStore.Load().DiscordBotSecret;
        if (string.IsNullOrEmpty(secret))
            return (false, "DiscordBotSecret not set in secrets.json");

        var body = JsonSerializer.Serialize(new { roomCode, discordUsername });
        using var req = new HttpRequestMessage(HttpMethod.Post, botUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-EK-Secret", secret);

        try
        {
            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, $"Bot returned {(int)resp.StatusCode}");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            bool resultOk = root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            string msg = root.TryGetProperty("deliveredTo", out var d) ? $"Invite sent to {d.GetString()}"
                       : root.TryGetProperty("error", out var e) ? e.GetString() ?? "Unknown error"
                       : "Unknown response";
            return (resultOk, msg);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discord invite HTTP error: {ex.Message}");
            return (false, "Discord invite failed — share the link instead");
        }
    }
}
