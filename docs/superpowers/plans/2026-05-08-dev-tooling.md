# Dev Tooling — Overlay Mock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `mock/overlay.html` — a single self-contained HTML+CSS file that renders all four EKLobbyMod overlay states side-by-side for browser-based UI iteration without launching the game.

**Architecture:** One HTML file with an inline `<style>` block. CSS custom properties encode the full EK brand palette extracted from `src/EKLobbyMod/OverlayPanel.cs`. Four `<div>` blocks in a flex row represent each overlay state at 1× game pixel dimensions. No JavaScript, no build step, no dependencies.

**Tech Stack:** HTML5, CSS3, VS Code Live Server extension (already available — no install needed)

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Create | `mock/overlay.html` | All four overlay states, CSS variables, mock data comment |

---

### Task 1: File scaffold, CSS variables, and page chrome

**Files:**
- Create: `mock/overlay.html`

- [ ] **Step 1: Create the file with DOCTYPE, head, and CSS variables**

Create `mock/overlay.html` with this exact content:

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>EKLobbyMod — Overlay Preview</title>
<style>
  /* ── Reset ── */
  * { box-sizing: border-box; margin: 0; padding: 0; }

  /* ── EK brand palette (source: src/EKLobbyMod/OverlayPanel.cs) ── */
  :root {
    --ek-black:     rgba(15,  15,  15,  0.97); /* EkBlack    0.059,0.059,0.059 */
    --ek-red:       #81242D;                   /* EkRed      0.506,0.141,0.176 */
    --ek-off-white: #FCF8EE;                   /* EkOffWhite 0.988,0.972,0.933 */
    --ek-dark:      #242424;                   /* EkDark     0.14,0.14,0.14    */
    --ek-red-dark:  #52141A;                   /* EkRedDark  0.32,0.08,0.10    */
    --ek-green:     #1E7A1E;                   /* Rejoin-active 0.12,0.48,0.12 */
    --ek-yellow:    #FFD94D;                   /* Rejoin prompt 1,0.85,0.3     */
    --ek-row-a:     #292929;                   /* Friend row even  0.16,0.16   */
    --ek-row-b:     #1C1C1C;                   /* Friend row odd   0.11,0.11   */
  }

  /* ── Page ── */
  body {
    background: #111;
    font-family: 'Segoe UI', system-ui, sans-serif;
    padding: 32px;
    color: var(--ek-off-white);
  }
  h1 {
    font-size: 13px;
    letter-spacing: 2px;
    text-transform: uppercase;
    color: #666;
    margin-bottom: 4px;
  }
  .subtitle {
    font-size: 11px;
    color: #444;
    margin-bottom: 32px;
  }

  /* ── States grid ── */
  .states-grid {
    display: flex;
    gap: 40px;
    flex-wrap: wrap;
    align-items: flex-end;
  }
  .state-wrapper {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
  }
  .state-label {
    font-size: 10px;
    letter-spacing: 1.5px;
    text-transform: uppercase;
    color: #555;
  }
</style>
</head>
<body>

<!-- MOCK DATA — edit these to test different scenarios
     Lobby code : EK-A3F9C12B
     Friends    : Alice (online), Bob (online + in room → kick btn), Carol (offline), Dave (offline)
     Countdown  : shows "3" — change the digit to test other numbers
-->

<h1>EKLobbyMod — Overlay Preview</h1>
<p class="subtitle">All states at 1× scale · edit &amp; save to reload · VS Code Live Server</p>

<div class="states-grid">
  <!-- states go here in Tasks 2–7 -->
</div>

</body>
</html>
```

- [ ] **Step 2: Open in VS Code Live Server and verify**

Right-click `mock/overlay.html` in the VS Code explorer → "Open with Live Server".
Expected: dark page loads in the browser with the heading and subtitle visible, no errors in the browser console.

- [ ] **Step 3: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — scaffold, CSS vars, page chrome"
```

---

### Task 2: State 1 — Minimized tab

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add CSS for the minimized tab inside `<style>`**

Add after the `.state-label` block:

```css
  /* ── State 1: Minimized tab (220×40) ── */
  .min-tab {
    width: 220px;
    height: 40px;
    background: var(--ek-black);
    display: flex;
    align-items: center;
    border: 1px solid #222;
    cursor: pointer;
  }
  .min-tab .accent {
    width: 4px;
    height: 100%;
    background: var(--ek-red);
    flex-shrink: 0;
  }
  .min-tab .code-text {
    flex: 1;
    text-align: center;
    font-size: 13px;
    color: var(--ek-off-white);
    letter-spacing: 1px;
  }
```

- [ ] **Step 2: Add the minimized tab HTML inside `.states-grid`** (replace the `<!-- states go here -->` comment)

