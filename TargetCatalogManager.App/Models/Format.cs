namespace TargetCatalogManager.App.Models;

internal static class Format
{
    /// <summary>F1 decimal hours, except tiny non-zero values keep two decimals — 22 short 5 s frames
    /// are 0.03 h, and rendering that as "0.0" reads as missing rather than small.</summary>
    public static string Hours(double h) =>
        h != 0 && Math.Abs(h) < 0.05 ? h.ToString("F2") : h.ToString("F1");
}
