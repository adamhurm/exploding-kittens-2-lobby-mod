# EKLobbyMod Security Remediation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remediate all security findings from the 2026-05-08 security audit before shipping invite-discovery features that introduce a stored secret and outbound HTTP calls.

**Architecture:** Findings are addressed in-place — no new subsystems. The most impactful change is splitting `DiscordBotSecret` out of `config.json` into a separate secrets file with restricted Windows ACLs. All other changes are targeted, single-method fixes.

**Tech Stack:** C# / .NET 6 (EKLobbyMod), .NET 8 (EKLobbyTray), BepInEx 6 IL2CPP, System.Security.AccessControl (Windows ACLs), System.Net.Http (HTTPS enforcement).

---

## Finding Summary

| Severity | Count |
|----------|-------|
| CRITICAL | 2 |
| HIGH | 4 |
| MEDIUM | 5 |
| LOW | 4 |
| INFO | 2 |
| **Total** | **17** |

---

## CRITICAL Findings

---

### Task C-1: Move DiscordBotSecret out of config.json into a secrets file with restricted ACLs

**Severity:** CRITICAL
**Effort:** M

**Finding:** `DiscordBotSecret` (planned in `ConfigModel.cs` / `config.json`) is a shared secret that grants the ability to DM any member of the configured Discord guild. Storing it in the same plaintext JSON file as the non-sensitive lobby config means any process or user that can read `%AppData%\EKLobbyMod\config.json` obtains the secret. The current `config.json` has no ACL restrictions (see MEDIUM-1).

**Files:**
- Modify: `src/EKLobbyShared/ConfigModel.cs`
- Modify: `src/EKLobbyShared/ConfigStore.cs`
- Create: `src/EKLobbyShared/SecretsStore.cs`
- Create: `src/EKLobbyShared/SecretsModel.cs`

- [ ] **Step 1: Write a failing test for SecretsStore.Load() returning an empty model when no file exists**

Add to your test project (e.g. `tests/EKLobbyShared.Tests/SecretsStoreTests.cs`):

```csharp
[Fact]
public void Load_ReturnsEmpty_WhenFileAbsent()
{
    SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    var s = SecretsStore.Load();
    Assert.Equal("", s.DiscordBotSecret);
}
```

Run: `dotnet test --filter SecretsStoreTests`
Expected: compilation error — `SecretsStore` does not exist yet.

- [ ] **Step 2: Create SecretsModel.cs**

```csharp
// src/EKLobbyShared/SecretsModel.cs
namespace EKLobbyShared;

public class LobbySecrets
{
    public string DiscordBotSecret { get; set; } = "";
}
```

- [ ] **Step 3: Create SecretsStore.cs**

```csharp
// src/EKLobbyShared/SecretsStore.cs
using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EKLobbyShared;

public static class SecretsStore
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "EKLobbyMod", "secrets.json");

    public static string? OverridePath { get; set; }
    private static string ResolvedPath => OverridePath ?? DefaultPath;

    public static LobbySecrets Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new LobbySecrets();
        return JsonSerializer.Deserialize<LobbySecrets>(File.ReadAllText(path), JsonOpts)
               ?? new LobbySecrets();
    }

    public static void Save(LobbySecrets secrets)
    {
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(secrets, JsonOpts));
        RestrictToCurrentUser(path);
    }

    /// <summary>
    /// Removes inherited ACEs and grants Read+Write only to the current user.
    /// No-ops on non-Windows platforms (BepInEx mod only runs on Windows).
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        // Remove all existing rules
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(NTAccount)))
            acl.RemoveAccessRule(rule);
        // Grant current user only
        var me = WindowsIdentity.GetCurrent().Name;
        acl.AddAccessRule(new FileSystemAccessRule(
            me,
            FileSystemRights.Read | FileSystemRights.Write,
            AccessControlType.Allow));
        fi.SetAccessControl(acl);
    }
}
```

- [ ] **Step 4: Run the test — it should pass now**

Run: `dotnet test --filter SecretsStoreTests`
Expected: PASS

- [ ] **Step 5: Write a test that DiscordBotSecret round-trips through SecretsStore**

```csharp
[Fact]
public void Save_Load_RoundTrips_DiscordBotSecret()
{
    SecretsStore.OverridePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    SecretsStore.Save(new LobbySecrets { DiscordBotSecret = "mysecret" });
    var loaded = SecretsStore.Load();
    Assert.Equal("mysecret", loaded.DiscordBotSecret);
}
```

Run: `dotnet test --filter SecretsStoreTests`
Expected: PASS

- [ ] **Step 6: Remove DiscordBotSecret from LobbyConfig in ConfigModel.cs**

