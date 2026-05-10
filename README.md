# EKLobbyMod

Add parties and auto-queueing to Exploding Kittens 2.

Your lobby code stays active between games — share it once and friends can rejoin every round without a new invite. When your whole party is back in the lobby after a match, a 5-second countdown fires and the next game starts automatically. A minimized overlay tab shows your lobby code and live party size at a glance, with an amber warning if anyone is running a different mod version.

## Install

**One-liner** — paste into PowerShell:

```
Set-ExecutionPolicy Bypass -Scope Process -Force; irm ek.bring-us.com/get | iex
```

Or download the [latest release](https://github.com/adamhurm/exploding-kittens-2-lobby-mod/releases/latest), unzip, and double-click `RunInstall.bat`.

Requires [Exploding Kittens 2](https://store.steampowered.com/app/2999030/Exploding_Kittens_2/) on Steam and Windows 10+.

## Use

1. Launch the game through Steam.
2. Your lobby code (e.g. `EK-A3F9C12B`) appears in the top-left overlay tab.
3. Share that code with a friend. They enter it in the game's join-by-code field and hit Join.
4. When everyone is in the lobby, press Start Game. After the match ends, the party automatically re-forms and queues for the next round.

Click the overlay tab to expand it — use the buttons there to rejoin your lobby or invite Steam friends.

## Uninstall

```
Set-ExecutionPolicy Bypass -Scope Process -Force; irm ek.bring-us.com/uninstall | iex
```
