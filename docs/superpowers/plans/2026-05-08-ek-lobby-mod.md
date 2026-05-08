# EKLobbyMod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A BepInEx IL2CPP plugin for Exploding Kittens 2 that maintains a fixed private lobby code for a saved group of friends, with an in-game overlay panel and a system tray companion app.

**Architecture:** `EKLobbyMod.dll` is a BepInEx 6 IL2CPP plugin that runs inside the game process — it hooks Photon Realtime callbacks via `IMatchmakingCallbacks`, injects a uGUI overlay into the game canvas, and uses the in-process Steamworks.NET to enumerate friends and send invites. `EKLobbyTray.exe` is a .NET 8 WinForms tray app that watches the shared `%AppData%\EKLobbyMod\config.json` and sends Steam invites via `steam://` URIs when the game is not running. Both artifacts share a `EKLobbyShared` class library for config model and file I/O.

**Tech Stack:** .NET 6 (BepInEx plugin), .NET 8 WinForms (tray app), netstandard2.0 (shared lib), BepInEx 6 IL2CPP, Il2CppInterop, HarmonyX, Photon Realtime (IL2CPP stubs via Il2CppDumper), Steamworks.NET (IL2CPP stubs), Unity uGUI, xUnit + Moq (tests for shared lib and tray app only — plugin logic requires manual in-game testing)

> **Note:** Tasks 1–11 build the in-game plugin. Tasks 12–14 build the tray app and can be implemented in parallel after Task 2.

---

## File Map

```
exploding_kittens_mod/
├── Directory.Build.props              # defines $(GameDir) and $(RepoRoot)
├── EKLobbyMod.sln
├── src/
│   ├── EKLobbyShared/
│   │   ├── EKLobbyShared.csproj       # netstandard2.0
│   │   ├── ConfigModel.cs             # LobbyConfig, FriendEntry POCOs
│   │   └── ConfigStore.cs             # Load/Save/GetOrCreateRoomName
│   ├── EKLobbyMod/
│   │   ├── EKLobbyMod.csproj          # net6.0, references BepInEx + DummyDlls
│   │   ├── Plugin.cs                  # BepInEx entry point, SceneManager hook
│   │   ├── PhotonClientFinder.cs      # Locate LoadBalancingClient instance at runtime
│   │   ├── LobbyManager.cs            # IMatchmakingCallbacks, JoinOrCreateHomeLobby
│   │   ├── SteamInviter.cs            # Wrap in-process SteamFriends API
│   │   ├── OverlayPanel.cs            # Inject uGUI panel, minimised/expanded states
│   │   └── FriendPickerPopup.cs       # [+ Add] popup with Steam friend list
│   └── EKLobbyTray/
│       ├── EKLobbyTray.csproj         # net8.0-windows
│       ├── Program.cs                 # Entry point (no window, start tray)
│       ├── TrayApp.cs                 # NotifyIcon, context menu, FileSystemWatcher
│       └── SteamUriInviter.cs         # Open steam:// URIs via Process.Start
├── tests/
│   ├── EKLobbyShared.Tests/
│   │   ├── EKLobbyShared.Tests.csproj
│   │   └── ConfigStoreTests.cs
│   └── EKLobbyTray.Tests/
│       ├── EKLobbyTray.Tests.csproj
│       └── TrayAppTests.cs
├── tools/
│   └── dump/                          # gitignored — Il2CppDumper output
│       └── DummyDll/                  # generated type stubs
└── install.ps1
```

---

## Task 0: Prerequisites — BepInEx and Il2CppDumper

**Files:** none created yet

- [ ] **Step 1: Download BepInEx 6 IL2CPP**

  Download the latest BepInEx 6 prerelease IL2CPP x64 build from:
  `https://github.com/BepInEx/BepInEx/releases`
  Look for an asset named `BepInEx_win_x64_6.*.zip` (IL2CPP variant).

- [ ] **Step 2: Install BepInEx into the game directory**

  Extract the zip into `D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\`.
  After extraction the folder should contain:
  ```
  BepInEx/
    core/
      BepInEx.Core.dll
      BepInEx.IL2CPP.dll
      Il2CppInterop.Runtime.dll
      ...
    plugins/         ← your mod DLL goes here later
  doorstop_config.ini
  winhttp.dll        ← BepInEx's doorstep proxy
  ```

- [ ] **Step 3: Launch the game once to generate BepInEx interop assemblies**

  Run `ExplodingKittens.exe` through Steam. BepInEx will run its IL2CPP interop
  generation on first launch and create:
  ```
  BepInEx/interop/     ← unhollowed Unity + game assemblies appear here
  BepInEx/logs/BepInEx.log
  ```
  Close the game after the main menu loads.
  Verify `BepInEx/interop/PhotonRealtime.dll` exists.

- [ ] **Step 4: Download Il2CppDumper**

  Download the latest release from `https://github.com/Perfare/Il2CppDumper/releases`.
  Extract it to `C:\Users\adam\Desktop\exploding_kittens_mod\tools\Il2CppDumper\`.

- [ ] **Step 5: Run Il2CppDumper**

  ```powershell
  cd "C:\Users\adam\Desktop\exploding_kittens_mod\tools\Il2CppDumper"
  .\Il2CppDumper.exe `
    "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\GameAssembly.dll" `
    "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\ExplodingKittens_Data\il2cpp_data\Metadata\global-metadata.dat" `
    "C:\Users\adam\Desktop\exploding_kittens_mod\tools\dump"
  ```

  Expected output: `DummyDll/` folder inside `tools/dump/` containing stubs like
  `PhotonRealtime.dll`, `MGS.Platform.dll`, `UnityEngine.CoreModule.dll`, etc.

- [ ] **Step 6: Add tools/dump to .gitignore**

  Create `C:\Users\adam\Desktop\exploding_kittens_mod\.gitignore`:
  ```
  tools/dump/
  **/bin/
  **/obj/
  ```

---

## Task 1: Solution Scaffold

**Files:**
- Create: `Directory.Build.props`
- Create: `EKLobbyMod.sln`
- Create: `src/EKLobbyShared/EKLobbyShared.csproj`
- Create: `src/EKLobbyMod/EKLobbyMod.csproj`
- Create: `src/EKLobbyTray/EKLobbyTray.csproj`
- Create: `tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj`
- Create: `tests/EKLobbyTray.Tests/EKLobbyTray.Tests.csproj`

- [ ] **Step 1: Create Directory.Build.props**

  ```xml
  <!-- Directory.Build.props -->
  <Project>
    <PropertyGroup>
      <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
      <GameDir>D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2</GameDir>
      <BepInExDir>$(GameDir)\BepInEx</BepInExDir>
      <DumpDir>$(RepoRoot)tools\dump\DummyDll</DumpDir>
      <Nullable>enable</Nullable>
      <LangVersion>latest</LangVersion>
      <ImplicitUsings>enable</ImplicitUsings>
    </PropertyGroup>
  </Project>
  ```