Open `src/EKLobbyShared/ConfigModel.cs`. The planned (not yet implemented) field is:
```csharp
public string DiscordBotSecret { get; set; } = "";
```
Do NOT add this field to `LobbyConfig`. If it was already added, remove it now. The model stays as:

```csharp
public class LobbyConfig
{
    public string LobbyRoomName { get; set; } = "";
    public List<FriendEntry> Friends { get; set; } = new();
    public bool AutoLaunchTray { get; set; } = false;
}
```

- [ ] **Step 7: Update DiscordInviteClient (planned file) to load secret from SecretsStore**

When implementing `src/EKLobbyMod/DiscordInviteClient.cs` (per the invite-discovery plan), load the secret like this:

```csharp
var secret = SecretsStore.Load().DiscordBotSecret;
if (string.IsNullOrEmpty(secret))
    throw new InvalidOperationException("DiscordBotSecret not configured in secrets.json");
request.Headers.Add("X-EK-Secret", secret);
```

Never read `DiscordBotSecret` from `LobbyConfig`.

- [ ] **Step 8: Commit**

```
git add src/EKLobbyShared/SecretsModel.cs src/EKLobbyShared/SecretsStore.cs src/EKLobbyShared/ConfigModel.cs
git commit -m "security: move DiscordBotSecret to restricted secrets.json (CRITICAL-1)"
```

**Acceptance criteria:**
- `config.json` contains no `DiscordBotSecret` field.
- `secrets.json` is created with owner-only ACL on first save.
- `DiscordInviteClient` reads the secret from `SecretsStore`, not `ConfigStore`.
- Both `SecretsStoreTests` pass.

---

### Task C-2: Enforce HTTPS for Discord bot HTTP calls

**Severity:** CRITICAL
**Effort:** S

**Finding:** The Discord invite feature (planned in `DiscordInviteClient`) POSTs to `https://bot.bring-us.com/ek-invite` with the `X-EK-Secret` header. If the URL is ever misconfigured to `http://`, the secret and room code travel in cleartext. Additionally, the current design does not explicitly disable HTTP redirects that could downgrade the connection.

**Files:**
- Create/Modify: `src/EKLobbyMod/DiscordInviteClient.cs`

- [ ] **Step 1: Write a test that DiscordInviteClient rejects http:// URLs**

```csharp
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
```

Run: `dotnet test --filter DiscordInviteClientTests`
Expected: compilation error — `DiscordInviteClient` does not exist yet.

- [ ] **Step 2: Implement ValidateBotUrl and configure HttpClient in DiscordInviteClient**

```csharp
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
            Plugin.Log.LogWarning($"Discord invite HTTP error: {ex.Message}");
            return (false, "Discord invite failed — share the link instead");
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter DiscordInviteClientTests`
Expected: PASS

- [ ] **Step 4: Commit**

```
git add src/EKLobbyMod/DiscordInviteClient.cs
git commit -m "security: enforce https-only for Discord bot calls, disable auto-redirect (CRITICAL-2)"
```

**Acceptance criteria:**
- `DiscordInviteClient.ValidateBotUrl` throws `ArgumentException` for any non-`https://` URL.
- `HttpClientHandler.AllowAutoRedirect = false` prevents silent HTTP downgrade.
- Both tests pass.

---

## HIGH Findings

---

### Task H-1: Restrict config.json to current user with Windows ACLs

**Severity:** HIGH
**Effort:** S

**Finding:** `ConfigStore.Save()` calls `File.WriteAllText()` with no ACL configuration. On a multi-user Windows machine the file inherits the directory's ACL, which typically grants read access to all local users. `config.json` contains friends' Steam64 IDs and display names.

**Files:**
- Modify: `src/EKLobbyShared/ConfigStore.cs`

- [ ] **Step 1: Write a test that config.json is created with restricted ACL**

```csharp
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
```

Run: `dotnet test --filter ConfigStoreTests`
Expected: FAIL (file has inherited ACEs).

- [ ] **Step 2: Add RestrictToCurrentUser helper to ConfigStore.cs**

Add the following private method and call it inside `Save()`:

```csharp
public static void Save(LobbyConfig config)
{
    var path = ResolvedPath;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOpts));
    RestrictToCurrentUser(path);   // ← add this call
}

private static void RestrictToCurrentUser(string path)
{
    if (!OperatingSystem.IsWindows()) return;
    var fi = new FileInfo(path);
    var acl = fi.GetAccessControl();
    acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
    foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(NTAccount)))
        acl.RemoveAccessRule(rule);
    var me = WindowsIdentity.GetCurrent().Name;
    acl.AddAccessRule(new FileSystemAccessRule(
        me,
        FileSystemRights.Read | FileSystemRights.Write,
        AccessControlType.Allow));
    fi.SetAccessControl(acl);
}
```

