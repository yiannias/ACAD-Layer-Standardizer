using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcLayerStandardizer.Core;

public record LayerProperties
{
    public string Name { get; init; } = string.Empty;
    public Color Color { get; init; } = Color.FromColorIndex(ColorMethod.ByAci, 7);
    public string Linetype { get; init; } = "Continuous";
    public LineWeight LineWeight { get; init; } = LineWeight.LineWeight000;
    public string? Description { get; init; }
    public bool IsPlottable { get; init; } = true;

    public static LayerProperties FromLayerTableRecord(LayerTableRecord ltr, Transaction tr)
    {
        var linetype = "Continuous";
        if (ltr.LinetypeObjectId.IsValid)
        {
            var ltrLinetype = (LinetypeTableRecord)tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead);
            linetype = ltrLinetype.Name;
        }

        return new LayerProperties
        {
            Name = ltr.Name,
            Color = ltr.Color,
            Linetype = linetype,
            LineWeight = ltr.LineWeight,
            Description = ltr.Description,
            IsPlottable = ltr.IsPlottable,
        };
    }
}

public sealed record PropertyMatchSettings(
    bool MatchColor = true,
    bool MatchLinetype = true,
    bool MatchLineweight = true
);
