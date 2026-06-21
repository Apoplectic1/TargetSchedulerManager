namespace TargetSchedulerManager.App.Shared;

/// <summary>
/// Natural ("human") order for strings with embedded numbers: maximal digit runs compare by numeric value
/// (so "IC 405" &lt; "IC 1318" and "Abell 6" &lt; "Abell 21" &lt; "Abell 2218"), other characters
/// case-insensitively. Pure-managed — no <c>shlwapi</c> <c>StrCmpLogicalW</c> P/Invoke. Digit runs compare
/// without parsing, so arbitrarily long catalog numbers can't overflow; equal numeric values tie-break by fewer
/// leading zeros, so the order is total and stable. Used for the grid's Target / Project / Filter columns.
/// </summary>
internal sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                int byNumber = CompareDigitRun(x, ref ix, y, ref iy);
                if (byNumber != 0) return byNumber;
            }
            else
            {
                char cx = char.ToUpperInvariant(x[ix]);
                char cy = char.ToUpperInvariant(y[iy]);
                if (cx != cy) return cx.CompareTo(cy);
                ix++;
                iy++;
            }
        }

        // One side ran out: the shorter (a prefix of the other) sorts first.
        return (x.Length - ix).CompareTo(y.Length - iy);
    }

    // Both cursors sit on a digit; consume the maximal digit run from each, compare by value, and advance the
    // cursors past the runs. No integer parse (overflow-safe): compare significant-digit length, then digit by
    // digit, then leading-zero count so equal values stay deterministically ordered.
    private static int CompareDigitRun(string x, ref int ix, string y, ref int iy)
    {
        int startX = ix, startY = iy;
        while (ix < x.Length && char.IsDigit(x[ix])) ix++;
        while (iy < y.Length && char.IsDigit(y[iy])) iy++;

        int sigX = startX; while (sigX < ix - 1 && x[sigX] == '0') sigX++;   // strip leading zeros, keep one
        int sigY = startY; while (sigY < iy - 1 && y[sigY] == '0') sigY++;

        int lenX = ix - sigX, lenY = iy - sigY;
        if (lenX != lenY) return lenX.CompareTo(lenY);                       // more significant digits = larger
        for (int k = 0; k < lenX; k++)
        {
            int byDigit = x[sigX + k].CompareTo(y[sigY + k]);
            if (byDigit != 0) return byDigit;
        }
        return (sigX - startX).CompareTo(sigY - startY);                     // equal value → fewer leading zeros first
    }
}