Add required usings at the top of `ConfigStore.cs`:
```csharp
using System.Security.AccessControl;
using System.Security.Principal;
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter ConfigStoreTests`
Expected: PASS

- [ ] **Step 4: Commit**

```
git add src/EKLobbyShared/ConfigStore.cs
git commit -m "security: restrict config.json ACL to current user (HIGH-1)"
```

**Acceptance criteria:**
- After `ConfigStore.Save()`, the file has no inherited ACEs and grants access only to the current Windows user.
- Existing `ConfigStore` unit tests still pass.

---

### Task H-2: Validate Steam64ID format before building steam:// URI

**Severity:** HIGH
**Effort:** S

**Finding:** `SteamUriInviter.Invite(string steam64Id)` builds `steam://friends/invite/{steam64Id}` and passes it to `Process.Start` via `UseShellExecute = true`. A malformed or injected `steam64Id` (e.g. `"../../../windows/system32/cmd.exe"` or a crafted string that escapes the URI scheme) is passed directly to the shell. All `steam64Id` values ultimately come from `config.json`; a tampered config file or a future code path that accepts unvalidated input could trigger this.

**Files:**
- Modify: `src/EKLobbyTray/SteamUriInviter.cs`

- [ ] **Step 1: Write failing tests**

```csharp
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
```

Run: `dotnet test --filter SteamUriInviterTests`
Expected: compilation error — `IsValidSteam64Id` does not exist.

- [ ] **Step 2: Add validation to SteamUriInviter.cs**

```csharp
// src/EKLobbyTray/SteamUriInviter.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using EKLobbyShared;

namespace EKLobbyTray;

public static class SteamUriInviter
{
    /// <summary>
    /// Returns true only if the string is a valid decimal Steam64 ID
    /// (17-digit number in the documented SteamID64 range).
    /// </summary>
    public static bool IsValidSteam64Id(string steam64Id)
    {
        if (string.IsNullOrWhiteSpace(steam64Id)) return false;
        if (!ulong.TryParse(steam64Id, out var val)) return false;
        // Steam64 IDs are in the range [76561193972207616, 76561202255233023]
        // A simple sanity check: must start with 7656119 (the Steam universe/type prefix)
        return steam64Id.Length == 17 && steam64Id.StartsWith("7656119");
    }

    public static void InviteAll(IEnumerable<FriendEntry> friends)
    {
        foreach (var friend in friends)
            Invite(friend.Steam64Id);
    }

    public static void Invite(string steam64Id)
    {
        if (!IsValidSteam64Id(steam64Id))
        {
            // Log and skip; do not pass to shell
            System.Diagnostics.Debug.WriteLine($"[SteamUriInviter] Skipping invalid Steam64 ID: {steam64Id}");
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://friends/invite/{steam64Id}",
            UseShellExecute = true
        });
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter SteamUriInviterTests`
Expected: PASS

- [ ] **Step 4: Commit**

```
git add src/EKLobbyTray/SteamUriInviter.cs
git commit -m "security: validate Steam64 ID before building steam:// URI (HIGH-2)"
```

**Acceptance criteria:**
- `IsValidSteam64Id` returns false for any non-17-digit or non-numeric input.
- `Invite()` returns early without calling `Process.Start` for invalid IDs.
- All four validation tests pass.

---

### Task H-3: Sanitize and validate room name before persisting from Steam connect string

**Severity:** HIGH
**Effort:** S

**Finding:** `LobbyManager.JoinSpecificRoom(string roomName)` calls `UpdateRoomName(roomName)` which immediately persists any value to `config.json`. The `roomName` value comes from `GameRichPresenceJoinRequested_t.m_rgchConnect` (a Steam-provided string) and, in the planned cold-launch feature, from `Environment.GetCommandLineArgs()`. An attacker who can control the Steam rich-presence join string (e.g. via a crafted Steam invite) could poison the persisted room name.

**Files:**
- Modify: `src/EKLobbyMod/LobbyManager.cs`
- Modify: `src/EKLobbyShared/ConfigStore.cs` (or a shared validator)

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void IsValidRoomName_AcceptsNormalCode()
{
    Assert.True(LobbyManager.IsValidRoomName("EK-A3F9C12B"));
    Assert.True(LobbyManager.IsValidRoomName("EK-00000000"));
}

[Fact]
public void IsValidRoomName_RejectsOversizedInput()
{
    Assert.False(LobbyManager.IsValidRoomName(new string('A', 65)));
}

[Fact]
public void IsValidRoomName_RejectsPathTraversal()
{
    Assert.False(LobbyManager.IsValidRoomName("../../../evil"));
    Assert.False(LobbyManager.IsValidRoomName("EK-A3F9C12B\0"));
}

