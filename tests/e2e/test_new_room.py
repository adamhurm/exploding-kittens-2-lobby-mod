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
from ek_test_server import read_logs, screenshot, wait_for_game

# ---------------------------------------------------------------------------
# Click targets -- update if your game resolution differs from 1392x878.
# ---------------------------------------------------------------------------
CLICKS = {
    "start_game":        (390,  745),   # Title screen: "START GAME"
    "play":              (240,  325),   # Main menu: "PLAY"
    "play_with_friends": (870,  600),   # Game mode: "PLAY WITH FRIENDS"
    "create_game":       (855,  410),   # Create or join: "CREATE A GAME"
}

# A pixel that is white when the expected screen is fully rendered.
# Used by _wait_for_screen to avoid fixed sleeps.
WAIT_PIXELS = {
    "START GAME":    (390, 745),   # White text on dark-red title screen
}

# ---------------------------------------------------------------------------

_failures: list[str] = []
_screenshot_dir = Path(__file__).parent


def _pixel_is_bright(png: bytes, x: int, y: int, threshold: int = 200) -> bool:
    """Return True if pixel (x, y) has all RGB channels above threshold."""
    img = Image.open(io.BytesIO(png))
    r, g, b = img.getpixel((x, y))[:3]
    return r > threshold and g > threshold and b > threshold


def _wait_for_screen(label: str, x: int, y: int, timeout: int = 60) -> bool:
    """Poll until a bright (white) pixel appears at (x, y), indicating the
    expected screen is fully rendered. Returns False on timeout."""
    print(f"  Wait for {label} ...", end="  ", flush=True)
    deadline = time.time() + timeout
    while time.time() < deadline:
        png = screenshot().data
        if _pixel_is_bright(png, x, y):
            print("ready")
            return True
        time.sleep(1)
    print(f"timeout after {timeout}s")
    _failures.append(f'"{label}" did not appear within {timeout}s.')
    return False


def _save_screenshot(tag: str) -> None:
    img = screenshot()
    path = _screenshot_dir / f"screenshot_{tag}.png"
    path.write_bytes(img.data)
    print(f"       screenshot -> {path.name}")


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

    if not _wait_for_screen("START GAME", *WAIT_PIXELS["START GAME"], timeout=60):
        _step("Close game", close_game)
        _report()
        return
    _save_screenshot("01_title")

    # 2. Title screen -> main menu
    #    First click dismisses any overlay (Discord, etc.), second hits START GAME.
    _step("Click START GAME (dismiss any overlay)", click, *CLICKS["start_game"])
    time.sleep(0.5)
    _step("Click START GAME", click, *CLICKS["start_game"])
    time.sleep(2)
    _save_screenshot("02_main_menu")

    # 3. Main menu -> game mode
    _step("Click PLAY", click, *CLICKS["play"])
    time.sleep(1.5)
    _save_screenshot("03_game_mode")

    # 4. Game mode -> create or join
    _step("Click PLAY WITH FRIENDS", click, *CLICKS["play_with_friends"])
    time.sleep(1.5)
    _save_screenshot("04_create_or_join")

    # 5. Create game -> lobby
    _step("Click CREATE A GAME", click, *CLICKS["create_game"])
    time.sleep(3)
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
