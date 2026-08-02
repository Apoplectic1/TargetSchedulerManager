# velopack-self-update — tasks

## 1. Packaging + entry point (design D1, D6)

- [x] 1.1 csproj: add Velopack + MinVer packages, `MinVerTagPrefix=v`,
      `MinVerDefaultPreReleaseIdentifiers=alpha.0`, `DISABLE_XAML_GENERATED_MAIN` define
- [x] 1.2 `Program.cs`: custom `Main` — `VelopackApp.Build().Run()` first, then the unpackaged
      WinUI bootstrap (ComWrappers init, `Application.Start` + dispatcher sync context, `new App()`)
- [x] 1.3 Build + full test run green (ratchet: zero warnings); F5 run still boots normally

## 2. UpdateService (design D2, D4, D5)

- [x] 2.1 `Services/UpdateService.cs`: `CheckOnStartupAsync(XamlRoot)` — `IsInstalled` guard,
      `GithubSource` (public repo, no token, no prerelease), silent-catch with `Log.Warn`,
      ContentDialog prompt → download + `ApplyUpdatesAndRestart` on accept
- [x] 2.2 Wire into `App.OnLaunched` after window activation via `FireAndLog`; verify the load
      pipeline is not awaited/blocked by the check (code inspection — dev run is a no-op)

## 3. Release script (design D3)

- [x] 3.1 `scripts/release.ps1`: tag → `dotnet publish` App Release/win-x64 →
      `vpk pack -u TargetSchedulerManager -e tsmui.exe --packTitle "Target Scheduler Manager"` →
      `vpk upload github --tag` (`-NoUpload` dry-run switch); `Releases/` stays gitignored

## 4. Docs (same-commit rule)

- [x] 4.1 RELEASING.md: distribution section flips to the local-build Velopack flow (one-time vpk
      + GITHUB_TOKEN setup, per-release flow, dry-run recipe); content rules gain "AL binaries
      ship in releases, AL source stays unpublished"
- [x] 4.2 README.md: Install section (Setup.exe from Releases, SmartScreen caveat); Status section
      updated (installable now; source still doesn't build without the Library)
- [x] 4.3 VERIFICATION.md: dry-run + local-install verification recipe; CHANGELOG entry;
      CLAUDE.md router untouched unless a pointer is needed

## 5. Verify + release

- [ ] 5.1 Dry-run `scripts/release.ps1 -NoUpload`; install `Releases\...Setup.exe` locally;
      confirm install, shortcut, launch, and that the installed copy's startup check logs
      (user visual pass)
- [ ] 5.2 Tag `v1.1.0`, publish `main` per RELEASING.md, run `scripts/release.ps1` (real upload);
      verify the GitHub Release artifacts
- [ ] 5.3 Deferred: the in-app update prompt proves itself on the next release (v1.1.1+) — noted
      in CHANGELOG, not blocking archive