[Fact]
public void IsValidRoomName_RejectsNull()
{
    Assert.False(LobbyManager.IsValidRoomName(null));
    Assert.False(LobbyManager.IsValidRoomName(""));
}
```

Run: `dotnet test --filter LobbyManagerTests`
Expected: compilation error — `IsValidRoomName` does not exist.

- [ ] **Step 2: Add IsValidRoomName and guard JoinSpecificRoom**

In `src/EKLobbyMod/LobbyManager.cs`, add:

```csharp
/// <summary>
/// A valid room name is 1–64 printable ASCII characters with no control characters,
/// null bytes, or path-separator characters. This matches the Photon room name limits.
/// </summary>
public static bool IsValidRoomName(string? name)
{
    if (string.IsNullOrEmpty(name)) return false;
    if (name.Length > 64) return false;
    foreach (char c in name)
    {
        if (c < 0x20 || c > 0x7E) return false;  // non-printable or non-ASCII
        if (c == '/' || c == '\\' || c == '.') return false;  // path chars
    }
    return true;
}
```

Update `JoinSpecificRoom`:

```csharp
public void JoinSpecificRoom(string roomName)
{
    if (!IsValidRoomName(roomName))
    {
        Plugin.Log.LogWarning($"JoinSpecificRoom: rejected invalid room name (length={roomName?.Length})");
        return;
    }
    UpdateRoomName(roomName);
    JoinOrCreateHomeLobby();
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter LobbyManagerTests`
Expected: PASS

- [ ] **Step 4: Commit**

```
git add src/EKLobbyMod/LobbyManager.cs
git commit -m "security: validate room name before persisting from Steam connect string (HIGH-3)"
```

**Acceptance criteria:**
- `IsValidRoomName` returns false for null, empty, >64-char, non-printable, or path-separator-containing strings.
- `JoinSpecificRoom` returns early (with a log warning) for invalid names.
- All four tests pass.

---

### Task H-4: Harden AutoLaunchHelper.Enable() against NullReferenceException and path verification

**Severity:** HIGH
**Effort:** S

**Finding:** `AutoLaunchHelper.Enable()` accesses `Process.GetCurrentProcess().MainModule!.FileName` with a null-forgiving `!` operator. `MainModule` can return `null` on some Windows configurations (e.g. when the process has restricted permissions). A `NullReferenceException` here would propagate uncaught. Additionally, the registered path is not verified to be within a known deployment directory, which means a compromised working directory could register an unexpected binary to run at startup.

**Files:**
- Modify: `src/EKLobbyTray/AutoLaunchHelper.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void Enable_DoesNotThrow_WhenCalledNormally()
{
    // Integration test: just call Enable and Disable in sequence.
    // This verifies no NullReferenceException and that the registry key is cleaned up.
    AutoLaunchHelper.Enable();
    Assert.True(AutoLaunchHelper.IsEnabled());
    AutoLaunchHelper.Disable();
    Assert.False(AutoLaunchHelper.IsEnabled());
}
```

Run: `dotnet test --filter AutoLaunchHelperTests`
Expected: may throw `NullReferenceException` on some machines, or pass on others. Establishing the baseline.

- [ ] **Step 2: Harden AutoLaunchHelper.Enable()**

```csharp
// src/EKLobbyTray/AutoLaunchHelper.cs
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace EKLobbyTray;

public static class AutoLaunchHelper
{
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EKLobbyTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey);
        return key?.GetValue(ValueName) != null;
    }

    public static void Enable()
    {
        var exePath = GetCurrentExePath();
        if (exePath == null)
        {
            throw new InvalidOperationException(
                "Cannot determine current executable path. " +
                "EKLobbyTray must be run as a self-contained .exe to use auto-launch.");
        }

        // Verify the path resolves to an actual .exe (not a temp dir, dotnet CLI, etc.)
        if (!File.Exists(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Auto-launch path '{exePath}' does not point to a .exe file. " +
                "Publish EKLobbyTray as a self-contained executable before enabling auto-launch.");
        }

        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)
            ?? throw new InvalidOperationException($"Cannot open registry key: {RegKey}");
        key.SetValue(ValueName, exePath);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Returns the current process executable path, or null if unavailable.</summary>
    internal static string? GetCurrentExePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter AutoLaunchHelperTests`
Expected: PASS (Enable/Disable round-trip works; no NullReferenceException).

- [ ] **Step 4: Commit**

```
git add src/EKLobbyTray/AutoLaunchHelper.cs
git commit -m "security: harden AutoLaunchHelper against null MainModule and bad paths (HIGH-4)"
```

**Acceptance criteria:**
- `Enable()` throws a descriptive `InvalidOperationException` rather than `NullReferenceException` when `MainModule` is null.
- `Enable()` validates the resolved path ends in `.exe` and the file exists before writing to the registry.
- The Enable/Disable round-trip test passes.

---

## MEDIUM Findings

---

### Task M-1: Add integrity check (HMAC) to config.json load path

**Severity:** MEDIUM
**Effort:** M

**Finding:** `config.json` is loaded without any tamper detection. A local attacker (or malware) that can write to `%AppData%\EKLobbyMod\config.json` can set `LobbyRoomName` to a controlled room, causing the user to unknowingly join an attacker-controlled Photon room on next load. The `IsValidRoomName` guard added in H-3 reduces but does not eliminate this risk (an attacker can supply a valid-looking name).

**Approach:** Compute an HMAC-SHA256 of the serialized config using a machine-local key (derived from the current Windows username + a fixed app salt). Store the tag as a separate field or sidecar. On load, recompute and compare. If tampered, log a warning and return the default config rather than the tampered one.

**Files:**
- Modify: `src/EKLobbyShared/ConfigStore.cs`

- [ ] **Step 1: Write a failing test**

```csharp
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
```

Run: `dotnet test --filter ConfigStoreTests`
Expected: FAIL (currently loads tampered value).

- [ ] **Step 2: Add HMAC signing to ConfigStore**

Add a sidecar file `config.json.hmac` alongside `config.json`. The HMAC key is derived locally and never stored on disk.

```csharp
// Add to ConfigStore.cs

