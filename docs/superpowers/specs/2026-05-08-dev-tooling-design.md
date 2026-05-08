# Dev Tooling — Overlay Mock Server Design

**Date:** 2026-05-08
**Status:** Approved

## Goal

A browser-based preview of all EKLobbyMod overlay UI states, so overlay CSS/HTML changes can be iterated without launching Exploding Kittens 2.

## File Structure

```
mock/
└── overlay.html     ← single self-contained file (HTML + inline CSS, no build step)
```

One file added to the repo root. No `npm install`, no build tool, no terminal command required to use.

## Live Reload Workflow

1. Open `mock/overlay.html` in VS Code
2. Click **Go Live** in the VS Code status bar (Live Server extension)
3. Browser opens showing all four overlay states side-by-side
4. Edit `overlay.html`, save → browser reloads instantly
5. Click **Stop** in the status bar when done

## States Rendered

All four states appear in a single horizontal flex row at 1× pixel dimensions, each with a small label below.

| # | State | Dimensions | Key visual |
|---|-------|-----------|------------|
| 1 | Minimized | 220×40 | EkBlack tab, 4px EkRed left accent, lobby code centered |
| 2 | Expanded | 300×400 | Full panel, friend list, INVITE ALL + REJOIN (dark) |
| 3 | Post-game | 300×400 | Expanded + yellow "Game over" prompt + REJOIN green |
| 4 | Countdown *(placeholder)* | 300×400 | Expanded + semi-transparent overlay, big countdown digit, cancel hint |

The countdown panel is a placeholder for the Group A auto-queue countdown feature. It is clearly marked with `<!-- TODO: Group A — auto-queue countdown -->` in the source.

## EK Brand Colors

Extracted from `src/EKLobbyMod/OverlayPanel.cs`. All defined as CSS variables at the top of `overlay.html`:

| CSS variable | Unity source | Hex |
|---|---|---|
| `--ek-black` | `EkBlack` (α 0.97) | `rgba(15,15,15,0.97)` |
| `--ek-red` | `EkRed` | `#81242D` |
| `--ek-off-white` | `EkOffWhite` | `#FCF8EE` |
| `--ek-dark` | `EkDark` | `#242424` |
| `--ek-red-dark` | `EkRedDark` | `#52141A` |
| `--ek-green` | Rejoin-active color | `#1E7A1E` |
| `--ek-yellow` | Rejoin prompt label | `#FFD94D` |

## Mock Data

Hardcoded directly in HTML. A comment block at the top of the file lists all values for easy editing:

- **Lobby code**: `EK-A3F9C12B`
- **Friends**: Alice (online ●), Bob (online ●, in room → shows kick button), Carol (offline ○), Dave (offline ○)
- **Countdown digit**: `3` — change to any number to preview different countdown steps

No JavaScript. All state differences are pure HTML/CSS — the post-game prompt is a visible `<div>` where it's hidden in the normal expanded state; the countdown is an absolutely-positioned overlay `<div>`.

## What This Unblocks

- Group A (Party UX): iterate countdown overlay and party indicator styles before touching C# code
- Group B (Invite & Discovery): any overlay additions can be mocked first
- General: color/spacing/layout changes validated in browser before a full game launch
