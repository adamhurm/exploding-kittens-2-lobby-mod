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