using System.Security.Cryptography;

private static byte[] DeriveHmacKey()
{
    // Machine-local key: not secret from the current user, but opaque to other users/processes
    var salt = "EKLobbyMod-v1-integrity";
    var identity = Environment.UserName + Environment.MachineName;
    return SHA256.HashData(Encoding.UTF8.GetBytes(identity + salt));
}

private static string HmacPath(string configPath) => configPath + ".hmac";

private static string ComputeHmac(string json)
{
    var key = DeriveHmacKey();
    var tag = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(json));
    return Convert.ToBase64String(tag);
}

public static void Save(LobbyConfig config)
{
    var path = ResolvedPath;
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var json = JsonSerializer.Serialize(config, JsonOpts);
    File.WriteAllText(path, json);
    File.WriteAllText(HmacPath(path), ComputeHmac(json));
    RestrictToCurrentUser(path);
    RestrictToCurrentUser(HmacPath(path));
}

public static LobbyConfig Load()
{
    var path = ResolvedPath;
    if (!File.Exists(path)) return new LobbyConfig();

    var json = File.ReadAllText(path);
    var hmacPath = HmacPath(path);

    if (File.Exists(hmacPath))
    {
        var stored = File.ReadAllText(hmacPath).Trim();
        var expected = ComputeHmac(json);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(stored),
                Convert.FromBase64String(expected)))
        {
            // Log and return safe default rather than potentially malicious config
            System.Diagnostics.Debug.WriteLine("[ConfigStore] HMAC mismatch — config.json may be tampered. Returning defaults.");
            return new LobbyConfig();
        }
    }

    return JsonSerializer.Deserialize<LobbyConfig>(json, JsonOpts) ?? new LobbyConfig();
}
```

Add `using System.Text;` to the using block of `ConfigStore.cs` if not present.

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter ConfigStoreTests`
Expected: PASS (tamper test now passes; round-trip tests still pass).

- [ ] **Step 4: Commit**

```
git add src/EKLobbyShared/ConfigStore.cs
git commit -m "security: add HMAC integrity check to config.json (MEDIUM-2)"
```

**Acceptance criteria:**
- A file tampered after `Save()` causes `Load()` to return `new LobbyConfig()` (defaults).
- An untouched file loads normally.
- The HMAC sidecar file is created with owner-only ACL.

---

### Task M-2: Redact connect string from BepInEx log

**Severity:** MEDIUM
**Effort:** S

**Finding:** `Plugin.cs` line 47 logs `$"Steam join requested — connect: {connect}"` at `LogInfo` level. BepInEx writes `LogInfo` to `BepInEx/LogOutput.log` on disk. The connect string is the raw Photon room name. While not a Steam credential, it is PII (partial Steam ID encoded in it per MEDIUM-5) and enables any process that can read the log to know the user's room code.

**Files:**
- Modify: `src/EKLobbyMod/Plugin.cs`

- [ ] **Step 1: Change log level from LogInfo to LogDebug and truncate the value**

Open `src/EKLobbyMod/Plugin.cs`, line 47. Change:

```csharp
// BEFORE
Log.LogInfo($"Steam join requested — connect: {connect}");
```

