# Phoenixml Editor v0.19 walkthrough

Plan 29 ships LSP capability parity for XSLT and XQuery, a Find References
panel in the editor, and caret-anchored popups for completion, hover, and
signature help.

## Engine version sync

- `PhoenixmlDb.Core` 1.1.2 → 1.1.3
- `PhoenixmlDb.XQuery` 1.3.6 → 1.3.10
- `PhoenixmlDb.Xslt` 1.3.10 → 1.3.14

Bumped via `Directory.Packages.props`. The build is clean on the new
package set and 504/504 platform tests stay green.

## LSP capability parity

| Capability        | XQuery 1.3.10 | Xslt 1.3.14 |
|-------------------|--------------|-------------|
| documentSymbol    | yes          | yes         |
| signatureHelp     | yes          | yes (curated table, ~35 entries) |
| definition        | yes          | yes         |
| references        | **new**      | **new**     |
| hover             | yes          | n/a         |
| completion        | yes          | n/a         |

Both engines now advertise four LSP capabilities. The XSLT signature table
covers `current`, `document`, `format-number`, `key`, plus the common
`fn:*` set. The references handler is regex-based and word-boundary
anchored (so `$x` does not match inside `$xy`).

## Find References (Shift+F12)

Place the caret on a variable or function name and press **Shift+F12**.
A "References" panel slides in on the right (replacing the outline panel
in the same dock slot). Each row shows the line and column; clicking it
moves the caret to that location.

The panel is automatically dismissed when the active document switches.
Pressing Shift+F12 in a fresh location refreshes the list.

## Caret-anchored popups

Completion, hover, and signature-help popups now anchor to the caret's
pixel position inside the text view, rather than the last pointer
position. On AvaloniaEdit API drift the code falls back to pointer
placement, so popups never crash the UI.

## Test coverage

- XSLT LSP: 17/17 (signatureHelp + definition + references each have a
  fresh test class).
- XQuery LSP: 22/22 (4 new references tests covering variable + function
  resolution, word boundaries, and unknown tokens).
- Editor: 135/135 pass after wiring.
