using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public enum OpeningKind
{
    GarageDoor,
    Door,
    Window,
    Penetration,
}

public sealed class BuildingOpening
{
    public BuildingOpening(
        ObjectId id,
        OpeningKind kind,
        MountingSurface surface,
        Point3 centre,
        double widthMillimetres,
        double heightMillimetres,
        string label)
    {
        if (surface is not (MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
            MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior or
            MountingSurface.WestWallInterior or MountingSurface.WestWallExterior or
            MountingSurface.EastWallInterior or MountingSurface.EastWallExterior))
        {
            throw new ArgumentException("A building opening must belong to a wall surface.", nameof(surface));
        }
        if (!double.IsFinite(centre.X) || !double.IsFinite(centre.Y) || !double.IsFinite(centre.Z) || centre.Z < 0 ||
            !double.IsFinite(widthMillimetres) || widthMillimetres <= 0 ||
            !double.IsFinite(heightMillimetres) || heightMillimetres <= 0)
        {
            throw new ArgumentException("Opening coordinates and dimensions must be finite and positive.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Kind = kind;
        Surface = surface;
        Centre = centre;
        WidthMillimetres = widthMillimetres;
        HeightMillimetres = heightMillimetres;
        Label = label;
    }

    public ObjectId Id { get; }
    public OpeningKind Kind { get; internal set; }
    public MountingSurface Surface { get; internal set; }
    public Point3 Centre { get; internal set; }
    public double WidthMillimetres { get; internal set; }
    public double HeightMillimetres { get; internal set; }
    public string Label { get; set; }
}