to:

```csharp
// AFTER — debug level only; truncate to confirm presence without exposing full value
Log.LogDebug($"Steam join requested — connect string present ({connect?.Length ?? 0} chars)");
```

- [ ] **Step 2: Verify no other LogInfo calls emit raw room codes or Steam IDs**

Search `src/` for any `LogInfo` containing `connect`, `RoomName`, `Steam64`, or `UserId`:

Run: `grep -rn "LogInfo.*connect\|LogInfo.*RoomName\|LogInfo.*Steam64\|LogInfo.*UserId" src/`

For each result, demote to `LogDebug` or redact the sensitive portion. The `LobbyManager.cs` log lines (e.g. `"Room created: {name}"`, `"Joined room: {roomName}"`) are acceptable at `LogInfo` because the room name is already known to the user and the game — demote only the Steam connect string passed from Steam's RPC callback.

- [ ] **Step 3: Commit**

```
git add src/EKLobbyMod/Plugin.cs
git commit -m "security: demote Steam connect-string log to LogDebug, truncate value (MEDIUM-3)"
```

**Acceptance criteria:**
- `OnGameJoinRequested` no longer logs the raw `connect` string at `LogInfo`.
- `LogOutput.log` does not contain the room code from Steam join requests during normal operation.

---

### Task M-3: Add HTTPS enforcement documentation and startup assertion for Discord bot URL

**Severity:** MEDIUM
**Effort:** S

**Finding:** Even though `DiscordInviteClient` (Task C-2) enforces HTTPS at call time, the bot URL `https://bot.bring-us.com/ek-invite` is a constant that future maintainers might change to an `http://` URL in config without realizing the secret would then be exposed. A startup check makes the constraint explicit.

**Files:**
- Modify: `src/EKLobbyMod/DiscordInviteClient.cs`

- [ ] **Step 1: Add BotUrl as a validated constant**

In `DiscordInviteClient.cs`, add:

```csharp
// The bot URL is a compile-time constant. Changing it to http:// will throw at startup.
public const string BotUrl = "https://bot.bring-us.com/ek-invite";

// Static constructor validates the constant at class initialization time (caught in tests)
static DiscordInviteClient()
{
    ValidateBotUrl(BotUrl);
}
```

This means if a developer accidentally changes `BotUrl` to `http://`, the `ArgumentException` fires the first time the class is touched — caught by any test that references the type.

- [ ] **Step 2: Write a test that the static initializer does not throw with the current constant**

```csharp
[Fact]
public void BotUrl_IsHttps()
{
    // Accessing the constant exercises the static constructor
    Assert.StartsWith("https://", DiscordInviteClient.BotUrl);
}
```

Run: `dotnet test --filter DiscordInviteClientTests`
Expected: PASS

- [ ] **Step 3: Commit**

```
git add src/EKLobbyMod/DiscordInviteClient.cs
git commit -m "security: validate BotUrl constant is https:// at static init (MEDIUM-4)"
```

**Acceptance criteria:**
- `BotUrl` constant is `https://`.
- Static constructor calls `ValidateBotUrl` — changing to `http://` fails at initialization.
- Test passes.

---

### Task M-4: Decouple room code from Steam ID — use a random suffix

**Severity:** MEDIUM
**Effort:** S

**Finding:** `ConfigStore.GetOrCreateRoomName()` derives the room code as `EK-` + the last 8 hex digits of the user's Steam64 ID. The shareable URL `https://ek.bring-us.com/?code=EK-A3F9C12B` exposes a partial Steam ID. Anyone who receives this URL can reverse the last 4 bytes of the Steam64 ID, potentially narrowing a brute-force search of the ~4 billion possible IDs in that range.

**Fix:** Generate the 8-character suffix from `System.Security.Cryptography.RandomNumberGenerator` instead of deriving it from the Steam ID. Existing users with already-generated room codes keep their code (the function only generates if `LobbyRoomName` is empty).

**Files:**
- Modify: `src/EKLobbyShared/ConfigStore.cs`

- [ ] **Step 1: Write a failing test**

```csharp
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
```

Run: `dotnet test --filter ConfigStoreTests`
Expected: FAIL (current code produces `EK-02FAF080` for that Steam ID).

- [ ] **Step 2: Replace Steam-ID derivation with random generation**

In `ConfigStore.cs`, replace `GetOrCreateRoomName`:

```csharp
using System.Security.Cryptography;

public static string GetOrCreateRoomName(ulong steam64Id)
{
    var config = Load();
    if (!string.IsNullOrEmpty(config.LobbyRoomName))
        return config.LobbyRoomName;

    // Generate a cryptographically random 8-char hex suffix, independent of Steam ID
    var bytes = RandomNumberGenerator.GetBytes(4);
    var suffix = Convert.ToHexString(bytes); // uppercase 8-char hex
    config.LobbyRoomName = $"EK-{suffix}";
    Save(config);
    return config.LobbyRoomName;
}
```