- [ ] **Step 2: Create EKLobbyShared.csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>netstandard2.0</TargetFramework>
      <AssemblyName>EKLobbyShared</AssemblyName>
      <RootNamespace>EKLobbyShared</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="System.Text.Json" Version="8.0.0" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 3: Create EKLobbyMod.csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net6.0</TargetFramework>
      <AssemblyName>EKLobbyMod</AssemblyName>
      <RootNamespace>EKLobbyMod</RootNamespace>
      <Nullable>enable</Nullable>
    </PropertyGroup>
    <ItemGroup>
      <!-- BepInEx core DLLs from game install -->
      <Reference Include="BepInEx.Core">
        <HintPath>$(BepInExDir)\core\BepInEx.Core.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="BepInEx.IL2CPP">
        <HintPath>$(BepInExDir)\core\BepInEx.IL2CPP.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="Il2CppInterop.Runtime">
        <HintPath>$(BepInExDir)\core\Il2CppInterop.Runtime.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <!-- Game type stubs from BepInEx interop (generated on first game launch) -->
      <Reference Include="PhotonRealtime">
        <HintPath>$(BepInExDir)\interop\PhotonRealtime.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="MGS.Platform">
        <HintPath>$(BepInExDir)\interop\MGS.Platform.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="UnityEngine.CoreModule">
        <HintPath>$(BepInExDir)\interop\UnityEngine.CoreModule.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="UnityEngine.UI">
        <HintPath>$(BepInExDir)\interop\UnityEngine.UI.dll</HintPath>
        <Private>False</Private>
      </Reference>
      <Reference Include="UnityEngine.TextMeshPro">
        <HintPath>$(BepInExDir)\interop\Unity.TextMeshPro.dll</HintPath>
        <Private>False</Private>
      </Reference>
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\EKLobbyShared\EKLobbyShared.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 4: Create EKLobbyTray.csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0-windows</TargetFramework>
      <OutputType>WinExe</OutputType>
      <UseWindowsForms>true</UseWindowsForms>
      <AssemblyName>EKLobbyTray</AssemblyName>
      <RootNamespace>EKLobbyTray</RootNamespace>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="..\EKLobbyShared\EKLobbyShared.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 5: Create test project EKLobbyShared.Tests.csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0</TargetFramework>
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="xunit" Version="2.9.0" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
        <PrivateAssets>all</PrivateAssets>
      </PackageReference>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\src\EKLobbyShared\EKLobbyShared.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 6: Create EKLobbyTray.Tests.csproj**

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net8.0-windows</TargetFramework>
      <UseWindowsForms>true</UseWindowsForms>
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="xunit" Version="2.9.0" />
      <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
        <PrivateAssets>all</PrivateAssets>
      </PackageReference>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
      <PackageReference Include="Moq" Version="4.20.72" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\src\EKLobbyTray\EKLobbyTray.csproj" />
    </ItemGroup>
  </Project>
  ```

- [ ] **Step 7: Create the solution file**

  ```powershell
  cd "C:\Users\adam\Desktop\exploding_kittens_mod"
  dotnet new sln -n EKLobbyMod
  dotnet sln add src/EKLobbyShared/EKLobbyShared.csproj
  dotnet sln add src/EKLobbyMod/EKLobbyMod.csproj
  dotnet sln add src/EKLobbyTray/EKLobbyTray.csproj
  dotnet sln add tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj
  dotnet sln add tests/EKLobbyTray.Tests/EKLobbyTray.Tests.csproj
  ```

- [ ] **Step 8: Verify shared and tray projects build**

  ```powershell
  dotnet build src/EKLobbyShared/EKLobbyShared.csproj
  dotnet build src/EKLobbyTray/EKLobbyTray.csproj
  ```

  Expected: both succeed (EKLobbyMod will fail until BepInEx interop DLLs exist — that's fine for now).

- [ ] **Step 9: Commit**

  ```powershell
  git init
  git add .gitignore Directory.Build.props EKLobbyMod.sln src/ tests/
  git commit -m "chore: solution scaffold with project files"
  ```

---

## Task 2: Shared Config Model and ConfigStore

**Files:**
- Create: `src/EKLobbyShared/ConfigModel.cs`
- Create: `src/EKLobbyShared/ConfigStore.cs`
- Create: `tests/EKLobbyShared.Tests/ConfigStoreTests.cs`

- [ ] **Step 1: Write failing tests for ConfigStore**

  Create `tests/EKLobbyShared.Tests/ConfigStoreTests.cs`:

  ```csharp
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
          // Steam64 ID 76561198000000001 → hex 011000010000001 → last 8: 00000001 → "EK-00000001"
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
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```powershell
  dotnet test tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj -v normal
  ```

  Expected: build error — `ConfigStore`, `LobbyConfig`, `FriendEntry` not defined yet.

- [ ] **Step 3: Create ConfigModel.cs**

  ```csharp
  // src/EKLobbyShared/ConfigModel.cs
  using System.Collections.Generic;

  namespace EKLobbyShared;

  public class FriendEntry
  {
      public string Steam64Id { get; set; } = "";
      public string DisplayName { get; set; } = "";
  }

  public class LobbyConfig
  {
      public string LobbyRoomName { get; set; } = "";
      public List<FriendEntry> Friends { get; set; } = new();
      public bool AutoLaunchTray { get; set; } = false;
  }
  ```

- [ ] **Step 4: Create ConfigStore.cs**

  ```csharp
  // src/EKLobbyShared/ConfigStore.cs
  using System;
  using System.IO;
  using System.Text.Json;
  using System.Text.Json.Serialization;

  namespace EKLobbyShared;

  public static class ConfigStore
  {
      private static readonly JsonSerializerOptions JsonOpts =
          new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

      private static readonly string DefaultPath = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
          "EKLobbyMod", "config.json");

      // Set in tests to redirect file I/O to a temp path
      public static string? OverridePath { get; set; }

      private static string ResolvedPath => OverridePath ?? DefaultPath;

      public static LobbyConfig Load()
      {
          var path = ResolvedPath;
          if (!File.Exists(path))
              return new LobbyConfig();
          return JsonSerializer.Deserialize<LobbyConfig>(File.ReadAllText(path), JsonOpts)
                 ?? new LobbyConfig();
      }

      public static void Save(LobbyConfig config)
      {
          var path = ResolvedPath;
          Directory.CreateDirectory(Path.GetDirectoryName(path)!);
          File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOpts));
      }

      public static string GetOrCreateRoomName(ulong steam64Id)
      {
          var config = Load();
          if (!string.IsNullOrEmpty(config.LobbyRoomName))
              return config.LobbyRoomName;
          var hex = steam64Id.ToString("X16");
          config.LobbyRoomName = $"EK-{hex.Substring(hex.Length - 8)}";
          Save(config);
          return config.LobbyRoomName;
      }
  }
  ```

