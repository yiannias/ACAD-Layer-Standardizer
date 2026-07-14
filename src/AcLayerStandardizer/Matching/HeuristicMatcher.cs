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

    // CAD layer names are namespaced by delimiter-separated segments (a
    // leading single-letter discipline code, then increasingly specific
    // qualifiers -- "A-DOOR-FULL"), not free text, so comparing them as
    // plain strings misses the structure that actually carries meaning:
    // a Levenshtein diff on the raw string penalizes "A-DOOR" against
    // "A-DOOR-FULL" almost as hard as against an unrelated name, and a
    // naive character-class prefix strip (the old approach) could eat an
    // entire legitimate word ("LEVEL", "AISLE") whose letters all happen
    // to be discipline codes. Tokenizing and scoring segment-by-segment
    // fixes both: same-discipline, same-base-word layers score high even
    // with extra qualifiers, and cross-discipline collisions on a shared
    // word ("S-WALL" vs "A-WALL") are penalized instead of conflated.
    public static double CalculateSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        a = a.Trim().ToUpperInvariant();
        b = b.Trim().ToUpperInvariant();

        // Bound xrefs rename layers "XREFFILE$LAYERNAME" (AutoCAD replaces the
        // live-xref '|' delimiter with '$' on bind) -- match on the real layer
        // name, ignoring the now-stale source-file prefix.
        a = StripBoundPrefix(a);
        b = StripBoundPrefix(b);

        if (a == b) return 1.0;

        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);

        if (tokensA.Length <= 1 && tokensB.Length <= 1)
            return WholeStringSimilarity(a, b);

        return TokenSimilarity(tokensA, tokensB);
    }

    private static string StripBoundPrefix(string s)
    {
        var dollar = s.LastIndexOf('$');
        return dollar >= 0 ? s[(dollar + 1)..] : s;
    }

    private static string[] Tokenize(string s) =>
        s.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);

    private static double WholeStringSimilarity(string a, string b)
    {
        if (a.Contains(b) || b.Contains(a))
        {
            var shorter = a.Length <= b.Length ? a : b;
            var longer = a.Length <= b.Length ? b : a;

            // A short fragment swallowed by a much longer name is a much
            // weaker signal than near-complete containment -- scale within
            // a high band instead of a flat score regardless of coverage.
            return 0.6 + 0.3 * ((double)shorter.Length / longer.Length);
        }

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - (double)distance / maxLen;
    }

    private static double TokenSimilarity(string[] tokensA, string[] tokensB)
    {
        var disciplineA = tokensA[0].Length == 1 ? tokensA[0] : null;
        var disciplineB = tokensB[0].Length == 1 ? tokensB[0] : null;

        var restA = disciplineA is not null ? tokensA.Skip(1).ToArray() : tokensA;
        var restB = disciplineB is not null ? tokensB.Skip(1).ToArray() : tokensB;

        // One side is nothing but a bare discipline code (e.g. just "A") --
        // no segments left to compare token-wise, so fall back to comparing
        // the full original strings.
        if (restA.Length == 0 || restB.Length == 0)
            return WholeStringSimilarity(string.Join("-", tokensA), string.Join("-", tokensB));

        var (shorter, longer) = restA.Length <= restB.Length ? (restA, restB) : (restB, restA);

        double totalScore = 0;
        var usedIndices = new HashSet<int>();
        foreach (var token in shorter)
        {
            var best = 0.0;
            var bestIndex = -1;
            for (var i = 0; i < longer.Length; i++)
            {
                if (usedIndices.Contains(i)) continue;
                var score = token == longer[i] ? 1.0 : TokenFuzzyScore(token, longer[i]);
                if (score > best)
                {
                    best = score;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0) usedIndices.Add(bestIndex);
            totalScore += best;
        }

        // How well the smaller side's segments are explained by the other.
        var coverage = totalScore / shorter.Length;

        // Tokens on the longer side left unmatched are treated as
        // qualifiers/subtypes ("-FULL", "-01") rather than mismatches --
        // still shave a little off so a bare "A-DOOR" doesn't tie with an
        // exact "A-DOOR-FULL" hit.
        var extraCount = longer.Length - usedIndices.Count;
        var extraPenalty = 1.0 - Math.Min(0.3, extraCount * 0.08);

        var result = coverage * extraPenalty;

        if (disciplineA is not null && disciplineB is not null &&
            !string.Equals(disciplineA, disciplineB, StringComparison.OrdinalIgnoreCase))
        {
            // Different discipline namespaces sharing a word is a weak
            // signal, not a strong one -- e.g. S-WALL and A-WALL are not
            // the same layer just because they both say "WALL".
            result *= 0.3;
        }

        // Reserve 1.0 for true full-string equality, handled above.
        return Math.Min(Math.Max(result, 0.0), 0.99);
    }

    // Below this threshold two tokens aren't "the same word with a typo",
    // they're just noise -- treating them as 0 keeps unrelated segments
    // from dragging up the coverage score.
    private const double TokenFuzzyThreshold = 0.75;

    private static double TokenFuzzyScore(string x, string y)
    {
        if (x.Length == 0 || y.Length == 0) return 0.0;

        var distance = LevenshteinDistance(x, y);
        var maxLen = Math.Max(x.Length, y.Length);
        var similarity = 1.0 - (double)distance / maxLen;
        return similarity >= TokenFuzzyThreshold ? similarity : 0.0;
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
