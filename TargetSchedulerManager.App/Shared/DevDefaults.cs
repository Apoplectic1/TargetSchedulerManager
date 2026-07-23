using Astronomy.Core.Locations;

namespace TargetSchedulerManager;

/// <summary>
/// Dev-default paths for this machine — machine paths must not live in the consumer-neutral library, so they
/// sit in the App's <c>Shared\</c> folder. <see cref="TsDatabaseLive"/> is the live db on the imaging PC
/// (BIRDWATCHER); <see cref="TsDatabase"/> is the local working copy under <c>Processing\Catalog\TS Database\</c>
/// — one location TSM + IS both read. Under the sync model the local copy is the ONLY db the app loads and
/// edits: <c>TsSync</c> refreshes it from BIRDWATCHER at open (baseline-skipped when unchanged) and replays
/// journaled edits back at an explicit push.
/// </summary>
internal static class DevDefaults
{
    public const string Catalog = @"E:\Photography\Astro Photography\Processing\Catalog\Catalog.db";
    public const string Library = @"E:\Photography\Astro Photography\Processing";

    /// <summary>The LOCAL working copy of the TS db — the one db the app reads and edits.</summary>
    public const string TsDatabase = @"E:\Photography\Astro Photography\Processing\Catalog\TS Database\schedulerdb.sqlite";

    /// <summary>The LIVE TS db on the imaging PC (BIRDWATCHER), over SMB — read at pull, written only by push (path mirrors XFM's).</summary>
    public const string TsDatabaseLive = @"\\BIRDWATCHER\SchedulerPlugin\schedulerdb.sqlite";

    // ---- Observing site (the Visible-tonight pass) ----
    // The rig's home site (values mirror TP's "Penns Park" preset). Same dev-default pattern as the
    // db paths above; a settings page can replace these later.
    public const string SiteName            = "Penns Park";
    public const double SiteLatitude        = 40.282835;   // magnitude; hemisphere in SiteNorth
    public const bool   SiteNorth           = true;
    public const double SiteLongitude       = 74.997369;   // magnitude; hemisphere in SiteWest
    public const bool   SiteWest            = true;
    public const double SiteElevationMeters = 80.67;
    public const string SiteTimeZoneId      = "Eastern Standard Time";

    /// <summary>The observing site as the library's <see cref="Location"/>. The Visible-Tonight pass's
    /// duration/altitude knobs live on the toolbar (both default 30), not here — this is position only.</summary>
    public static Location Site() => new(
        name:         SiteName,
        latitude:     SiteLatitude,  north: SiteNorth,
        longitude:    SiteLongitude, west:  SiteWest,
        timeZoneInfo: TimeZoneInfo.FindSystemTimeZoneById(SiteTimeZoneId),
        elevation:    SiteElevationMeters);
}
