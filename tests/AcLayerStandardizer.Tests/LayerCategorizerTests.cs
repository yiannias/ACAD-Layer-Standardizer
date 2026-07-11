using System.IO;
using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class LayerCategorizerTests
{
    // The dictionary shipped in installer/assets, loaded directly rather than
    // reimplemented here, so this test actually exercises what gets installed.
    private static LayerDictionaryDefinition LoadShippedDictionary()
    {
        var dir = AppContext.BaseDirectory;
        var path = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..",
            "installer", "assets", "layer_dictionary.json"));
        Assert.True(File.Exists(path), $"Expected dictionary at {path}");

        var json = File.ReadAllText(path);
        var dict = System.Text.Json.JsonSerializer.Deserialize<LayerDictionaryDefinition>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(dict);
        return dict!;
    }

    // The full, real 325-layer list read directly from chris's
    // "STANDARD TEMPLATE - Copy.dxf" LAYER table -- using the actual
    // production dataset rather than a hand-picked sample, since fold
    // thresholds only behave correctly at realistic volume.
    private static readonly string[] RealLayers =
    [
        "0", "DEFPOINTS", "A-GRID", "A-ANNO-SYM-CALLOUT", "A-DT-6", "X-SCRATCH",
        "A-DT-253", "A-DT-1", "A-DT-2", "A-DT-4", "A-DT-5", "X-GUIDES",
        "A-DT-HATCH-251", "A-WIPEOUT", "A-DOOR-SWNG", "A-FL-EXT-GL-GLASS",
        "A-FL-EXT-GL-SILL", "A-TLBLK-BKGND", "A-TLBLK-TEXT-WHT", "A-TLBLK-HA_LOGO",
        "A-ANNO-TTLB", "A-TLBLK", "A-TLBLK-TEXT", "0-COL-AMENITY", "0-COL-CORE",
        "0-COL-CXT", "0-COL-GRASS", "0-COL-OFFICE", "0-COL-PARKING", "0-COL-RETAIL",
        "0-COL-RETAIL_DD", "0-COL-STREET", "0-COL-UNIT_A", "0-COL-UNIT_B",
        "0-COL-UNIT_C", "0-COL-UNIT_CIRC", "0-COL-WATER", "A-ANNO-DIM",
        "A-ANNO-DIM-UNIT", "A-ANNO-DWG_TTL", "A-ANNO-LIFESAFETY",
        "A-ANNO-LIFESAFETY-1HR", "A-ANNO-LIFESAFETY-2HR", "A-ANNO-LIFESAFETY-3HR",
        "A-ANNO-LIFESAFETY-4HR", "A-ANNO-LIFESAFETY-TRAVEL",
        "A-ANNO-LIFESAFETY-TRAVEL-D", "A-ANNO-LIFESAFETY-TRAVEL-S",
        "A-ANNO-NOT_IN_CONTRACT", "A-ANNO-REV-01-CLOUD", "A-ANNO-REV-01-NOTE",
        "A-ANNO-REV-TAG", "A-ANNO-SYM", "A-ANNO-SYM-MATCHLINE", "A-ANNO-TAG-DOOR",
        "A-ANNO-TAG-ELEVATION", "A-ANNO-TAG-FINISH", "A-ANNO-TAG-ROOM",
        "A-ANNO-TAG-WALL", "A-ANNO-TAG-WINDOW", "A-ANNO-TEXT-PRESENTATION",
        "A-ANNO-TEXT-PRESENTATION-U", "A-AREA", "A-AREA-BOMA", "A-AREA-COMMERCIAL",
        "A-AREA-FAR-ELEV_STAIR", "A-AREA-FAR-MECH_ACCESSORY",
        "A-AREA-FAR-NOT_INCLUDED", "A-AREA-FAR-PARK_LOADING", "A-AREA-GREEN_ROOF",
        "A-AREA-LIGHTVENT", "A-AREA-MEP", "A-AREA-NOTE", "A-AREA-RETAIL",
        "A-AREA-RETAIL-NET", "A-CL-CEILING_TILE", "A-CL-ELECTRICAL", "A-CL-HEADER",
        "A-CL-LIGHT_FIXTURE", "A-CL-LIGHT_FIXTURE-ALT", "A-CL-LIGHT_SWITCHING",
        "A-CL-MECH", "A-CL-OPENING", "A-CL-SOFFIT", "A-CL-SOFFIT-HATCH", "A-DEMO",
        "A-DEMO-DOOR", "A-DEMO-ELEC", "A-DEMO-EQUIPMENT", "A-DEMO-GL",
        "A-DEMO-HATCH", "A-DEMO-ITEM", "A-DEMO-MECH", "A-DEMO-OPENING",
        "A-DEMO-PLUMB", "A-DEMO-WALL", "A-DT-1-HID", "A-DT-2-HID", "A-DT-251",
        "A-DT-251-HID", "A-DT-252", "A-DT-252-HID", "A-DT-253-HID", "A-DT-254",
        "A-DT-255", "A-DT-3", "A-DT-3-HID", "A-DT-4-HID", "A-DT-5-HID",
        "A-DT-6-HID", "A-DT-7", "A-DT-BLOCK", "A-DT-HATCH", "A-DT-HATCH-252",
        "A-DT-HATCH-253", "A-DT-HATCH-255", "A-DT-MAT-AWB", "A-DT-MAT-EXIST",
        "A-DT-MAT-MEMBRANE_FLASHING", "A-DT-MAT-METAL_FLASHING", "A-DT-MAT-STRUCT",
        "A-FL-ADA-CLEARANCE", "A-FL-ADA-GRAB_BAR", "A-FL-ADA-GRAB_BAR-FUTURE",
        "A-FL-ADA-SCRATCH", "A-FL-APPLIANCE", "A-FL-APPLIANCE_BELOW",
        "A-FL-CASEWORK", "A-FL-CASEWORK-HIDDEN", "A-FL-CASEWORK-OVERHEAD",
        "A-FL-DOOR", "A-FL-EQUIPMENT", "A-FL-EXT-ABOVE", "A-FL-EXT-BELOW",
        "A-FL-EXT-DOOR", "A-FL-EXT-GL-FRAME", "A-FL-EXT-GL-OPERATION",
        "A-FL-EXT-WALL", "A-FL-EXT-WALL-COMPONENT", "A-FL-EXT-WALL-COMPONENT-IN",
        "A-FL-EXT-WALL-HATCH", "A-FL-FINISH", "A-FL-FURNITURE",
        "A-FL-FURNITURE-NIC", "A-FL-INT_GL-FRAME", "A-FL-INT_GL-GLASS", "A-FL-MEP",
        "A-FL-MEP-CLEARANCE", "A-FL-MEP-EQUIPMENT", "A-FL-MEP-FIXTURE",
        "A-FL-MEP-RISER", "A-FL-OVERHEAD", "A-FL-PARKING-NUMBERING",
        "A-FL-PARKING-SPACE", "A-FL-PARKING-SPACE-BIKE", "A-FL-PARKING-STRIPE",
        "A-FL-PARKING-STRIPE-BIKE", "A-FL-UNIT-APPLIANCE",
        "A-FL-UNIT-APPLIANCE_BELOW", "A-FL-UNIT-CASEWORK",
        "A-FL-UNIT-CASEWORK-HIDDEN", "A-FL-UNIT-CASEWORK-OVERHEA",
        "A-FL-UNIT-DOOR", "A-FL-UNIT-EQUIPMENT", "A-FL-UNIT-FINISH",
        "A-FL-UNIT-FURNITURE", "A-FL-UNIT-FURNITURE-NIC", "A-FL-UNIT-MEP-FIXTURES",
        "A-FL-UNIT-WALL", "A-FL-VT-CHUTE-LAUNDRY", "A-FL-VT-CHUTE-TRASH",
        "A-FL-VT-ELEVATOR", "A-FL-VT-ELEVATOR-CLEARANCE",
        "A-FL-VT-ELEVATOR-COMPONENT", "A-FL-VT-ESCALATOR",
        "A-FL-VT-ESCALATOR-COMPONEN", "A-FL-VT-STAIR", "A-FL-VT-STAIR-ARA",
        "A-FL-VT-STAIR-CLEARANCE", "A-FL-VT-STAIR-HID", "A-FL-VT-STAIR-RAIL",
        "A-FL-VT-STAIR-SYMBOL", "A-FL-WALL", "A-FL-WALL-COMPONENT",
        "A-FL-WALL-COMPONENT-INSULA", "A-FL-WALL-COMPONENT-TILE",
        "A-FL-WALL-DEMISING", "A-FL-WALL-HATCH", "A-FL-WALL-PARTIAL",
        "A-FL-WALL-RATED", "A-GRID-DIMENSION", "A-GRID-INSIDE", "A-GRID-SYMBOL",
        "A-SP-BUILDING", "A-SP-BUILDING-HATCH", "A-SP-CURB", "A-SP-CURB-BELOW",
        "A-SP-CURB-INTERNAL", "A-SP-CONTEXT-BUILDINGS", "A-SP-CONTEXT-CTA",
        "A-SP-CONTEXT-HATCH", "A-SP-CONTEXT-TREES", "A-SP-FENCE",
        "A-SP-LS-GROUNDCOVER", "A-SP-LS-PLANT", "A-SP-LS-TREES",
        "A-SP-PARKING-SPACE", "A-SP-PARKING-SPACE-BIKE", "A-SP-PARKING-STRIPE",
        "A-SP-PARKING-STRIPE-BIKE", "A-SP-PROP", "A-SP-PROP-PARCEL",
        "A-SP-PROP-SETBACKS", "A-SP-ROAD-CENTER", "A-SP-ROAD-CURB",
        "A-SP-ROAD-CURB-PATT", "A-SP-ROAD-SIDEWALK", "A-SP-SIGN",
        "A-SP-TURNING_RADIUS", "A-SP-WALL", "A-TLBLK-PLOTSTAMP", "A-TLBLK-SEAL",
        "A-TLBLK-TEXT-ORG", "S-FL-BEAM", "S-FL-BEAM-CENTER", "S-FL-BEAM-FLANGE",
        "S-FL-BELOW", "S-FL-COL", "S-FL-COL-CONC", "S-FL-COL-CONC-HATCH",
        "S-FL-COL-STEEL", "S-FL-COL-STEEL-HATCH", "S-FL-FIREPROOFING",
        "S-FL-MISC_METALS", "S-FL-OVERHEAD", "S-FL-SLAB_EDGE", "X-BLOCKS",
        "X-BLOCKS-UNIT", "X-REF", "X-REF-GRID", "X-SCRATCH-COORD_NOTES-GENE",
        "X-SCRATCH-COORD_NOTES-INTE", "X-SCRATCH-COORD_NOTES-MECH",
        "X-SCRATCH-COORD_NOTES-PLUM", "X-SCRATCH-COORD_NOTES-STRU", "X-VIEWPORT",
        "A-SP-SITE-CURB-BELOW", "A-SP-SITE-CURB-INTERNAL",
        "ADSK_ASSOC_ENTITY_BACKUPS", "A-FL-PLUMBING_FIXTURE", "0-BLOCK",
        "A-FL-CAS-BELOW", "A-FL-ADA", "A-AREA-NET", "A-AREA-GROSS",
        "ADSK_CONSTRAINTS", "A-ANNO-TEXT", "A-ANNO-VPRT", "A-ANNO-NONPLOT",
        "A-TLBLK-TEXT-BLK", "A-SECT-C", "A-FL-VT-PARKING_RAMP",
        "A-FL-VT-LOADING_RAMP", "A-FL-VT-PARKING_RAMP-BELOW", "A-DT-HATCH-254",
        "A-FL-OVERHEAD-CEILING_FAN", "A-FL-EXISTING-WALL", "A-FL-EXISTING-EXT",
        "A-FL-EXISTING-EXT-DEMO", "A-FL-EXISTING-WALL-DEMO", "A-ANNO-TEXT-DEMO",
        "C-BLDG-N", "A-FL-VT-STAIR-OVERHEAD", "A-SP-EQUIPMENT", "A-SP-HATCH",
        "A-FL-BELOW", "A-FL-EDGE", "0-IMAGE", "0-LOGO", "0-SURVEY",
        "A-ANNO-REVISION_CLOUDS-01", "A-ANNO-REVISION_CLOUDS-02",
        "A-ANNO-REVISION_CLOUDS-03", "A-ANNO-REVISION_CLOUDS-04",
        "A-ANNO-REVISION_CLOUDS-05", "A-ANNO-REVISION_CLOUDS-06",
        "A-ANNO-REVISION_CLOUDS-07", "A-ANNO-REVISION_CLOUDS-08",
        "A-ANNO-REVISION_CLOUDS-09", "A-ANNO-REVISION_CLOUDS-10",
        "A-ANNO-REVISION_CLOUDS-11", "A-ANNO-REVISION_CLOUDS-12",
        "A-ANNO-REVISION_CLOUDS-13", "A-ANNO-REVISION_CLOUDS-14",
        "A-ANNO-REVISION_CLOUDS-15", "A-ANNO-REVISION_CLOUDS-16",
        "A-ANNO-REVISION_CLOUDS-17", "A-ANNO-REVISION_CLOUDS-18",
        "A-ANNO-REVISION_CLOUDS-19", "A-ANNO-REVISION_CLOUDS-20",
        "A-FL-ROOF-DRAIN", "A-FL-ROOF-EQUIPMENT", "A-FL-ROOF-FEATURES-MAJOR",
        "A-FL-ROOF-FEATURES-MINOR", "A-FL-ROOF-GEOMETRY", "A-FL-ROOF-PAVERS",
        "A-FL-EQUIPMENT-FITNESS", "A-FL-EQUIPMENT-MISC", "A-FL-FINISH-PATTERN",
        "A-FL-FINISH-PATTERN-FLOOR", "A-FL-FINISH-PATTERN-TILE", "A-FL-WALL-FINISH",
        "A-ANNO-DIM-EXT_WALL", "A-ANNO-DIM-SITE", "A-ANNO-DIM-PARKING",
        "A-ANNO-TEXT-UNIT_INFO", "A-ANNO-TEXT-UNIT_TYPE", "A-SP-ROAD-MARKINGS",
        "A-SP-ROAD-FEATURES", "A-ANNO-TEXT-ROAD", "A-SP-CONTEXT-UTILITIES",
        "A-SP-ENTOURAGE", "A-SP-PARKING-VEHICLES", "A-FL-ENTOURAGE", "0___1",
    ];

    [Fact]
    public void Excluded_layers_never_get_tags_and_are_marked_always_hidden()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("ADSK_ASSOC_ENTITY_BACKUPS", result.AlwaysHidden);
        Assert.Contains("ADSK_CONSTRAINTS", result.AlwaysHidden);
        Assert.Contains("DEFPOINTS", result.AlwaysHidden);
        Assert.Contains("0___1", result.AlwaysHidden);
        Assert.DoesNotContain("ADSK_ASSOC_ENTITY_BACKUPS", result.LayerTags.Keys);
    }

    [Fact]
    public void Annotative_is_exclusive_for_content_tags_but_keeps_the_discipline_tag()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        // A-ANNO-TAG-WALL would also match the cross-cutting "Wall" tag if
        // Annotative weren't exclusive -- confirm it doesn't. The discipline
        // tag survives, though: with union filtering, "Architectural on"
        // must show every A- layer including its annotation content.
        var tags = result.LayerTags["A-ANNO-TAG-WALL"];
        Assert.Contains("Annotative", tags);
        Assert.Contains("Architectural", tags);
        Assert.DoesNotContain("Wall", tags);
        Assert.Equal(2, tags.Count);
    }

    [Fact]
    public void Cross_cutting_wall_tag_applies_regardless_of_field_position()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("Wall", result.LayerTags["A-FL-EXT-WALL"]);
        Assert.Contains("Wall", result.VisibleCategories);
    }

    [Fact]
    public void Plumbing_fixture_tokens_fold_into_MEP_not_a_separate_category()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("MEP", result.LayerTags["A-FL-PLUMBING_FIXTURE"]);
        Assert.Contains("MEP", result.VisibleCategories);
        Assert.DoesNotContain("Plumbing Fixture", result.VisibleCategories);
    }

    [Fact]
    public void Life_safety_stays_visible_despite_annotative_claiming_most_of_its_matches()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        // Annotative (exclusive) claims every A-ANNO-LIFESAFETY* layer first,
        // leaving only A-FL-VT-STAIR-ARA as a real Life Safety match -- below
        // the fold threshold on its own. AlwaysShow keeps it from vanishing.
        Assert.Contains("Life Safety", result.LayerTags["A-FL-VT-STAIR-ARA"]);
        Assert.Contains("Life Safety", result.VisibleCategories);
    }

    [Fact]
    public void Coordination_notes_collapse_to_one_tag_regardless_of_discipline_suffix()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("Coordination Notes", result.LayerTags["X-SCRATCH-COORD_NOTES-MECH"]);
        Assert.Contains("Coordination Notes", result.LayerTags["X-SCRATCH-COORD_NOTES-INTE"]);
        Assert.Contains("Coordination Notes", result.VisibleCategories);
    }

    [Fact]
    public void Vertical_transportation_clears_threshold_on_real_data()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("Vertical Transportation", result.VisibleCategories);
        Assert.Contains("Vertical Transportation", result.LayerTags["A-FL-VT-ELEVATOR"]);
    }

    [Fact]
    public void Structural_stands_alone_civil_folds_into_engineering()
    {
        var dict = LoadShippedDictionary();
        var result = LayerCategorizer.Classify(RealLayers, dict);

        Assert.Contains("Structural", result.VisibleCategories);
        Assert.Contains("Engineering", result.LayerTags["C-BLDG-N"]);
    }
}
