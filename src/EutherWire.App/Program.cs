using EutherWire.App;
using EutherWire.Document.Geometry;
using SystemRegisIII.WaylandForge.App;
using SystemRegisIII.WaylandForge.Ui;

return ForgeApplicationHost.Run(
    new EutherWireApplication(),
    new ForgeWindowOptions(1280, 800, "EutherWire - Garage Draft"));

internal sealed class EutherWireApplication : IForgeApplication
{
    private const int ToolbarWidth = 72;
    private const int InspectorWidth = 280;
    private const int StatusHeight = 28;

    private readonly CanvasCamera _camera = new();
    private Point2 _central = new(0, 0);
    private Point2 _switch = new(6500, 800);
    private Point2 _cameraNorth = new(11200, -2600);
    private readonly List<Point2> _northRoute = [new(6500, 800), new(6500, -2600), new(11200, -2600)];
    private PointerState _previousPointer;
    private uint _handledScrollSerial;
    private string? _activeHandle;

    public void Render(in ForgeFrame frame)
    {
        HandleEditing(frame);
        HandleNavigation(frame);
        SoftwareCanvas canvas = frame.Canvas;
        canvas.Clear(0xff111820);

        int canvasRight = Math.Max(ToolbarWidth + 1, canvas.Width - InspectorWidth);
        int canvasBottom = Math.Max(1, canvas.Height - StatusHeight);
        var work = new RectI(ToolbarWidth, 0, canvasRight - ToolbarWidth, canvasBottom);
        using (canvas.PushClip(work))
        {
            canvas.FillRect(work.X, work.Y, work.Width, work.Height, 0xff17212a);
            DrawGrid(canvas, work);
            DrawGarageFixture(canvas);
            DrawHandles(canvas);
        }

        DrawChrome(canvas, work, frame.Pointer);
        _previousPointer = frame.Pointer;
    }

    private void HandleNavigation(in ForgeFrame frame)
    {
        PointerState pointer = frame.Pointer;
        bool panning = pointer.Buttons.HasFlag(PointerButtons.Middle) ||
            pointer.Buttons.HasFlag(PointerButtons.Right);
        if (panning && _previousPointer.IsInside)
        {
            _camera.Pan(pointer.X - _previousPointer.X, pointer.Y - _previousPointer.Y);
        }

        if (frame.ScrollInput.Serial != 0 && frame.ScrollInput.Serial != _handledScrollSerial)
        {
            _handledScrollSerial = frame.ScrollInput.Serial;
            _camera.ZoomAt(pointer.X, pointer.Y, frame.ScrollInput.Delta);
        }
    }

    private void HandleEditing(in ForgeFrame frame)
    {
        PointerState pointer = frame.Pointer;
        bool pressed = pointer.LeftPressed;
        bool started = pressed && !_previousPointer.LeftPressed;
        if (started)
        {
            _activeHandle = HitHandle(pointer.X, pointer.Y);
        }
        if (!pressed)
        {
            _activeHandle = null;
            return;
        }
        if (_activeHandle is null)
        {
            return;
        }

        Point2 position = _camera.ScreenToDocument(pointer.X, pointer.Y);
        switch (_activeHandle)
        {
            case "central:move":
                _central = position;
                break;
            case "poe-switch:move":
                _switch = position;
                _northRoute[0] = position;
                break;
            case "camera-north:move":
                _cameraNorth = position;
                _northRoute[^1] = position;
                break;
            case "camera-north-route:vertex:0":
                _northRoute[0] = position;
                _switch = position;
                break;
            case "camera-north-route:vertex:1":
                _northRoute[1] = position;
                break;
            case "camera-north-route:vertex:2":
                _northRoute[2] = position;
                _cameraNorth = position;
                break;
        }
    }

    private string? HitHandle(int screenX, int screenY)
    {
        foreach ((string id, Point2 position, _) in EnumerateHandles().Reverse())
        {
            (double x, double y) = _camera.DocumentToScreen(position);
            if (Math.Abs(screenX - x) <= 9 && Math.Abs(screenY - y) <= 9)
            {
                return id;
            }
        }
        return null;
    }

    private void DrawGrid(SoftwareCanvas canvas, RectI work)
    {
        double minorStep = SelectGridStep();
        Point2 topLeft = _camera.ScreenToDocument(work.X, work.Y);
        Point2 bottomRight = _camera.ScreenToDocument(work.Right, work.Bottom);
        double minX = Math.Min(topLeft.X, bottomRight.X);
        double maxX = Math.Max(topLeft.X, bottomRight.X);
        double minY = Math.Min(topLeft.Y, bottomRight.Y);
        double maxY = Math.Max(topLeft.Y, bottomRight.Y);

        double startX = Math.Floor(minX / minorStep) * minorStep;
        for (double x = startX; x <= maxX; x += minorStep)
        {
            (double screenX, _) = _camera.DocumentToScreen(new Point2(x, 0));
            bool major = IsMajorGridLine(x, minorStep);
            canvas.DrawLine((int)Math.Round(screenX), work.Y, (int)Math.Round(screenX), work.Bottom, major ? 0xff334756 : 0xff24333e);
        }

        double startY = Math.Floor(minY / minorStep) * minorStep;
        for (double y = startY; y <= maxY; y += minorStep)
        {
            (_, double screenY) = _camera.DocumentToScreen(new Point2(0, y));
            bool major = IsMajorGridLine(y, minorStep);
            canvas.DrawLine(work.X, (int)Math.Round(screenY), work.Right, (int)Math.Round(screenY), major ? 0xff334756 : 0xff24333e);
        }
    }

