namespace AcLayerStandardizer.Core;

public class LayerCategorizationResult
{
    // layer name -> resolved, fold-adjusted tag set
    public Dictionary<string, HashSet<string>> LayerTags { get; } = new(StringComparer.OrdinalIgnoreCase);

    // layers that must never be shown, regardless of any filter's state
    // (ADSK_*, DEFPOINTS, and other junk/system layers)
    public HashSet<string> AlwaysHidden { get; } = new(StringComparer.OrdinalIgnoreCase);

    // every tag actually present on >=1 layer after folding, ordered
    // Discipline group first, then General, then Specific, alphabetized
    // within each group -- see LayerCategorizer.Classify
    public List<string> VisibleCategories { get; } = [];

    // tag name -> "Discipline"/"General"/"Specific", for every tag in
    // VisibleCategories (including virtual fallback/Misc buckets)
    public Dictionary<string, string> SortGroupByTag { get; } = new(StringComparer.OrdinalIgnoreCase);
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
        // several), skipping anything an exclusive category already
        // claimed -- EXCEPT Discipline-tier categories, which tag claimed
        // layers too. Exclusivity exists so annotation content never also
        // counts as Floor Plan/Wall/etc. CONTENT when filtering, but an
        // A-ANNO layer is still an Architectural layer: with the Target
        // Filter's union semantics, "Architectural on" must show every A-
        // layer including its annotation (chris, 2026-07-11 -- turning
        // Architectural on showed only a handful of layers because
        // Annotative had stripped the discipline tag from the rest).
        var nonExclusiveCats = dict.Categories.Where(c => !c.Exclusive).ToList();
        foreach (var cat in nonExclusiveCats)
        {
            bool isDiscipline = string.Equals(cat.SortGroup, "Discipline", StringComparison.OrdinalIgnoreCase);
            foreach (var name in remaining)
            {
                if (claimedExclusive.Contains(name) && !isDiscipline) continue;
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

        // Explicit categories carry their own SortGroup. Virtual fallback
        // buckets (e.g. "Engineering", "Floor Plan Misc") only exist as
        // strings referenced via FallbackGroup, so they inherit the
        // SortGroup of the first declared category that folds into them --
        // this is what makes "Engineering" behave as an inclusive Discipline
        // bucket automatically, with no separate declaration needed. The
        // literal Misc catch-all (Pass 4) has no source category, so it's
        // hardcoded to Specific.
        var sortGroupByCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in dict.Categories)
            sortGroupByCategory[cat.Name] = cat.SortGroup;
        foreach (var cat in dict.Categories)
        {
            if (string.IsNullOrEmpty(cat.FallbackGroup)) continue;
            if (!sortGroupByCategory.ContainsKey(cat.FallbackGroup))
                sortGroupByCategory[cat.FallbackGroup] = cat.SortGroup;
        }
        sortGroupByCategory[MiscCategory] = "Specific";

        string GroupOf(string tag) => sortGroupByCategory.TryGetValue(tag, out var g) ? g : "Specific";
        int GroupRank(string group) => group switch
        {
            "Discipline" => 0,
            "General" => 1,
            _ => 2,
        };

        result.VisibleCategories.AddRange(allAssignedTags
            .OrderBy(t => GroupRank(GroupOf(t)))
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase));

        foreach (var tag in result.VisibleCategories)
            result.SortGroupByTag[tag] = GroupOf(tag);

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
