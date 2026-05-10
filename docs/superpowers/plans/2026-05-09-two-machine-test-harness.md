# Two-Machine E2E Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the E2E test harness to support two-machine Steam invite join/leave testing via shared-filesystem coordination.

**Architecture:** Identical MCP servers on both machines, coordination via shared state files at `$env:EK_COORDINATION_DIR`. The MCP server gains one new tool (`wait_for_pixel`) and one new config field (`steam_friend_name`). Test prompts (host/joiner) are committed markdown files that Claude Code executes directly.

**Tech Stack:** Python (FastMCP, mss), PowerShell (bootstrap), Claude Code prompts

---

### Task 1: Add `steam_friend_name` to config and server

**Files:**
- Modify: `tests/e2e/ek_test_server.py:17-19`

- [ ] **Step 1: Read `steam_friend_name` from config at startup**

In `ek_test_server.py`, replace lines 17-19:

```python
_config_path = Path(__file__).parent / "ek_test_config.json"
_config = json.loads(_config_path.read_text())
GAME_DIR = Path(_config["game_dir"])
STEAM_APP_ID = _config["steam_app_id"]
```

With:

```python
_config_path = Path(__file__).parent / "ek_test_config.json"
_config = json.loads(_config_path.read_text())
GAME_DIR = Path(_config["game_dir"])
STEAM_APP_ID = _config["steam_app_id"]
STEAM_FRIEND_NAME = _config.get("steam_friend_name", "")
```

- [ ] **Step 2: Verify no startup errors from existing configs**

