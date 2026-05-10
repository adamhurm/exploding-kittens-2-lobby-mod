<#
.SYNOPSIS
    One-shot bootstrap for Machine B E2E test harness.

.DESCRIPTION
    Installs Python dependencies, copies the MCP server from the shared
    network location, writes machine-specific config, and creates a scheduled
    task that runs the MCP server as an HTTP service at logon.

    After bootstrap, Machine A's Claude Code session controls Machine B's game
    via the HTTP MCP endpoint — no second Claude Code session needed.

.PREREQUISITES
    Run these once before running this script:
        winget install Git.Git Python.Python.3.14

    After install, restart the terminal so python, pip, and git are on PATH.

.PARAMETER GameDir
    Full path to the Exploding Kittens 2 install directory on this machine.

.PARAMETER SteamAppId
    Steam App ID for Exploding Kittens 2 (e.g. 2999030).

.PARAMETER FriendSteamName
    The OTHER machine's Steam persona name (used to send/receive invites).

.PARAMETER BootstrapShare
    Network share where Machine A published ek_test_server.py.
    Default: \\<network-share>\EKTest\ek_test_bootstrap

.PARAMETER InstallDir
    Where to create the ek-test-b workspace. Default: %LOCALAPPDATA%\ek-test-b

.PARAMETER ServerPort
    Port for the MCP HTTP server (default: 8080).

.EXAMPLE
    .\bootstrap_machine_b.ps1 `
        -GameDir "D:\Steam\steamapps\common\Exploding Kittens 2" `
        -SteamAppId 2999030 `
        -FriendSteamName "HostSteamName"
#>

param(
    [Parameter(Mandatory = $true)]
    [string] $GameDir,

    [Parameter(Mandatory = $true)]
    [string] $SteamAppId,

    [Parameter(Mandatory = $true)]
    [string] $FriendSteamName,

    [string] $BootstrapShare = "\\<network-share>\EKTest\ek_test_bootstrap",

    [string] $InstallDir = "$env:LOCALAPPDATA\ek-test-b",

    [int] $ServerPort = 8080
)

$ErrorActionPreference = "Stop"

Write-Host "=== Machine B bootstrap ==="

# 1. Python deps
Write-Host "Installing Python dependencies..."
pip install mcp mss pyautogui psutil pywin32 Pillow
if ($LASTEXITCODE -ne 0) {
    Write-Warning "pip install returned non-zero; continuing anyway"
}

# 2. Create directory structure
$e2eDir = "$InstallDir\tests\e2e"
New-Item -ItemType Directory -Force -Path $e2eDir | Out-Null
Write-Host "Created: $e2eDir"

# 3. Copy MCP server from shared location
$serverSrc = "$BootstrapShare\ek_test_server.py"
if (-not (Test-Path $serverSrc)) {
    Write-Error "MCP server not found at: $serverSrc"
    Write-Error "Ask Machine A to run: copy-item tests\e2e\ek_test_server.py $BootstrapShare"
    exit 1
}
Copy-Item $serverSrc $e2eDir
Write-Host "Copied ek_test_server.py"

# 4. Write machine-specific config
$config = @{
    game_dir          = $GameDir
    steam_app_id      = $SteamAppId
    steam_friend_name = $FriendSteamName
    mcp_port          = $ServerPort
} | ConvertTo-Json
$config | Out-File -Encoding utf8 "$e2eDir\ek_test_config.json"
Write-Host "Wrote ek_test_config.json"

# 5. Create scheduled task to run MCP HTTP server at logon
$pythonExe = (Get-Command python).Source
$taskName = "EKTest-MCP-Server"
$taskAction = New-ScheduledTaskAction -Execute $pythonExe `
    -Argument "`"$e2eDir\ek_test_server.py`" --transport http --port $ServerPort" `
    -WorkingDirectory $InstallDir
$taskTrigger = New-ScheduledTaskTrigger -AtLogOn
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive
$taskSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

$task = Register-ScheduledTask -TaskName $taskName -Action $taskAction `
    -Trigger $taskTrigger -Principal $taskPrincipal -Settings $taskSettings `
    -Description "EK E2E Test MCP HTTP server for remote game control" `
    -Force
Write-Host "Created scheduled task: $taskName"

# Start the task now
Start-ScheduledTask -TaskName $taskName
Write-Host "Started MCP server (port $ServerPort)"

# 6. Print connection info
$hostname = [System.Net.Dns]::GetHostName()
Write-Host ""
Write-Host "============================================================"
Write-Host "  Machine B bootstrap complete."
Write-Host ""
Write-Host "  MCP HTTP server:  http://${hostname}:$ServerPort/mcp"
Write-Host "  Scheduled task:   $taskName (runs at logon)"
Write-Host ""
Write-Host "  On Machine A, add this to .mcp.json:"
Write-Host '  {'
Write-Host '    "ek-test-b": {'
Write-Host "      `"url`": `"http://${hostname}:$ServerPort/mcp`""
Write-Host '    }'
Write-Host '  }'
Write-Host "============================================================"