- [ ] **Step 5: Run tests to confirm they pass**

  ```powershell
  dotnet test tests/EKLobbyShared.Tests/EKLobbyShared.Tests.csproj -v normal
  ```

  Expected: 4 tests pass.

- [ ] **Step 6: Commit**

  ```powershell
  git add src/EKLobbyShared/ tests/EKLobbyShared.Tests/
  git commit -m "feat: shared config model and ConfigStore with tests"
  ```

---

## Task 3: IL2CPP Type Discovery

This is a research task, not a code task. Its output informs Task 5 (PhotonClientFinder).

**Files:** none — findings recorded as comments in `PhotonClientFinder.cs` later.

- [ ] **Step 1: Verify BepInEx interop assemblies were generated (Task 0 prerequisite)**

  ```powershell
  ls "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\interop" | Select-String "MGS|Photon|Platform"
  ```

  Expected: `MGS.Platform.dll`, `MGS.GameLift.Realtime.dll`, `PhotonRealtime.dll` visible.
  If missing, re-run the game once with BepInEx installed (Task 0 Step 3).

- [ ] **Step 2: Find the class that holds LoadBalancingClient**

  Open a PowerShell session and use `dnSpy` or `ILSpy` to inspect the interop assemblies,
  OR use this script to search via reflection:

  ```powershell
  Add-Type -Path "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\interop\PhotonRealtime.dll"

  # Search all interop DLLs for types with a LoadBalancingClient field
  $interopDir = "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\interop"
  Get-ChildItem $interopDir -Filter "MGS.*.dll" | ForEach-Object {
      try {
          $asm = [System.Reflection.Assembly]::LoadFrom($_.FullName)
          $asm.GetTypes() | ForEach-Object {
              $t = $_
              $t.GetFields([System.Reflection.BindingFlags]::Instance -bor
                           [System.Reflection.BindingFlags]::NonPublic -bor
                           [System.Reflection.BindingFlags]::Public) |
              Where-Object { $_.FieldType.Name -like "*LoadBalancingClient*" } |
              ForEach-Object { Write-Host "$($t.FullName) -> field: $($_.Name) : $($_.FieldType.Name)" }
          }
      } catch {}
  }
  ```

  **Record the output.** You are looking for something like:
  ```
  MGS.Networking.PhotonNetworkManager -> field: _loadBalancingClient : LoadBalancingClient
  ```
  Note the full class name and field name — you will need them in Task 5.

- [ ] **Step 3: Find the Steamworks SteamManager singleton**

  ```powershell
  $asm = [System.Reflection.Assembly]::LoadFrom(
      "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\interop\MGS.Platform.dll")
  $asm.GetTypes() | Where-Object { $_.Name -like "*Steam*" } | Select-Object FullName
  ```

  Record the full name of the `SteamManager`-like class and note how it exposes the local
  player's Steam64 ID (look for a property named `SteamId`, `LocalSteamId`, `MySteamId`, etc.):

  ```powershell
  $steamType = $asm.GetType("MGS.Platform.SteamManager")  # replace with actual name
  $steamType.GetProperties() | Select-Object Name, PropertyType
  $steamType.GetFields() | Select-Object Name, FieldType
  ```

---

## Task 4: BepInEx Plugin Entry Point

**Files:**
- Create: `src/EKLobbyMod/Plugin.cs`

- [ ] **Step 1: Create Plugin.cs**

  Replace `YOUR.STEAM.CLASS` with the full type name found in Task 3 Step 3.

  ```csharp
  // src/EKLobbyMod/Plugin.cs
  using BepInEx;
  using BepInEx.IL2CPP;
  using BepInEx.Logging;
  using UnityEngine.SceneManagement;

  namespace EKLobbyMod;

  [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
  public class Plugin : BasePlugin
  {
      public const string PluginGuid = "com.eklobbymod.plugin";
      public const string PluginName = "EKLobbyMod";
      public const string PluginVersion = "1.0.0";

      internal static ManualLogSource Log = null!;
      private LobbyManager? _lobbyManager;

      public override void Load()
      {
          Log = base.Log;
          Log.LogInfo($"{PluginName} v{PluginVersion} loaded");
          SceneManager.sceneLoaded += OnSceneLoaded;
      }

      private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
      {
          Log.LogInfo($"Scene loaded: {scene.name}");
          var client = PhotonClientFinder.FindClient();
          if (client == null)
          {
              Log.LogWarning("LoadBalancingClient not found in this scene — will retry next scene load");
              return;
          }
          if (_lobbyManager == null)
              _lobbyManager = new LobbyManager(client);
          OverlayPanel.Inject(_lobbyManager);
      }
  }
  ```

