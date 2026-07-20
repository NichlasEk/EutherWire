using EutherWire.Document.Model;

namespace EutherWire.Document.Geometry;

public static class WallCoordinateSystem
{
    public static bool IsWall(MountingSurface surface) => surface is
        MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
        MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior or
        MountingSurface.WestWallInterior or MountingSurface.WestWallExterior or
        MountingSurface.EastWallInterior or MountingSurface.EastWallExterior;

    public static bool IsExterior(MountingSurface surface) => surface is
        MountingSurface.NorthWallExterior or MountingSurface.SouthWallExterior or
        MountingSurface.WestWallExterior or MountingSurface.EastWallExterior;

    public static double Width(SpaceVolume space, MountingSurface surface)
    {
        RequireWall(surface);
        bool northSouth = surface is
            MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
            MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior;
        double width = northSouth ? space.WidthMillimetres : space.DepthMillimetres;
        return width + (IsExterior(surface) ? space.WallThicknessMillimetres * 2 : 0);
    }

    public static double Height(SpaceVolume space, MountingSurface surface)
    {
        RequireWall(surface);
        return space.HeightMillimetres + (IsExterior(surface) ? space.CeilingThicknessMillimetres : 0);
    }

    public static Point3 FromWallCoordinates(SpaceVolume space, MountingSurface surface, double horizontal, double elevation)
    {
        RequireWall(surface);
        double x0 = space.Origin.X;
        double x1 = x0 + space.WidthMillimetres;
        double y0 = space.Origin.Y;
        double y1 = y0 + space.DepthMillimetres;
        double wall = space.WallThicknessMillimetres;
        return surface switch
        {
            MountingSurface.NorthWallInterior => new Point3(x0 + horizontal, y0, elevation),
            MountingSurface.NorthWallExterior => new Point3(x1 + wall - horizontal, y0 - wall, elevation),
            MountingSurface.SouthWallInterior => new Point3(x1 - horizontal, y1, elevation),
            MountingSurface.SouthWallExterior => new Point3(x0 - wall + horizontal, y1 + wall, elevation),
            MountingSurface.WestWallInterior => new Point3(x0, y1 - horizontal, elevation),
            MountingSurface.WestWallExterior => new Point3(x0 - wall, y0 - wall + horizontal, elevation),
            MountingSurface.EastWallInterior => new Point3(x1, y0 + horizontal, elevation),
            MountingSurface.EastWallExterior => new Point3(x1 + wall, y1 + wall - horizontal, elevation),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
    }

    public static double HorizontalCoordinate(SpaceVolume space, MountingSurface surface, Point3 point)
    {
        RequireWall(surface);
        double x0 = space.Origin.X;
        double x1 = x0 + space.WidthMillimetres;
        double y0 = space.Origin.Y;
        double y1 = y0 + space.DepthMillimetres;
        double wall = space.WallThicknessMillimetres;
        return surface switch
        {
            MountingSurface.NorthWallInterior => point.X - x0,
            MountingSurface.NorthWallExterior => x1 + wall - point.X,
            MountingSurface.SouthWallInterior => x1 - point.X,
            MountingSurface.SouthWallExterior => point.X - (x0 - wall),
            MountingSurface.WestWallInterior => y1 - point.Y,
            MountingSurface.WestWallExterior => point.Y - (y0 - wall),
            MountingSurface.EastWallInterior => point.Y - y0,
            MountingSurface.EastWallExterior => y1 + wall - point.Y,
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
    }

    public static bool IsOnWall(SpaceVolume space, MountingSurface surface, Point3 point, double toleranceMillimetres = 1)
    {
        RequireWall(surface);
        double plane = surface switch
        {
            MountingSurface.NorthWallInterior => space.Origin.Y,
            MountingSurface.NorthWallExterior => space.Origin.Y - space.WallThicknessMillimetres,
            MountingSurface.SouthWallInterior => space.Origin.Y + space.DepthMillimetres,
            MountingSurface.SouthWallExterior => space.Origin.Y + space.DepthMillimetres + space.WallThicknessMillimetres,
            MountingSurface.WestWallInterior => space.Origin.X,
            MountingSurface.WestWallExterior => space.Origin.X - space.WallThicknessMillimetres,
            MountingSurface.EastWallInterior => space.Origin.X + space.WidthMillimetres,
            MountingSurface.EastWallExterior => space.Origin.X + space.WidthMillimetres + space.WallThicknessMillimetres,
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
        double coordinate = surface is
            MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
            MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior
            ? point.Y
            : point.X;
        return Math.Abs(coordinate - plane) <= toleranceMillimetres;
    }

    private static void RequireWall(MountingSurface surface)
    {
        if (!IsWall(surface)) throw new ArgumentException($"{surface} is not a wall surface.", nameof(surface));
    }
}