Run from the repo root:
```powershell
python -c "import sys; sys.path.insert(0, 'tests/e2e'); import ek_test_server; print('OK:', ek_test_server.STEAM_FRIEND_NAME)"
```
Expected: `OK: ` (empty string — existing configs don't have the field yet, but `.get()` handles it).

- [ ] **Step 3: Commit**

```bash
git add tests/e2e/ek_test_server.py
git commit -m "feat: add steam_friend_name config field to MCP server"
```

---

### Task 2: Add `wait_for_pixel` tool to MCP server

**Files:**
- Modify: `tests/e2e/ek_test_server.py` (add new function after `type_text`, before `close_game`)

- [ ] **Step 1: Add the tool function**

Insert after the `type_text` tool (after line 108) and before `close_game`:

```python
@mcp.tool()
def wait_for_pixel(x: int, y: int, r: int, g: int, b: int,
                   tolerance: int = 10, timeout: int = 30) -> str:
    """Poll pixel (x, y) until it matches (r, g, b) within tolerance.

    Returns "matched after N.Ns" on success or "timeout after Ns" on failure.
    Use this instead of repeated screenshot() calls when waiting for a known
    UI element to appear (overlay open, invite popup, lobby transition).
    """
    import time as _time
    deadline = _time.time() + timeout
```

```python
@mcp.tool()
def wait_for_pixel(x: int, y: int, r: int, g: int, b: int,
                   tolerance: int = 10, timeout: int = 30) -> str:
    """Poll pixel (x, y) until it matches (r, g, b) within tolerance.

    Returns "matched after N.Ns" on success or "timeout after Ns" on failure.
    Use this instead of repeated screenshot() calls when waiting for a known
    UI element to appear (overlay open, invite popup, lobby transition).
    """
    import time as _time
    start = _time.time()
    deadline = start + timeout
    with mss.mss() as sct:
        while _time.time() < deadline:
            raw = sct.grab(sct.monitors[1])
            offset = (y * raw.width + x) * 3
            if offset + 2 < len(raw.rgb):
                pr, pg, pb = raw.rgb[offset], raw.rgb[offset + 1], raw.rgb[offset + 2]
                if (abs(pr - r) <= tolerance and
                    abs(pg - g) <= tolerance and
                    abs(pb - b) <= tolerance):
                    return f"matched after {_time.time() - start:.1f}s"
            _time.sleep(0.3)
    return f"timeout after {timeout}s"
```

- [ ] **Step 2: Verify the tool registers correctly**

```powershell
python -c "import sys; sys.path.insert(0, 'tests/e2e'); from ek_test_server import mcp; tools = [t.name for t in mcp._tool_manager._tools.values()]; print('wait_for_pixel' in tools)"
```
Expected: `True`

- [ ] **Step 3: Commit**

```bash
git add tests/e2e/ek_test_server.py
git commit -m "feat: add wait_for_pixel tool to MCP server"
```

---

### Task 3: Update config example

**Files:**
- Modify: `tests/e2e/ek_test_config.json.example`

- [ ] **Step 1: Add `steam_friend_name` to the example**

Replace the entire file content:

```json
{
  "game_dir": "D:\\Program Files (x86)\\Steam\\SteamApps\\common\\Exploding Kittens 2",
  "steam_app_id": 2999030,
  "steam_friend_name": ""
}
```

- [ ] **Step 2: Commit**

```bash
git add tests/e2e/ek_test_config.json.example
git commit -m "docs: document steam_friend_name in config example"
```

---

### Task 4: Create Machine B bootstrap script

**Files:**
- Create: `tests/e2e/bootstrap_machine_b.ps1`

- [ ] **Step 1: Write the bootstrap script**

```powershell
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
```

- [ ] **Step 2: Verify script syntax**

```powershell
$ErrorActionPreference = "Stop"
try {
    $null = Get-Command "tests/e2e/bootstrap_machine_b.ps1" -ErrorAction Stop
    Write-Host "Script found"
} catch {
    # Check syntax by tokenizing without executing
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Resolve-Path "tests/e2e/bootstrap_machine_b.ps1"),
        [ref]$null, [ref]$null
    )
    if ($ast) {
        Write-Host "Syntax OK"
    } else {
        Write-Error "Parse failed"
    }
}
```
Expected: `Script found`

- [ ] **Step 3: Commit**

```bash
git add tests/e2e/bootstrap_machine_b.ps1
git commit -m "feat: add Machine B bootstrap script for two-machine E2E testing"
```

---

### Task 5: Create host test prompt

**Files:**
- Create: `tests/e2e/prompts/host.md`

- [ ] **Step 1: Write the host prompt**

File content:

```markdown
# Host (Machine A) — Steam Invite E2E Test

You are the HOST. Your job: launch the game, create a lobby, send a Steam
invite to the joiner, and verify they appear in your lobby.

**Setup:** Set `$env:EK_COORDINATION_DIR` to the shared coordination directory
before starting. Both machines must have read/write access.

---

## Step 1: Signal readiness

Run in shell:
```powershell
$dir = "$env:EK_COORDINATION_DIR"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
"ready" | Out-File -Encoding utf8 "$dir\host.state"
Get-Content "$dir\host.state"
```

## Step 2: Launch the game

Call `launch_game`, then `wait_for_game(timeout=90)`, then `focus_game`.

## Step 3: Wait for title screen and dismiss overlay

Press `shift+tab` to dismiss the Steam overlay. Wait for the title screen:
use `wait_for_pixel(x=100, y=400, r=80, g=40, b=40, tolerance=30, timeout=60)`
to detect the dark-red title background.

Save a screenshot as `host_01_title`.

## Step 4: Navigate to lobby

Click through the menus:
- Click START GAME (see screenshot for coordinates)
- Wait 0.5s, click START GAME again (dismisses Marmalade splash)
- Wait 8s for publisher splash to clear
- Click PLAY
- Wait 2.5s
- Click PLAY WITH FRIENDS
- Wait 2.5s
- Click CREATE A GAME
- Wait 4s

Save a screenshot as `host_02_lobby`.

## Step 5: Verify lobby

You should see the overlay panel with a lobby code. Take a screenshot.
Read the BepInEx logs with `read_logs(errors_only=true)` — should be clean.

## Step 6: Send Steam invite

Press `shift+tab` to open the Steam overlay. Wait 2s for it to render.

Save a screenshot as `host_03_overlay_open`.

Your Steam friend list should be visible. The joiner's Steam persona name is
stored in the MCP server config as `steam_friend_name`. Look at the screenshot
and find their name in the friend list. Click on their name to open the
context menu, then click "Invite to Game".

Wait 1s. Press `escape` to close the overlay. Wait 1s.

## Step 7: Signal invite sent

Run in shell:
```powershell
"invite_sent" | Out-File -Encoding utf8 "$env:EK_COORDINATION_DIR\host.state"
```

## Step 8: Wait for joiner to arrive

Poll the joiner's state file until it says "in_lobby". Run in shell:
```powershell
$dir = "$env:EK_COORDINATION_DIR"
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $state = (Get-Content "$dir\joiner.state" -ErrorAction SilentlyContinue | Select-Object -First 1) -replace '\s+$'
    if ($state -eq "in_lobby") { Write-Host "JOINED"; break }
    if ($state -eq "error") { Write-Error "Joiner reported error"; exit 1 }
    Start-Sleep 2
}
Write-Host "Done waiting"
```

If timeout: save a screenshot as `host_error_timeout`, run `read_logs(errors_only=true)`, write "error" to host.state, and report FAIL.

## Step 9: Verify joiner in lobby

The overlay panel should now show 2 party members. Take a screenshot as
`host_04_joined`. Verify the joiner's name is visible in the panel.

## Step 10: Signal done

Run in shell:
```powershell
"done" | Out-File -Encoding utf8 "$env:EK_COORDINATION_DIR\host.state"
```

## Step 11: Close game

Call `close_game`. Read `read_logs(errors_only=true)` one final time.

## Report

If all steps completed without error:
```
-- PASS -- Host completed successfully. Joiner joined lobby.
Screenshots: host_01_title, host_02_lobby, host_03_overlay_open, host_04_joined
```

If any step failed:
```
-- FAIL -- <step description>
Screenshot saved at error point.
```
```

- [ ] **Step 2: Commit**

```bash
git add tests/e2e/prompts/host.md
git commit -m "feat: add host role test prompt for two-machine E2E"
```

---

### Task 6: Create joiner test prompt

**Files:**
- Create: `tests/e2e/prompts/joiner.md`

- [ ] **Step 1: Write the joiner prompt**

File content:

