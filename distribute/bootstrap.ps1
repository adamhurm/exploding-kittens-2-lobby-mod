#Requires -Version 5.1
<#
  EKLobbyMod — One-line installer bootstrap
  Hosted at: https://ek.bring-us.com/get

  Usage (paste into PowerShell):
      irm ek.bring-us.com/get | iex

  What this does:
    1. Fetches the latest release ZIP from GitHub
    2. Extracts it to a temp folder
    3. Runs Install.ps1 from within the extracted package
    4. Cleans up temp files
#>

$ErrorActionPreference = "Stop"
$repo = "adamhurm/exploding-kittens-2-lobby-mod"

Write-Host ""
Write-Host "  EKLobbyMod Installer" -ForegroundColor White
Write-Host "  Fetching latest release..." -ForegroundColor DarkGray

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
    $asset   = $release.assets | Where-Object { $_.name -like "EKLobbyMod-v*.zip" } | Select-Object -First 1

    if (-not $asset) {
        Write-Host "  [X] No release ZIP found. Check https://github.com/$repo/releases" -ForegroundColor Red
        exit 1
    }

    $version = $release.tag_name
    $zipUrl  = $asset.browser_download_url
    Write-Host "  Found: $($asset.name) ($version)" -ForegroundColor Cyan
} catch {
    Write-Host "  [X] Could not reach GitHub API: $_" -ForegroundColor Red
    Write-Host "      Download manually: https://github.com/$repo/releases" -ForegroundColor Gray
    exit 1
}

$tmp    = Join-Path $env:TEMP "EKLobbyMod-bootstrap"
$zipDst = "$tmp.zip"

if (Test-Path $tmp)    { Remove-Item $tmp    -Recurse -Force }
if (Test-Path $zipDst) { Remove-Item $zipDst -Force }

try {
    Write-Host "  Downloading..." -ForegroundColor DarkGray
    Invoke-WebRequest $zipUrl -OutFile $zipDst -UseBasicParsing
    Expand-Archive $zipDst -DestinationPath $tmp -Force
} catch {
    Write-Host "  [X] Download failed: $_" -ForegroundColor Red
    exit 1
}

$installer = Join-Path $tmp "Install.ps1"
if (-not (Test-Path $installer)) {
    Write-Host "  [X] Install.ps1 not found in package." -ForegroundColor Red
    exit 1
}

try {
    & $installer
} finally {
    Remove-Item $zipDst -Force -ErrorAction SilentlyContinue
    Remove-Item $tmp    -Recurse -Force -ErrorAction SilentlyContinue
}