```html
  <!-- ── State 1: Minimized ── -->
  <div class="state-wrapper">
    <div class="min-tab">
      <div class="accent"></div>
      <span class="code-text">EK-A3F9C12B</span>
    </div>
    <span class="state-label">1 — Minimized</span>
  </div>

  <!-- states 2–4 go here -->
```

- [ ] **Step 3: Verify in browser**

Save the file. The browser should reload automatically and show:
- A 220×40 near-black pill/tab at the bottom of the flex row
- A 4px red left-edge accent strip
- "EK-A3F9C12B" centered in off-white text
- State label "1 — MINIMIZED" below it

- [ ] **Step 4: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 1 minimized tab"
```

---

### Task 3: State 2 — Expanded panel shell (header + code row)

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add CSS for the expanded panel shell**

Add after the `.min-tab .code-text` block:

```css
  /* ── States 2–4: Expanded panel (300×400) ── */
  .expanded-panel {
    width: 300px;
    height: 400px;
    background: var(--ek-black);
    border: 1px solid #222;
    display: flex;
    flex-direction: column;
    position: relative;
  }

  /* Header strip */
  .header-strip {
    height: 44px;
    background: var(--ek-red);
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 10px;
    flex-shrink: 0;
  }
  .header-title {
    font-size: 17px;
    font-weight: bold;
    color: var(--ek-off-white);
    letter-spacing: 1px;
  }
  .minimize-btn {
    width: 32px;
    height: 30px;
    background: rgba(0,0,0,0.35);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .minimize-bar {
    width: 65%;
    height: 3px;
    background: var(--ek-off-white);
  }

  /* Code row */
  .code-row {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 6px 6px 4px;
    flex-shrink: 0;
  }
  .code-label {
    font-size: 13px;
    color: var(--ek-off-white);
    white-space: nowrap;
    flex: 1;
  }
  .copy-btn, .icon-btn {
    font-size: 11px;
    color: var(--ek-off-white);
    background: var(--ek-dark);
    border: none;
    cursor: pointer;
    height: 22px;
    padding: 0 6px;
  }
  .icon-btn { width: 28px; }
```

- [ ] **Step 2: Add state 2 HTML inside `.states-grid`** (after state 1, replace `<!-- states 2–4 go here -->`)

```html
  <!-- ── State 2: Expanded ── -->
  <div class="state-wrapper">
    <div class="expanded-panel">
      <div class="header-strip">
        <span class="header-title">MY LOBBY</span>
        <div class="minimize-btn"><div class="minimize-bar"></div></div>
      </div>
      <div class="code-row">
        <span class="code-label">Code: EK-A3F9C12B</span>
        <button class="copy-btn">Copy</button>
        <button class="icon-btn">✏</button>
      </div>
      <!-- friend list, add btn, rejoin prompt, action row added in Tasks 4–5 -->
    </div>
    <span class="state-label">2 — Expanded</span>
  </div>

  <!-- states 3–4 go here -->
```

- [ ] **Step 3: Verify in browser**

Save. Expected:
- 300×400 near-black panel appears to the right of the minimized tab
- Red 44px header with "MY LOBBY" bold and a small minimize button (dark bar icon) on the right
- Code row: "Code: EK-A3F9C12B" with Copy and ✏ buttons

- [ ] **Step 4: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 2 panel shell (header + code row)"
```

---

### Task 4: State 2 — Friend list

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add CSS for the friend list**

Add after the `.icon-btn` block:

```css
  /* Friend list */
  .friend-list {
    flex: 1;
    overflow: hidden;
    margin: 0 6px;
  }
  .friend-row {
    display: flex;
    align-items: center;
    height: 22px;
    padding: 0 4px;
    font-size: 11px;
    color: var(--ek-off-white);
  }
  .friend-row:nth-child(odd)  { background: var(--ek-row-a); }
  .friend-row:nth-child(even) { background: var(--ek-row-b); }
  .friend-row.offline { color: #8a8a8a; }
  .friend-name { flex: 1; }
  .kick-btn {
    font-size: 9px;
    background: var(--ek-red);
    color: var(--ek-off-white);
    border: none;
    padding: 0 4px;
    height: 20px;
    cursor: pointer;
    margin-right: 4px;
  }
  .remove-btn {
    font-size: 10px;
    background: var(--ek-red-dark);
    color: var(--ek-off-white);
    border: none;
    width: 20px;
    height: 20px;
    cursor: pointer;
  }
  .add-btn {
    font-size: 12px;
    color: var(--ek-off-white);
    background: var(--ek-dark);
    border: none;
    padding: 0 8px;
    height: 22px;
    margin: 4px 6px 2px;
    cursor: pointer;
    align-self: flex-start;
    flex-shrink: 0;
  }
```

- [ ] **Step 2: Replace the `<!-- friend list ... -->` comment in state 2's panel**

```html
      <div class="friend-list">
        <div class="friend-row">
          <span class="friend-name">● Alice</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row">
          <span class="friend-name">● Bob</span>
          <button class="kick-btn">kick</button>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Carol</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Dave</span>
          <button class="remove-btn">✕</button>
        </div>
      </div>
      <button class="add-btn">+ Add</button>
      <!-- rejoin prompt and action row added in Task 5 -->
```

- [ ] **Step 3: Verify in browser**

Save. Expected:
- Friend list with 4 rows, alternating dark backgrounds (#292929 / #1C1C1C)
- Alice and Bob online (off-white text), Carol and Dave offline (gray text)
- Bob's row has a red "kick" button (he's the in-room player)
- All rows have a dark-red ✕ remove button on the right
- "+ Add" button below the list

- [ ] **Step 4: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 2 friend list"
```

---

### Task 5: State 2 — Rejoin prompt (hidden) + action buttons

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add CSS for rejoin prompt and action row**

Add after the `.add-btn` block:

```css
  /* Rejoin prompt */
  .rejoin-prompt {
    font-size: 11px;
    color: var(--ek-yellow);
    height: 22px;
    display: flex;
    align-items: center;
    padding: 0 6px;
    flex-shrink: 0;
  }
  .rejoin-prompt.hidden { visibility: hidden; }

  /* Action buttons row */
  .action-row {
    display: flex;
    gap: 8px;
    padding: 6px;
    flex-shrink: 0;
  }
  .invite-btn, .rejoin-btn {
    flex: 1;
    height: 38px;
    font-size: 13px;
    font-weight: bold;
    color: var(--ek-off-white);
    border: none;
    cursor: pointer;
    letter-spacing: 1px;
  }
  .invite-btn { background: var(--ek-red); }
  .rejoin-btn { background: var(--ek-dark); }
  .rejoin-btn.active { background: var(--ek-green); }
```

- [ ] **Step 2: Replace `<!-- rejoin prompt and action row ... -->` in state 2**

```html
      <div class="rejoin-prompt hidden">Game over — return to your lobby?</div>
      <div class="action-row">
        <button class="invite-btn">INVITE ALL</button>
        <button class="rejoin-btn">REJOIN</button>
      </div>
```

- [ ] **Step 3: Verify in browser**

Save. Expected:
- INVITE ALL (EkRed) and REJOIN (EkDark) buttons appear side by side at the bottom, each 50% wide and 38px tall
- The yellow rejoin prompt is invisible (hidden via `visibility: hidden` — it still takes up space, keeping layout stable)
- State 2 is now visually complete

- [ ] **Step 4: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 2 complete (rejoin prompt + action buttons)"
```

---

### Task 6: State 3 — Post-game

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add state 3 HTML** (after state 2, replacing `<!-- states 3–4 go here -->`)

This is identical to state 2 with two differences: the rejoin-prompt loses `hidden`, and the rejoin-btn gains `active`.

```html
  <!-- ── State 3: Post-game ── -->
  <div class="state-wrapper">
    <div class="expanded-panel">
      <div class="header-strip">
        <span class="header-title">MY LOBBY</span>
        <div class="minimize-btn"><div class="minimize-bar"></div></div>
      </div>
      <div class="code-row">
        <span class="code-label">Code: EK-A3F9C12B</span>
        <button class="copy-btn">Copy</button>
        <button class="icon-btn">✏</button>
      </div>
      <div class="friend-list">
        <div class="friend-row">
          <span class="friend-name">● Alice</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row">
          <span class="friend-name">● Bob</span>
          <button class="kick-btn">kick</button>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Carol</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Dave</span>
          <button class="remove-btn">✕</button>
        </div>
      </div>
      <button class="add-btn">+ Add</button>
      <div class="rejoin-prompt">Game over — return to your lobby?</div>
      <div class="action-row">
        <button class="invite-btn">INVITE ALL</button>
        <button class="rejoin-btn active">REJOIN</button>
      </div>
    </div>
    <span class="state-label">3 — Post-game</span>
  </div>

  <!-- state 4 goes here -->
```

- [ ] **Step 2: Verify in browser**

Save. Expected:
- State 3 panel is identical to state 2 except:
  - Yellow "Game over — return to your lobby?" text is now visible
  - REJOIN button is green (#1E7A1E) instead of dark gray

- [ ] **Step 3: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 3 post-game"
```

---

### Task 7: State 4 — Countdown placeholder

**Files:**
- Modify: `mock/overlay.html`

- [ ] **Step 1: Add CSS for the countdown overlay**

Add after the `.rejoin-btn.active` block:

```css
  /* ── State 4: Countdown overlay (placeholder for Group A) ── */
  .countdown-overlay {
    position: absolute;
    inset: 44px 0 0 0; /* below the header strip */
    background: rgba(0, 0, 0, 0.82);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    z-index: 10;
  }
  .countdown-number {
    font-size: 64px;
    font-weight: bold;
    color: var(--ek-red);
    line-height: 1;
  }
  .countdown-label {
    font-size: 13px;
    color: var(--ek-off-white);
    letter-spacing: 1px;
  }
  .countdown-hint {
    font-size: 11px;
    color: #888;
    margin-top: 4px;
  }
```

- [ ] **Step 2: Add state 4 HTML** (replace `<!-- state 4 goes here -->`)

```html
  <!-- ── State 4: Countdown (placeholder — Group A feature) ── -->
  <div class="state-wrapper">
    <div class="expanded-panel">
      <div class="header-strip">
        <span class="header-title">MY LOBBY</span>
        <div class="minimize-btn"><div class="minimize-bar"></div></div>
      </div>
      <div class="code-row">
        <span class="code-label">Code: EK-A3F9C12B</span>
        <button class="copy-btn">Copy</button>
        <button class="icon-btn">✏</button>
      </div>
      <div class="friend-list">
        <div class="friend-row">
          <span class="friend-name">● Alice</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row">
          <span class="friend-name">● Bob</span>
          <button class="kick-btn">kick</button>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Carol</span>
          <button class="remove-btn">✕</button>
        </div>
        <div class="friend-row offline">
          <span class="friend-name">○ Dave</span>
          <button class="remove-btn">✕</button>
        </div>
      </div>
      <button class="add-btn">+ Add</button>
      <div class="rejoin-prompt hidden">placeholder</div>
      <div class="action-row">
        <button class="invite-btn">INVITE ALL</button>
        <button class="rejoin-btn">REJOIN</button>
      </div>
      <!-- TODO: Group A — auto-queue countdown overlay -->
      <div class="countdown-overlay">
        <div class="countdown-number">3</div>
        <div class="countdown-label">REJOINING LOBBY…</div>
        <div class="countdown-hint">Click Leave in-game to cancel</div>
      </div>
    </div>
    <span class="state-label">4 — Countdown</span>
  </div>
```

- [ ] **Step 3: Verify in browser**

Save. Expected:
- State 4 shows the same expanded panel as state 2 but with a dark semi-transparent overlay covering everything below the red header
- Big red "3" centered, "REJOINING LOBBY…" label below it, small gray hint text at the bottom
- The underlying friend list is faintly visible through the overlay

- [ ] **Step 4: Commit**

```bash
git add mock/overlay.html
git commit -m "feat: overlay mock — state 4 countdown placeholder"
```

---

### Task 8: Final cross-check and dimension verification

**Files:**
- Modify: `mock/overlay.html` (if corrections needed)

- [ ] **Step 1: Cross-check every dimension against `src/EKLobbyMod/OverlayPanel.cs`**

Open `src/EKLobbyMod/OverlayPanel.cs` alongside `mock/overlay.html`. Verify these values at scale factor `_s = 1.0`:

| Element | OverlayPanel.cs value | mock/overlay.html value | Match? |
|---|---|---|---|
| Min tab width | `220 * s` = 220px | `width: 220px` | ✓ |
| Min tab height | `40 * s` = 40px | `height: 40px` | ✓ |
| Left accent width | `4 * s` = 4px | `width: 4px` | ✓ |
| Expanded panel width | `300 * s` = 300px | `width: 300px` | ✓ |
| Expanded panel height | `400 * s` = 400px | `height: 400px` | ✓ |
| Header strip height | `44 * s` = 44px | `height: 44px` | ✓ |
| Code row font size | `13 * s` = 13px | `font-size: 13px` | ✓ |
| Header font size | `17 * s` = 17px | `font-size: 17px` | ✓ |
| Friend row height | `22 * s` = 22px | `height: 22px` | ✓ |
| Action button height | `38 * s` = 38px | `height: 38px` | ✓ |
| Action button font | `13 * s` = 13px | `font-size: 13px` | ✓ |

If any value is off, correct it in `mock/overlay.html` before proceeding.

- [ ] **Step 2: Open in VS Code Live Server and do a final visual pass**

Verify all four states are visible and labeled. Confirm no browser console errors.

- [ ] **Step 3: Commit if any corrections were made**

```bash
git add mock/overlay.html
git commit -m "fix: overlay mock — dimension corrections from OverlayPanel.cs cross-check"
```

(Skip this commit if no corrections were needed in Step 1.)
