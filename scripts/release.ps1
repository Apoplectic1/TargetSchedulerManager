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

    # The publish path derives from the csproj TFM — hardcoding it shipped stale payloads when
    # the 2026-08-10 TFM raise left the old output dir on disk (v1.5.1/v1.5.2 packed v1.5.0
    # bits; the path check passed against the leftover directory).
    $tfm = [regex]::Match((Get-Content (Join-Path $repoRoot 'TargetSchedulerManager.App\TargetSchedulerManager.App.csproj') -Raw),
        '<TargetFramework>([^<]+)</TargetFramework>').Groups[1].Value
    if (-not $tfm) { throw "Could not read <TargetFramework> from TargetSchedulerManager.App.csproj" }
    $publish = Join-Path $repoRoot "TargetSchedulerManager.App\bin\Release\$tfm\win-x64\publish"
    if (-not (Test-Path (Join-Path $publish 'tsmui.exe'))) { throw "Publish output not found at $publish" }

    # Stamp gate (XFM model): the packed exe must stamp the tag's version — catches stale
    # output dirs and MinVer cache leaks alike, making this class of failure unshippable.
    $exeVer = (Get-Item (Join-Path $publish 'tsmui.exe')).VersionInfo.ProductVersion
    if (($exeVer -split '\+')[0] -ne $version) {
        throw "Packed tsmui.exe stamps '$exeVer' - expected '$version' from tag $tag (stale output dir or MinVer mismatch)."
    }

    # AL coordination gate (see RELEASING.md): the payload embeds the sibling Library working
    # tree at pack time, unpinned - it must be a published (tagged, clean) AL state.
    $alDirty = git -C (Join-Path $repoRoot '..\Library') status --porcelain
    if ($alDirty) { throw "..\Library working tree is dirty - commit and release AL first (Library\RELEASING.md)." }
    $alVer = (Get-Item (Join-Path $publish 'Astronomy.Core.dll')).VersionInfo.ProductVersion
    if ($alVer -match '-alpha') { throw "Embedded Astronomy.Core.dll stamps '$alVer' (untagged AL state) - release AL first (Library\RELEASING.md)." }

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
