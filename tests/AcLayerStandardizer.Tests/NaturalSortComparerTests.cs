using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void Numeric_segments_sort_numerically_not_lexicographically()
    {
        var names = new[] { "A-DT-251", "A-DT-3", "A-DT-2" };
        var sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(["A-DT-2", "A-DT-3", "A-DT-251"], sorted);
    }

    [Fact]
    public void Non_numeric_names_still_sort_alphabetically()
    {
        var names = new[] { "L-WALL", "A-DOOR", "E-LITE" };
        var sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(["A-DOOR", "E-LITE", "L-WALL"], sorted);
    }

    [Fact]
    public void Leading_zeros_do_not_affect_numeric_order()
    {
        var names = new[] { "A-DT-010", "A-DT-2" };
        var sorted = names.OrderBy(n => n, NaturalSortComparer.Instance).ToArray();

        Assert.Equal(["A-DT-2", "A-DT-010"], sorted);
    }

    [Fact]
    public void Equal_strings_compare_as_equal()
    {
        Assert.Equal(0, NaturalSortComparer.Instance.Compare("A-DT-2", "A-DT-2"));
    }
}
