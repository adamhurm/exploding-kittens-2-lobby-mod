param(
    [Parameter(Mandatory = $true)]
    [string] $GameDir,

    [Parameter(Mandatory = $true)]
    [string] $SteamAppId,

    [Parameter(Mandatory = $true)]
    [string] $FriendSteamName,

    [string] $BootstrapShare = "\\<network-share>\EKTest\ek_test_bootstrap",

    [string] $InstallDir = "$env:LOCALAPPDATA\ek-test-b"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Machine B bootstrap ==="

# 1. Python deps
Write-Host "Installing Python dependencies..."
pip install mcp[cli] mss pyautogui psutil pywin32 Pillow
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
} | ConvertTo-Json
$config | Out-File -Encoding utf8 "$e2eDir\ek_test_config.json"
Write-Host "Wrote ek_test_config.json"

# 5. Write .mcp.json
$mcpJson = @{
    mcpServers = @{
        "ek-test" = @{
            command = "python"
            args    = @("tests/e2e/ek_test_server.py")
            cwd     = '${workspaceRoot}'
        }
    }
} | ConvertTo-Json -Depth 3
$mcpJson | Out-File -Encoding utf8 "$InstallDir\.mcp.json"
Write-Host "Wrote .mcp.json"

Write-Host ""
Write-Host "Bootstrap complete. Open Claude Code in: $InstallDir"
Write-Host "Set env var before running tests: `$env:EK_COORDINATION_DIR = '<shared-path>\ek_test_coordination'"
