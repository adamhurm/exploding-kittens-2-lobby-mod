#Requires -Version 5.1
<#
  EKLobbyMod - Installer
  BepInEx will be downloaded automatically if not already installed.

  If Windows blocks this script, double-click RunInstall.bat instead,
  or run: powershell -ExecutionPolicy Bypass -File Install.ps1
#>
param([switch]$NonInteractive)

$BepInExUrl ="https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip"
$ModName    = "EKLobbyMod"
$GameFolder = "Exploding Kittens 2"
$PluginFiles = @("EKLobbyMod.dll", "EKLobbyShared.dll")
$GameFiles   = @("EKLobbyTray.exe")
$PluginSrc   = Join-Path $PSScriptRoot "files\plugins"
$GameSrc     = Join-Path $PSScriptRoot "files\game"

function Write-Step { param($m) Write-Host "  $m"        -ForegroundColor Cyan   }
function Write-Ok   { param($m) Write-Host "  [OK] $m"   -ForegroundColor Green  }
function Write-Warn { param($m) Write-Host "   !   $m"   -ForegroundColor Yellow }
function Write-Fail { param($m) Write-Host "  [X]  $m"   -ForegroundColor Red    }

function Find-SteamLibraries {
    $libs = [System.Collections.Generic.List[string]]::new()

    foreach ($hive in @("HKCU:\Software\Valve\Steam",
                        "HKLM:\Software\Valve\Steam",
                        "HKLM:\Software\Wow6432Node\Valve\Steam")) {
        $reg = Get-ItemProperty $hive -Name "SteamPath" -ErrorAction SilentlyContinue
        if ($reg -and $libs -notcontains $reg.SteamPath) { $libs.Add($reg.SteamPath) }
    }

    if ($libs.Count -gt 0) {
        $vdf = Join-Path $libs[0] "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $text = [IO.File]::ReadAllText($vdf)
            foreach ($m in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                $p = $m.Groups[1].Value -replace '\\\\', '\'
                if ($libs -notcontains $p) { $libs.Add($p) }
            }
        }
    }

    foreach ($p in @("C:\Program Files (x86)\Steam", "C:\Program Files\Steam",
                     "D:\Steam", "D:\Program Files (x86)\Steam",
                     "E:\Steam", "F:\Steam", "E:\SteamLibrary", "F:\SteamLibrary")) {
        if ($libs -notcontains $p) { $libs.Add($p) }
    }
    return $libs
}

function Find-GamePath {
    foreach ($lib in Find-SteamLibraries) {
        $c = Join-Path $lib "steamapps\common\$GameFolder"
        if (Test-Path $c) { return $c }
    }
    return $null
}

# ── Banner ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ==========================================" -ForegroundColor DarkRed
Write-Host "   $ModName Installer" -ForegroundColor White
Write-Host "  ==========================================" -ForegroundColor DarkRed
Write-Host ""

# ── Package integrity ─────────────────────────────────────────────────────────
Write-Step "Checking package files..."
$missing = @()
foreach ($f in $PluginFiles) {
    if (-not (Test-Path (Join-Path $PluginSrc $f))) { $missing += "files\plugins\$f" }
}
foreach ($f in $GameFiles) {
    if (-not (Test-Path (Join-Path $GameSrc $f))) { $missing += "files\game\$f" }
}
if ($missing.Count -gt 0) {
    Write-Fail "Package is incomplete. Missing files:"
    $missing | ForEach-Object { Write-Fail "  $_" }
    Write-Warn "Re-download and re-extract the mod package, then try again."
    if (-not $NonInteractive) { Read-Host "`nPress Enter to exit" }
    exit 1
}
Write-Ok "Package OK."

# ── Locate game ────────────────────────────────────────────────────────────────
Write-Step "Searching for $GameFolder..."
$gamePath = Find-GamePath

if (-not $gamePath) {
    Write-Warn "Could not find the game automatically."
    Write-Host ""
    Write-Host "  Enter the full path to '$GameFolder':" -ForegroundColor Gray
    Write-Host "  Example: D:\Steam\steamapps\common\Exploding Kittens 2" -ForegroundColor DarkGray
    Write-Host "  > " -NoNewline
    $input = (Read-Host).Trim().Trim('"')
    if (-not $input -or -not (Test-Path $input)) {
        Write-Fail "Invalid path. Exiting."
        if (-not $NonInteractive) { Read-Host "`nPress Enter to exit" }
        exit 1
    }
    $gamePath = $input
}
Write-Ok "Game found: $gamePath"

# ── BepInEx check / auto-install ─────────────────────────────────────────────
$pluginsDir = Join-Path $gamePath "BepInEx\plugins"
if (-not (Test-Path (Join-Path $gamePath "BepInEx\core"))) {
    Write-Host ""
    Write-Warn "BepInEx is not installed."
    Write-Host "  Download and install BepInEx automatically? (Y/n): " -NoNewline
    $doInstall = (Read-Host).Trim().ToUpper()
    if ($doInstall -eq "N") {
        Write-Warn "BepInEx is required. Exiting."
        if (-not $NonInteractive) { Read-Host "`nPress Enter to exit" }
        exit 1
    }

    Write-Step "Downloading BepInEx..."
    $tmpZip = Join-Path $env:TEMP "BepInEx_IL2CPP.zip"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        (New-Object Net.WebClient).DownloadFile($BepInExUrl, $tmpZip)
    } catch {
        Write-Fail "Download failed: $_"
        if (-not $NonInteractive) { Read-Host "`nPress Enter to exit" }
        exit 1
    }
    Write-Ok "Downloaded."

    Write-Step "Extracting to game folder..."
    try {
        Expand-Archive -Path $tmpZip -DestinationPath $gamePath -Force
    } catch {
        Write-Fail "Extraction failed: $_"
        if (-not $NonInteractive) { Read-Host "`nPress Enter to exit" }
        exit 1
    }
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    Write-Ok "BepInEx installed."
}

if (-not (Test-Path $pluginsDir)) {
    New-Item -ItemType Directory $pluginsDir -Force | Out-Null
}
Write-Ok "BepInEx ready."

# ── Upgrade detection ─────────────────────────────────────────────────────────
$upgrading = Test-Path (Join-Path $pluginsDir "EKLobbyMod.dll")
if ($upgrading) { Write-Warn "Existing install found - upgrading." }

# ── Install plugin ────────────────────────────────────────────────────────────
Write-Step "Installing plugin..."
foreach ($f in $PluginFiles) {
    Copy-Item (Join-Path $PluginSrc $f) $pluginsDir -Force
    Write-Ok "$f -> BepInEx\plugins\"
}

# ── Install tray app ──────────────────────────────────────────────────────────
Write-Step "Installing tray companion..."
foreach ($f in $GameFiles) {
    Copy-Item (Join-Path $GameSrc $f) $gamePath -Force
    Write-Ok "$f -> game root"
}

# ── Done ───────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ==========================================" -ForegroundColor DarkGreen
Write-Host "   $(if ($upgrading) { 'Upgraded' } else { 'Installed' }) successfully!" -ForegroundColor Green
Write-Host "  ==========================================" -ForegroundColor DarkGreen
Write-Host ""
Write-Host "  Launch Exploding Kittens 2 via Steam." -ForegroundColor Gray
Write-Host "  The lobby overlay appears in the lower-left corner of" -ForegroundColor Gray
Write-Host "  the Multiplayer > Play With Friends screen." -ForegroundColor Gray
Write-Host ""
if (-not $NonInteractive) { Read-Host "Press Enter to exit" }
