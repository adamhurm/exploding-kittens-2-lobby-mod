import json
import os
import re
import time
from pathlib import Path

import mss
import mss.tools
import psutil
import pyautogui
import win32con
import win32gui
from mcp.server.fastmcp import FastMCP, Image

_config_path = Path(__file__).parent / "ek_test_config.json"
_config = json.loads(_config_path.read_text())
GAME_DIR = Path(_config["game_dir"])
STEAM_APP_ID = _config["steam_app_id"]
_GAME_EXE = "ExplodingKittens"
_GAME_WINDOW = "Exploding Kittens"

mcp = FastMCP("ek-test")


@mcp.tool()
def launch_game() -> str:
    """Launch Exploding Kittens 2 via Steam (ensures Steamworks initializes correctly)."""
    os.startfile(f"steam://rungameid/{STEAM_APP_ID}")
    return f"Game launch requested via steam://rungameid/{STEAM_APP_ID}"


@mcp.tool()
def wait_for_game(timeout: int = 60) -> str:
    """Poll until the game process appears. Call after launch_game."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        for proc in psutil.process_iter(["name", "pid"]):
            if _GAME_EXE in (proc.info["name"] or ""):
                return f"Game running: PID {proc.info['pid']}"
        time.sleep(2)
    return f"Timeout after {timeout}s — game process not found"


@mcp.tool()
def focus_game() -> str:
    """Bring the game window to the foreground so clicks land correctly."""
    found: list[int] = []

    def _cb(hwnd: int, _: object) -> None:
        if win32gui.IsWindowVisible(hwnd) and _GAME_WINDOW in win32gui.GetWindowText(hwnd):
            found.append(hwnd)

    win32gui.EnumWindows(_cb, None)
    if not found:
        return "Game window not found"
    hwnd = found[0]
    win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)
    win32gui.SetForegroundWindow(hwnd)
    return f"Focused window: {win32gui.GetWindowText(hwnd)}"


@mcp.tool()
def screenshot() -> Image:
    """Capture the primary monitor and return it as a PNG image you can inspect."""
    with mss.mss() as sct:
        raw = sct.grab(sct.monitors[1])
        png = mss.tools.to_png(raw.rgb, raw.size)
    return Image(data=png, format="png")


@mcp.tool()
def click(x: int, y: int) -> str:
    """Left-click at screen coordinates (x, y)."""
    pyautogui.click(x, y)
    return f"Clicked at ({x}, {y})"


@mcp.tool()
def type_text(text: str) -> str:
    """Send keystrokes for each character in text. For special chars use key names like 'enter'."""
    pyautogui.write(text, interval=0.05)
    return f"Typed: {text!r}"


@mcp.tool()
def close_game() -> str:
    """Terminate the game process."""
    killed: list[str] = []
    for proc in psutil.process_iter(["name", "pid"]):
        if _GAME_EXE in (proc.info["name"] or ""):
            proc.terminate()
            killed.append(str(proc.info["pid"]))
    return f"Terminated PIDs: {', '.join(killed)}" if killed else "Game not running"


@mcp.tool()
def read_logs(kind: str = "output", tail: int = 200, errors_only: bool = False) -> str:
    """Read BepInEx log files.

    kind: 'output' reads LogOutput.log, 'error' reads ErrorLog.log.
    tail: return only the last N lines.
    errors_only: when True, return only [Fatal]/[Error]/[Warning] lines plus
                 any immediately-following stack trace lines.
    """
    filename = "LogOutput.log" if kind == "output" else "ErrorLog.log"
    log_path = GAME_DIR / "BepInEx" / filename
    if not log_path.exists():
        return f"Log file not found: {log_path}"

    lines = log_path.read_text(encoding="utf-8", errors="replace").splitlines(keepends=True)
    lines = lines[-tail:]

    if not errors_only:
        return "".join(lines)

    result: list[str] = []
    capturing = False
    for line in lines:
        if re.search(r"\[(Fatal|Error|Warning)\]", line):
            result.append(line)
            capturing = True
        elif capturing and (line.startswith((" ", "\t")) or " at " in line):
            result.append(line)
        else:
            capturing = False

    return "".join(result) if result else "No errors or warnings found"


if __name__ == "__main__":
    mcp.run()
