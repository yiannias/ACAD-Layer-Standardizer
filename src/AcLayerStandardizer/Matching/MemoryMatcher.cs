using AcLayerStandardizer.Data;

namespace AcLayerStandardizer.Matching;

public class MemoryMatcher
{
    private readonly TranslationMemory _memory;

    public MemoryMatcher(TranslationMemory memory)
    {
        _memory = memory;
    }

    public MatchResult? TryMatch(string layerName)
    {
        if (_memory.Mappings.TryGetValue(layerName, out var target))
        {
            return new MatchResult
            {
                SourceLayer = layerName,
                TargetLayer = target,
                Confidence = 1.0,
                Source = MatchSource.Memory,
            };
        }

        return null;
    }
}
