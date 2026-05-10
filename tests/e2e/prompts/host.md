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

## Step 3: Wait for title screen

Wait for the title screen:
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

Your Steam friend list should be visible in the friend picker. The joiner's
Steam persona name is stored in the MCP server config as `steam_friend_name`.
Look at the screenshot and find their name in the friend list. Click on their
name to open the context menu, then click "Invite to Game".

Save a screenshot as `host_03_invite_sent`.

Wait 1s.

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
Screenshots: host_01_title, host_02_lobby, host_03_invite_sent, host_04_joined
```

If any step failed:
```
-- FAIL -- <step description>
Screenshot saved at error point.
```
