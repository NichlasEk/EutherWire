using EutherWire.App;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Templates;
using SystemRegisIII.WaylandForge.App;
using SystemRegisIII.WaylandForge.Ui;

string? startupDirectory = args.Length > 0
    ? args[0]
    : Directory.Exists(Path.Combine("examples", "garage.eutherwire"))
        ? Path.Combine("examples", "garage.eutherwire")
        : null;
ProjectDocument startupDocument = startupDirectory is not null
    ? ProjectToml.Load(startupDirectory)
    : ProjectTemplates.CreateGarageDraft();

return ForgeApplicationHost.Run(
    new EutherWireApplication(startupDocument, startupDirectory),
    new ForgeWindowOptions(1280, 800, $"EutherWire - {startupDocument.Name}"));

internal sealed class EutherWireApplication : IForgeApplication
{
    private const int ToolbarWidth = 72;
    private const int InspectorWidth = 280;
    private const int StatusHeight = 28;

    private readonly CanvasCamera _camera = new();
    private readonly ProjectDocument _document;
    private readonly string? _projectDirectory;
    private readonly CommandHistory _history = new();
    private PointerState _previousPointer;
    private uint _handledScrollSerial;
    private EditHandleId? _activeHandle;
    private ObjectId? _selectedObjectId;
    private ToolKind _activeTool = ToolKind.Select;
    private Point2 _dragOrigin;
    private string _statusMessage = "Ready";
    private bool _dirty;

    public EutherWireApplication(ProjectDocument document, string? projectDirectory)
    {
        _document = document;
        _projectDirectory = projectDirectory;
    }

    public void Render(in ForgeFrame frame)
    {
        int canvasRight = Math.Max(ToolbarWidth + 1, frame.Canvas.Width - InspectorWidth);
        int canvasBottom = Math.Max(1, frame.Canvas.Height - StatusHeight);
        var work = new RectI(ToolbarWidth, 0, canvasRight - ToolbarWidth, canvasBottom);
        bool chromeAction = HandleChromeActions(frame);
        if (!chromeAction)
        {
            HandleEditing(frame, work);
        }
        HandleNavigation(frame);
        SoftwareCanvas canvas = frame.Canvas;
        canvas.Clear(0xff111820);

        using (canvas.PushClip(work))
        {
            canvas.FillRect(work.X, work.Y, work.Width, work.Height, 0xff17212a);
            DrawGrid(canvas, work);
            DrawDocument(canvas);
            DrawHandles(canvas);
        }

        DrawChrome(canvas, work, frame.Pointer);
        _previousPointer = frame.Pointer;
    }

