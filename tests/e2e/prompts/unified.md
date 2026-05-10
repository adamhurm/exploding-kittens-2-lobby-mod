# Unified Two-Machine E2E Test — Invite All Flow

You control BOTH machines from this single session.

- **Machine A (Host):** `mcp__ek-test__*` tools
- **Machine B (Joiner):** `mcp__ek-test-b__*` tools

**Prerequisites:** Machine B's MCP HTTP server must be running (bootstrapped via
`bootstrap_machine_b.ps1`). Machine B's `ek_test_config.json` must have
`steam_friend_name` set to the Host's Steam persona name.

**Orientation:** At any point, take a screenshot of the confused machine and
read the key buttons/text on screen. Each step lists expected landmarks.
If you see a landmark from a different step, treat that as your true current
position and re-enter the flow from that step.

---

## Step 1: Launch both games

Launch both machines' games in parallel:

- Call `mcp__ek-test__launch_game()` (Machine A)
- Call `mcp__ek-test-b__launch_game()` (Machine B)

Then wait for both processes:

- `mcp__ek-test__wait_for_game(timeout=90)` (Machine A)
- `mcp__ek-test-b__wait_for_game(timeout=90)` (Machine B)

Then focus both windows:

- `mcp__ek-test__focus_game()` (Machine A)
- `mcp__ek-test-b__focus_game()` (Machine B)

## Step 2: Wait for title screens

**Landmark:** dark-red background, "START GAME" button visible.

Wait for both machines to reach the title screen:

- `mcp__ek-test__wait_for_pixel(x=100, y=400, r=80, g=40, b=40, tolerance=30, timeout=60)` (A)
- `mcp__ek-test-b__wait_for_pixel(x=100, y=400, r=80, g=40, b=40, tolerance=30, timeout=60)` (B)

Save screenshots as `unified_01_title_a` and `unified_01_title_b`.

## Step 3: Machine A — Navigate to lobby

**Landmarks (in order):** START GAME → PLAY → PLAY WITH FRIENDS → CREATE A GAME
→ mod overlay panel with lobby code.

On Machine A only (`mcp__ek-test__*`):

Take a screenshot. Click START GAME. Wait 0.5s. Click START GAME again
(dismisses Marmalade splash). Wait 8s for publisher splash.

Take a screenshot. Click PLAY. Wait 2.5s.

Take a screenshot. Click PLAY WITH FRIENDS. Wait 2.5s.

Take a screenshot. Click CREATE A GAME. Wait 4s.

Save a screenshot as `unified_02_host_lobby`.

## Step 4: Machine B — Navigate to main menu

**Landmark:** "PLAY" button on the main menu.

On Machine B only (`mcp__ek-test-b__*`):

Click START GAME. Wait 0.5s. Click START GAME again. Wait 8s for splash.

Save a screenshot as `unified_02_joiner_menu`. Confirm the PLAY button is
visible. Do NOT navigate further — the invite will route the joiner into the
host's lobby automatically.

## Step 5: Machine A — Verify lobby and send invite

**Landmark:** mod overlay panel expanded, showing lobby code and "INVITE ALL"
button.

On Machine A:

Take a screenshot. Confirm the overlay panel shows a lobby code.

Read logs: `mcp__ek-test__read_logs(errors_only=true)` — should be clean.

Click the "INVITE ALL" button in the mod overlay panel.

Wait 1s. Save a screenshot as `unified_03_invite_sent`.

## Step 6: Machine B — Accept the Steam invite

**Landmark:** Steam toast notification in the bottom-right corner.

On Machine B:

The Steam invite toast should appear within a few seconds. Take a screenshot
and look for the toast in the bottom-right corner. Click it to accept the invite.

Wait up to 15s for the game to transition into the lobby. Use
`mcp__ek-test-b__wait_for_pixel()` targeting a known pixel in the mod overlay
panel, or take a screenshot and confirm the overlay panel is visible.

Save a screenshot as `unified_04_joiner_lobby`.

## Step 7: Both machines — Verify lobby

Both machines should now show the mod overlay panel with 2 party members.

- Machine A: Screenshot as `unified_05_host_joined`. Verify joiner's name in panel.
- Machine B: Screenshot as `unified_05_joiner_joined`. Verify host's name in panel.

Read logs on both:
- `mcp__ek-test__read_logs(errors_only=true)`
- `mcp__ek-test-b__read_logs(errors_only=true)`

Both should be clean.

## Step 8: Cleanup

Close both games:
- `mcp__ek-test__close_game()`
- `mcp__ek-test-b__close_game()`

Read final logs on both with `errors_only=true`.

## Report

If all steps completed without error:
```
-- PASS -- Unified two-machine test completed.
Host created lobby, invited joiner, joiner joined successfully.
Screenshots: unified_01_title_a, unified_01_title_b,
             unified_02_host_lobby, unified_02_joiner_menu,
             unified_03_invite_sent,
             unified_04_joiner_lobby,
             unified_05_host_joined, unified_05_joiner_joined
```

If any step failed:
```
-- FAIL -- <step description> on <machine>
Screenshot saved at error point.
```