```markdown
# Joiner (Machine B) — Steam Invite E2E Test

You are the JOINER. Your job: launch the game, wait at the main menu, accept
the Steam invite when it arrives, and verify you land in the host's lobby.

**Setup:** Set `$env:EK_COORDINATION_DIR` to the shared coordination directory
before starting. Both machines must have read/write access.

---

## Step 1: Signal readiness

Run in shell:
```powershell
$dir = "$env:EK_COORDINATION_DIR"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
"ready" | Out-File -Encoding utf8 "$dir\joiner.state"
Get-Content "$dir\joiner.state"
```

## Step 2: Launch the game

Call `launch_game`, then `wait_for_game(timeout=90)`, then `focus_game`.

## Step 3: Wait for title screen and dismiss overlay

Press `shift+tab` to dismiss the Steam overlay. Wait for the title screen:
use `wait_for_pixel(x=100, y=400, r=80, g=40, b=40, tolerance=30, timeout=60)`.

Save a screenshot as `joiner_01_title`.

## Step 4: Wait at main menu

Click START GAME, wait 0.5s, click START GAME again, wait 8s for splash.
The main menu should appear. Do NOT navigate further — the Steam invite
will route you into the correct lobby.

Save a screenshot as `joiner_02_main_menu`.

## Step 5: Wait for invite

Poll the host's state file until "invite_sent". Run in shell:
```powershell
$dir = "$env:EK_COORDINATION_DIR"
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline) {
    $state = (Get-Content "$dir\host.state" -ErrorAction SilentlyContinue | Select-Object -First 1) -replace '\s+$'
    if ($state -eq "invite_sent") { Write-Host "INVITED"; break }
    if ($state -eq "error") { Write-Error "Host reported error"; exit 1 }
    Start-Sleep 2
}
Write-Host "Done waiting"
```

## Step 6: Accept the Steam invite

The Steam overlay should show an invite popup. Look for it with a screenshot.
The invite notification typically appears in the bottom-right corner of the
screen. Use `wait_for_pixel` to detect a bright pixel from the invite UI,
or take a screenshot and describe what you see.

If the invite popup is visible, focus the game window with `focus_game`,
then press `shift+tab` to open the overlay (if it closed). The invite should
have an "Accept" button — click it.

If the game has a "Join Game" button visible (sometimes the game shows its
own prompt), click it instead.

Wait up to 10s for the game to transition to the lobby.

## Step 7: Verify lobby join

Save a screenshot as `joiner_03_lobby`.

Look at the overlay panel — it should show the lobby code and 2 party members.
Verify the host's name is visible in the panel.

Read `read_logs(errors_only=true)` — should be clean.

## Step 8: Signal joined

Run in shell:
```powershell
"in_lobby" | Out-File -Encoding utf8 "$env:EK_COORDINATION_DIR\joiner.state"
```

## Step 9: Wait for host to complete

Poll the host's state file until "done". Run in shell:
```powershell
$dir = "$env:EK_COORDINATION_DIR"
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline) {
    $state = (Get-Content "$dir\host.state" -ErrorAction SilentlyContinue | Select-Object -First 1) -replace '\s+$'
    if ($state -eq "done") { Write-Host "DONE"; break }
    if ($state -eq "error") { Write-Error "Host reported error"; exit 1 }
    Start-Sleep 2
}
Write-Host "Done waiting"
```

## Step 10: Close game

Call `close_game`. Read `read_logs(errors_only=true)` one final time.

## Report

If all steps completed without error:
```
-- PASS -- Joiner completed successfully. Joined host lobby.
Screenshots: joiner_01_title, joiner_02_main_menu, joiner_03_lobby
```

If any step failed:
```
-- FAIL -- <step description>
Screenshot saved at error point.
```
```

- [ ] **Step 2: Commit**

```bash
git add tests/e2e/prompts/joiner.md
git commit -m "feat: add joiner role test prompt for two-machine E2E"
```

---

### Task 7: Final verification

- [ ] **Step 1: List all changed and new files**

```bash
git status
```
Expected: working tree clean, all changes committed.

- [ ] **Step 2: Verify all 5 files exist with expected content**

```bash
git log --oneline -6
```
Expected: 6 new commits on main (one for each task, plus the spec commit).

- [ ] **Step 3: Smoke test the MCP server still starts**

```powershell
python -c "import sys; sys.path.insert(0, 'tests/e2e'); from ek_test_server import mcp, STEAM_FRIEND_NAME; tools = sorted([t.name for t in mcp._tool_manager._tools.values()]); print('Tools:', tools); print('STEAM_FRIEND_NAME:', repr(STEAM_FRIEND_NAME))"
```
Expected: Tools list includes `wait_for_pixel`, `STEAM_FRIEND_NAME` is empty string (existing config doesn't have the field).

- [ ] **Step 4: Verify bootstrap script syntax**

```powershell
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    (Resolve-Path "tests/e2e/bootstrap_machine_b.ps1"),
    [ref]$null, [ref]$null
)
if ($ast) { Write-Host "Syntax OK" } else { throw "Parse failed" }
```
Expected: `Syntax OK`
