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
