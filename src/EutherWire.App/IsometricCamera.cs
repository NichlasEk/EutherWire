using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using SystemRegisIII.WaylandForge.Ui;

namespace EutherWire.App;

internal sealed class IsometricCamera
{
    private double _yawRadians = Math.PI / 4;
    private double _depthSlope = 0.45;
    private double _zoom = 1;
    private double _panX;
    private double _panY;
    private double _scale = 0.05;
    private double _offsetX;
    private double _offsetY;
    private SpaceVolume _space = SpaceVolume.GarageDefault;
    private CameraView _view = CameraView.Iso;

    public string ViewLabel => _view.ToString().ToUpperInvariant();

    public void Orbit(double deltaX, double deltaY)
    {
        _yawRadians = NormalizeRadians(_yawRadians + deltaX * 0.008);
        _depthSlope = Math.Clamp(_depthSlope + deltaY * 0.004, 0.12, 0.9);
        _view = CameraView.Free;
    }

    public void Pan(double deltaX, double deltaY)
    {
        _panX += deltaX;
        _panY += deltaY;
        _view = CameraView.Free;
    }

    public void Zoom(double scrollDelta)
    {
        double factor = Math.Pow(1.12, Math.Clamp(scrollDelta, -8, 8));
        _zoom = Math.Clamp(_zoom * factor, 0.45, 4);
    }

    public void CycleView()
    {
        CameraView next = _view switch
        {
            CameraView.Iso or CameraView.Free => CameraView.North,
            CameraView.North => CameraView.East,
            CameraView.East => CameraView.South,
            CameraView.South => CameraView.West,
            _ => CameraView.Iso,
        };
        SetView(next);
    }

    public void SetNamedView(string name)
    {
        if (!Enum.TryParse(name, true, out CameraView view) || view == CameraView.Free)
        {
            throw new ArgumentException($"Unknown 3D camera view '{name}'.", nameof(name));
        }
        SetView(view);
    }

    private void SetView(CameraView view)
    {
        _view = view;
        _yawRadians = view switch
        {
            CameraView.North => Math.PI / 8,
            CameraView.East => Math.PI * 5 / 8,
            CameraView.South => Math.PI * 9 / 8,
            CameraView.West => Math.PI * 13 / 8,
            _ => Math.PI / 4,
        };
        _depthSlope = view == CameraView.Iso ? 0.45 : 0.32;
        _zoom = 1;
        _panX = 0;
        _panY = 0;
    }

    public void Configure(RectI viewport, SpaceVolume space)
    {
        _space = space;
        double wall = space.WallThicknessMillimetres;
        double outerTop = space.HeightMillimetres + space.CeilingThicknessMillimetres;
        Point3[] corners =
        [
            new(space.Origin.X - wall, space.Origin.Y - wall, 0),
            new(space.Origin.X + space.WidthMillimetres + wall, space.Origin.Y - wall, 0),
            new(space.Origin.X - wall, space.Origin.Y + space.DepthMillimetres + wall, 0),
            new(space.Origin.X + space.WidthMillimetres + wall, space.Origin.Y + space.DepthMillimetres + wall, 0),
            new(space.Origin.X - wall, space.Origin.Y - wall, outerTop),
            new(space.Origin.X + space.WidthMillimetres + wall, space.Origin.Y - wall, outerTop),
            new(space.Origin.X - wall, space.Origin.Y + space.DepthMillimetres + wall, outerTop),
            new(space.Origin.X + space.WidthMillimetres + wall, space.Origin.Y + space.DepthMillimetres + wall, outerTop),
        ];
        (double X, double Y)[] projected = corners.Select(ProjectUnit).ToArray();
        double minX = projected.Min(point => point.X);
        double maxX = projected.Max(point => point.X);
        double minY = projected.Min(point => point.Y);
        double maxY = projected.Max(point => point.Y);
        _scale = Math.Min((viewport.Width - 80) / (maxX - minX), (viewport.Height - 80) / (maxY - minY)) * _zoom;
        _offsetX = viewport.X + (viewport.Width - (maxX - minX) * _scale) / 2 - minX * _scale + _panX;
        _offsetY = viewport.Y + (viewport.Height - (maxY - minY) * _scale) / 2 - minY * _scale + _panY;
    }

    public (double X, double Y) Project(Point3 point)
    {
        (double x, double y) = ProjectUnit(point);
        return (_offsetX + x * _scale, _offsetY + y * _scale);
    }

    public Point2 UnprojectFloor(double screenX, double screenY)
        => UnprojectSurface(screenX, screenY, MountingSurface.FloorInterior).Plan;

