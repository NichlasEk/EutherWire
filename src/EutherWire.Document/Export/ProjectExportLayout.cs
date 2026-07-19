using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Export;

public readonly record struct ProjectBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public ProjectBounds Expand(double amount) => new(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);
}

public readonly record struct ExportDeviceStyle(double Width, double Height, uint Argb, string SvgColor);

public static class ProjectExportLayout
{
    public const double MarginMillimetres = 500;

    public static ProjectBounds CalculateBounds(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        void Include(Point2 point)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        foreach (Conduit conduit in document.Conduits.Values)
        {
            foreach (Point2 point in conduit.Route.Points) Include(point);
        }
        foreach (CableRoute cable in document.Cables.Values)
        {
            foreach (Point2 point in cable.Route.Points) Include(point);
        }
        foreach (Annotation annotation in document.Annotations.Values) Include(annotation.Position);
        foreach (Device device in document.Devices.Values)
        {
            ExportDeviceStyle style = DeviceStyle(device.Kind);
            Include(new Point2(device.Position.X - style.Width / 2, device.Position.Y - style.Height / 2));
            Include(new Point2(device.Position.X + style.Width / 2, device.Position.Y + style.Height / 2));
        }

        return double.IsFinite(minX)
            ? new ProjectBounds(minX, minY, maxX, maxY)
            : new ProjectBounds(-500, -500, 500, 500);
    }

    public static ExportDeviceStyle DeviceStyle(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => new(1500, 900, 0xffb88115, "#b88115"),
        DeviceKind.PoeSwitch => new(1300, 700, 0xff167fae, "#167fae"),
        DeviceKind.Camera => new(700, 500, 0xff258a4d, "#258a4d"),
        _ => new(800, 600, 0xff667985, "#667985"),
    };
}
