# Chrome Constraints — Cross-Platform Findings

Output of the chrome spike (`spike/Phoenixml.Platform.ChromeSpike/`). Records what
works, what falls back to OS chrome, and what's deferred. Production
`Shell.MainWindow` is built against this constraint set, not against an aspirational
spec.

The spike uses Avalonia 11.2.4's `ExtendClientAreaToDecorationsHint="True"` +
`ExtendClientAreaTitleBarHeightHint="-1"` to draw a custom title bar over the
client area. Min/max/close buttons are drawn in XAML and hidden on macOS so the
native traffic-light buttons take over.

**Last updated:** 2026-05-02. Linux smoke-tested; interactive validation on
Linux/Win/macOS pending user confirmation.

## How to validate on each platform

```bash
cd /raid/elvogel/repos/phoenixml-platform
dotnet run --project spike/Phoenixml.Platform.ChromeSpike
```

Then exercise the behavior table for that platform and update this doc.

---

## Linux

### Test environment (smoke test)

- OS: `Linux Mint 22.3 (Zena)` — kernel 6.14.0-37-generic
- Desktop: `X-Cinnamon`
- Session type: `X11`
- Smoke test 2026-05-02: spike launched and ran for 5 seconds without crashing
  or emitting stderr. Interactive behaviors below remain TBD until the user
  exercises the window.

### Findings

| Behavior                            | Status   | Notes |
|-------------------------------------|----------|-------|
| Window renders without startup crash | Pass | Smoke-tested 2026-05-02 on Cinnamon/X11 |
| Custom title bar renders            | TBD      | User validation needed |
| Drag title bar to move window       | TBD      | `BeginMoveDrag` on left-button-pressed |
| Min/max/close buttons               | TBD      | Drawn in XAML; should work via `WindowState` |
| Double-click title bar to maximize  | TBD      | Not wired in spike — fallback to OS if compositor handles it |
| Resize from window edges            | TBD      | Should work via OS-provided resize handles |
| Window respects panel/dock area     | TBD      | Cinnamon panel at bottom — does maximize cover it? |
| HiDPI scaling                       | TBD      | Test on a HiDPI monitor if available |
| Tile via OS shortcut (Super+Left)   | TBD      | Cinnamon's tile shortcut |
| Alt+F4 close                        | TBD      | Standard expectation |
| Wayland compatibility               | n/a yet  | This session is X11; needs separate Wayland test (GNOME or KDE) |

(Pass = works as expected, Fail = broken, Fallback = OS chrome takes over —
acceptable per spec)

### Decisions (filled once findings come in)

- **Production chrome on Linux:** TBD
- **Compositor-specific quirks to handle:** TBD
- **Deferred behaviors:** TBD

---

## Windows

**Status:** TBD — pending user validation on Windows 11 (and Windows 10 if available).

### Test instructions for the user

1. Clone or pull `/raid/elvogel/repos/phoenixml-platform`.
2. From the repo root: `dotnet run --project spike/Phoenixml.Platform.ChromeSpike`.
3. Run through the behavior table below.
4. Pay special attention to:
   - Snap zones (drag window to screen edge → tile preview).
   - Win+Arrow keyboard tiling.
   - Taskbar interaction (window list, jump-list).
   - Aero Snap regions on the title bar.
5. Report findings by editing this file.

### Findings

| Behavior                            | Status | Notes |
|-------------------------------------|--------|-------|
| Custom title bar renders            | TBD    |       |
| Drag title bar to move window       | TBD    |       |
| Min/max/close buttons               | TBD    |       |
| Snap-to-edge tile preview           | TBD    |       |
| Win+Arrow tiling                    | TBD    |       |
| Maximize respects taskbar area      | TBD    |       |
| Alt+F4 close                        | TBD    |       |
| HiDPI scaling                       | TBD    |       |
| Windows 10 vs 11 chrome differences | TBD    |       |

### Decisions

TBD.

---

## macOS

**Status:** TBD — pending user validation.

### Test instructions for the user

1. Same `dotnet run` invocation on a Mac.
2. Run through the behavior table.
3. macOS-specific: the explicit min/max/close buttons should NOT show; native
   traffic-light buttons should appear via `ExtendClientAreaToDecorationsHint`.
   Confirm this works (the spike's `WindowsButtons` stack panel is hidden when
   `RuntimeInformation.IsOSPlatform(OSPlatform.OSX)`).
4. Pay special attention to:
   - Traffic-light button positions (top-left, native macOS convention).
   - Full-screen mode via the green button (should expand into Mission Control space).
   - Mission Control gesture / shortcut behavior.
   - Menu bar visibility (system menu bar, not in-app).
5. Report findings.

### Findings

| Behavior                            | Status | Notes |
|-------------------------------------|--------|-------|
| Native traffic lights show          | TBD    | Should appear at top-left |
| Custom min/max/close hidden         | TBD    | Should be invisible |
| Drag title bar to move window       | TBD    |       |
| Green button → full-screen          | TBD    |       |
| Mission Control behavior            | TBD    |       |
| Menu bar shows system menu          | TBD    |       |
| Cmd+W close                         | TBD    |       |
| HiDPI / Retina scaling              | TBD    |       |
| Apple Silicon vs Intel differences  | TBD    | If both available |

### Decisions

TBD.

---

## Decision matrix (filled once all three platforms are validated)

| Platform | Custom chrome ships? | Falls back to OS chrome? | Deferred behaviors |
|----------|---------------------|--------------------------|--------------------|
| Linux (X11)    | TBD | TBD | TBD |
| Linux (Wayland)| TBD | TBD | TBD |
| Windows 11     | TBD | TBD | TBD |
| Windows 10     | TBD | TBD | TBD |
| macOS          | TBD | TBD | TBD |

This matrix updates spec §4.2 once complete and informs `ChromePlatform.Detect()`
in `Phoenixml.Platform.Shell` (Plan 3 Task 9).