Note: `steam64Id` parameter is kept in the signature for API compatibility but is no longer used in room name generation. It can be removed in a follow-up if callers are updated.

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter ConfigStoreTests`
Expected: PASS (new code generates a random suffix; the test verifies it doesn't match the Steam-ID-derived value).

- [ ] **Step 4: Commit**

```
git add src/EKLobbyShared/ConfigStore.cs
git commit -m "security: generate room code from CSPRNG, not Steam ID suffix (MEDIUM-5)"
```

**Acceptance criteria:**
- `GetOrCreateRoomName` generates a random 8-char hex code unrelated to the Steam ID.
- Existing non-empty `LobbyRoomName` values are preserved (no re-generation on subsequent calls).
- Test passes.

---

## LOW Findings

---

### Task L-1: Validate +connect arg length and character set before passing to JoinSpecificRoom

**Severity:** LOW
**Effort:** S

**Finding:** The planned cold-launch feature in `Plugin.cs` reads `Environment.GetCommandLineArgs()` and passes the `+connect` value to `LobbyManager.JoinSpecificRoom()` without length or character validation. Task H-3 adds a `IsValidRoomName` guard inside `JoinSpecificRoom`, but the cold-launch code path should also log a specific warning when the arg is rejected, making debugging easier.

**Files:**
- Modify: `src/EKLobbyMod/Plugin.cs`

- [ ] **Step 1: Implement cold-launch arg reading with explicit validation logging**

In `Plugin.Load()`, after Harmony patch registration, add:

```csharp
// Cold-launch: Steam may pass the room code as a command-line arg
var args = System.Environment.GetCommandLineArgs();
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "+connect")
    {
        var connectArg = args[i + 1];
        if (LobbyManager.IsValidRoomName(connectArg))
        {
            Log.LogInfo($"Cold launch: valid +connect arg received ({connectArg.Length} chars)");
            _pendingConnectArg = connectArg;
        }
        else
        {
            Log.LogWarning($"Cold launch: +connect arg failed validation (length={connectArg?.Length ?? 0}) — ignored");
        }
        break;
    }
}
```

Add the field to `Plugin`:
```csharp
private string? _pendingConnectArg;
```

In `LobbyManager.Initialize()` (called from `PhotonClientFinder` after the controller is captured), check for and apply the pending arg:

```csharp
// In LobbyManager.Initialize, after setting _localSteamId:
if (Plugin.Instance?._pendingConnectArg is string pending)
{
    Instance.JoinSpecificRoom(pending);
    Plugin.Instance._pendingConnectArg = null;
}
```

Note: expose `Plugin.Instance` as `internal static Plugin? Instance` set in `Load()` (`Instance = this`).

- [ ] **Step 2: Write a test for the validation path**

```csharp
[Fact]
public void IsValidRoomName_RejectsOversizeArg()
{
    Assert.False(LobbyManager.IsValidRoomName(new string('A', 65)));
}
```

This test is covered by H-3 — confirm it still passes.

Run: `dotnet test --filter LobbyManagerTests`
Expected: PASS

- [ ] **Step 3: Commit**

```
git add src/EKLobbyMod/Plugin.cs src/EKLobbyMod/LobbyManager.cs
git commit -m "feat: cold-launch +connect arg with validation guard (LOW-2)"
```

**Acceptance criteria:**
- Invalid `+connect` args (>64 chars, non-printable chars) are logged and ignored.
- Valid args are passed to `JoinSpecificRoom` which applies the `IsValidRoomName` guard (Task H-3).

---

### Task L-2: Cap DisplayName length in TrayApp menu items

**Severity:** LOW
**Effort:** S

**Finding:** `TrayApp.BuildMenu()` creates `ToolStripMenuItem` labels directly from `friend.DisplayName` with no length limit. Steam display names can be up to 32 characters, but `config.json` is user-writable, and an edited or tampered entry with a very long display name could cause display issues in the Windows tray context menu.

**Files:**
- Modify: `src/EKLobbyTray/TrayApp.cs`

- [ ] **Step 1: Write a test**

```csharp
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
```

Run: `dotnet test --filter TrayAppTests`
Expected: FAIL (no truncation currently).

- [ ] **Step 2: Add truncation helper and apply in BuildMenu**

Add a private helper to `TrayApp.cs`:

```csharp
private static string TruncateDisplayName(string name, int maxLen = 35)
{
    if (string.IsNullOrEmpty(name)) return "(no name)";
    return name.Length <= maxLen ? name : name[..maxLen] + "...";
}
```

In `BuildMenu()`, change the friend item label:

```csharp
// BEFORE
var friendItem = new ToolStripMenuItem(f.DisplayName);
// AFTER
var friendItem = new ToolStripMenuItem(TruncateDisplayName(f.DisplayName));
```

In `BuildMenuItems()` (the testable helper):

```csharp
// BEFORE
foreach (var f in config.Friends) items.Add(f.DisplayName);
// AFTER
foreach (var f in config.Friends) items.Add(TruncateDisplayName(f.DisplayName));
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter TrayAppTests`
Expected: PASS

- [ ] **Step 4: Commit**

```
git add src/EKLobbyTray/TrayApp.cs
git commit -m "security: truncate DisplayName to 35 chars in tray menu items (LOW-4)"
```

**Acceptance criteria:**
- Display names longer than 35 characters are shown as `<first 35 chars>...` in the tray menu.
- `BuildMenuItems` test passes.

---

### Task L-3: Document clipboard exposure in user-facing help text

**Severity:** LOW
**Effort:** S

**Finding:** Both `OverlayPanel.CopyCodeToClipboard()` and `TrayApp.BuildMenu()` write the room code (and, when implemented, the share link) to the system clipboard. Any application running under the same Windows session can read the clipboard. This is inherent to the clipboard mechanism and cannot be fully mitigated in-app, but users should be aware.

**Action:** This is documentation and UX hardening only — no code change is required. The risk is LOW and inherent to clipboard use.

- [ ] **Step 1: Add a comment at each clipboard write site**

In `src/EKLobbyMod/OverlayPanel.cs`, line ~371:

```csharp
// NOTE: The room code is written to the system clipboard. Any application in this
// Windows session can read it. This is intentional — the code is non-secret by design
// (it is shared to invite friends). Do not write Steam credentials or the Discord secret here.
private void CopyCodeToClipboard() =>
    GUIUtility.systemCopyBuffer = _manager.Config.LobbyRoomName;
