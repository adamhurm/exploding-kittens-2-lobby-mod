# Uninstall Oneliner — Design Spec

**Date:** 2026-05-09  
**Status:** Approved

## Summary

Add `irm ek.bring-us.com/uninstall | iex` as a one-line uninstaller, mirroring the existing install oneliner at `irm ek.bring-us.com/get | iex`.

## New file: `hosting/ek.bring-us.com/public/uninstall`

A standalone PowerShell script derived from `distribute/Uninstall.ps1` with two changes:

1. **Header comment** updated to reflect the hosted URL:
   ```
   EKLobbyMod — One-line uninstaller
   Hosted at: https://ek.bring-us.com/uninstall
   Usage: irm ek.bring-us.com/uninstall | iex
   ```

2. **`Read-Host "Press Enter to exit"` calls removed.** In an `iex` context the terminal session ends when the script finishes, so the pause has no effect and adds unnecessary friction.

Everything else is identical to `distribute/Uninstall.ps1`: `Find-SteamLibraries`, `Find-GamePath`, plugin file removal, tray file removal, optional config deletion.

## What does NOT change

- **`release.ps1`** — no version strings in the uninstall script, so it doesn't need to be in the bump list.
- **CI/CD** — the Cloudflare Pages deploy job already deploys all of `hosting/ek.bring-us.com/public/`; the new file is picked up automatically.
- **`distribute/Uninstall.ps1`** — unchanged. It remains the canonical local/bundled uninstaller.

## Website update: `hosting/ek.bring-us.com/public/index.html`

Add an uninstall oneliner block near the existing install oneliner (around line 658). Mirrors the install block structure: label, `<code>` with copy button.

```
To uninstall — paste into PowerShell:
  Set-ExecutionPolicy Bypass -Scope Process -Force; irm ek.bring-us.com/uninstall | iex
```

## Sync risk

`hosting/ek.bring-us.com/public/uninstall` and `distribute/Uninstall.ps1` will be kept in sync manually. Uninstall logic is stable (removes a fixed set of named files), so drift is unlikely. Any future changes to `distribute/Uninstall.ps1` should be mirrored to the hosted file.