- [ ] **Step 2: Build the plugin project**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: succeeds (warning about missing types is OK at this stage — `LobbyManager`,
  `PhotonClientFinder`, `OverlayPanel` don't exist yet). Fix any hard errors before continuing.

---

## Task 5: PhotonClientFinder

**Files:**
- Create: `src/EKLobbyMod/PhotonClientFinder.cs`

> Prerequisite: Task 3 must be complete. Replace `"_loadBalancingClient"` and the
> MonoBehaviour type with the actual names you found in Task 3 Step 2.

- [ ] **Step 1: Create PhotonClientFinder.cs**

  ```csharp
  // src/EKLobbyMod/PhotonClientFinder.cs
  using Il2CppInterop.Runtime;
  using Photon.Realtime;
  using UnityEngine;

  namespace EKLobbyMod;

  public static class PhotonClientFinder
  {
      // Field name discovered via Task 3 Step 2 reflection search.
      // Replace "_loadBalancingClient" if the dump revealed a different name.
      private const string ClientFieldName = "_loadBalancingClient";

      public static LoadBalancingClient? FindClient()
      {
          var allBehaviours = Object.FindObjectsOfType<MonoBehaviour>();
          foreach (var mb in allBehaviours)
          {
              try
              {
                  var il2Type = mb.GetIl2CppType();
                  var field = il2Type.GetField(ClientFieldName,
                      System.Reflection.BindingFlags.Instance |
                      System.Reflection.BindingFlags.NonPublic |
                      System.Reflection.BindingFlags.Public);
                  if (field == null) continue;

                  var value = field.GetValue(mb);
                  if (value == null) continue;

                  return value.Cast<LoadBalancingClient>();
              }
              catch
              {
                  // Il2CppInterop throws on incompatible casts — skip and continue
              }
          }
          Plugin.Log.LogWarning($"No MonoBehaviour with field '{ClientFieldName}' found");
          return null;
      }
  }
  ```

- [ ] **Step 2: Build**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: no new errors from this file.

- [ ] **Step 3: Manual smoke test (in-game)**

  Copy the built `EKLobbyMod.dll` to
  `D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2\BepInEx\plugins\`.
  Launch the game through Steam. Open `BepInEx/logs/BepInEx.log`. Look for:
  ```
  [Info   :EKLobbyMod] EKLobbyMod v1.0.0 loaded
  [Info   :EKLobbyMod] Scene loaded: <scene name>
  ```
  If you see `LoadBalancingClient not found` in the log, re-run Task 3 Step 2 with the
  correct scene name and re-examine which scene the Photon manager lives in.

---

## Task 6: LobbyManager

**Files:**
- Create: `src/EKLobbyMod/LobbyManager.cs`

> `IMatchmakingCallbacks` in IL2CPP has `Il2CppSystem` parameter types instead of
> `System` types. The interface members below use the correct IL2CPP signatures.

- [ ] **Step 1: Create LobbyManager.cs**

  ```csharp
  // src/EKLobbyMod/LobbyManager.cs
  using System;
  using EKLobbyShared;
  using Photon.Realtime;

  namespace EKLobbyMod;

  public class LobbyManager : IMatchmakingCallbacks
  {
      private readonly LoadBalancingClient _client;
      public LobbyConfig Config { get; private set; }
      public bool PendingRejoin { get; private set; }
      private bool _creatingHomeLobby = false;

      public event Action? RejoinAvailable;
      public event Action? RejoinConfirmed;

      public LobbyManager(LoadBalancingClient client)
      {
          _client = client;
          Config = ConfigStore.Load();
          _client.AddCallbackTarget(this);
          Plugin.Log.LogInfo($"LobbyManager ready — home lobby: {Config.LobbyRoomName}");
      }

      public void EnsureRoomName(ulong steam64Id)
      {
          if (string.IsNullOrEmpty(Config.LobbyRoomName))
              Config.LobbyRoomName = ConfigStore.GetOrCreateRoomName(steam64Id);
      }

      public void JoinOrCreateHomeLobby()
      {
          if (string.IsNullOrEmpty(Config.LobbyRoomName))
          {
              Plugin.Log.LogWarning("Home lobby room name not set — cannot rejoin");
              return;
          }
          var options = new RoomOptions
          {
              IsVisible = false,
              MaxPlayers = 5,
              EmptyRoomTtl = 0,
          };
          Plugin.Log.LogInfo($"Joining or creating room: {Config.LobbyRoomName}");
          _creatingHomeLobby = true;
          _client.JoinOrCreateRoom(Config.LobbyRoomName, options, TypedLobby.Default);
      }

      public void AddFriend(FriendEntry friend)
      {
          Config.Friends.Add(friend);
          ConfigStore.Save(Config);
      }

      public void RemoveFriend(string steam64Id)
      {
          Config.Friends.RemoveAll(f => f.Steam64Id == steam64Id);
          ConfigStore.Save(Config);
      }

      // IMatchmakingCallbacks — called by Photon on the main Unity thread

      public void OnCreatedRoom()
      {
          var name = _client.CurrentRoom.Name;
          // Only adopt the room name if we initiated the create via JoinOrCreateHomeLobby,
          // not when the game's normal matchmaking creates a room
          if (_creatingHomeLobby || string.IsNullOrEmpty(Config.LobbyRoomName))
          {
              Config.LobbyRoomName = name;
              ConfigStore.Save(Config);
          }
          _creatingHomeLobby = false;
          Plugin.Log.LogInfo($"Room created: {name} (saved: {Config.LobbyRoomName})");
      }

      public void OnJoinedRoom()
      {
          PendingRejoin = false;
          RejoinConfirmed?.Invoke();
          Plugin.Log.LogInfo($"Joined room: {_client.CurrentRoom.Name}");
      }

      public void OnLeftRoom()
      {
          PendingRejoin = true;
          RejoinAvailable?.Invoke();
          Plugin.Log.LogInfo("Left room — rejoin available");
      }

      public void OnCreateRoomFailed(short returnCode, string message) =>
          Plugin.Log.LogWarning($"CreateRoom failed ({returnCode}): {message}");

      public void OnJoinRoomFailed(short returnCode, string message) =>
          Plugin.Log.LogWarning($"JoinRoom failed ({returnCode}): {message}");

      public void OnJoinRandomFailed(short returnCode, string message) { }

      public void OnFriendListUpdate(
          Il2CppSystem.Collections.Generic.List<FriendInfo> friendList) { }
  }
  ```

- [ ] **Step 2: Build**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: no errors. (If `Il2CppSystem` namespace is not found, add a `using Il2CppSystem;`
  at the top or check the BepInEx interop DLL for the correct namespace.)

- [ ] **Step 3: Manual in-game test**

  Copy the updated DLL to BepInEx/plugins. Launch the game, join/create a room through
  the normal game UI. Check `BepInEx.log` for:
  ```
  [Info   :EKLobbyMod] Room created: <room-name>
  ```
  or the `Joined room` / `Left room` lines.

- [ ] **Step 4: Commit**

  ```powershell
  git add src/EKLobbyMod/
  git commit -m "feat: Photon client finder and lobby manager"
  ```

---

## Task 7: SteamInviter (In-Process)

**Files:**
- Create: `src/EKLobbyMod/SteamInviter.cs`

> Steamworks.NET is already loaded in-process by the game (`MGS.Platform.SteamManager`).
> The interop stub at `BepInEx/interop/MGS.Platform.dll` exposes the Steamworks types.
> Replace `SteamManager.Instance.SteamId` with the actual property name from Task 3 Step 3.

- [ ] **Step 1: Create SteamInviter.cs**

  ```csharp
  // src/EKLobbyMod/SteamInviter.cs
  using System.Collections.Generic;
  using EKLobbyShared;
  using Steamworks;   // namespace from MGS.Platform interop or Steamworks.NET interop

  namespace EKLobbyMod;

  public static class SteamInviter
  {
      public static ulong GetLocalSteamId()
      {
          // SteamUser.GetSteamID() is the Steamworks.NET call — available in-process
          return SteamUser.GetSteamID().m_SteamID;
      }

      public static IEnumerable<FriendEntry> GetAllSteamFriends()
      {
          int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
          for (int i = 0; i < count; i++)
          {
              var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
              yield return new FriendEntry
              {
                  Steam64Id = id.m_SteamID.ToString(),
                  DisplayName = SteamFriends.GetFriendPersonaName(id)
              };
          }
      }

      public static bool IsOnline(string steam64Id)
      {
          if (!ulong.TryParse(steam64Id, out var raw)) return false;
          var state = SteamFriends.GetFriendPersonaState(new CSteamID(raw));
          return state != EPersonaState.k_EPersonaStateOffline;
      }

      public static void InviteAll(IEnumerable<string> steam64Ids)
      {
          foreach (var idStr in steam64Ids)
          {
              if (!ulong.TryParse(idStr, out var raw)) continue;
              // Empty connect string — just notifies friend to join; they use their own Rejoin button
              SteamFriends.InviteUserToGame(new CSteamID(raw), "");
          }
      }
  }
  ```

- [ ] **Step 2: Update Plugin.cs to populate the home lobby room name from Steam ID**

  In `Plugin.cs`, in `OnSceneLoaded`, after constructing `_lobbyManager`, add:

  ```csharp
  _lobbyManager.EnsureRoomName(SteamInviter.GetLocalSteamId());
  ```

- [ ] **Step 3: Build and manual test**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Copy to plugins, launch game, verify in log that `home lobby:` shows a valid `EK-XXXXXXXX` code.

- [ ] **Step 4: Commit**

  ```powershell
  git add src/EKLobbyMod/
  git commit -m "feat: in-process Steam friend enumeration and invite"
  ```

---

## Task 8: Overlay Panel — Canvas Injection and Shell

**Files:**
- Create: `src/EKLobbyMod/OverlayPanel.cs`

The panel is a uGUI `GameObject` injected into the game's topmost `Canvas`. It starts minimised
(shows only the lobby code label) and expands on click or when `RejoinAvailable` fires.

- [ ] **Step 1: Create OverlayPanel.cs**

  ```csharp
  // src/EKLobbyMod/OverlayPanel.cs
  using System.Collections.Generic;
  using EKLobbyShared;
  using UnityEngine;
  using UnityEngine.UI;

  namespace EKLobbyMod;

  public class OverlayPanel : MonoBehaviour
  {
      private LobbyManager _manager = null!;
      private bool _expanded = false;

      // Root panel objects
      private GameObject _expandedPanel = null!;
      private Text _codeText = null!;
      private Transform _friendListContainer = null!;
      private Button _rejoinButton = null!;
      private Button _inviteAllButton = null!;

      // Tab shown when minimised
      private GameObject _minTab = null!;

      public static OverlayPanel? Inject(LobbyManager manager)
      {
          var canvas = FindTopCanvas();
          if (canvas == null)
          {
              Plugin.Log.LogWarning("No Canvas found — overlay not injected");
              return null;
          }

          // Reuse existing panel if already present in this scene
          var existing = canvas.GetComponentInChildren<OverlayPanel>();
          if (existing != null)
          {
              existing._manager = manager;
              return existing;
          }

          var go = new GameObject("EKLobbyOverlay");
          go.transform.SetParent(canvas.transform, false);
          var panel = go.AddComponent<OverlayPanel>();
          panel._manager = manager;
          panel.Build();
          manager.RejoinAvailable += panel.ShowRejoinPrompt;
          manager.RejoinConfirmed += panel.HideRejoinPrompt;
          return panel;
      }

      private void Build()
      {
          BuildMinTab();
          BuildExpandedPanel();
          SetExpanded(false);
      }

      private void BuildMinTab()
      {
          _minTab = CreatePanel(transform, new Vector2(160, 32),
              new Vector2(0, 0), new Vector2(0, 0),   // anchor bottom-left
              new Vector2(80, 16));                     // pivot centre of tab

          var tabButton = _minTab.AddComponent<Button>();
          tabButton.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

          var bg = _minTab.AddComponent<Image>();
          bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

          var tabLabel = CreateText(_minTab.transform, "", 12);
          _codeText = tabLabel;   // reused in expanded panel too
          RefreshCodeLabel();
      }

      private void BuildExpandedPanel()
      {
          _expandedPanel = CreatePanel(transform, new Vector2(220, 280),
              new Vector2(0, 0), new Vector2(0, 0),
              new Vector2(110, 140));

          var bg = _expandedPanel.AddComponent<Image>();
          bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

          // Header
          var header = CreateText(_expandedPanel.transform, "EK Lobby", 14);
          PositionRect(header.rectTransform, new Vector2(0, 260), new Vector2(180, 20));

          // Collapse button
          var collapseBtn = CreateButton(_expandedPanel.transform, "[_]", 11,
              new Vector2(185, 260), new Vector2(30, 20));
          collapseBtn.onClick.AddListener((UnityEngine.Events.UnityAction)ToggleExpanded);

          // Code row
          var codeLabel = CreateText(_expandedPanel.transform,
              $"Code: {_manager.Config.LobbyRoomName}", 12);
          PositionRect(codeLabel.rectTransform, new Vector2(5, 235), new Vector2(160, 18));

          var copyBtn = CreateButton(_expandedPanel.transform, "Copy", 11,
              new Vector2(170, 235), new Vector2(40, 18));
          copyBtn.onClick.AddListener((UnityEngine.Events.UnityAction)CopyCodeToClipboard);

          // Friend list scroll area
          var scroll = new GameObject("FriendListScroll");
          scroll.transform.SetParent(_expandedPanel.transform, false);
          PositionRect(scroll.GetOrAddComponent<RectTransform>(),
              new Vector2(5, 60), new Vector2(210, 165));
          _friendListContainer = scroll.transform;

          // [+ Add] button
          var addBtn = CreateButton(_expandedPanel.transform, "+ Add", 11,
              new Vector2(160, 43), new Vector2(50, 18));
          addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)OpenFriendPicker);

          // Invite All button
          _inviteAllButton = CreateButton(_expandedPanel.transform, "Invite All", 12,
              new Vector2(5, 5), new Vector2(90, 24));
          _inviteAllButton.onClick.AddListener((UnityEngine.Events.UnityAction)InviteAll);

          // Rejoin button
          _rejoinButton = CreateButton(_expandedPanel.transform, "Rejoin", 12,
              new Vector2(115, 5), new Vector2(90, 24));
          _rejoinButton.onClick.AddListener((UnityEngine.Events.UnityAction)DoRejoin);
      }

      public void ShowRejoinPrompt()
      {
          SetExpanded(true);
          // Tint rejoin button green to draw attention
          var colors = _rejoinButton.colors;
          colors.normalColor = new Color(0.2f, 0.8f, 0.2f, 1f);
          _rejoinButton.colors = colors;
      }

      public void HideRejoinPrompt()
      {
          var colors = _rejoinButton.colors;
          colors.normalColor = ColorBlock.defaultColorBlock.normalColor;
          _rejoinButton.colors = colors;
      }

      private void SetExpanded(bool expanded)
      {
          _expanded = expanded;
          _expandedPanel.SetActive(expanded);
          _minTab.SetActive(!expanded);
          if (expanded) RefreshFriendList();
      }

      private void ToggleExpanded() => SetExpanded(!_expanded);

      private void RefreshCodeLabel()
      {
          if (_codeText != null)
              _codeText.text = _manager.Config.LobbyRoomName;
      }

      private void RefreshFriendList()
      {
          foreach (Transform child in _friendListContainer)
              Destroy(child.gameObject);

          float y = 0f;
          foreach (var friend in _manager.Config.Friends)
          {
              bool online = SteamInviter.IsOnline(friend.Steam64Id);
              var row = CreateText(_friendListContainer, $"{(online ? "●" : "○")} {friend.DisplayName}", 11);
              PositionRect(row.rectTransform, new Vector2(0, -y), new Vector2(200, 18));
              y += 20f;
          }
      }

      private void CopyCodeToClipboard() =>
          GUIUtility.systemCopyBuffer = _manager.Config.LobbyRoomName;

      private void InviteAll() =>
          SteamInviter.InviteAll(_manager.Config.Friends.ConvertAll(f => f.Steam64Id));

      private void DoRejoin() => _manager.JoinOrCreateHomeLobby();

      private void OpenFriendPicker() =>
          FriendPickerPopup.Open(_manager, transform.parent);

      // ── uGUI helpers ─────────────────────────────────────────────────────────

      private static Canvas? FindTopCanvas()
      {
          Canvas? top = null;
          int maxOrder = int.MinValue;
          foreach (var c in FindObjectsOfType<Canvas>())
          {
              if (c.sortingOrder > maxOrder) { maxOrder = c.sortingOrder; top = c; }
          }
          return top;
      }

      private static GameObject CreatePanel(Transform parent, Vector2 size,
          Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
      {
          var go = new GameObject("Panel");
          go.transform.SetParent(parent, false);
          var rt = go.AddComponent<RectTransform>();
          rt.anchorMin = anchorMin;
          rt.anchorMax = anchorMax;
          rt.pivot = pivot;
          rt.sizeDelta = size;
          rt.anchoredPosition = Vector2.zero;
          return go;
      }

      private static Text CreateText(Transform parent, string content, int size)
      {
          var go = new GameObject("Text");
          go.transform.SetParent(parent, false);
          go.AddComponent<RectTransform>();
          var t = go.AddComponent<Text>();
          t.text = content;
          t.fontSize = size;
          t.color = Color.white;
          t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
          return t;
      }

      private static Button CreateButton(Transform parent, string label, int fontSize,
          Vector2 anchoredPos, Vector2 size)
      {
          var go = new GameObject($"Btn_{label}");
          go.transform.SetParent(parent, false);
          var rt = go.AddComponent<RectTransform>();
          rt.anchoredPosition = anchoredPos;
          rt.sizeDelta = size;
          var img = go.AddComponent<Image>();
          img.color = new Color(0.25f, 0.25f, 0.25f, 1f);
          var btn = go.AddComponent<Button>();
          CreateText(go.transform, label, fontSize);
          return btn;
      }

      private static void PositionRect(RectTransform rt, Vector2 anchoredPos, Vector2 size)
      {
          rt.anchoredPosition = anchoredPos;
          rt.sizeDelta = size;
      }
  }

  internal static class GameObjectExt
  {
      public static T GetOrAddComponent<T>(this GameObject go) where T : Component =>
          go.GetComponent<T>() ?? go.AddComponent<T>();
  }
  ```

- [ ] **Step 2: Build**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

- [ ] **Step 3: Create a stub FriendPickerPopup.cs to unblock compilation**

  ```csharp
  // src/EKLobbyMod/FriendPickerPopup.cs — stub, replaced in Task 9
  using UnityEngine;

  namespace EKLobbyMod;

  public static class FriendPickerPopup
  {
      public static void Open(LobbyManager manager, Transform parent) { }
  }
  ```

- [ ] **Step 4: Build again and verify clean**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Expected: no errors.

- [ ] **Step 5: Manual in-game test**

  Copy DLL to BepInEx/plugins. Launch game, reach the main menu. The overlay tab should
  appear in the bottom-left corner. Click it — the expanded panel should appear with the
  lobby code. Click `[_]` to collapse back to the tab. Check `BepInEx.log` for any errors.

- [ ] **Step 6: Commit**

  ```powershell
  git add src/EKLobbyMod/
  git commit -m "feat: uGUI overlay panel injection with minimise/expand"
  ```

---

## Task 9: Friend Picker Popup

**Files:**
- Modify: `src/EKLobbyMod/FriendPickerPopup.cs` (replace stub)

- [ ] **Step 1: Replace FriendPickerPopup.cs**

  ```csharp
  // src/EKLobbyMod/FriendPickerPopup.cs
  using System.Linq;
  using EKLobbyShared;
  using UnityEngine;
  using UnityEngine.UI;

  namespace EKLobbyMod;

  public static class FriendPickerPopup
  {
      public static void Open(LobbyManager manager, Transform canvasTransform)
      {
          // Remove any existing picker
          var existing = canvasTransform.Find("EKFriendPicker");
          if (existing != null) Object.Destroy(existing.gameObject);

          var root = new GameObject("EKFriendPicker");
          root.transform.SetParent(canvasTransform, false);

          var rt = root.AddComponent<RectTransform>();
          rt.anchorMin = new Vector2(0.5f, 0.5f);
          rt.anchorMax = new Vector2(0.5f, 0.5f);
          rt.sizeDelta = new Vector2(260, 340);
          rt.anchoredPosition = Vector2.zero;

          var bg = root.AddComponent<Image>();
          bg.color = new Color(0.12f, 0.12f, 0.12f, 0.97f);

          // Title
          var title = CreateText(root.transform, "Add Friend", 14);
          var titleRt = title.GetComponent<RectTransform>();
          titleRt.anchoredPosition = new Vector2(0, 150);
          titleRt.sizeDelta = new Vector2(240, 24);

          // Close button
          var closeBtn = CreateButton(root.transform, "X", 12,
              new Vector2(110, 150), new Vector2(24, 24));
          closeBtn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
              Object.Destroy(root)));

          // Scroll view for friend list
          var scrollContent = new GameObject("ScrollContent");
          scrollContent.transform.SetParent(root.transform, false);
          var scrollRt = scrollContent.AddComponent<RectTransform>();
          scrollRt.anchorMin = new Vector2(0, 0);
          scrollRt.anchorMax = new Vector2(1, 1);
          scrollRt.offsetMin = new Vector2(5, 40);
          scrollRt.offsetMax = new Vector2(-5, -130);

          // Populate friend rows — only friends not already on the list
          var alreadySaved = manager.Config.Friends
              .Select(f => f.Steam64Id)
              .ToHashSet();

          float y = 0f;
          foreach (var friend in SteamInviter.GetAllSteamFriends())
          {
              if (alreadySaved.Contains(friend.Steam64Id)) continue;

              var captured = friend;
              var row = new GameObject($"Row_{friend.DisplayName}");
              row.transform.SetParent(scrollContent.transform, false);
              var rowRt = row.AddComponent<RectTransform>();
              rowRt.anchoredPosition = new Vector2(0, -y);
              rowRt.sizeDelta = new Vector2(240, 26);

              var nameText = CreateText(row.transform, friend.DisplayName, 11);
              nameText.GetComponent<RectTransform>().sizeDelta = new Vector2(180, 22);

              var addBtn = CreateButton(row.transform, "+", 12,
                  new Vector2(100, 0), new Vector2(26, 22));
              addBtn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
              {
                  manager.AddFriend(captured);
                  Object.Destroy(root);
              }));

              y += 28f;
          }
      }

      private static Text CreateText(Transform parent, string content, int size)
      {
          var go = new GameObject("Text");
          go.transform.SetParent(parent, false);
          go.AddComponent<RectTransform>();
          var t = go.AddComponent<Text>();
          t.text = content;
          t.fontSize = size;
          t.color = Color.white;
          t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
          return t;
      }

      private static Button CreateButton(Transform parent, string label, int fontSize,
          Vector2 pos, Vector2 size)
      {
          var go = new GameObject($"Btn_{label}");
          go.transform.SetParent(parent, false);
          var rt = go.AddComponent<RectTransform>();
          rt.anchoredPosition = pos;
          rt.sizeDelta = size;
          go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 1f);
          var btn = go.AddComponent<Button>();
          CreateText(go.transform, label, fontSize);
          return btn;
      }
  }
  ```

- [ ] **Step 2: Build and deploy**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  Copy to BepInEx/plugins. Launch game. Open overlay, click `+ Add`. A modal should appear
  listing your Steam friends. Click `+` next to a name — they should appear in the friend list.
  Verify the name persists after closing and reopening the overlay.

- [ ] **Step 3: Commit**

  ```powershell
  git add src/EKLobbyMod/FriendPickerPopup.cs
  git commit -m "feat: friend picker popup with Steam friend list"
  ```

---

## Task 10: Post-Game Auto-Rejoin Flow

The `LobbyManager.RejoinAvailable` event is already wired in `OverlayPanel.Inject` — the
overlay expands and tints the Rejoin button when `OnLeftRoom` fires. This task verifies the
end-to-end flow and adds a label to clarify the prompt.

**Files:**
- Modify: `src/EKLobbyMod/OverlayPanel.cs`

- [ ] **Step 1: Add a rejoin prompt label to OverlayPanel**

  Add the field to the `OverlayPanel` class alongside the other private fields:

  ```csharp
  private Text _rejoinPromptLabel = null!;
  ```

  Then at the end of `BuildExpandedPanel()`, add:

  ```csharp
  // After building the Rejoin button, add:
  _rejoinPromptLabel = CreateText(_expandedPanel.transform, "Game over — return to your lobby?", 10);
  PositionRect(_rejoinPromptLabel.rectTransform, new Vector2(5, 30), new Vector2(210, 18));
  _rejoinPromptLabel.color = new Color(1f, 0.85f, 0.3f, 1f);  // amber
  _rejoinPromptLabel.gameObject.SetActive(false);
  ```

- [ ] **Step 2: Update ShowRejoinPrompt and HideRejoinPrompt to toggle the label**

  ```csharp
  public void ShowRejoinPrompt()
  {
      SetExpanded(true);
      _rejoinPromptLabel.gameObject.SetActive(true);
      var colors = _rejoinButton.colors;
      colors.normalColor = new Color(0.2f, 0.8f, 0.2f, 1f);
      _rejoinButton.colors = colors;
  }

  public void HideRejoinPrompt()
  {
      _rejoinPromptLabel.gameObject.SetActive(false);
      var colors = _rejoinButton.colors;
      colors.normalColor = ColorBlock.defaultColorBlock.normalColor;
      _rejoinButton.colors = colors;
  }
  ```

- [ ] **Step 3: Build, deploy, and manual end-to-end test**

  ```powershell
  dotnet build src/EKLobbyMod/EKLobbyMod.csproj
  ```

  1. Launch game, open the overlay, note the lobby code.
  2. Join a game via normal game UI.
  3. Play until the game ends and return to the main menu.
  4. Verify the overlay expands automatically with the amber prompt and green Rejoin button.
  5. Click Rejoin — verify you join/create the expected room (check `BepInEx.log`).

- [ ] **Step 4: Commit**

  ```powershell
  git add src/EKLobbyMod/
  git commit -m "feat: auto-rejoin prompt on return to lobby after game ends"
  ```

---

## Task 11: System Tray App Shell

**Files:**
- Create: `src/EKLobbyTray/Program.cs`
- Create: `src/EKLobbyTray/TrayApp.cs`
- Create: `tests/EKLobbyTray.Tests/TrayAppTests.cs`

- [ ] **Step 1: Write failing test for TrayApp config loading**

  ```csharp
  // tests/EKLobbyTray.Tests/TrayAppTests.cs
  using System;
  using System.IO;
  using EKLobbyShared;
  using EKLobbyTray;
  using Xunit;

  namespace EKLobbyTray.Tests;

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
  }
  ```

- [ ] **Step 2: Run tests to confirm failure**

  ```powershell
  dotnet test tests/EKLobbyTray.Tests/EKLobbyTray.Tests.csproj
  ```

  Expected: build error — `TrayApp.BuildMenuItems` not defined.

- [ ] **Step 3: Create TrayApp.cs**

  ```csharp
  // src/EKLobbyTray/TrayApp.cs
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.IO;
  using System.Windows.Forms;
  using EKLobbyShared;

  namespace EKLobbyTray;

  public class TrayApp : IDisposable
  {
      private readonly NotifyIcon _icon;
      private readonly FileSystemWatcher _watcher;
      private LobbyConfig _config;

      public TrayApp()
      {
          _config = ConfigStore.Load();
          _icon = new NotifyIcon
          {
              Text = "EK Lobby",
              Icon = SystemIcons.Application,  // replace with custom icon if available
              Visible = true
          };
          _icon.ContextMenuStrip = BuildMenu();

          var configDir = Path.GetDirectoryName(ConfigStore.OverridePath ?? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
              "EKLobbyMod", "config.json"))!;
          Directory.CreateDirectory(configDir);

          _watcher = new FileSystemWatcher(configDir, "config.json")
          {
              NotifyFilter = NotifyFilters.LastWrite,
              EnableRaisingEvents = true
          };
          _watcher.Changed += OnConfigChanged;
      }

      private void OnConfigChanged(object sender, FileSystemEventArgs e)
      {
          _config = ConfigStore.Load();
          _icon.ContextMenuStrip = BuildMenu();
      }

      private ContextMenuStrip BuildMenu()
      {
          var menu = new ContextMenuStrip();
          var codeItem = new ToolStripMenuItem($"Lobby code: {_config.LobbyRoomName}");
          codeItem.Click += (_, _) => Clipboard.SetText(_config.LobbyRoomName);
          menu.Items.Add(codeItem);
          menu.Items.Add(new ToolStripSeparator());

          var inviteAll = new ToolStripMenuItem("Invite All Friends");
          inviteAll.Click += (_, _) => SteamUriInviter.InviteAll(_config.Friends);
          menu.Items.Add(inviteAll);

          foreach (var friend in _config.Friends)
          {
              var f = friend;
              var friendItem = new ToolStripMenuItem(f.DisplayName);
              var removeItem = new ToolStripMenuItem("Remove from list");
              removeItem.Click += (_, _) =>
              {
                  _config.Friends.Remove(f);
                  ConfigStore.Save(_config);
              };
              friendItem.DropDownItems.Add(removeItem);
              menu.Items.Add(friendItem);
          }

          menu.Items.Add(new ToolStripSeparator());

          var launchItem = new ToolStripMenuItem("Launch Game");
          launchItem.Click += (_, _) =>
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
              {
                  FileName = "steam://rungameid/2999030",
                  UseShellExecute = true
              });
          menu.Items.Add(launchItem);

          var autoLaunch = new ToolStripMenuItem("Start with Windows")
              { Checked = AutoLaunchHelper.IsEnabled() };
          autoLaunch.Click += (_, _) =>
          {
              if (AutoLaunchHelper.IsEnabled()) AutoLaunchHelper.Disable();
              else AutoLaunchHelper.Enable();
              autoLaunch.Checked = AutoLaunchHelper.IsEnabled();
          };
          menu.Items.Add(autoLaunch);

          menu.Items.Add(new ToolStripSeparator());
          var quit = new ToolStripMenuItem("Quit");
          quit.Click += (_, _) => Application.Exit();
          menu.Items.Add(quit);
          return menu;
      }

      // Testable helper — returns descriptions of menu items (no UI)
      public static List<string> BuildMenuItems(LobbyConfig config)
      {
          var items = new List<string>();
          items.Add($"Lobby code: {config.LobbyRoomName}");
          items.Add("Invite All Friends");
          foreach (var f in config.Friends) items.Add(f.DisplayName);
          items.Add("Launch Game");
          items.Add("Start with Windows");
          items.Add("Quit");
          return items;
      }

      public void Dispose()
      {
          _watcher.Dispose();
          _icon.Dispose();
      }
  }
  ```

- [ ] **Step 4: Create Program.cs**

  ```csharp
  // src/EKLobbyTray/Program.cs
  using System;
  using System.Windows.Forms;
  using EKLobbyTray;

  Application.SetHighDpiMode(HighDpiMode.SystemAware);
  Application.EnableVisualStyles();
  Application.SetCompatibleTextRenderingDefault(false);

  using var tray = new TrayApp();
  Application.Run();  // blocks; tray.Dispose() called on exit
  ```

- [ ] **Step 5: Create AutoLaunchHelper.cs**

  ```csharp
  // src/EKLobbyTray/AutoLaunchHelper.cs
  using Microsoft.Win32;

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
          using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)!;
          key.SetValue(ValueName, System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName);
      }

      public static void Disable()
      {
          using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)!;
          key.DeleteValue(ValueName, throwOnMissingValue: false);
      }
  }
  ```

- [ ] **Step 6: Run tests**

  ```powershell
  dotnet test tests/EKLobbyTray.Tests/EKLobbyTray.Tests.csproj -v normal
  ```

  Expected: 2 tests pass.

- [ ] **Step 7: Commit**

  ```powershell
  git add src/EKLobbyTray/ tests/EKLobbyTray.Tests/
  git commit -m "feat: system tray app with friend list and auto-launch"
  ```

---

## Task 12: Tray Steam URI Inviter

**Files:**
- Create: `src/EKLobbyTray/SteamUriInviter.cs`

- [ ] **Step 1: Create SteamUriInviter.cs**

  ```csharp
  // src/EKLobbyTray/SteamUriInviter.cs
  using System.Collections.Generic;
  using System.Diagnostics;
  using EKLobbyShared;

  namespace EKLobbyTray;

  public static class SteamUriInviter
  {
      public static void InviteAll(IEnumerable<FriendEntry> friends)
      {
          foreach (var friend in friends)
              Invite(friend.Steam64Id);
      }

      public static void Invite(string steam64Id)
      {
          // Opens Steam's built-in invite dialog for this friend
          Process.Start(new ProcessStartInfo
          {
              FileName = $"steam://friends/invite/{steam64Id}",
              UseShellExecute = true
          });
      }
  }
  ```

- [ ] **Step 2: Build tray app**

  ```powershell
  dotnet build src/EKLobbyTray/EKLobbyTray.csproj
  ```

  Expected: success.

- [ ] **Step 3: Manual test**

  ```powershell
  dotnet run --project src/EKLobbyTray/EKLobbyTray.csproj
  ```

  A tray icon should appear. Right-click it. Verify lobby code appears in the menu.
  If a friend is on the saved list, verify their name appears. Click "Invite All Friends"
  with a friend listed — the Steam invite dialog should open.

- [ ] **Step 4: Commit**

  ```powershell
  git add src/EKLobbyTray/SteamUriInviter.cs
  git commit -m "feat: tray Steam URI inviter"
  ```

---

## Task 13: Installer Script

**Files:**
- Create: `install.ps1`

- [ ] **Step 1: Create install.ps1**

  ```powershell
  # install.ps1 — Run from the repo root after dotnet publish
  param(
      [string]$GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2"
  )

  $ErrorActionPreference = "Stop"

  Write-Host "Building EKLobbyMod..."
  dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod

  Write-Host "Building EKLobbyTray..."
  dotnet publish src/EKLobbyTray/EKLobbyTray.csproj -c Release -o out/EKLobbyTray `
      --self-contained false -r win-x64

  $pluginsDir = Join-Path $GameDir "BepInEx\plugins"
  if (-not (Test-Path $pluginsDir)) {
      Write-Error "BepInEx plugins directory not found at $pluginsDir. Install BepInEx first."
      exit 1
  }

  Write-Host "Installing EKLobbyMod.dll..."
  Copy-Item "out\EKLobbyMod\EKLobbyMod.dll" $pluginsDir -Force
  Copy-Item "out\EKLobbyMod\EKLobbyShared.dll" $pluginsDir -Force

  Write-Host "Installing EKLobbyTray.exe..."
  Copy-Item "out\EKLobbyTray\EKLobbyTray.exe" $GameDir -Force
  Copy-Item "out\EKLobbyTray\EKLobbyShared.dll" $GameDir -Force

  Write-Host ""
  Write-Host "Installation complete."
  Write-Host "1. Launch Exploding Kittens 2 through Steam."
  Write-Host "2. The EK Lobby overlay will appear in the bottom-left corner."
  Write-Host "3. Optionally run EKLobbyTray.exe for the system tray companion."
  ```

- [ ] **Step 2: Test the installer**

  ```powershell
  .\install.ps1
  ```

  Expected: both projects publish, DLLs copy to BepInEx/plugins, exe copies to game dir.

- [ ] **Step 3: Final end-to-end test**

  1. Launch game via Steam — verify overlay appears in bottom-left.
  2. Open overlay, confirm lobby code shown.
  3. Add a Steam friend via `[+ Add]`.
  4. Click `Invite All` — verify Steam invite dialog appears.
  5. Click `Rejoin` — verify Photon join call fires (check BepInEx.log).
  6. Play a game through to completion — verify overlay auto-expands with prompt on return.
  7. Launch `EKLobbyTray.exe` — verify tray icon and friend appear in menu.

- [ ] **Step 4: Commit**

  ```powershell
  git add install.ps1
  git commit -m "feat: installer script"
  ```
