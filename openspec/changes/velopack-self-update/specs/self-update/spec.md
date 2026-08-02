# self-update — delta

## Purpose

How TSM is distributed and how an installed copy updates itself: the release-artifact contract
(installer packages on the public repo's GitHub Releases, versions derived from the same `vX.Y.Z`
tags that gate `main` pushes), the startup update check's observable behavior, and the
local-build rule (releases are packed from the dev machine, where the unpublished sibling
library resolves — never from CI).

## ADDED Requirements

### Requirement: Releases are installable packages on GitHub Releases, versioned by tag

Each published release SHALL consist of Velopack artifacts (Setup installer, full package, and
delta packages with their release manifest) attached to a GitHub Release on the public TSM repo
whose tag equals the git tag (`vX.Y.Z`) of the released commit. The assembly version of the
packaged app SHALL derive from that same tag. Builds not made from a release tag SHALL carry a
prerelease-shaped version, and the updater SHALL ignore prerelease versions — a dev build can
never roll out to an installed copy.

#### Scenario: Release artifacts land under the release tag
- **WHEN** a release is packed and uploaded for tag `vX.Y.Z`
- **THEN** the GitHub Release tagged `vX.Y.Z` holds the installer, package(s), and manifest, and the packaged app reports version `X.Y.Z`

#### Scenario: Dev builds are invisible to installed copies
- **WHEN** the app is built from an untagged commit
- **THEN** its version is prerelease-shaped and no installed copy ever offers it as an update

### Requirement: Releases build locally, never in CI

Release packaging SHALL run on the development machine against a local Release publish (the app's
project references resolve only there — the sibling shared library has no public mirror). No CI
pipeline SHALL build or publish releases. The compiled shared-library assemblies ship inside the
release packages; their source remains unpublished.

#### Scenario: Packing uses the local publish output
- **WHEN** a release is packed
- **THEN** the packed payload is the dev machine's self-contained Release publish output, including the shared-library assemblies

### Requirement: The installed app checks for updates at startup, silently on failure

On startup, an **installed** copy SHALL check the public repo's GitHub Releases for a newer
stable version: on none, or on any failure (network down, rate limit), it SHALL stay silent to
the user and leave only a log entry; on a hit it SHALL prompt with the available version and,
only on explicit accept, download, apply, and restart. Declining SHALL change nothing and re-ask
no sooner than the next startup. A non-installed (dev/F5) run SHALL NOT check at all. The check
SHALL NOT delay or block the app's load pipeline, and no other update surface exists (startup
check only — user decision 2026-08-02).

#### Scenario: Up to date or offline stays silent
- **WHEN** an installed copy starts with no newer release available, or the check fails
- **THEN** the user sees nothing and the outcome is recorded in the log

#### Scenario: Update available prompts, accept installs and restarts
- **WHEN** an installed copy starts and a newer stable release exists
- **THEN** a prompt names the new version; accepting downloads, applies, and restarts the app; declining leaves the session untouched

#### Scenario: Dev run is a no-op
- **WHEN** the app runs from a local build rather than an install
- **THEN** no update check happens and no update UI can appear
