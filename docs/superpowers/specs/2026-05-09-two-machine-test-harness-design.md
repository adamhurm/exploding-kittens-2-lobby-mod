# Two-Machine E2E Test Harness Design

**Date:** 2026-05-09  
**Status:** Approved

## Problem

The E2E test harness currently controls a single game instance on one machine. Steam invite join/leave — the core multiplayer feature — cannot be tested without two game instances on separate Steam accounts that interact through the Steam overlay.

## Goal

Extend the test harness to support two Windows machines, each running its own game instance with its own Steam account, coordinated via a shared filesystem path (`\\<network-share>\EKTest`). Each machine runs an identical MCP server. Two Claude Code sessions — one per machine — execute role-specific test prompts that synchronize through a simple file-based mailbox protocol.

## Architecture

```
Machine A (host)                              Machine B (joiner)
┌──────────────────────────┐                  ┌──────────────────────────┐
│ Claude Code              │                  │ Claude Code              │
│  ├─ MCP → ek_test_server │                  │  ├─ MCP → ek_test_server │
│  └─ shell → coord files ─┼──┐            ┌──┼─ shell → coord files    │
└──────────────────────────┘  │            │  └──────────────────────────┘
                              ▼            ▼
                   \\<network-share>\EKTest\ek_test_coordination\
                     host.state, joiner.state
```

The MCP server stays game-only (no coordination logic). Coordination lives entirely in the Claude Code shell layer, reading and writing state files on the shared filesystem.

## MCP Server Changes

### Config extension

`ek_test_config.json` gains one optional field:

```json
{
  "game_dir": "D:\\...",
  "steam_app_id": 2999030,
  "steam_friend_name": "OtherSteamPersona"
}
```

`steam_friend_name` — the Steam persona name of the other machine's Steam account. Used to locate them in the Steam overlay friend list when sending invites.

### New tool: `wait_for_pixel`

```python
@mcp.tool()
def wait_for_pixel(x: int, y: int, r: int, g: int, b: int,
                   tolerance: int = 10, timeout: int = 30) -> str:
    """Poll pixel (x, y) until it matches (r, g, b) within tolerance.
    Returns "matched" or "timeout after Ns"."""
```

Uses `mss` grab in a tight 0.3s loop. Avoids the latency of repeated `screenshot()` calls through MCP when polling for UI transitions (overlay open, invite popup, lobby join).

### No other changes

The 9 existing tools (`launch_game`, `wait_for_game`, `focus_game`, `screenshot`, `click`, `press_key`, `type_text`, `close_game`, `read_logs`) are sufficient for all multiplayer test interactions. No coordination tools are added — coordination lives at the shell level.

## Coordination Protocol

### Directory

```
\\<network-share>\EKTest\ek_test_coordination\
  host.state      — single-line state string
  joiner.state    — single-line state string
```

### Format

Each `.state` file contains one line: the current state value. Writers write to a temp file then move (atomic on SMB). Readers read directly. No JSON — single words avoid parse errors.

### State values

| Value | Who writes | Meaning |
|-------|-----------|---------|
| `ready` | Both | Machine is booted and waiting |
| `invite_sent` | Host | Steam invite has been sent |
| `in_lobby` | Joiner | Joined the host's lobby |
| `done` | Both | Completed its role successfully |
| `error` | Both | A step failed; diagnostic info written alongside |

### Flow

| Step | Host (Machine A) | Joiner (Machine B) |
|------|----------|-----------|
| 1 | Writes `host.state = "ready"` | Writes `joiner.state = "ready"` |
| 2 | Launches game, navigates to lobby | Launches game, waits at main menu |
| 3 | Opens Steam overlay, sends invite to `steam_friend_name` | Polls for Steam invite popup |
| 4 | Writes `host.state = "invite_sent"` | Accepts invite, waits for lobby join |
| 5 | — | Writes `joiner.state = "in_lobby"` |
| 6 | Reads `joiner.state`, verifies `"in_lobby"` | — |
| 7 | Screenshot verifies joiner in overlay panel | Screenshot verifies host in overlay panel |
| 8 | Writes `host.state = "done"` | Writes `joiner.state = "done"` |
| 9 | Closes game | Closes game |

