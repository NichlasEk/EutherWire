using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public sealed record SpaceVolume(
    Point2 Origin,
    double WidthMillimetres,
    double DepthMillimetres,
    double HeightMillimetres,
    double WallThicknessMillimetres = 200,
    double CeilingThicknessMillimetres = 250)
{
    public static SpaceVolume GarageDefault { get; } = new(new Point2(-1500, -3500), 14000, 6000, 2800);

    public SpaceVolume Validate()
    {
        if (!double.IsFinite(Origin.X) || !double.IsFinite(Origin.Y) ||
            !double.IsFinite(WidthMillimetres) || WidthMillimetres <= 0 ||
            !double.IsFinite(DepthMillimetres) || DepthMillimetres <= 0 ||
            !double.IsFinite(HeightMillimetres) || HeightMillimetres <= 0 ||
            !double.IsFinite(WallThicknessMillimetres) || WallThicknessMillimetres <= 0 ||
            !double.IsFinite(CeilingThicknessMillimetres) || CeilingThicknessMillimetres <= 0)
        {
            throw new ArgumentException("Space volume coordinates and dimensions must be finite and positive.");
        }
        return this;
    }
}
