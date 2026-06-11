namespace TargetCatalogManager;

/// <summary>
/// Dev-default paths for this machine, single-sourced for both heads: the console host compiles this file
/// directly (root glob) and the WinUI app links it (<c>Compile Include="..\Shared\DevDefaults.cs"</c>) —
/// there is deliberately no TCM-shared assembly, and machine paths must not live in the consumer-neutral
/// library. Override via CLI args / the app's path fields. <see cref="TsDatabaseLive"/> is the live db on the
/// imaging PC (BIRDWATCHER); <see cref="TsDatabase"/> is the restorable local working copy under
/// <c>Processing\Catalog\TS Database\</c> — one location TCM + IS both read (<c>schedulerdb - Copy.sqlite</c>
/// restores it). <see cref="TsDatabaseResolver"/> prefers the live db when reachable, else falls back to the copy.
/// </summary>
internal static class DevDefaults
{
    public const string Catalog = @"E:\Photography\Astro Photography\Processing\Catalog\Catalog.db";
    public const string Library = @"E:\Photography\Astro Photography\Processing";

    /// <summary>The restorable LOCAL working copy of the TS db — the fallback when BIRDWATCHER is unreachable.</summary>
    public const string TsDatabase = @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";

    /// <summary>The LIVE TS db on the imaging PC (BIRDWATCHER), over SMB — preferred when network-reachable (path mirrors XFM's).</summary>
    public const string TsDatabaseLive = @"\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite";
}