    private void HandleNavigation(in ForgeFrame frame)
    {
        PointerState pointer = frame.Pointer;
        bool panning = pointer.Buttons.HasFlag(PointerButtons.Middle) ||
            pointer.Buttons.HasFlag(PointerButtons.Right) ||
            (_activeTool == ToolKind.Pan && pointer.LeftPressed);
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

    private void HandleEditing(in ForgeFrame frame, RectI work)
    {
        PointerState pointer = frame.Pointer;
        bool pressed = pointer.LeftPressed;
        bool started = pressed && !_previousPointer.LeftPressed;
        bool released = !pressed && _previousPointer.LeftPressed;

        if (started)
        {
            if (!work.Contains(pointer.X, pointer.Y))
            {
                return;
            }
            if (_activeTool == ToolKind.PlaceDevice)
            {
                PlaceDevice(_camera.ScreenToDocument(pointer.X, pointer.Y));
                return;
            }
            if (_activeTool != ToolKind.Select)
            {
                return;
            }

            _activeHandle = HitHandle(pointer.X, pointer.Y);
            if (_activeHandle is EditHandleId handleId)
            {
                _dragOrigin = DocumentHandleEditor.RequirePosition(_document, handleId);
            }
            else
            {
                _selectedObjectId = HitObject(pointer.X, pointer.Y);
                _statusMessage = _selectedObjectId is ObjectId selected ? $"Selected {selected}" : "Selection cleared";
            }
        }

        if (pressed && _activeHandle is EditHandleId active)
        {
            DocumentHandleEditor.SetPosition(_document, active, _camera.ScreenToDocument(pointer.X, pointer.Y));
        }

        if (released && _activeHandle is EditHandleId completed)
        {
            Point2 destination = DocumentHandleEditor.RequirePosition(_document, completed);
            DocumentHandleEditor.SetPosition(_document, completed, _dragOrigin);
            if (destination != _dragOrigin)
            {
                _history.Execute(_document, new MoveEditHandleCommand(completed, destination));
                _dirty = true;
                _statusMessage = $"Moved {completed}";
            }
            _activeHandle = null;
        }
    }

    private void PlaceDevice(Point2 position)
    {
        Point2 snapped = new(Math.Round(position.X / 100) * 100, Math.Round(position.Y / 100) * 100);
        ObjectId id = ObjectId.Parse($"junction-{Guid.NewGuid():N}");
        int number = _document.Devices.Values.Count(device => device.Kind == DeviceKind.JunctionBox) + 1;
        var device = new Device(
            id,
            DeviceKind.JunctionBox,
            snapped,
            $"DOSA-{number:00}",
            [new Port("generic", PortKind.Generic, new Point2(0, 0))]);
        _history.Execute(_document, new AddDeviceCommand(device));
        _selectedObjectId = id;
        _dirty = true;
        _statusMessage = $"Placed {id} at {snapped.X:0}, {snapped.Y:0} mm";
    }

    private bool HandleChromeActions(in ForgeFrame frame)
    {
        PointerState pointer = frame.Pointer;
        if (!pointer.LeftPressed || _previousPointer.LeftPressed)
        {
            return false;
        }

        int inspectorX = Math.Max(ToolbarWidth + 1, frame.Canvas.Width - InspectorWidth);
        for (int index = 0; index < ToolCount; index++)
        {
            if (ToolRect(index).Contains(pointer.X, pointer.Y))
            {
                _activeTool = (ToolKind)index;
                _activeHandle = null;
                _statusMessage = $"Tool: {ToolLabel(_activeTool)}";
                return true;
            }
        }
        if (ButtonRect(inspectorX, 0).Contains(pointer.X, pointer.Y))
        {
            SaveProject();
            return true;
        }
        if (ButtonRect(inspectorX, 1).Contains(pointer.X, pointer.Y))
        {
            if (_history.Undo(_document))
            {
                EnsureSelectionExists();
                _dirty = true;
                _statusMessage = "Undo";
            }
            return true;
        }
        if (ButtonRect(inspectorX, 2).Contains(pointer.X, pointer.Y))
        {
            if (_history.Redo(_document))
            {
                EnsureSelectionExists();
                _dirty = true;
                _statusMessage = "Redo";
            }
            return true;
        }
        return false;
    }

    private void EnsureSelectionExists()
    {
        if (_selectedObjectId is ObjectId selected && !_document.Contains(selected))
        {
            _selectedObjectId = null;
        }
    }

    private void SaveProject()
    {
        if (_projectDirectory is null)
        {
            _statusMessage = "No project directory; start EutherWire with a .eutherwire path";
            return;
        }
        try
        {
            ProjectToml.Save(_projectDirectory, _document);
            _dirty = false;
            _statusMessage = $"Saved {Path.Combine(_projectDirectory, ProjectToml.FileName)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _statusMessage = $"Save failed: {exception.Message}";
        }
    }

    private EditHandleId? HitHandle(int screenX, int screenY)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document).Reverse())
        {
            if (_selectedObjectId != handle.Id.ObjectId)
            {
                continue;
            }
            if (!DocumentHandleEditor.CanSetPosition(handle.Id))
            {
                continue;
            }
            (double x, double y) = _camera.DocumentToScreen(handle.Position);
            if (Math.Abs(screenX - x) <= 9 && Math.Abs(screenY - y) <= 9)
            {
                return handle.Id;
            }
        }
        return null;
    }

    private ObjectId? HitObject(int screenX, int screenY)
    {
        Point2 point = _camera.ScreenToDocument(screenX, screenY);
        foreach (Device device in _document.Devices.Values.Reverse())
        {
            (double width, double height, _) = DeviceStyle(device.Kind);
            if (Math.Abs(point.X - device.Position.X) <= width / 2 &&
                Math.Abs(point.Y - device.Position.Y) <= height / 2)
            {
                return device.Id;
            }
        }
        foreach (CableRoute cable in _document.Cables.Values.Reverse())
        {
            if (HitRoute(screenX, screenY, cable.Route.Points, 8))
            {
                return cable.Id;
            }
        }
        foreach (Conduit conduit in _document.Conduits.Values.Reverse())
        {
            if (HitRoute(screenX, screenY, conduit.Route.Points, 10))
            {
                return conduit.Id;
            }
        }
        return null;
    }

    private bool HitRoute(int screenX, int screenY, IReadOnlyList<Point2> points, double tolerancePixels)
    {
        for (int index = 1; index < points.Count; index++)
        {
            (double x0, double y0) = _camera.DocumentToScreen(points[index - 1]);
            (double x1, double y1) = _camera.DocumentToScreen(points[index]);
            if (DistanceToSegment(screenX, screenY, x0, y0, x1, y1) <= tolerancePixels)
            {
                return true;
            }
        }
        return false;
    }

    private static double DistanceToSegment(double x, double y, double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        if (dx == 0 && dy == 0)
        {
            return Math.Sqrt(Math.Pow(x - x0, 2) + Math.Pow(y - y0, 2));
        }
        double t = Math.Clamp(((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy), 0, 1);
        double nearestX = x0 + t * dx;
        double nearestY = y0 + t * dy;
        return Math.Sqrt(Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2));
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

    private void DrawDocument(SoftwareCanvas canvas)
    {
        foreach (Conduit conduit in _document.Conduits.Values)
        {
            if (_selectedObjectId == conduit.Id)
            {
                DrawRoute(canvas, conduit.Route.Points, 0xffffcc66, 9);
            }
            DrawRoute(canvas, conduit.Route.Points, 0xff708898, 5);
        }
        foreach (CableRoute cable in _document.Cables.Values)
        {
            if (_selectedObjectId == cable.Id)
            {
                DrawRoute(canvas, cable.Route.Points, 0xffffcc66, 6);
            }
            DrawRoute(canvas, cable.Route.Points, 0xff55c8ff, 2);
        }
        foreach (Device device in _document.Devices.Values)
        {
            (double width, double height, uint color) = DeviceStyle(device.Kind);
            DrawDevice(canvas, device, width, height, color, _selectedObjectId == device.Id);
        }
    }

    private void DrawHandles(SoftwareCanvas canvas)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document))
        {
            if (_selectedObjectId != handle.Id.ObjectId)
            {
                continue;
            }
            (double x, double y) = _camera.DocumentToScreen(handle.Position);
            bool active = _activeHandle == handle.Id;
            int size = active ? 11 : handle.Id.Kind == EditHandleKind.Port ? 5 : 7;
            uint color = HandleColor(handle.Id.Kind);
            int left = (int)Math.Round(x) - size / 2;
            int top = (int)Math.Round(y) - size / 2;
            canvas.FillRect(left, top, size, size, 0xff101820);
            canvas.DrawRect(left, top, size, size, color);
        }
    }

    private void DrawDevice(SoftwareCanvas canvas, Device device, double widthMm, double heightMm, uint color, bool selected)
    {
        (double x, double y) = _camera.DocumentToScreen(new Point2(device.Position.X - widthMm / 2, device.Position.Y - heightMm / 2));
        int width = Math.Max(8, (int)Math.Round(widthMm * _camera.PixelsPerMillimetre));
        int height = Math.Max(8, (int)Math.Round(heightMm * _camera.PixelsPerMillimetre));
        canvas.FillRect((int)x, (int)y, width, height, 0xff20303b);
        canvas.DrawRect((int)x, (int)y, width, height, color);
        if (selected)
        {
            canvas.DrawRect((int)x - 3, (int)y - 3, width + 6, height + 6, 0xffffcc66);
        }
        canvas.DrawText((int)x + 6, (int)y + 7, device.Label, color);

        double radians = (device.RotationDegrees - 90) * Math.PI / 180;
        (double centreX, double centreY) = _camera.DocumentToScreen(device.Position);
        int directionLength = Math.Max(8, Math.Min(width, height) / 2);
        canvas.DrawLine(
            (int)centreX,
            (int)centreY,
            (int)Math.Round(centreX + Math.Cos(radians) * directionLength),
            (int)Math.Round(centreY + Math.Sin(radians) * directionLength),
            color);
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
        for (int index = 0; index < ToolCount; index++)
        {
            RectI rect = ToolRect(index);
            bool active = (int)_activeTool == index;
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, active ? 0xff63d4ff : 0xff3c5261);
            canvas.DrawText(rect.X + 10, rect.Y + 13, ToolLabel((ToolKind)index), active ? 0xff63d4ff : 0xff9eb0bb);
        }

        int inspectorX = work.Right;
        canvas.FillRect(inspectorX, 0, canvas.Width - inspectorX, canvas.Height, 0xff101820);
        canvas.DrawLine(inspectorX, 0, inspectorX, canvas.Height, 0xff40515e);
        canvas.DrawText(inspectorX + 18, 20, _document.Name.ToUpperInvariant(), 0xffe3edf2, 2);
        DrawSelectionInspector(canvas, inspectorX);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 0), "SAVE", _projectDirectory is not null);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 1), "UNDO", _history.UndoCount > 0);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 2), "REDO", _history.RedoCount > 0);
        canvas.DrawText(inspectorX + 18, 202, _dirty ? "MODIFIED" : "SAVED", _dirty ? 0xffffcc66 : 0xff61e294);
        canvas.DrawText(inspectorX + 18, 226, _statusMessage, 0xff9eb0bb);

        canvas.FillRect(ToolbarWidth, work.Bottom, work.Width, StatusHeight, 0xff0b1117);
        Point2 documentPoint = _camera.ScreenToDocument(pointer.X, pointer.Y);
        canvas.DrawText(ToolbarWidth + 12, work.Bottom + 10,
            $"X {documentPoint.X:0} mm   Y {documentPoint.Y:0} mm   Zoom {_camera.PixelsPerMillimetre * 1000:0} px/m   MMB/RMB pan   Wheel zoom",
            0xff91a6b3);
    }

    private void DrawSelectionInspector(SoftwareCanvas canvas, int inspectorX)
    {
        if (_selectedObjectId is not ObjectId selected)
        {
            canvas.DrawText(inspectorX + 18, 60, "Selected: none", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 86, $"Objects: {_document.Devices.Count + _document.Cables.Count + _document.Conduits.Count}", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 112, $"Undo: {_history.UndoCount}   Redo: {_history.RedoCount}", 0xff9eb0bb);
            return;
        }
        canvas.DrawText(inspectorX + 18, 56, selected.ToString(), 0xff63d4ff);
        if (_document.Devices.TryGetValue(selected, out Device? device))
        {
            canvas.DrawText(inspectorX + 18, 78, $"{device.Kind}  {device.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"{device.Position.X:0}, {device.Position.Y:0} mm  R {device.RotationDegrees:0}°", 0xff9eb0bb);
        }
        else if (_document.Cables.TryGetValue(selected, out CableRoute? cable))
        {
            canvas.DrawText(inspectorX + 18, 78, $"{cable.Kind}  {cable.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"Length {cable.Route.LengthMillimetres / 1000:0.00} m", 0xff9eb0bb);
        }
        else if (_document.Conduits.TryGetValue(selected, out Conduit? conduit))
        {
            canvas.DrawText(inspectorX + 18, 78, $"Conduit  {conduit.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"Ø {conduit.InnerDiameterMillimetres:0} mm  {conduit.Route.LengthMillimetres / 1000:0.00} m", 0xff9eb0bb);
        }
    }

    private static RectI ButtonRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index * 78, 146, 68, 32);

    private static void DrawChromeButton(SoftwareCanvas canvas, RectI rect, string label, bool enabled)
    {
        uint border = enabled ? 0xff63d4ff : 0xff34434d;
        uint text = enabled ? 0xffccebf7 : 0xff667680;
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, 0xff15212a);
        canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, border);
        canvas.DrawText(rect.X + 14, rect.Y + 12, label, text);
    }

    private static RectI ToolRect(int index) => new(10, 64 + index * 48, 52, 34);

    private const int ToolCount = 6;

    private static string ToolLabel(ToolKind tool) => tool switch
    {
        ToolKind.Select => "SEL",
        ToolKind.Pan => "PAN",
        ToolKind.PlaceDevice => "DEV",
        ToolKind.Wire => "WIRE",
        ToolKind.Conduit => "PIPE",
        ToolKind.Text => "TEXT",
        _ => "?",
    };

    private static (double Width, double Height, uint Color) DeviceStyle(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => (1500, 900, 0xffd7a84a),
        DeviceKind.PoeSwitch => (1300, 700, 0xff4aa6d7),
        DeviceKind.Camera => (700, 500, 0xff64c987),
        _ => (800, 600, 0xffb4c0c8),
    };

    private static uint HandleColor(EditHandleKind kind) => kind switch
    {
        EditHandleKind.Move => 0xffffcc66,
        EditHandleKind.Rotate => 0xffff70d2,
        EditHandleKind.Vertex => 0xff63d4ff,
        EditHandleKind.Port => 0xff61e294,
        _ => 0xffd7e0e5,
    };

    private enum ToolKind
    {
        Select,
        Pan,
        PlaceDevice,
        Wire,
        Conduit,
        Text,
    }

}
