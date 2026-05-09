# install.ps1 — Developer deploy script. Run from the repo root.
# Usage:  .\install.ps1
#         .\install.ps1 -GameDir "D:\Steam\steamapps\common\Exploding Kittens 2"
param(
    [string]$GameDir = "D:\Program Files (x86)\Steam\steamapps\common\Exploding Kittens 2"
)

$ErrorActionPreference = "Stop"

$BepInExUrl = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip"

if (-not (Test-Path "libs\BepInEx\core")) {
    Write-Host "BepInEx core not found — downloading..." -ForegroundColor Gray
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $tmpZip = Join-Path $env:TEMP "BepInEx_core.zip"
    Invoke-WebRequest $BepInExUrl -OutFile $tmpZip -UseBasicParsing
    Expand-Archive $tmpZip -DestinationPath libs -Force
    Remove-Item $tmpZip -Force
    Write-Host "BepInEx ready." -ForegroundColor Green
}

Write-Host "Building EKLobbyMod..." -ForegroundColor Cyan
dotnet publish src/EKLobbyMod/EKLobbyMod.csproj -c Release -o out/EKLobbyMod

Write-Host "Building EKLobbyTray..." -ForegroundColor Cyan
dotnet publish src/EKLobbyTray/EKLobbyTray.csproj -c Release -o out/EKLobbyTray `
    --self-contained false -r win-x64

$pluginsDir = Join-Path $GameDir "BepInEx\plugins"
if (-not (Test-Path $pluginsDir)) {
    Write-Error "BepInEx plugins directory not found: $pluginsDir"
    exit 1
}

Write-Host "Deploying plugin..." -ForegroundColor Cyan
Copy-Item "out\EKLobbyMod\EKLobbyMod.dll"    $pluginsDir -Force
Copy-Item "out\EKLobbyMod\EKLobbyShared.dll" $pluginsDir -Force

Write-Host "Deploying tray app..." -ForegroundColor Cyan
Copy-Item "out\EKLobbyTray\EKLobbyTray.exe"  $GameDir -Force
Copy-Item "out\EKLobbyTray\EKLobbyShared.dll" $GameDir -Force

Write-Host ""
Write-Host "Done. Launch Exploding Kittens 2 through Steam." -ForegroundColor Green
