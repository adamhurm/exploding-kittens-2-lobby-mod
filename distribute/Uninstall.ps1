#Requires -Version 5.1
<#
  EKLobbyMod - Uninstaller
  If Windows blocks this script, double-click RunUninstall.bat instead,
  or run:  powershell -ExecutionPolicy Bypass -File Uninstall.ps1
#>

$ModName     = "EKLobbyMod"
$GameFolder  = "Exploding Kittens 2"
$PluginFiles = @("EKLobbyMod.dll", "EKLobbyShared.dll")
$GameFiles   = @("EKLobbyTray.exe", "EKLobbyShared.dll")
$ConfigFile  = "com.eklobbymod.plugin.cfg"

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
Write-Host "   $ModName Uninstaller" -ForegroundColor White
Write-Host "  ==========================================" -ForegroundColor DarkRed
Write-Host ""

# ── Locate game ────────────────────────────────────────────────────────────────
Write-Step "Searching for $GameFolder..."
$gamePath = Find-GamePath

if (-not $gamePath) {
    Write-Warn "Could not find the game automatically."
    Write-Host "  Enter the full path to '$GameFolder':" -ForegroundColor Gray
    Write-Host "  > " -NoNewline
    $input = (Read-Host).Trim().Trim('"')
    if (-not $input -or -not (Test-Path $input)) {
        Write-Fail "Invalid path. Exiting."
        Read-Host "`nPress Enter to exit"
        exit 1
    }
    $gamePath = $input
}
Write-Ok "Game found: $gamePath"

$pluginsDir = Join-Path $gamePath "BepInEx\plugins"
$configDir  = Join-Path $gamePath "BepInEx\config"

# ── Check installed ───────────────────────────────────────────────────────────
$installed = Test-Path (Join-Path $pluginsDir "EKLobbyMod.dll")
if (-not $installed) {
    Write-Warn "$ModName does not appear to be installed."
    Read-Host "`nPress Enter to exit"
    exit 0
}

# ── Confirm ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  This will remove $ModName from:" -ForegroundColor White
Write-Host "    $pluginsDir" -ForegroundColor DarkGray
Write-Host "    $gamePath  (tray app)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Continue? (Y/N): " -NoNewline
$confirm = (Read-Host).Trim().ToUpper()
if ($confirm -ne "Y") {
    Write-Host "  Cancelled." -ForegroundColor Gray
    Read-Host "`nPress Enter to exit"
    exit 0
}

# ── Remove plugin files ───────────────────────────────────────────────────────
Write-Step "Removing plugin..."
foreach ($f in $PluginFiles) {
    $p = Join-Path $pluginsDir $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Ok "Removed $f" }
    else              { Write-Warn "$f not found (already removed?)" }
}

# ── Remove tray files ─────────────────────────────────────────────────────────
Write-Step "Removing tray companion..."
foreach ($f in $GameFiles) {
    $p = Join-Path $gamePath $f
    if (Test-Path $p) { Remove-Item $p -Force; Write-Ok "Removed $f" }
    else              { Write-Warn "$f not found (already removed?)" }
}

# ── Offer config removal ──────────────────────────────────────────────────────
$cfgPath = Join-Path $configDir $ConfigFile
if (Test-Path $cfgPath) {
    Write-Host ""
    Write-Host "  Saved lobby config found (friends list + room name)." -ForegroundColor Gray
    Write-Host "  Delete it too? (Y/N): " -NoNewline
    $delCfg = (Read-Host).Trim().ToUpper()
    if ($delCfg -eq "Y") {
        Remove-Item $cfgPath -Force
        Write-Ok "Config removed."
    } else {
        Write-Warn "Config kept at: $cfgPath"
    }
}

# ── Done ───────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "  ==========================================" -ForegroundColor DarkGreen
Write-Host "   $ModName removed successfully." -ForegroundColor Green
Write-Host "  ==========================================" -ForegroundColor DarkGreen
Write-Host ""
Read-Host "Press Enter to exit"
