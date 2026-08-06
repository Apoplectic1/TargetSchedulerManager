# self-update — delta (dialog-behaviors-on-type)

## MODIFIED Requirements

### Requirement: The installed app checks for updates at startup, silently on failure

On startup, an **installed** copy SHALL check the public repo's GitHub Releases for a newer
stable version: on none, or on any failure (network down, rate limit), it SHALL stay silent to
the user and leave only a log entry; on a hit it SHALL prompt with the available version and,
only on explicit accept, download, apply, and restart. Declining SHALL change nothing and re-ask
no sooner than the next startup. A non-installed (dev/F5) run SHALL NOT check at all. The check
SHALL NOT delay or block the app's load pipeline, and no other update surface exists (startup
check only — user decision 2026-08-02).

The prompt SHALL be a standard app dialog — centered, repositionable by dragging any
non-interactive spot, and diagnostics-capable — carrying the same dialog surface as every other
dialog in the app, not a bare framework dialog.

#### Scenario: Up to date or offline stays silent
- **WHEN** an installed copy starts with no newer release available, or the check fails
- **THEN** the user sees nothing and the outcome is recorded in the log

#### Scenario: Update available prompts, accept installs and restarts
- **WHEN** an installed copy starts and a newer stable release exists
- **THEN** a prompt names the new version; accepting downloads, applies, and restarts the app; declining leaves the session untouched

#### Scenario: Dev run is a no-op
- **WHEN** the app runs from a local build rather than an install
- **THEN** no update check happens and no update UI can appear

#### Scenario: The prompt behaves like every other app dialog
- **WHEN** the update prompt is open
- **THEN** it can be repositioned by dragging a non-interactive spot, exactly as the app's editor dialogs can
