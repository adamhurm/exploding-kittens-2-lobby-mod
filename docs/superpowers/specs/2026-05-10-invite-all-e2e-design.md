# E2E Test: Mod Invite All Flow

**Date:** 2026-05-10
**Scope:** Update two-machine E2E prompts to test the mod's "Invite All" button instead of the Steam overlay friend-list invite.

## Goal

Verify that clicking "Invite All" in the mod overlay produces the same outcome as the game's built-in Invite Friend flow: the joiner lands in the host's lobby with 2 party members showing.

## Prerequisite

The joiner's Steam ID is pre-configured in the host's `config.json` friend list before the test runs. No UI steps needed for friend-list setup.

## Host flow (host.md Step 6)

1. After creating a lobby, the mod overlay panel is already expanded.
2. Take a screenshot to confirm the "INVITE ALL" button is visible.
3. Click "INVITE ALL".
4. Save screenshot as `host_03_invite_sent`.
5. Write `invite_sent` to the coordination state file.

No `shift+tab` or overlay open/close keystrokes.

## Joiner flow (joiner.md Step 6)

1. After polling for `invite_sent`, take a screenshot.
2. Look for the Steam toast notification in the bottom-right corner.
3. Click it to accept.
4. Wait up to 15s for the game to transition to the lobby.
5. Confirm the mod overlay panel appears (signals successful join).

## Success criteria

- Joiner overlay panel shows the lobby code and 2 party members.
- Host overlay panel shows 2 party members.
- Both `read_logs(errors_only=true)` return clean.

## Agent orientation

At any point an agent can re-orient itself by taking a screenshot and reading the key button/text visible on screen. Each step has an expected landmark (e.g. "START GAME", "PLAY", "INVITE ALL", the lobby code panel). If the agent sees a landmark from a different step than expected, it should treat that as its true current position and re-enter the flow from that step rather than blindly continuing from where it assumed it was.

## Out of scope

- `test_new_room.py` (single-machine, no invite flow).
- Testing the game's built-in invite buttons directly.
- Log verification that `MGS.Platform.InviteFriend` was used over the fallback.