```

In `src/EKLobbyTray/TrayApp.cs`, line ~58:

```csharp
// NOTE: Room code is non-secret (shared with friends). Clipboard exposure is acceptable.
codeItem.Click += (_, _) => Clipboard.SetText(_config.LobbyRoomName);
```

- [ ] **Step 2: Ensure DiscordBotSecret is never written to the clipboard**

Search for any future code path that might copy `DiscordBotSecret` to the clipboard. Run:

`grep -rn "DiscordBotSecret\|systemCopyBuffer\|Clipboard.Set" src/`

Confirm no code paths combine the two. This is a verification step — no change expected.

- [ ] **Step 3: Commit**

```
git add src/EKLobbyMod/OverlayPanel.cs src/EKLobbyTray/TrayApp.cs
git commit -m "docs: annotate clipboard writes — room code is intentionally non-secret (LOW-3)"
```

**Acceptance criteria:**
- Each clipboard write site has a comment confirming the value written is non-secret by design.
- No code path writes `DiscordBotSecret` to the clipboard.

---

## INFO Findings (No Action Required)

---

### INFO-1: Photon version property visibility

`LobbyManager` (planned) writes `Plugin.PluginVersion` to `PhotonNetwork.LocalPlayer.CustomProperties["ekmod_ver"]`. This string is visible to all clients in the Photon room. Per audit scope, Photon confidentiality is informational only. No action required — the version string is intentionally public (it's the mechanism for the drift indicator feature).

---

### INFO-2: Photon room name visibility

`LobbyRoomName` is visible to all Photon participants in the room by definition (it is the room identifier). Per audit scope, Photon confidentiality is informational only. No action required.

---

## Execution Order

Implement in this order to avoid rework:

1. **C-1** (secrets file) — must be done before invite-discovery feature ships
2. **C-2** (HTTPS enforcement) — must be done before `DiscordInviteClient` is used
3. **H-3** (room name validation) — must be done before cold-launch (+connect) is implemented
4. **H-2** (Steam64 URI validation) — can be done anytime before invite-discovery ships
5. **H-1** (config ACL) — can be done anytime
6. **H-4** (AutoLaunchHelper) — can be done anytime
7. **M-1** (config HMAC) — after H-1 (ACL) and H-3 (room name validation) are in place
8. **M-2** (log redaction) — quick win, do early
9. **M-3** (HTTPS constant check) — after C-2
10. **M-4** (random room code) — before any shareable links are shipped
11. **L-1** (cold-launch validation) — part of invite-discovery implementation
12. **L-2** (display name truncation) — quick win, do anytime
13. **L-3** (clipboard documentation) — last, documentation only
