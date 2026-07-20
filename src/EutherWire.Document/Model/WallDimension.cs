using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public sealed class WallDimension
{
    public WallDimension(ObjectId id, MountingSurface surface, Point3 start, Point3 end, string label = "")
    {
        if (!WallCoordinateSystem.IsWall(surface))
        {
            throw new ArgumentException("Dimensions must belong to a wall surface.", nameof(surface));
        }
        Id = id;
        Surface = surface;
        Start = start;
        End = end;
        Label = label ?? string.Empty;
    }

    public ObjectId Id { get; }
    public MountingSurface Surface { get; internal set; }
    public Point3 Start { get; internal set; }
    public Point3 End { get; internal set; }
    public string Label { get; internal set; }

}