    private double SelectGridStep()
    {
        double[] steps = [10, 20, 50, 100, 200, 500, 1000, 2000, 5000];
        return steps.First(step => step * _camera.PixelsPerMillimetre >= 14);
    }

    private static bool IsMajorGridLine(double coordinate, double minorStep) =>
        Math.Abs(coordinate / (minorStep * 5) - Math.Round(coordinate / (minorStep * 5))) < 0.00001;

    private void DrawGarageFixture(SoftwareCanvas canvas)
    {
        DrawDevice(canvas, _central, 1500, 900, "CENTRAL", 0xffd7a84a);
        DrawDevice(canvas, _switch, 1300, 700, "POE-SW", 0xff4aa6d7);
        DrawDevice(canvas, _cameraNorth, 700, 500, "KAM-N", 0xff64c987);

        DrawRoute(canvas, _northRoute, 0xff708898, 5);
        DrawRoute(canvas, _northRoute, 0xff55c8ff, 2);
    }

    private void DrawHandles(SoftwareCanvas canvas)
    {
        foreach ((string id, Point2 position, uint color) in EnumerateHandles())
        {
            (double x, double y) = _camera.DocumentToScreen(position);
            int size = id == _activeHandle ? 11 : 7;
            canvas.FillRect((int)Math.Round(x) - size / 2, (int)Math.Round(y) - size / 2, size, size, 0xff101820);
            canvas.DrawRect((int)Math.Round(x) - size / 2, (int)Math.Round(y) - size / 2, size, size, color);
        }
    }

    private IEnumerable<(string Id, Point2 Position, uint Color)> EnumerateHandles()
    {
        yield return ("central:move", _central, 0xffffcc66);
        yield return ("poe-switch:move", _switch, 0xffffcc66);
        yield return ("camera-north:move", _cameraNorth, 0xffffcc66);
        for (int index = 0; index < _northRoute.Count; index++)
        {
            yield return ($"camera-north-route:vertex:{index}", _northRoute[index], 0xff63d4ff);
        }
    }

    private void DrawDevice(SoftwareCanvas canvas, Point2 centre, double widthMm, double heightMm, string label, uint color)
    {
        (double x, double y) = _camera.DocumentToScreen(new Point2(centre.X - widthMm / 2, centre.Y - heightMm / 2));
        int width = Math.Max(8, (int)Math.Round(widthMm * _camera.PixelsPerMillimetre));
        int height = Math.Max(8, (int)Math.Round(heightMm * _camera.PixelsPerMillimetre));
        canvas.FillRect((int)x, (int)y, width, height, 0xff20303b);
        canvas.DrawRect((int)x, (int)y, width, height, color);
        canvas.DrawText((int)x + 6, (int)y + 7, label, color);
    }

    private void DrawRoute(SoftwareCanvas canvas, IReadOnlyList<Point2> points, uint color, int thickness)
    {
        for (int index = 1; index < points.Count; index++)
        {
            (double x0, double y0) = _camera.DocumentToScreen(points[index - 1]);
            (double x1, double y1) = _camera.DocumentToScreen(points[index]);
            for (int offset = -(thickness / 2); offset <= thickness / 2; offset++)
            {
                canvas.DrawLine((int)x0 + offset, (int)y0, (int)x1 + offset, (int)y1, color);
            }
        }
    }

    private void DrawChrome(SoftwareCanvas canvas, RectI work, PointerState pointer)
    {
        canvas.FillRect(0, 0, ToolbarWidth, canvas.Height, 0xff0c1218);
        canvas.DrawLine(ToolbarWidth - 1, 0, ToolbarWidth - 1, canvas.Height, 0xff40515e);
        canvas.DrawText(14, 16, "EW", 0xff63d4ff, 2);
        string[] tools = ["SEL", "PAN", "DEV", "WIRE", "PIPE", "TEXT"];
        for (int index = 0; index < tools.Length; index++)
        {
            int y = 64 + index * 48;
            canvas.DrawRect(10, y, 52, 34, index == 0 ? 0xff63d4ff : 0xff3c5261);
            canvas.DrawText(20, y + 13, tools[index], index == 0 ? 0xff63d4ff : 0xff9eb0bb);
        }

        int inspectorX = work.Right;
        canvas.FillRect(inspectorX, 0, canvas.Width - inspectorX, canvas.Height, 0xff101820);
        canvas.DrawLine(inspectorX, 0, inspectorX, canvas.Height, 0xff40515e);
        canvas.DrawText(inspectorX + 18, 20, "GARAGE DRAFT", 0xffe3edf2, 2);
        canvas.DrawText(inspectorX + 18, 60, $"Handle: {_activeHandle ?? "none"}", 0xff9eb0bb);
        canvas.DrawText(inspectorX + 18, 86, "Layer: Installation", 0xff9eb0bb);

        canvas.FillRect(ToolbarWidth, work.Bottom, work.Width, StatusHeight, 0xff0b1117);
        Point2 documentPoint = _camera.ScreenToDocument(pointer.X, pointer.Y);
        canvas.DrawText(ToolbarWidth + 12, work.Bottom + 10,
            $"X {documentPoint.X:0} mm   Y {documentPoint.Y:0} mm   Zoom {_camera.PixelsPerMillimetre * 1000:0} px/m   MMB/RMB pan   Wheel zoom",
            0xff91a6b3);
    }
}
