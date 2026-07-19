using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using SystemRegisIII.WaylandForge.Ui;

namespace EutherWire.App;

internal sealed class IsometricCamera
{
    private const double YawRadians = Math.PI / 4;
    private const double DepthSlope = 0.45;
    private double _scale = 0.05;
    private double _offsetX;
    private double _offsetY;
    private SpaceVolume _space = SpaceVolume.GarageDefault;

    public void Configure(RectI viewport, SpaceVolume space)
    {
        _space = space;
        Point3[] corners =
        [
            new(space.Origin.X, space.Origin.Y, 0),
            new(space.Origin.X + space.WidthMillimetres, space.Origin.Y, 0),
            new(space.Origin.X, space.Origin.Y + space.DepthMillimetres, 0),
            new(space.Origin.X + space.WidthMillimetres, space.Origin.Y + space.DepthMillimetres, 0),
            new(space.Origin.X, space.Origin.Y, space.HeightMillimetres),
            new(space.Origin.X + space.WidthMillimetres, space.Origin.Y, space.HeightMillimetres),
            new(space.Origin.X, space.Origin.Y + space.DepthMillimetres, space.HeightMillimetres),
            new(space.Origin.X + space.WidthMillimetres, space.Origin.Y + space.DepthMillimetres, space.HeightMillimetres),
        ];
        (double X, double Y)[] projected = corners.Select(ProjectUnit).ToArray();
        double minX = projected.Min(point => point.X);
        double maxX = projected.Max(point => point.X);
        double minY = projected.Min(point => point.Y);
        double maxY = projected.Max(point => point.Y);
        _scale = Math.Min((viewport.Width - 80) / (maxX - minX), (viewport.Height - 80) / (maxY - minY));
        _offsetX = viewport.X + (viewport.Width - (maxX - minX) * _scale) / 2 - minX * _scale;
        _offsetY = viewport.Y + (viewport.Height - (maxY - minY) * _scale) / 2 - minY * _scale;
    }

    public (double X, double Y) Project(Point3 point)
    {
        (double x, double y) = ProjectUnit(point);
        return (_offsetX + x * _scale, _offsetY + y * _scale);
    }

    public Point2 UnprojectFloor(double screenX, double screenY)
    {
        double rotatedX = (screenX - _offsetX) / _scale;
        double rotatedY = (screenY - _offsetY) / (_scale * DepthSlope);
        double centreX = _space.Origin.X + _space.WidthMillimetres / 2;
        double centreY = _space.Origin.Y + _space.DepthMillimetres / 2;
        double cosine = Math.Cos(YawRadians);
        double sine = Math.Sin(YawRadians);
        double x = centreX + rotatedX * cosine + rotatedY * sine;
        double y = centreY - rotatedX * sine + rotatedY * cosine;
        return new Point2(x, y);
    }

    private (double X, double Y) ProjectUnit(Point3 point)
    {
        double centreX = _space.Origin.X + _space.WidthMillimetres / 2;
        double centreY = _space.Origin.Y + _space.DepthMillimetres / 2;
        double x = point.X - centreX;
        double y = point.Y - centreY;
        double cosine = Math.Cos(YawRadians);
        double sine = Math.Sin(YawRadians);
        double rotatedX = x * cosine - y * sine;
        double rotatedY = x * sine + y * cosine;
        return (rotatedX, rotatedY * DepthSlope - point.Z);
    }
}
