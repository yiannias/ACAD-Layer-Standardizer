namespace AcLayerStandardizer.Matching;

public enum MatchSource
{
    /// <summary>Found in JSON translation memory.</summary>
    Memory,
    /// <summary>Matched via heuristic/fuzzy comparison.</summary>
    Heuristic,
    /// <summary>No match found — requires manual mapping.</summary>
    Unmatched,
}

public record MatchResult
{
    public string SourceLayer { get; init; } = string.Empty;
    public string? TargetLayer { get; init; }
    public double Confidence { get; init; }
    public MatchSource Source { get; init; } = MatchSource.Unmatched;
}
