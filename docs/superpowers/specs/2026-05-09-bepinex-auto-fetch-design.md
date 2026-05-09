# BepInEx Auto-Fetch Design

**Date:** 2026-05-09

## Problem

Local builds require a manual step to populate `libs/BepInEx/core/` before `dotnet publish` can succeed. Developers must either copy from a local BepInEx install or run the CI download command by hand. Neither `install.ps1` nor `package.ps1` handles this automatically.

The end-user `distribute/Install.ps1` already downloads BepInEx at install time. This design brings the same behavior to the developer-facing scripts.

## Scope

Modify two files:
- `install.ps1` — local developer deploy script
- `package.ps1` — build and distribution packaging script

No changes to `release.yml`, `distribute/Install.ps1`, or any other files.

## Design

Add a self-contained BepInEx fetch block to both scripts, placed before the first `dotnet publish` call.

### Check condition

```powershell
if (-not (Test-Path "libs\BepInEx\core")) { ... }
```

Checks for the `libs/BepInEx/core/` directory. Sufficient for normal use. A partial extraction would naturally surface as a build error.

### Fetch block (identical in both scripts)

```powershell
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
```

Error handling is provided by `$ErrorActionPreference = "Stop"` already present in both scripts — a network or extraction failure stops the script with a clear error.

### URL placement

`$BepInExUrl` is defined at the top of each script alongside other constants, consistent with how `distribute/Install.ps1` declares it.

## What does not change

- `release.yml`: The explicit CI download step is retained. It runs before `package.ps1`, so `libs/BepInEx/core/` exists when the script's check runs. The check is a no-op in CI. The explicit step keeps the CI workflow readable and self-documenting.
- `distribute/Install.ps1`: Already handles BepInEx download at end-user install time. No change needed.
- `libs/BepInEx/interop/`: IL2CPP interop stubs remain committed to git.

## Success criteria

- `.\install.ps1` succeeds on a clean checkout (no `libs/BepInEx/core/`) without any manual setup step.
- `.\package.ps1` succeeds on a clean checkout without any manual setup step.
- When `libs/BepInEx/core/` already exists, both scripts skip the download silently.
- CI build continues to pass without modification.
