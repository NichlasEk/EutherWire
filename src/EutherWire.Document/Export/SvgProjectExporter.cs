using System.Globalization;
using System.Text;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Export;

public static class SvgProjectExporter
{
    private const double MarginMillimetres = 500;

    public static string Export(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Bounds bounds = CalculateBounds(document).Expand(MarginMillimetres);
        var svg = new StringBuilder();
        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
            .Append(Number(bounds.MinX)).Append(' ').Append(Number(bounds.MinY)).Append(' ')
            .Append(Number(bounds.Width)).Append(' ').Append(Number(bounds.Height))
            .Append("\" role=\"img\">\n");
        svg.Append("  <title>").Append(Xml(document.Name)).Append("</title>\n");
        svg.Append("  <rect x=\"").Append(Number(bounds.MinX)).Append("\" y=\"").Append(Number(bounds.MinY))
            .Append("\" width=\"").Append(Number(bounds.Width)).Append("\" height=\"").Append(Number(bounds.Height))
            .Append("\" fill=\"#ffffff\"/>\n");

        svg.Append("  <g id=\"conduits\" fill=\"none\" stroke=\"#708898\" stroke-width=\"70\" stroke-linejoin=\"round\">\n");
        foreach (Conduit conduit in document.Conduits.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            Polyline(svg, conduit.Id, conduit.Route.Points);
        }
        svg.Append("  </g>\n");

        svg.Append("  <g id=\"cables\" fill=\"none\" stroke=\"#159dcc\" stroke-width=\"28\" stroke-linejoin=\"round\">\n");
        foreach (CableRoute cable in document.Cables.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            Polyline(svg, cable.Id, cable.Route.Points);
        }
        svg.Append("  </g>\n");

        svg.Append("  <g id=\"devices\" font-family=\"sans-serif\" font-size=\"170\">\n");
        foreach (Device device in document.Devices.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            (double width, double height, string color) = DeviceStyle(device.Kind);
            svg.Append("    <g id=\"").Append(Xml(device.Id.Value)).Append("\" transform=\"rotate(")
                .Append(Number(device.RotationDegrees)).Append(' ').Append(Number(device.Position.X)).Append(' ').Append(Number(device.Position.Y)).Append(")\">\n");
            svg.Append("      <rect x=\"").Append(Number(device.Position.X - width / 2)).Append("\" y=\"")
                .Append(Number(device.Position.Y - height / 2)).Append("\" width=\"").Append(Number(width))
                .Append("\" height=\"").Append(Number(height)).Append("\" fill=\"#eef3f6\" stroke=\"").Append(color)
                .Append("\" stroke-width=\"28\"/>\n");
            svg.Append("      <text x=\"").Append(Number(device.Position.X - width / 2 + 90)).Append("\" y=\"")
                .Append(Number(device.Position.Y)).Append("\" fill=\"#17212a\">").Append(Xml(device.Label)).Append("</text>\n");
            foreach (Port port in device.Ports.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                svg.Append("      <circle data-port=\"").Append(Xml(port.Id)).Append("\" cx=\"")
                    .Append(Number(device.Position.X + port.Position.X)).Append("\" cy=\"")
                    .Append(Number(device.Position.Y + port.Position.Y)).Append("\" r=\"45\" fill=\"#20a968\"/>\n");
            }
            svg.Append("    </g>\n");
        }
        svg.Append("  </g>\n");

        svg.Append("  <g id=\"annotations\" font-family=\"sans-serif\" font-size=\"180\" fill=\"#17212a\">\n");
        foreach (Annotation annotation in document.Annotations.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            svg.Append("    <text id=\"").Append(Xml(annotation.Id.Value)).Append("\" x=\"")
                .Append(Number(annotation.Position.X)).Append("\" y=\"").Append(Number(annotation.Position.Y))
                .Append("\">").Append(Xml(annotation.Text)).Append("</text>\n");
        }
        svg.Append("  </g>\n</svg>\n");
        return svg.ToString();
    }

    public static void Save(string path, ProjectDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, Export(document));
        File.Move(temporaryPath, path, true);
    }

    private static void Polyline(StringBuilder svg, ObjectId id, IReadOnlyList<Point2> points)
    {
        svg.Append("    <polyline id=\"").Append(Xml(id.Value)).Append("\" points=\"");
        for (int index = 0; index < points.Count; index++)
        {
            if (index > 0) svg.Append(' ');
            svg.Append(Number(points[index].X)).Append(',').Append(Number(points[index].Y));
        }
        svg.Append("\"/>\n");
    }

    private static Bounds CalculateBounds(ProjectDocument document)
    {
        var bounds = new Bounds();
        foreach (Conduit conduit in document.Conduits.Values) bounds.Include(conduit.Route.Points);
        foreach (CableRoute cable in document.Cables.Values) bounds.Include(cable.Route.Points);
        foreach (Annotation annotation in document.Annotations.Values) bounds.Include(annotation.Position);
        foreach (Device device in document.Devices.Values)
        {
            (double width, double height, _) = DeviceStyle(device.Kind);
            bounds.Include(new Point2(device.Position.X - width / 2, device.Position.Y - height / 2));
            bounds.Include(new Point2(device.Position.X + width / 2, device.Position.Y + height / 2));
        }
        return bounds.HasValue ? bounds : new Bounds(-500, -500, 500, 500);
    }

    private static (double Width, double Height, string Color) DeviceStyle(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => (1500, 900, "#b88115"),
        DeviceKind.PoeSwitch => (1300, 700, "#167fae"),
        DeviceKind.Camera => (700, 500, "#258a4d"),
        _ => (800, 600, "#667985"),
    };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Xml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    private sealed class Bounds
    {
        public Bounds()
        {
        }

        public Bounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            HasValue = true;
        }

        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool HasValue { get; private set; }

        public void Include(IEnumerable<Point2> points)
        {
            foreach (Point2 point in points) Include(point);
        }

        public void Include(Point2 point)
        {
            if (!HasValue)
            {
                MinX = MaxX = point.X;
                MinY = MaxY = point.Y;
                HasValue = true;
                return;
            }
            MinX = Math.Min(MinX, point.X);
            MinY = Math.Min(MinY, point.Y);
            MaxX = Math.Max(MaxX, point.X);
            MaxY = Math.Max(MaxY, point.Y);
        }

        public Bounds Expand(double amount) => new(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);
    }
}
