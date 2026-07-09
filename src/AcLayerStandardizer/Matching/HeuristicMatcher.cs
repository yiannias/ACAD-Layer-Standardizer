namespace AcLayerStandardizer.Matching;

public class HeuristicMatcher
{
    private readonly IReadOnlyCollection<string> _standardLayerNames;
    private readonly double _minConfidence;

    public HeuristicMatcher(IReadOnlyCollection<string> standardLayerNames, double minConfidence = 0.6)
    {
        _standardLayerNames = standardLayerNames;
        _minConfidence = minConfidence;
    }

    public MatchResult? TryMatch(string layerName)
    {
        MatchResult? best = null;

        foreach (var standard in _standardLayerNames)
        {
            var confidence = CalculateSimilarity(layerName, standard);
            if (confidence >= _minConfidence && (best is null || confidence > best.Confidence))
            {
                best = new MatchResult
                {
                    SourceLayer = layerName,
                    TargetLayer = standard,
                    Confidence = confidence,
                    Source = MatchSource.Heuristic,
                };
            }
        }

        return best;
    }

    public static double CalculateSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        a = a.ToUpperInvariant();
        b = b.ToUpperInvariant();

        if (a == b) return 1.0;
        if (a.Contains(b) || b.Contains(a)) return 0.85;

        var normalizedA = StripCommonPrefixes(a);
        var normalizedB = StripCommonPrefixes(b);

        if (normalizedA != a || normalizedB != b)
        {
            if (normalizedA == normalizedB) return 0.9;
            if (normalizedA.Contains(normalizedB) || normalizedB.Contains(normalizedA)) return 0.8;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLen;
    }

    private static string StripCommonPrefixes(string s)
    {
        return s.TrimStart('A', 'S', 'E', 'L', 'V', 'I', '-', '_');
    }

    public static int LevenshteinDistance(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}
