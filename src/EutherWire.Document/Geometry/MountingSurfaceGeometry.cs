using EutherWire.Document.Model;

namespace EutherWire.Document.Geometry;

public static class MountingSurfaceGeometry
{
    public static Point3 Constrain(SpaceVolume space, MountingSurface surface, Point3 point)
    {
        space.Validate();
        double x0 = space.Origin.X;
        double x1 = x0 + space.WidthMillimetres;
        double y0 = space.Origin.Y;
        double y1 = y0 + space.DepthMillimetres;
        double wall = space.WallThicknessMillimetres;
        double top = space.HeightMillimetres;
        double outerTop = top + space.CeilingThicknessMillimetres;
        bool exterior = surface.ToString().EndsWith("Exterior", StringComparison.Ordinal);
        double minX = x0 - (exterior ? wall : 0);
        double maxX = x1 + (exterior ? wall : 0);
        double minY = y0 - (exterior ? wall : 0);
        double maxY = y1 + (exterior ? wall : 0);
        double maxZ = exterior ? outerTop : top;

        return surface switch
        {
            MountingSurface.Free => point,
            MountingSurface.FloorInterior => new Point3(Math.Clamp(point.X, x0, x1), Math.Clamp(point.Y, y0, y1), 0),
            MountingSurface.CeilingInterior => new Point3(Math.Clamp(point.X, x0, x1), Math.Clamp(point.Y, y0, y1), top),
            MountingSurface.CeilingExterior => new Point3(Math.Clamp(point.X, minX, maxX), Math.Clamp(point.Y, minY, maxY), outerTop),
            MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior => new Point3(Math.Clamp(point.X, minX, maxX), exterior ? y0 - wall : y0, Math.Clamp(point.Z, 0, maxZ)),
            MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior => new Point3(Math.Clamp(point.X, minX, maxX), exterior ? y1 + wall : y1, Math.Clamp(point.Z, 0, maxZ)),
            MountingSurface.WestWallInterior or MountingSurface.WestWallExterior => new Point3(exterior ? x0 - wall : x0, Math.Clamp(point.Y, minY, maxY), Math.Clamp(point.Z, 0, maxZ)),
            MountingSurface.EastWallInterior or MountingSurface.EastWallExterior => new Point3(exterior ? x1 + wall : x1, Math.Clamp(point.Y, minY, maxY), Math.Clamp(point.Z, 0, maxZ)),
            _ => point,
        };
    }
}
