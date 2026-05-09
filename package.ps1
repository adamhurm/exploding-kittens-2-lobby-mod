# package.ps1 — Build and create a distributable ZIP.
# Run from the repo root:  .\package.ps1
# Output:  releases\EKLobbyMod-v<version>.zip

$ErrorActionPreference = "Stop"

$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip"

# Read version from Plugin.cs so there's one source of truth
$versionLine = Select-String -Path "src\EKLobbyMod\Plugin.cs" -Pattern 'PluginVersion\s*=\s*"([^"]+)"'
$version = $versionLine.Matches[0].Groups[1].Value
$packageName = "EKLobbyMod-v$version"

Write-Host "Packaging $packageName..." -ForegroundColor Cyan

# ── BepInEx core ──────────────────────────────────────────────────────────────
if (-not (Test-Path "libs\BepInEx\core")) {
    Write-Host "BepInEx core not found — downloading..." -ForegroundColor Gray
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $tmpZip = Join-Path $env:TEMP "BepInEx_core.zip"
    Invoke-WebRequest $BepInExUrl -OutFile $tmpZip -UseBasicParsing
    Expand-Archive $tmpZip -DestinationPath libs -Force
    Remove-Item $tmpZip -Force
    Write-Host "BepInEx ready." -ForegroundColor Green
}

# ── Build ──────────────────────────────────────────────────────────────────────
Write-Host "Building EKLobbyMod..." -ForegroundColor Gray
dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod --nologo -v quiet

Write-Host "Building EKLobbyTray..." -ForegroundColor Gray
dotnet publish src/EKLobbyTray/EKLobbyTray.csproj -c Release -o out/EKLobbyTray `
    --self-contained false -r win-x64 -p:PublishSingleFile=true --nologo -v quiet

# ── Stage package folder ───────────────────────────────────────────────────────
$stage = "releases\$packageName"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory "$stage\files\plugins" -Force | Out-Null
New-Item -ItemType Directory "$stage\files\game"    -Force | Out-Null

# Plugin DLLs (go to BepInEx/plugins)
Copy-Item "out\EKLobbyMod\EKLobbyMod.dll"    "$stage\files\plugins\" -Force
Copy-Item "out\EKLobbyMod\EKLobbyShared.dll" "$stage\files\plugins\" -Force

# Tray app (single-file exe — EKLobbyShared bundled inside)
Copy-Item "out\EKLobbyTray\EKLobbyTray.exe"   "$stage\files\game\" -Force

# Installer scripts
Copy-Item "distribute\Install.ps1"    $stage -Force
Copy-Item "distribute\Uninstall.ps1"  $stage -Force
Copy-Item "distribute\RunInstall.bat" $stage -Force
Copy-Item "distribute\RunUninstall.bat" $stage -Force

# ── Zip ────────────────────────────────────────────────────────────────────────
$zipPath = "releases\$packageName.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zipPath

# ── Summary ────────────────────────────────────────────────────────────────────
$size = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host ""
Write-Host "  Package ready: $zipPath  ($size KB)" -ForegroundColor Green
Write-Host ""
Write-Host "  Contents:" -ForegroundColor Gray
Get-ChildItem $stage -Recurse | Where-Object { -not $_.PSIsContainer } |
    ForEach-Object { Write-Host "    $($_.FullName.Replace((Resolve-Path $stage).Path + '\', ''))" -ForegroundColor DarkGray }
Write-Host ""
Write-Host "  Share the ZIP. Recipient extracts it and runs RunInstall.bat." -ForegroundColor Gray
