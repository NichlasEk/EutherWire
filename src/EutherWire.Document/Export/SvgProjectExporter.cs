using System.Globalization;
using System.Text;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Export;

public static class SvgProjectExporter
{
    public static string Export(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectBounds bounds = ProjectExportLayout.CalculateBounds(document).Expand(ProjectExportLayout.MarginMillimetres);
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

        svg.Append("  <g id=\"openings\" fill=\"none\" stroke=\"#c56f42\" stroke-width=\"90\">\n");
        foreach (BuildingOpening opening in document.Openings.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            Polyline(svg, opening.Id, ProjectExportLayout.OpeningPlanEdge(opening));
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
            ExportDeviceStyle style = ProjectExportLayout.DeviceStyle(device.Kind);
            (double width, double height, string color) = (style.Width, style.Height, style.SvgColor);
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

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Xml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

}
