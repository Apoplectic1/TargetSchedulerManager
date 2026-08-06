using Astronomy.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Velopack;
using Velopack.Sources;

namespace TargetSchedulerManager.App.Services;

/// <summary>
/// The startup update check (spec <c>self-update</c>): a thin facade over Velopack's
/// <see cref="UpdateManager"/> keyed at the public repo's GitHub Releases. Startup-only by design
/// (user decision 2026-08-02 — no manual surface): silent on no-update and on any failure (log
/// trail only — the user must never be greeted by an error dialog because their network is down),
/// a ContentDialog prompt on a hit, download + apply + restart only on explicit accept. A
/// non-installed run (F5 / dev build) is a complete no-op, and <c>prerelease: false</c> plus
/// MinVer's alpha shaping on untagged commits means a dev build can never be offered as an update.
/// </summary>
internal static class UpdateService
{
    private const string RepoUrl = "https://github.com/Apoplectic1/TargetSchedulerManager";

    public static async Task CheckOnStartupAsync(Window owner)
    {
        try
        {
            UpdateManager manager = new(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!manager.IsInstalled)
                return;   // F5 / dev build, not via Setup.exe

            UpdateInfo? update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                Log.Info("update check: up to date");
                return;
            }

            string version = update.TargetFullRelease.Version.ToString();
            Controls.AppDialog prompt = new()
            {
                XamlRoot = owner.Content.XamlRoot,
                Title = "Update available",
                Content = $"Target Scheduler Manager {version} is available.\n\nInstall now and restart?",
                PrimaryButtonText = "Install and restart",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await prompt.ShowAsync() != ContentDialogResult.Primary)
            {
                Log.Info($"update check: {version} available, declined — will re-offer next start");
                return;
            }

            Log.Info($"update check: downloading {version}");
            await manager.DownloadUpdatesAsync(update);
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            // Silent to the user on any failure by contract; tsm.log keeps the trail so
            // "update prompts never appear" is one grep from a root cause.
            Log.Warn("update check failed (silent by design)", ex);
        }
    }
}
