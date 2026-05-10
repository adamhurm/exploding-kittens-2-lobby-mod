#!/usr/bin/env python3
"""
Minimum E2E validation loop: launch game -> navigate to a new game room.

Verifies that a user can reach the Play With Friends lobby without errors.
Saves screenshots at each stage for visual inspection.
Asserts that BepInEx logs contain no errors or warnings.

All verification is local -- no external API calls.

Usage:
    python tests/e2e/test_new_room.py

Coordinates assume 1392x878 fullscreen resolution. Update CLICKS below if
your game runs at a different resolution.
"""
import io
import sys
import time
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).parent))
from ek_test_server import click, close_game, focus_game, launch_game
from ek_test_server import press_key, read_logs, screenshot, wait_for_game

# ---------------------------------------------------------------------------
# Click targets -- update if your game resolution differs from 1392x878.
# ---------------------------------------------------------------------------
CLICKS = {
    "start_game":        (390,  745),   # Title screen: "START GAME"
    "play":              (240,  330),   # Main menu: "PLAY"
    "play_with_friends": (857,  635),   # Game mode: "PLAY WITH FRIENDS"
    "create_game":       (855,  450),   # Create or join: "CREATE A GAME"
}

# Pixel coordinates used by _wait_for_screen to detect each screen.
WAIT_PIXELS = {
    "START GAME": (100, 400),  # Title screen background (dark red); white during Unity splash
    "MAIN MENU":  (240, 330),  # PLAY button (vivid red); dark during splash/overlay
}

# ---------------------------------------------------------------------------

_failures: list[str] = []
_screenshot_dir = Path(__file__).parent


def _pixel_is_bright(png: bytes, x: int, y: int, threshold: int = 200) -> bool:
    """Return True if pixel (x, y) has all RGB channels above threshold."""
    img = Image.open(io.BytesIO(png))
    r, g, b = img.getpixel((x, y))[:3]
    return r > threshold and g > threshold and b > threshold


def _pixel_is_dark_red(png: bytes, x: int, y: int) -> bool:
    """Return True if pixel (x, y) matches the dark-red title screen background."""
    img = Image.open(io.BytesIO(png))
    r, g, b = img.getpixel((x, y))[:3]
    return r > 80 and g < 60 and b < 60


def _pixel_is_bright_red(png: bytes, x: int, y: int) -> bool:
    """Return True if pixel (x, y) is a vivid red (e.g. the PLAY button on main menu)."""
    img = Image.open(io.BytesIO(png))
    r, g, b = img.getpixel((x, y))[:3]
    return r > 160 and g < 80 and b < 80


def _pixel_is_mostly_dark(png: bytes, x: int, y: int) -> bool:
    """Return True if pixel (x, y) is near-black, indicating an overlay is active."""
    img = Image.open(io.BytesIO(png))
    r, g, b = img.getpixel((x, y))[:3]
    return r < 30 and g < 30 and b < 30


def _wait_for_screen(label: str, x: int, y: int, timeout: int = 60, check: str = "bright") -> bool:
    """Poll until a pixel condition at (x, y) is met.
    check: 'bright' (white), 'dark_red' (title bg), 'bright_red' (PLAY button).
    Returns False on timeout."""
    print(f"  Wait for {label} ...", end="  ", flush=True)
    deadline = time.time() + timeout
    while time.time() < deadline:
        png = screenshot().data
        if check == "dark_red":
            matched = _pixel_is_dark_red(png, x, y)
        elif check == "bright_red":
            matched = _pixel_is_bright_red(png, x, y)
        else:
            matched = _pixel_is_bright(png, x, y)
        if matched:
            print("ready")
            return True
        time.sleep(1)
    print(f"timeout after {timeout}s")
    _failures.append(f'"{label}" did not appear within {timeout}s.')
    return False


def _dismiss_overlay_if_present() -> None:
    """If a Steam/Discord overlay is darkening the screen, press Escape to close it."""
    png = screenshot().data
    if _pixel_is_mostly_dark(png, 696, 440):
        print("  (overlay detected -- pressing Escape)")
        focus_game()
        press_key("escape")
        time.sleep(1)


def _save_screenshot(tag: str) -> None:
    img = screenshot()
    path = _screenshot_dir / f"screenshot_{tag}.png"
    path.write_bytes(img.data)
    print(f"       screenshot -> {path.name}")


def _focused_click(label: str, x: int, y: int) -> None:
    """Focus the game window then click — prevents focus loss between steps."""
    focus_game()
    time.sleep(0.3)   # let the window fully accept foreground before clicking
    _step(label, click, x, y)


def _step(label: str, fn, *args, **kwargs) -> object:
    print(f"  {label} ...", end="  ", flush=True)
    result = fn(*args, **kwargs)
    print(result if isinstance(result, str) else "ok")
    return result


def run() -> None:
    print("\n-- E2E: launch -> Play With Friends -> Create Game --\n")

    # 1. Launch and wait for process
    _step("Launch game", launch_game)
    result = _step("Wait for process (90s)", wait_for_game, timeout=90)
    if "Timeout" in result:
        _failures.append("Game process did not appear within 90 seconds.")
        _report()
        return

    # Retry focus until the window is ready (not visible immediately after launch)
    print("  Focus window ...", end="  ", flush=True)
    for _ in range(10):
        r = focus_game()
        if "Focused" in r:
            print(r)
            break
        time.sleep(2)
    else:
        print("not found after retries")
        _failures.append("Game window never became focusable.")
        _step("Close game", close_game)
        _report()
        return

    if not _wait_for_screen("START GAME", *WAIT_PIXELS["START GAME"], timeout=60, check="dark_red"):
        _step("Close game", close_game)
        _report()
        return
    _save_screenshot("01_title")

    # 2. Title screen -> main menu
    #    Marmalade Game Studio publisher splash plays between title and main menu
    #    (no reliable pixel anchor — use a generous fixed sleep to clear it).
    _focused_click("Click START GAME (dismiss any overlay)", *CLICKS["start_game"])
    time.sleep(0.5)
    _focused_click("Click START GAME", *CLICKS["start_game"])
    time.sleep(8)
    _save_screenshot("02_main_menu")

    # 3. Main menu -> game mode
    _focused_click("Click PLAY", *CLICKS["play"])
    time.sleep(2.5)
    _save_screenshot("03_game_mode")

    # 4. Game mode -> create or join
    _focused_click("Click PLAY WITH FRIENDS", *CLICKS["play_with_friends"])
    time.sleep(2.5)
    _save_screenshot("04_create_or_join")

    # 5. Create game -> lobby
    _focused_click("Click CREATE A GAME", *CLICKS["create_game"])
    time.sleep(4)
    _save_screenshot("05_game_room")

    # 6. Assert: no BepInEx errors or warnings
    print("  Check BepInEx logs ...", end="  ", flush=True)
    logs = read_logs(kind="output", errors_only=True)
    if logs == "No errors or warnings found":
        print("clean")
    else:
        print("ERRORS FOUND")
        _failures.append(f"BepInEx errors/warnings:\n{logs}")

    # 7. Done
    _step("Close game", close_game)
    _report()


def _report() -> None:
    print()
    if _failures:
        print(f"FAIL -- {len(_failures)} assertion(s) failed:")
        for f in _failures:
            for line in f.splitlines():
                print(f"  * {line}")
        sys.exit(1)
    else:
        print("PASS -- launch -> main menu -> Play With Friends -> game room")
        print(f"Screenshots in: {_screenshot_dir}")


if __name__ == "__main__":
    run()
