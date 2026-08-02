#requires -version 5.1
# Build, pack, and (optionally) upload a TargetSchedulerManager release to GitHub.
# Modeled on TargetPlanner's release.ps1; TSM differences: packs the *publish* output
# (unpackaged self-contained WinUI needs the WinAppSDK + .NET runtimes in the payload)
# and the exe is tsmui.exe.
#
# Prerequisites (one-time per machine):
#   dotnet tool install -g vpk
#   $env:GITHUB_TOKEN = "<personal-access-token-with-public_repo-scope>"
#
# Per-release flow (see RELEASING.md):
#   1. git tag vX.Y.Z on main, push main + tag
#   2. .\scripts\release.ps1
#
# The script reads the latest reachable tag via `git describe --tags --abbrev=0` and uses
# that as the release version. MinVer (in TargetSchedulerManager.App.csproj) reads the same
# tag at build time so the assembly version matches.

[CmdletBinding()]
param(
    # Skip the GitHub upload step (useful for local dry-runs of vpk pack).
    [switch] $NoUpload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $tag = git describe --tags --abbrev=0 2>$null
    if (-not $tag) {
        throw "No git tag reachable from HEAD. Tag a release first (e.g. 'git tag v1.1.0')."
    }
    $version = $tag.TrimStart('v')
    Write-Host "Releasing TargetSchedulerManager $version (tag $tag)" -ForegroundColor Cyan

    Write-Host "`n--> dotnet publish (Release|win-x64, self-contained)" -ForegroundColor Cyan
    dotnet publish TargetSchedulerManager.App -c Release -r win-x64 --self-contained true -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $publish = Join-Path $repoRoot 'TargetSchedulerManager.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish'
    if (-not (Test-Path (Join-Path $publish 'tsmui.exe'))) { throw "Publish output not found at $publish" }

    Write-Host "`n--> vpk pack" -ForegroundColor Cyan
    vpk pack `
        -u TargetSchedulerManager `
        -v $version `
        -p $publish `
        -e tsmui.exe `
        --packTitle 'Target Scheduler Manager'
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

    if ($NoUpload) {
        Write-Host "`nDone. Skipping upload (-NoUpload). Output is in .\Releases\" -ForegroundColor Yellow
        return
    }

    if (-not $env:GITHUB_TOKEN) {
        throw "GITHUB_TOKEN env var is not set. Either set it or re-run with -NoUpload."
    }

    Write-Host "`n--> vpk upload github (publish)" -ForegroundColor Cyan
    # --tag aligns the GitHub release tag with the git tag (vpk's default would be the bare
    # version "1.1.0", but the git/MinVer/RELEASING.md convention is "v1.1.0").
    vpk upload github `
        --repoUrl 'https://github.com/Apoplectic1/TargetSchedulerManager' `
        --token $env:GITHUB_TOKEN `
        --tag $tag `
        --publish
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

    Write-Host "`nReleased TargetSchedulerManager $version to GitHub." -ForegroundColor Green
}
finally {
    Pop-Location
}