### Error handling

Every polling step has a timeout. On timeout:
- The failing session writes its state to `"error"`
- Both sessions check for `"error"` in the other's state before proceeding
- Diagnostic screenshot saved with tag `error_<step>`
- BepInEx logs dumped (errors_only) into the failure report

## Test Prompts

Two version-controlled prompt files drive the sessions:

- `tests/e2e/prompts/host.md` — given to Claude Code on Machine A
- `tests/e2e/prompts/joiner.md` — given to Claude Code on Machine B

Each is a self-contained 60-80 line sequence of MCP tool calls and shell coordination commands. Both save screenshots at each stage and produce a `-- PASS / FAIL --` report.

### Host prompt skeleton

1. Write `host.state = "ready"`
2. Launch game, wait, focus, dismiss overlay
3. Navigate: START GAME → PLAY → PLAY WITH FRIENDS → CREATE A GAME
4. Screenshot, verify lobby panel visible
5. Open Steam overlay (`shift+tab`)
6. Screenshot, locate `steam_friend_name` in friend list, click Invite
7. Close overlay (`escape`)
8. Write `host.state = "invite_sent"`
9. Poll `joiner.state` until `"in_lobby"`
10. Screenshot, verify joiner name in overlay party panel
11. Write `host.state = "done"`
12. Close game

### Joiner prompt skeleton

1. Write `joiner.state = "ready"`
2. Launch game, wait, focus, dismiss overlay
3. Wait at main menu
4. Poll `host.state` until `"invite_sent"`
5. Watch for Steam invite popup (`wait_for_pixel` or screenshot loop)
6. Accept invite
7. Wait for lobby join
8. Screenshot, verify host name in overlay party panel
9. Write `joiner.state = "in_lobby"`
10. Poll `host.state` until `"done"`
11. Close game

## Machine B Bootstrap

Machine B doesn't need the repo. An install script ships in the repo and is copied to `\\<network-share>\EKTest\ek_test_bootstrap\` by Machine A.

### Script: `tests/e2e/bootstrap_machine_b.ps1`

```powershell
param(
    [Parameter(Mandatory=$true)] [string] $GameDir,
    [Parameter(Mandatory=$true)] [string] $SteamAppId,
    [Parameter(Mandatory=$true)] [string] $FriendSteamName,
    [string] $BootstrapShare = "\\<network-share>\EKTest\ek_test_bootstrap",
    [string] $InstallDir = "$env:LOCALAPPDATA\ek-test-b"
)
```

Does: pip install deps → create install dir → copy MCP server from share → write config → write `.mcp.json` → print instructions.

Machine A publishes the MCP server to the share with:
```
copy-item tests/e2e/ek_test_server.py \\<network-share>\EKTest\ek_test_bootstrap\
```

### What Machine B ends up with

```
%LOCALAPPDATA%\ek-test-b\
  .mcp.json
  tests\e2e\
    ek_test_server.py
    ek_test_config.json
```

Open Claude Code in `%LOCALAPPDATA%\ek-test-b\` — the MCP server is picked up automatically.

## Files Changed / Added

| File | Action | Purpose |
|------|--------|---------|
| `tests/e2e/ek_test_server.py` | Edit | Add `wait_for_pixel` tool, read `steam_friend_name` from config |
| `tests/e2e/ek_test_config.json.example` | Edit | Document `steam_friend_name` field |
| `tests/e2e/bootstrap_machine_b.ps1` | New | One-shot bootstrap script for Machine B |
| `tests/e2e/prompts/host.md` | New | Host role test prompt |
| `tests/e2e/prompts/joiner.md` | New | Joiner role test prompt |

No changes to `.mcp.json`, `requirements.txt`, or any `src/` code.
