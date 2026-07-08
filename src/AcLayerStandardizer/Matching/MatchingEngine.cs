namespace AcLayerStandardizer.Matching;

public class MatchingEngine
{
    private readonly MemoryMatcher _memoryMatcher;
    private readonly HeuristicMatcher _heuristicMatcher;

    public MatchingEngine(MemoryMatcher memoryMatcher, HeuristicMatcher heuristicMatcher)
    {
        _memoryMatcher = memoryMatcher;
        _heuristicMatcher = heuristicMatcher;
    }

    public MatchResult Classify(string layerName)
    {
        return _memoryMatcher.TryMatch(layerName)
            ?? _heuristicMatcher.TryMatch(layerName)
            ?? new MatchResult
            {
                SourceLayer = layerName,
                TargetLayer = null,
                Confidence = 0.0,
                Source = MatchSource.Unmatched,
            };
    }

    public IReadOnlyList<MatchResult> ClassifyAll(IEnumerable<string> layerNames)
    {
        return layerNames.Select(Classify).ToList();
    }
}
