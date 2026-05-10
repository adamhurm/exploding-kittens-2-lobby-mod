import ctypes
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
STEAM_FRIEND_NAME = _config.get("steam_friend_name", "")
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
    title = win32gui.GetWindowText(hwnd)
    win32gui.ShowWindow(hwnd, win32con.SW_RESTORE)

    # SetForegroundWindow is blocked from background processes unless we attach
    # to the foreground thread first (Windows restriction).
    u32 = ctypes.windll.user32
    k32 = ctypes.windll.kernel32
    fg_hwnd = u32.GetForegroundWindow()
    fg_tid = u32.GetWindowThreadProcessId(fg_hwnd, None)
    my_tid = k32.GetCurrentThreadId()
    if fg_tid and fg_tid != my_tid:
        u32.AttachThreadInput(my_tid, fg_tid, True)
    u32.SetForegroundWindow(hwnd)
    u32.BringWindowToTop(hwnd)
    if fg_tid and fg_tid != my_tid:
        u32.AttachThreadInput(my_tid, fg_tid, False)

    return f"Focused window: {title}"


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
def press_key(key: str) -> str:
    """Press a key or key combination. Use '+' for combos, e.g. 'escape', 'shift+tab'."""
    if "+" in key:
        pyautogui.hotkey(*key.split("+"))
    else:
        pyautogui.press(key)
    return f"Pressed: {key}"


@mcp.tool()
def type_text(text: str) -> str:
    """Send keystrokes for each character in text. For special chars use key names like 'enter'."""
    pyautogui.write(text, interval=0.05)
    return f"Typed: {text!r}"


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
    import argparse

    parser = argparse.ArgumentParser(description="EK E2E MCP server")
    parser.add_argument(
        "--transport",
        choices=["stdio", "http"],
        default="stdio",
        help="Transport mode (default: stdio)",
    )
    parser.add_argument(
        "--host",
        default="0.0.0.0",
        help="Bind address for HTTP mode (default: 0.0.0.0)",
    )
    parser.add_argument(
        "--port",
        type=int,
        default=8080,
        help="Port for HTTP mode (default: 8080)",
    )
    args = parser.parse_args()

    if args.transport == "http":
        mcp.settings.host = args.host
        mcp.settings.port = args.port
        mcp.run(transport="streamable-http")
    else:
        mcp.run()
