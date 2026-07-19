using EutherWire.Document.Model;

namespace EutherWire.Document.Geometry;

public static class BuildingOpeningGeometry
{
    public static bool UsesXAxis(MountingSurface surface) => surface is
        MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
        MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior;

    public static Point3 ConstrainCentre(SpaceVolume space, MountingSurface surface, Point3 centre, double width, double height)
    {
        Point3 onSurface = MountingSurfaceGeometry.Constrain(space, surface, centre);
        double wall = space.WallThicknessMillimetres;
        bool exterior = surface.ToString().EndsWith("Exterior", StringComparison.Ordinal);
        double minX = space.Origin.X - (exterior ? wall : 0);
        double maxX = space.Origin.X + space.WidthMillimetres + (exterior ? wall : 0);
        double minY = space.Origin.Y - (exterior ? wall : 0);
        double maxY = space.Origin.Y + space.DepthMillimetres + (exterior ? wall : 0);
        double maxZ = space.HeightMillimetres + (exterior ? space.CeilingThicknessMillimetres : 0);
        double halfWidth = Math.Min(width / 2, UsesXAxis(surface) ? (maxX - minX) / 2 : (maxY - minY) / 2);
        double halfHeight = Math.Min(height / 2, maxZ / 2);
        return UsesXAxis(surface)
            ? new Point3(Math.Clamp(onSurface.X, minX + halfWidth, maxX - halfWidth), onSurface.Y, Math.Clamp(onSurface.Z, halfHeight, maxZ - halfHeight))
            : new Point3(onSurface.X, Math.Clamp(onSurface.Y, minY + halfWidth, maxY - halfWidth), Math.Clamp(onSurface.Z, halfHeight, maxZ - halfHeight));
    }

    public static Point3[] Corners(BuildingOpening opening)
    {
        double halfWidth = opening.WidthMillimetres / 2;
        double halfHeight = opening.HeightMillimetres / 2;
        return UsesXAxis(opening.Surface)
            ? [new(opening.Centre.X - halfWidth, opening.Centre.Y, opening.Centre.Z - halfHeight), new(opening.Centre.X + halfWidth, opening.Centre.Y, opening.Centre.Z - halfHeight), new(opening.Centre.X + halfWidth, opening.Centre.Y, opening.Centre.Z + halfHeight), new(opening.Centre.X - halfWidth, opening.Centre.Y, opening.Centre.Z + halfHeight)]
            : [new(opening.Centre.X, opening.Centre.Y - halfWidth, opening.Centre.Z - halfHeight), new(opening.Centre.X, opening.Centre.Y + halfWidth, opening.Centre.Z - halfHeight), new(opening.Centre.X, opening.Centre.Y + halfWidth, opening.Centre.Z + halfHeight), new(opening.Centre.X, opening.Centre.Y - halfWidth, opening.Centre.Z + halfHeight)];
    }

    public static Point3 ResizeHandle(BuildingOpening opening, string name)
    {
        Point3[] corners = Corners(opening);
        return name switch
        {
            "start" => corners[0],
            "end" => corners[2],
            _ => throw new ArgumentException($"Unknown opening resize handle '{name}'.", nameof(name)),
        };
    }

    public static void ResizeFromHandle(BuildingOpening opening, string name, Point3 destination)
    {
        Point3 opposite = ResizeHandle(opening, name == "start" ? "end" : "start");
        double width = UsesXAxis(opening.Surface) ? Math.Abs(destination.X - opposite.X) : Math.Abs(destination.Y - opposite.Y);
        double height = Math.Abs(destination.Z - opposite.Z);
        if (width < 100 || height < 100) return;
        opening.WidthMillimetres = width;
        opening.HeightMillimetres = height;
        opening.Centre = new Point3(
            (destination.X + opposite.X) / 2,
            (destination.Y + opposite.Y) / 2,
            (destination.Z + opposite.Z) / 2);
    }
}
