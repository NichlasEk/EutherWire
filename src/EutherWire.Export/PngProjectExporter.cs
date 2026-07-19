using System.Buffers.Binary;
using System.IO.Compression;
using EutherWire.Document.Export;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using SystemRegisIII.WaylandForge.Ui;

namespace EutherWire.Export;

public static class PngProjectExporter
{
    public const int DefaultMaxWidth = 1600;
    public const int DefaultMaxHeight = 1200;

    public static byte[] Export(ProjectDocument document, int maxWidth = DefaultMaxWidth, int maxHeight = DefaultMaxHeight)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (maxWidth is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (maxHeight is < 64 or > 8192) throw new ArgumentOutOfRangeException(nameof(maxHeight));

        ProjectBounds bounds = ProjectExportLayout.CalculateBounds(document).Expand(ProjectExportLayout.MarginMillimetres);
        double scale = Math.Min(maxWidth / bounds.Width, maxHeight / bounds.Height);
        int width = Math.Clamp((int)Math.Ceiling(bounds.Width * scale), 1, maxWidth);
        int height = Math.Clamp((int)Math.Ceiling(bounds.Height * scale), 1, maxHeight);
        var pixels = new uint[checked(width * height)];
        Render(document, bounds, scale, width, height, pixels);
        return EncodePng(width, height, pixels);
    }

    public static void Save(string path, ProjectDocument document, int maxWidth = DefaultMaxWidth, int maxHeight = DefaultMaxHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, Export(document, maxWidth, maxHeight));
        File.Move(temporaryPath, path, true);
    }

    private static unsafe void Render(ProjectDocument document, ProjectBounds bounds, double scale, int width, int height, uint[] pixels)
    {
        fixed (uint* pointer = pixels)
        {
            var canvas = new SoftwareCanvas();
            canvas.Bind(pointer, width, height, width);
            canvas.Clear(0xffffffff);

            (int X, int Y) Screen(Point2 point) => (
                (int)Math.Round((point.X - bounds.MinX) * scale),
                (int)Math.Round((point.Y - bounds.MinY) * scale));

            foreach (Conduit conduit in document.Conduits.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                DrawRoute(canvas, conduit.Route.Points, Screen, 0xff708898, Math.Max(3, (int)Math.Round(scale * 70)));
            }
            foreach (BuildingOpening opening in document.Openings.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                DrawRoute(canvas, ProjectExportLayout.OpeningPlanEdge(opening), Screen, 0xffc56f42, Math.Max(4, (int)Math.Round(scale * 90)));
            }
            foreach (CableRoute cable in document.Cables.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                DrawRoute(canvas, cable.Route.Points, Screen, 0xff159dcc, Math.Max(2, (int)Math.Round(scale * 28)));
            }
            foreach (Device device in document.Devices.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                ExportDeviceStyle style = ProjectExportLayout.DeviceStyle(device.Kind);
                (int left, int top) = Screen(new Point2(device.Position.X - style.Width / 2, device.Position.Y - style.Height / 2));
                int deviceWidth = Math.Max(8, (int)Math.Round(style.Width * scale));
                int deviceHeight = Math.Max(8, (int)Math.Round(style.Height * scale));
                canvas.FillRect(left, top, deviceWidth, deviceHeight, 0xffeef3f6);
                DrawRect(canvas, left, top, deviceWidth, deviceHeight, style.Argb, Math.Max(2, (int)Math.Round(scale * 28)));

                int textScale = Math.Clamp((int)Math.Round(scale * 16), 1, 4);
                canvas.DrawText(left + 6, top + Math.Max(4, deviceHeight / 2 - 4 * textScale), device.Label, 0xff17212a, textScale);
                foreach (Port port in device.Ports.OrderBy(item => item.Id, StringComparer.Ordinal))
                {
                    Point2 portPosition = RotatePort(device, port);
                    (int portX, int portY) = Screen(portPosition);
                    int size = Math.Max(5, (int)Math.Round(scale * 90));
                    canvas.FillRect(portX - size / 2, portY - size / 2, size, size, 0xff20a968);
                }
            }
            foreach (Annotation annotation in document.Annotations.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                (int x, int y) = Screen(annotation.Position);
                int textScale = Math.Clamp((int)Math.Round(scale * 18), 1, 4);
                canvas.DrawText(x, y, annotation.Text, 0xff17212a, textScale);
            }
        }
    }

    private static Point2 RotatePort(Device device, Port port)
    {
        double radians = device.RotationDegrees * Math.PI / 180;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new Point2(
            device.Position.X + port.Position.X * cosine - port.Position.Y * sine,
            device.Position.Y + port.Position.X * sine + port.Position.Y * cosine);
    }

    private static void DrawRoute(
        SoftwareCanvas canvas,
        IReadOnlyList<Point2> points,
        Func<Point2, (int X, int Y)> screen,
        uint color,
        int thickness)
    {
        for (int index = 1; index < points.Count; index++)
        {
            (int x0, int y0) = screen(points[index - 1]);
            (int x1, int y1) = screen(points[index]);
            double dx = x1 - x0;
            double dy = y1 - y0;
            double length = Math.Sqrt(dx * dx + dy * dy);
            for (int offset = -(thickness / 2); offset <= thickness / 2; offset++)
            {
                int offsetX = length > 0 ? (int)Math.Round(-dy / length * offset) : offset;
                int offsetY = length > 0 ? (int)Math.Round(dx / length * offset) : 0;
                canvas.DrawLine(x0 + offsetX, y0 + offsetY, x1 + offsetX, y1 + offsetY, color);
            }
        }
    }

    private static void DrawRect(SoftwareCanvas canvas, int x, int y, int width, int height, uint color, int thickness)
    {
        for (int offset = 0; offset < thickness; offset++)
        {
            canvas.DrawRect(x + offset, y + offset, width - offset * 2, height - offset * 2, color);
        }
    }

    private static byte[] EncodePng(int width, int height, IReadOnlyList<uint> pixels)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR"u8, header);

        byte[] scanlines = new byte[checked(height * (width * 4 + 1))];
        int destination = 0;
        for (int y = 0; y < height; y++)
        {
            scanlines[destination++] = 0;
            for (int x = 0; x < width; x++)
            {
                uint argb = pixels[y * width + x];
                scanlines[destination++] = (byte)(argb >> 16);
                scanlines[destination++] = (byte)(argb >> 8);
                scanlines[destination++] = (byte)argb;
                scanlines[destination++] = (byte)(argb >> 24);
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, true))
        {
            zlib.Write(scanlines);
        }
        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        uint crc = 0xffffffff;
        foreach (byte value in type) crc = UpdateCrc(crc, value);
        foreach (byte value in data) crc = UpdateCrc(crc, value);
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xffffffff);
        output.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }
}
