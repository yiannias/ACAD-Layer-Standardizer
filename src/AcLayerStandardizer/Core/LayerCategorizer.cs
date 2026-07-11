namespace AcLayerStandardizer.Core;

public class LayerCategorizationResult
{
    // layer name -> resolved, fold-adjusted tag set
    public Dictionary<string, HashSet<string>> LayerTags { get; } = new(StringComparer.OrdinalIgnoreCase);

    // layers that must never be shown, regardless of any filter's state
    // (ADSK_*, DEFPOINTS, and other junk/system layers)
    public HashSet<string> AlwaysHidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    // every tag actually present on >=1 layer after folding, in dictionary
    // declaration order with virtual fallback/Misc buckets appended after
    public List<string> VisibleCategories { get; } = [];
}

// Classifies a template's target/standard layer names into the smart-group
// tags defined by layer_dictionary.json. See that file's own "description"
// for the user-facing schema notes; this is the runtime side of it.
public static class LayerCategorizer
{
    public const string MiscCategory = "Misc";

    public static LayerCategorizationResult Classify(IEnumerable<string> layerNames, LayerDictionaryDefinition dict)
    {
        var result = new LayerCategorizationResult();

        var excludedLayers = new HashSet<string>(dict.ExcludedLayers, StringComparer.OrdinalIgnoreCase);
        var remaining = new List<string>();

        foreach (var name in layerNames)
        {
            bool excluded = excludedLayers.Contains(name)
                || dict.ExcludedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (excluded)
            {
                result.AlwaysHidden.Add(name);
                continue;
            }

            remaining.Add(name);
            result.LayerTags[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var fieldsByLayer = remaining.ToDictionary(
            n => n,
            n => n.Split(dict.Delimiter, StringSplitOptions.None),
            StringComparer.OrdinalIgnoreCase);

        // Pass 1: exclusive categories claim layers outright -- a matched
        // layer gets ONLY that tag, and is removed from all further passes.
        var claimedExclusive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in dict.Categories.Where(c => c.Exclusive))
        {
            foreach (var name in remaining)
            {
                if (claimedExclusive.Contains(name)) continue;
                if (!MatchesCategory(fieldsByLayer[name], cat, dict.FieldsScanned)) continue;

                result.LayerTags[name].Clear();
                result.LayerTags[name].Add(cat.Name);
                claimedExclusive.Add(name);
            }
        }

        // Pass 2: non-exclusive categories, additive (a layer can carry
        // several), skipping anything an exclusive category already claimed.
        var nonExclusiveCats = dict.Categories.Where(c => !c.Exclusive).ToList();
        foreach (var cat in nonExclusiveCats)
        {
            foreach (var name in remaining)
            {
                if (claimedExclusive.Contains(name)) continue;
                if (MatchesCategory(fieldsByLayer[name], cat, dict.FieldsScanned))
                    result.LayerTags[name].Add(cat.Name);
            }
        }

        // Pass 3: fold categories that are too sparse in THIS template to be
        // worth their own toggle into their fallbackGroup (or drop the tag
        // if there isn't one). Fallback/virtual buckets themselves are never
        // subject to this check -- they're already the "too small to stand
        // alone" catch-all, so any non-empty result is worth showing.
        foreach (var cat in nonExclusiveCats)
        {
            if (cat.AlwaysShow) continue;

            var members = remaining.Where(n => result.LayerTags[n].Contains(cat.Name)).ToList();
            if (members.Count >= dict.FoldThreshold) continue;

            foreach (var name in members)
            {
                result.LayerTags[name].Remove(cat.Name);
                if (!string.IsNullOrEmpty(cat.FallbackGroup))
                    result.LayerTags[name].Add(cat.FallbackGroup);
            }
        }

        // Pass 4: anything left completely untagged falls into Misc.
        foreach (var name in remaining)
        {
            if (result.LayerTags[name].Count == 0)
                result.LayerTags[name].Add(MiscCategory);
        }

        var allAssignedTags = result.LayerTags.Values
            .SelectMany(t => t)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orderedKnown = dict.Categories
            .Select(c => c.Name)
            .Where(allAssignedTags.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var extras = allAssignedTags
            .Where(t => !orderedKnown.Contains(t, StringComparer.OrdinalIgnoreCase))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase);

        result.VisibleCategories.AddRange(orderedKnown);
        result.VisibleCategories.AddRange(extras);

        return result;
    }

    private static bool MatchesCategory(string[] fields, LayerCategoryDefinition cat, int fieldsScanned)
    {
        var window = cat.MatchAnywhere
            ? fields
            : fields.Take(Math.Max(fieldsScanned, 0));

        foreach (var field in window)
        {
            foreach (var token in cat.Tokens)
            {
                if (string.Equals(field, token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
