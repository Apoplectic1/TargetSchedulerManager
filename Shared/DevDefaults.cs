namespace TargetCatalogManager;

/// <summary>
/// Dev-default paths for this machine, single-sourced for both heads: the console host compiles this file
/// directly (root glob) and the WinUI app links it (<c>Compile Include="..\Shared\DevDefaults.cs"</c>) —
/// there is deliberately no TCM-shared assembly, and machine paths must not live in the consumer-neutral
/// library. Override via CLI args / the app's path fields. The live TS database lives on the imaging PC
/// (BIRDWATCHER); <see cref="TsDatabase"/> is the restorable working copy under
/// <c>Processing\Catalog\TS Database\</c> — one location TCM + IS both read
/// (<c>schedulerdb - Copy.sqlite</c> restores it).
/// </summary>
internal static class DevDefaults
{
    public const string Catalog = @"E:\Photography\Astro Photography\Processing\Catalog\Catalog.db";
    public const string Library = @"E:\Photography\Astro Photography\Processing";
    public const string TsDatabase = @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";
}
