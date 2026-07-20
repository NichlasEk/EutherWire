using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using SystemRegisIII.WaylandForge.Ui;

namespace EutherWire.App;

internal sealed class WallCamera
{
    private const double PaddingPixels = 54;
    private static readonly double[] ZoomLevels =
    [
        0.20, 0.25, 0.33, 0.40, 0.50, 0.67, 0.80, 1.00,
        1.25, 1.50, 2.00, 2.50, 3.20, 4.00, 5.00, 6.50,
        8.00, 10.00, 12.00, 16.00, 20.00, 24.00, 32.00,
    ];
    private RectI _work;
    private SpaceVolume _space = SpaceVolume.GarageDefault;
    private MountingSurface _surface = MountingSurface.NorthWallInterior;
    private double _fitScale = 0.1;
    private double _zoom = 1;
    private int _zoomIndex = 7;
    private double _panX;
    private double _panY;

    public double PixelsPerMillimetre => _fitScale * _zoom;
    public int ZoomPercent => (int)Math.Round(_zoom * 100);

    public double WallWidthMillimetres => WallCoordinateSystem.Width(_space, _surface);

    public double WallHeightMillimetres => WallCoordinateSystem.Height(_space, _surface);

    public void Configure(RectI work, SpaceVolume space, MountingSurface surface)
    {
        if (!WallCoordinateSystem.IsWall(surface)) surface = MountingSurface.NorthWallInterior;
        bool changed = _surface != surface || _space != space;
        _work = work;
        _space = space;
        _surface = surface;
        double availableWidth = Math.Max(1, work.Width - PaddingPixels * 2);
        double availableHeight = Math.Max(1, work.Height - PaddingPixels * 2);
        _fitScale = Math.Max(0.0001, Math.Min(availableWidth / WallWidthMillimetres, availableHeight / WallHeightMillimetres));
        if (changed)
        {
            _zoom = 1;
            _zoomIndex = 7;
            _panX = 0;
            _panY = 0;
        }
    }

    public (double X, double Y) Project(Point3 point)
    {
        double horizontal = HorizontalCoordinate(point);
        double scale = PixelsPerMillimetre;
        return (
            _work.X + _work.Width / 2.0 + (horizontal - WallWidthMillimetres / 2) * scale + _panX,
            _work.Y + _work.Height / 2.0 - (point.Z - WallHeightMillimetres / 2) * scale + _panY);
    }

    public Point3 Unproject(double screenX, double screenY)
    {
        double scale = PixelsPerMillimetre;
        double horizontal = WallWidthMillimetres / 2 + (screenX - (_work.X + _work.Width / 2.0) - _panX) / scale;
        double elevation = WallHeightMillimetres / 2 - (screenY - (_work.Y + _work.Height / 2.0) - _panY) / scale;
        return PointFromWallCoordinates(
            Math.Clamp(horizontal, 0, WallWidthMillimetres),
            Math.Clamp(elevation, 0, WallHeightMillimetres));
    }

    public void Pan(double deltaX, double deltaY)
    {
        _panX += deltaX;
        _panY += deltaY;
    }

    public void ZoomAt(double screenX, double screenY, double delta)
    {
        if (delta == 0) return;
        Point3 anchor = Unproject(screenX, screenY);
        int nextIndex = Math.Clamp(_zoomIndex + (delta < 0 ? 1 : -1), 0, ZoomLevels.Length - 1);
        if (nextIndex == _zoomIndex) return;
        _zoomIndex = nextIndex;
        _zoom = ZoomLevels[_zoomIndex];
        (double projectedX, double projectedY) = Project(anchor);
        _panX += screenX - projectedX;
        _panY += screenY - projectedY;
    }

    public bool IsOnWall(Point3 point, double toleranceMillimetres = 1)
        => WallCoordinateSystem.IsOnWall(_space, _surface, point, toleranceMillimetres);

    public double HorizontalCoordinate(Point3 point)
        => WallCoordinateSystem.HorizontalCoordinate(_space, _surface, point);

    public Point3 PointFromWallCoordinates(double horizontal, double elevation)
        => WallCoordinateSystem.FromWallCoordinates(_space, _surface, horizontal, elevation);
}
