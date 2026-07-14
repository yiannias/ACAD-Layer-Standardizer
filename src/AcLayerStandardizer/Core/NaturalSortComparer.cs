namespace AcLayerStandardizer.Core;

// Layer names embed numeric segments ("A-DT-2", "A-DT-251") that should sort
// numerically, not lexicographically -- plain string comparison puts
// "A-DT-251" before "A-DT-3".
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null) return y is null ? 0 : -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            char cx = x[i], cy = y[j];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int startI = i, startJ = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var spanX = x.AsSpan(startI, i - startI).TrimStart('0');
                var spanY = y.AsSpan(startJ, j - startJ).TrimStart('0');

                if (spanX.Length != spanY.Length)
                    return spanX.Length - spanY.Length;

                var cmp = spanX.CompareTo(spanY, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                if (cx != cy) return cx.CompareTo(cy);
                i++;
                j++;
            }
        }

        return (x.Length - i) - (y.Length - j);
    }
}
