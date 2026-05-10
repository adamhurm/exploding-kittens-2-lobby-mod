# E2E Test Harness Design

**Date:** 2026-05-09  
**Status:** Approved

## Problem

The mod has 48 xUnit unit tests covering config, secrets, and tray app logic, but IL2CPP constraints make it impossible to unit-test the game state machine (LobbyManager, OverlayPanel, Harmony patches) without a running game. The only way to verify real behavior — plugin load, overlay render, lobby join, Steam invite flow — is to drive the live game process.

## Goal

Give Claude a set of native MCP tools to launch Exploding Kittens 2, observe the screen, interact with it, and read BepInEx logs. Short-term: locally-driven single-player smoke tests. Longer-term: two-agent multiplayer tests.

## Architecture

```
Claude Code  ←—MCP protocol—→  ek_test_server.py  ←—Win32/Python—→  Windows Desktop
                                (FastMCP, Python)                     (game, logs)
```

The server is registered in `.mcp.json` at the project root — Claude Code picks it up automatically on session start.

## Tools

| Tool | Signature | Returns |
|------|-----------|---------|
| `launch_game` | `()` | `str` |
| `wait_for_game` | `(timeout=60)` | `str` |
| `focus_game` | `()` | `str` |
| `screenshot` | `()` | `Image` (PNG) |
| `click` | `(x: int, y: int)` | `str` |
| `type_text` | `(text: str)` | `str` |
| `close_game` | `()` | `str` |
| `read_logs` | `(kind="output"\|"error", tail=200, errors_only=False)` | `str` |

## Screen Interaction Model

Coordinate-based. Claude calls `screenshot()`, receives a PNG it can see directly (multimodal), reasons about element positions, then calls `click(x, y)`. No stored coordinates — adapts to any resolution automatically.

## Log Analysis

`read_logs` returns the last `tail` lines by default. With `errors_only=True`, it extracts only `[Fatal]`, `[Error]`, `[Warning]` lines plus immediately-following stack trace lines. Returns "No errors or warnings found" when clean.

Log paths (from config):
- `<game_dir>/BepInEx/LogOutput.log`
- `<game_dir>/BepInEx/ErrorLog.log`

## Game Launch

`os.startfile(f"steam://rungameid/{steam_app_id}")` — Steam protocol ensures Steamworks initializes correctly. Steam App ID: `2999030`.

## Configuration

`tests/e2e/ek_test_config.json` (gitignored, machine-specific):
```json
{
  "game_dir": "D:\\Program Files (x86)\\Steam\\SteamApps\\common\\Exploding Kittens 2",
  "steam_app_id": 2999030
}
```
Committed template: `tests/e2e/ek_test_config.json.example`

## Dependencies

```
mcp[cli]>=1.0.0
mss>=9.0.0
pyautogui>=0.9.54
psutil>=5.9.0
pywin32>=306
Pillow>=10.0.0
```

## Verification (Smoke Test)

Start a new Claude Code session and run in order:
1. `launch_game()` — "Game launch requested …"
2. `wait_for_game(timeout=90)` — "Game running: PID …"
3. `focus_game()` — "Focused window: Exploding Kittens …"
4. `screenshot()` — Claude sees main menu
5. `read_logs(errors_only=True)` — "No errors or warnings found"
6. `close_game()` — "Terminated PIDs: …"

## Future: Two-Agent Multiplayer Test

Run two separate Claude Code sessions, each with its own `ek_test_config.json` pointing to a different Steam account / game instance. The MCP servers are identical — the agents coordinate via shared state (e.g., a temp file or a small HTTP endpoint) to exchange lobby codes and verify connection.