    public Point3 UnprojectSurface(double screenX, double screenY, MountingSurface surface)
    {
        double rotatedX = (screenX - _offsetX) / _scale;
        double projectedY = (screenY - _offsetY) / _scale;
        double centreX = _space.Origin.X + _space.WidthMillimetres / 2;
        double centreY = _space.Origin.Y + _space.DepthMillimetres / 2;
        double cosine = Math.Cos(_yawRadians);
        double sine = Math.Sin(_yawRadians);
        double wall = _space.WallThicknessMillimetres;

        if (surface is MountingSurface.FloorInterior or MountingSurface.CeilingInterior or MountingSurface.CeilingExterior or MountingSurface.Free)
        {
            double z = surface switch
            {
                MountingSurface.CeilingInterior => _space.HeightMillimetres,
                MountingSurface.CeilingExterior => _space.HeightMillimetres + _space.CeilingThicknessMillimetres,
                _ => 0,
            };
            double rotatedY = (projectedY + z) / _depthSlope;
            double x = centreX + rotatedX * cosine + rotatedY * sine;
            double y = centreY - rotatedX * sine + rotatedY * cosine;
            double inset = surface == MountingSurface.CeilingExterior ? wall : 0;
            return new Point3(
                Math.Clamp(x, _space.Origin.X - inset, _space.Origin.X + _space.WidthMillimetres + inset),
                Math.Clamp(y, _space.Origin.Y - inset, _space.Origin.Y + _space.DepthMillimetres + inset),
                z);
        }

        bool fixedX = surface is MountingSurface.WestWallInterior or MountingSurface.WestWallExterior or MountingSurface.EastWallInterior or MountingSurface.EastWallExterior;
        double xPlane = surface switch
        {
            MountingSurface.WestWallInterior => _space.Origin.X,
            MountingSurface.WestWallExterior => _space.Origin.X - wall,
            MountingSurface.EastWallInterior => _space.Origin.X + _space.WidthMillimetres,
            MountingSurface.EastWallExterior => _space.Origin.X + _space.WidthMillimetres + wall,
            _ => 0,
        };
        double yPlane = surface switch
        {
            MountingSurface.NorthWallInterior => _space.Origin.Y,
            MountingSurface.NorthWallExterior => _space.Origin.Y - wall,
            MountingSurface.SouthWallInterior => _space.Origin.Y + _space.DepthMillimetres,
            MountingSurface.SouthWallExterior => _space.Origin.Y + _space.DepthMillimetres + wall,
            _ => 0,
        };
        double relativeX;
        double relativeY;
        if (fixedX)
        {
            relativeX = xPlane - centreX;
            relativeY = (relativeX * cosine - rotatedX) / SafeDivisor(sine);
        }
        else
        {
            relativeY = yPlane - centreY;
            relativeX = (rotatedX + relativeY * sine) / SafeDivisor(cosine);
        }
        double zValue = _depthSlope * (relativeX * sine + relativeY * cosine) - projectedY;
        bool exterior = surface.ToString().EndsWith("Exterior", StringComparison.Ordinal);
        double minX = _space.Origin.X - (exterior ? wall : 0);
        double maxX = _space.Origin.X + _space.WidthMillimetres + (exterior ? wall : 0);
        double minY = _space.Origin.Y - (exterior ? wall : 0);
        double maxY = _space.Origin.Y + _space.DepthMillimetres + (exterior ? wall : 0);
        double maxZ = _space.HeightMillimetres + (exterior ? _space.CeilingThicknessMillimetres : 0);
        return new Point3(
            fixedX ? xPlane : Math.Clamp(centreX + relativeX, minX, maxX),
            fixedX ? Math.Clamp(centreY + relativeY, minY, maxY) : yPlane,
            Math.Clamp(zValue, 0, maxZ));
    }

    public Point3 UnprojectElevation(double screenY, Point2 planPosition)
    {
        double centreX = _space.Origin.X + _space.WidthMillimetres / 2;
        double centreY = _space.Origin.Y + _space.DepthMillimetres / 2;
        double relativeX = planPosition.X - centreX;
        double relativeY = planPosition.Y - centreY;
        double rotatedY = relativeX * Math.Sin(_yawRadians) + relativeY * Math.Cos(_yawRadians);
        double projectedY = (screenY - _offsetY) / _scale;
        double elevation = Math.Max(0, rotatedY * _depthSlope - projectedY);
        return new Point3(planPosition.X, planPosition.Y, elevation);
    }

    private (double X, double Y) ProjectUnit(Point3 point)
    {
        double centreX = _space.Origin.X + _space.WidthMillimetres / 2;
        double centreY = _space.Origin.Y + _space.DepthMillimetres / 2;
        double x = point.X - centreX;
        double y = point.Y - centreY;
        double cosine = Math.Cos(_yawRadians);
        double sine = Math.Sin(_yawRadians);
        double rotatedX = x * cosine - y * sine;
        double rotatedY = x * sine + y * cosine;
        return (rotatedX, rotatedY * _depthSlope - point.Z);
    }

    private static double SafeDivisor(double value) => Math.Abs(value) < 0.05 ? Math.CopySign(0.05, value == 0 ? 1 : value) : value;

    private static double NormalizeRadians(double radians)
    {
        double full = Math.PI * 2;
        double normalized = radians % full;
        return normalized < 0 ? normalized + full : normalized;
    }

    private enum CameraView
    {
        Iso,
        North,
        East,
        South,
        West,
        Free,
    }
}
