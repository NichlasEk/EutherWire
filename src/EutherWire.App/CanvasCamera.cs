using EutherWire.Document.Geometry;

namespace EutherWire.App;

internal sealed class CanvasCamera
{
    public double OriginScreenX { get; private set; } = 480;
    public double OriginScreenY { get; private set; } = 320;
    public double PixelsPerMillimetre { get; private set; } = 0.08;

    public Point2 ScreenToDocument(double screenX, double screenY) => new(
        (screenX - OriginScreenX) / PixelsPerMillimetre,
        (screenY - OriginScreenY) / PixelsPerMillimetre);

    public (double X, double Y) DocumentToScreen(Point2 point) => (
        OriginScreenX + point.X * PixelsPerMillimetre,
        OriginScreenY + point.Y * PixelsPerMillimetre);

    public void Pan(double screenDeltaX, double screenDeltaY)
    {
        OriginScreenX += screenDeltaX;
        OriginScreenY += screenDeltaY;
    }

    public void ZoomAt(double screenX, double screenY, int wheelDelta)
    {
        Point2 anchor = ScreenToDocument(screenX, screenY);
        double factor = Math.Pow(1.12, Math.Clamp(wheelDelta, -8, 8));
        PixelsPerMillimetre = Math.Clamp(PixelsPerMillimetre * factor, 0.005, 8.0);
        OriginScreenX = screenX - anchor.X * PixelsPerMillimetre;
        OriginScreenY = screenY - anchor.Y * PixelsPerMillimetre;
    }
}
