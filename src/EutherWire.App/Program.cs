using EutherWire.App;
using EutherWire.Document.Analysis;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Templates;
using EutherWire.Export;
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
    private readonly List<Point2> _draftPoints = [];
    private PortReference? _draftFrom;
    private PortReference? _draftTo;
    private Point2 _draftPointer;
    private UiContext? _ui;
    private DeviceKind _placementKind = DeviceKind.JunctionBox;

    public EutherWireApplication(ProjectDocument document, string? projectDirectory)
    {
        _document = document;
        _projectDirectory = projectDirectory;
    }

    public void Render(in ForgeFrame frame)
    {
        _ui ??= new UiContext(frame.Canvas, UiTheme.Default);
        _ui.BeginFrame(frame.Pointer, _previousPointer, frame.TextInput, frame.ScrollInput);
        int canvasRight = Math.Max(ToolbarWidth + 1, frame.Canvas.Width - InspectorWidth);
        int canvasBottom = Math.Max(1, frame.Canvas.Height - StatusHeight);
        var work = new RectI(ToolbarWidth, 0, canvasRight - ToolbarWidth, canvasBottom);
        bool chromeAction = HandleChromeActions(frame);
        if (!chromeAction)
        {
            HandleEditing(frame, work);
        }
        if (work.Contains(frame.Pointer.X, frame.Pointer.Y))
        {
            _draftPointer = SnapRoutePoint(_camera.ScreenToDocument(frame.Pointer.X, frame.Pointer.Y));
        }
        HandleNavigation(frame);
        SoftwareCanvas canvas = frame.Canvas;
        canvas.Clear(0xff111820);

        using (canvas.PushClip(work))
        {
            canvas.FillRect(work.X, work.Y, work.Width, work.Height, 0xff17212a);
            DrawGrid(canvas, work);
            DrawDocument(canvas);
            DrawDraft(canvas);
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
            if (_activeTool == ToolKind.Text)
            {
                PlaceAnnotation(_camera.ScreenToDocument(pointer.X, pointer.Y));
                return;
            }
            if (_activeTool is ToolKind.Wire or ToolKind.Conduit)
            {
                AddDraftPoint(pointer.X, pointer.Y);
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

    private void AddDraftPoint(int screenX, int screenY)
    {
        Point2 point = SnapRoutePoint(_camera.ScreenToDocument(screenX, screenY));
        PortReference? port = _activeTool == ToolKind.Wire ? HitPort(screenX, screenY) : null;
        if (port is PortReference portReference)
        {
            point = PortPosition(portReference);
            if (_draftPoints.Count == 0)
            {
                _draftFrom = portReference;
            }
            else
            {
                _draftTo = portReference;
            }
        }
        else if (_activeTool == ToolKind.Wire && _draftPoints.Count > 0)
        {
            _draftTo = null;
        }
        if (_draftPoints.Count == 0 || _draftPoints[^1] != point)
        {
            _draftPoints.Add(point);
            _statusMessage = $"{ToolLabel(_activeTool)} point {_draftPoints.Count}: {point.X:0}, {point.Y:0} mm";
        }
    }

    private Point2 SnapRoutePoint(Point2 point)
    {
        Point2 snapped = new(Math.Round(point.X / 100) * 100, Math.Round(point.Y / 100) * 100);
        if (_draftPoints.Count == 0)
        {
            return snapped;
        }
        Point2 origin = _draftPoints[^1];
        double dx = snapped.X - origin.X;
        double dy = snapped.Y - origin.Y;
        if (dx == 0 && dy == 0)
        {
            return snapped;
        }
        double angle = Math.Atan2(dy, dx);
        double snappedAngle = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        double length = Math.Sqrt(dx * dx + dy * dy);
        return new Point2(
            Math.Round((origin.X + Math.Cos(snappedAngle) * length) / 100) * 100,
            Math.Round((origin.Y + Math.Sin(snappedAngle) * length) / 100) * 100);
    }

    private PortReference? HitPort(int screenX, int screenY)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document))
        {
            if (handle.Id.Kind != EditHandleKind.Port || handle.Id.Name is null)
            {
                continue;
            }
            (double x, double y) = _camera.DocumentToScreen(handle.Position);
            if (Math.Abs(screenX - x) <= 10 && Math.Abs(screenY - y) <= 10)
            {
                return new PortReference(handle.Id.ObjectId, handle.Id.Name);
            }
        }
        return null;
    }

    private Point2 PortPosition(PortReference reference)
    {
        var handleId = new EditHandleId(reference.DeviceId, EditHandleKind.Port, Name: reference.PortId);
        return DocumentHandleEditor.RequirePosition(_document, handleId);
    }

    private void PlaceDevice(Point2 position)
    {
        Point2 snapped = new(Math.Round(position.X / 100) * 100, Math.Round(position.Y / 100) * 100);
        ObjectId id = ObjectId.Parse($"junction-{Guid.NewGuid():N}");
        int number = _document.Devices.Values.Count(device => device.Kind == _placementKind) + 1;
        var device = CreatePlacedDevice(id, snapped, number);
        _history.Execute(_document, new AddDeviceCommand(device));
        _selectedObjectId = id;
        _dirty = true;
        _statusMessage = $"Placed {id} at {snapped.X:0}, {snapped.Y:0} mm";
    }

    private void PlaceAnnotation(Point2 position)
    {
        Point2 snapped = new(Math.Round(position.X / 100) * 100, Math.Round(position.Y / 100) * 100);
        ObjectId id = ObjectId.Parse($"note-{Guid.NewGuid():N}");
        int number = _document.Annotations.Count + 1;
        var annotation = new Annotation(id, snapped, $"ANTECKNING {number:00}");
        _history.Execute(_document, new AddAnnotationCommand(annotation));
        _selectedObjectId = id;
        _activeTool = ToolKind.Select;
        _dirty = true;
        _statusMessage = $"Placed {id}; edit text in the inspector";
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
                ToolKind nextTool = (ToolKind)index;
                if (nextTool != _activeTool)
                {
                    CancelDraft("Draft cancelled");
                }
                _activeTool = nextTool;
                _activeHandle = null;
                _statusMessage = $"Tool: {ToolLabel(_activeTool)}";
                return true;
            }
        }
        if (_draftPoints.Count > 0 && FinishRect(inspectorX).Contains(pointer.X, pointer.Y))
        {
            FinishDraft();
            return true;
        }
        if (_draftPoints.Count > 0 && CancelRect(inspectorX).Contains(pointer.X, pointer.Y))
        {
            CancelDraft("Draft cancelled");
            return true;
        }
        if (_activeTool == ToolKind.PlaceDevice)
        {
            for (int index = 0; index < PlacementKinds.Length; index++)
            {
                if (SymbolRect(inspectorX, index).Contains(pointer.X, pointer.Y))
                {
                    _placementKind = PlacementKinds[index];
                    _statusMessage = $"Place: {_placementKind}";
                    return true;
                }
            }
        }
        ProjectAnalysis analysis = ProjectAnalyzer.Analyze(_document);
        for (int index = 0; index < Math.Min(3, analysis.Diagnostics.Count); index++)
        {
            ProjectDiagnostic diagnostic = analysis.Diagnostics[index];
            if (diagnostic.ObjectId is ObjectId objectId && DiagnosticRect(inspectorX, index).Contains(pointer.X, pointer.Y))
            {
                _selectedObjectId = objectId;
                _activeTool = ToolKind.Select;
                _activeHandle = null;
                SyncSelectedLabelEditor();
                _statusMessage = diagnostic.Message;
                return true;
            }
        }
        if (_draftPoints.Count == 0 && _selectedObjectId is ObjectId selected)
        {
            if (DeleteRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                DeleteSelection(selected);
                return true;
            }
            if (_document.Cables.TryGetValue(selected, out CableRoute? cable) && PropertyMinusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                CycleCableKind(cable, -1);
                return true;
            }
            if (_document.Cables.TryGetValue(selected, out cable) && PropertyPlusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                CycleCableKind(cable, 1);
                return true;
            }
            if (_document.Cables.TryGetValue(selected, out cable) && StatusMinusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                CycleInstallationStatus(cable, -1);
                return true;
            }
            if (_document.Cables.TryGetValue(selected, out cable) && StatusPlusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                CycleInstallationStatus(cable, 1);
                return true;
            }
            if (_document.Conduits.TryGetValue(selected, out Conduit? conduit) && PropertyMinusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                SetConduitDiameter(conduit, -1);
                return true;
            }
            if (_document.Conduits.TryGetValue(selected, out conduit) && PropertyPlusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                SetConduitDiameter(conduit, 1);
                return true;
            }
            if (AddVertexRect(inspectorX).Contains(pointer.X, pointer.Y) && TryGetEditableRoute(selected, out Polyline? addRoute))
            {
                AddRouteVertex(selected, addRoute!);
                return true;
            }
            if (DeleteVertexRect(inspectorX).Contains(pointer.X, pointer.Y) && TryGetEditableRoute(selected, out Polyline? deleteRoute))
            {
                DeleteRouteVertex(selected, deleteRoute!);
                return true;
            }
        }
        if (ButtonRect(inspectorX, 0).Contains(pointer.X, pointer.Y))
        {
            SaveProject();
            return true;
        }
        if (ExportPngRect(inspectorX).Contains(pointer.X, pointer.Y))
        {
            ExportPng();
            return true;
        }
        if (ButtonRect(inspectorX, 1).Contains(pointer.X, pointer.Y))
        {
            if (_history.Undo(_document))
            {
                EnsureSelectionExists();
                SyncSelectedLabelEditor();
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
                SyncSelectedLabelEditor();
                _dirty = true;
                _statusMessage = "Redo";
            }
            return true;
        }
        return false;
    }

    private void DeleteSelection(ObjectId selected)
    {
        try
        {
            _history.Execute(_document, new DeleteObjectCommand(selected));
            _selectedObjectId = null;
            _dirty = true;
            _statusMessage = $"Deleted {selected}";
        }
        catch (InvalidOperationException exception)
        {
            _statusMessage = exception.Message;
        }
    }

    private void CycleCableKind(CableRoute cable, int direction)
    {
        CableKind[] kinds = Enum.GetValues<CableKind>();
        int index = (Array.IndexOf(kinds, cable.Kind) + direction + kinds.Length) % kinds.Length;
        _history.Execute(_document, new SetCableKindCommand(cable.Id, kinds[index]));
        _dirty = true;
        _statusMessage = $"Cable type: {kinds[index]}";
    }

    private void CycleInstallationStatus(CableRoute cable, int direction)
    {
        InstallationStatus[] statuses = Enum.GetValues<InstallationStatus>();
        int index = (Array.IndexOf(statuses, cable.InstallationStatus) + direction + statuses.Length) % statuses.Length;
        _history.Execute(_document, new SetCableInstallationCommand(cable.Id, statuses[index], cable.ActualLengthMillimetres));
        _dirty = true;
        _statusMessage = $"Installation: {statuses[index]}";
    }

    private void SetConduitDiameter(Conduit conduit, int direction)
    {
        double[] diameters = [16, 20, 25, 32, 40, 50, 63, 75, 90, 110];
        int nearest = Enumerable.Range(0, diameters.Length)
            .MinBy(index => Math.Abs(diameters[index] - conduit.InnerDiameterMillimetres));
        int index = Math.Clamp(nearest + direction, 0, diameters.Length - 1);
        _history.Execute(_document, new SetConduitDiameterCommand(conduit.Id, diameters[index]));
        _dirty = true;
        _statusMessage = $"Conduit diameter: {diameters[index]} mm";
    }

    private bool TryGetEditableRoute(ObjectId id, out Polyline? route)
    {
        if (_document.Conduits.TryGetValue(id, out Conduit? conduit))
        {
            route = conduit.Route;
            return true;
        }
        if (_document.Cables.TryGetValue(id, out CableRoute? cable) && cable.ConduitId is null)
        {
            route = cable.Route;
            return true;
        }
        route = null;
        return false;
    }

    private void AddRouteVertex(ObjectId routeId, Polyline route)
    {
        int segment = 0;
        double longest = -1;
        for (int index = 1; index < route.Points.Count; index++)
        {
            double dx = route.Points[index].X - route.Points[index - 1].X;
            double dy = route.Points[index].Y - route.Points[index - 1].Y;
            double length = dx * dx + dy * dy;
            if (length > longest)
            {
                longest = length;
                segment = index;
            }
        }
        Point2 before = route.Points[segment - 1];
        Point2 after = route.Points[segment];
        var midpoint = new Point2((before.X + after.X) / 2, (before.Y + after.Y) / 2);
        _history.Execute(_document, new InsertRouteVertexCommand(routeId, segment, midpoint));
        _dirty = true;
        _statusMessage = $"Inserted {routeId}:vertex:{segment}";
    }

    private void DeleteRouteVertex(ObjectId routeId, Polyline route)
    {
        if (route.Points.Count <= 2)
        {
            _statusMessage = "A route must retain at least two points";
            return;
        }
        int index = route.Points.Count - 2;
        _history.Execute(_document, new DeleteRouteVertexCommand(routeId, index));
        _dirty = true;
        _statusMessage = $"Deleted {routeId}:vertex:{index}";
    }

    private void FinishDraft()
    {
        if (_draftPoints.Count < 2)
        {
            _statusMessage = "A route needs at least two distinct points";
            return;
        }

        var route = new Polyline(_draftPoints);
        if (_activeTool == ToolKind.Conduit)
        {
            ObjectId id = ObjectId.Parse($"conduit-{Guid.NewGuid():N}");
            int number = _document.Conduits.Count + 1;
            _history.Execute(_document, new AddConduitCommand(new Conduit(
                id,
                $"RÖR-{number:00}",
                25,
                route,
                InstallationMethod.Concealed)));
            _selectedObjectId = id;
        }
        else if (_activeTool == ToolKind.Wire)
        {
            ObjectId id = ObjectId.Parse($"cable-{Guid.NewGuid():N}");
            int number = _document.Cables.Count + 1;
            _history.Execute(_document, new AddCableCommand(new CableRoute(
                id,
                $"CAT6-{number:00}",
                CableKind.Cat6,
                route,
                _draftFrom,
                _draftTo)));
            _selectedObjectId = id;
        }
        else
        {
            return;
        }

        _dirty = true;
        _statusMessage = $"Created {_selectedObjectId} with {_draftPoints.Count} points";
        ClearDraft();
        _activeTool = ToolKind.Select;
    }

    private void CancelDraft(string message)
    {
        if (_draftPoints.Count == 0)
        {
            return;
        }
        ClearDraft();
        _statusMessage = message;
    }

    private void ClearDraft()
    {
        _draftPoints.Clear();
        _draftFrom = null;
        _draftTo = null;
    }

    private void EnsureSelectionExists()
    {
        if (_selectedObjectId is ObjectId selected && !_document.Contains(selected))
        {
            _selectedObjectId = null;
        }
    }

    private void SyncSelectedLabelEditor()
    {
        if (_ui is null || _selectedObjectId is not ObjectId selected)
        {
            return;
        }
        string? label = _document.Devices.TryGetValue(selected, out Device? device)
            ? device.Label
            : _document.Cables.TryGetValue(selected, out CableRoute? cable)
                ? cable.Label
                : _document.Conduits.TryGetValue(selected, out Conduit? conduit)
                    ? conduit.Label
                    : _document.Annotations.TryGetValue(selected, out Annotation? annotation)
                        ? annotation.Text
                        : null;
        if (label is not null)
        {
            _ui.SetText(new UiId($"inspector.label.{selected}"), label);
        }
        if (_document.Cables.TryGetValue(selected, out CableRoute? selectedCable))
        {
            _ui.SetText(
                new UiId($"inspector.actual.{selected}"),
                selectedCable.ActualLengthMillimetres?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
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

    private void ExportPng()
    {
        if (_projectDirectory is null)
        {
            _statusMessage = "No project directory; save the project before exporting";
            return;
        }
        try
        {
            string path = Path.Combine(_projectDirectory, "exports", "plan.png");
            PngProjectExporter.Save(path, _document);
            _statusMessage = $"Exported {path}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _statusMessage = $"PNG export failed: {exception.Message}";
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
        foreach (Annotation annotation in _document.Annotations.Values.Reverse())
        {
            double widthMillimetres = Math.Max(100, annotation.Text.Length * 6 / _camera.PixelsPerMillimetre);
            double heightMillimetres = 18 / _camera.PixelsPerMillimetre;
            if (point.X >= annotation.Position.X && point.X <= annotation.Position.X + widthMillimetres &&
                point.Y >= annotation.Position.Y && point.Y <= annotation.Position.Y + heightMillimetres)
            {
                return annotation.Id;
            }
        }
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
        foreach (Annotation annotation in _document.Annotations.Values)
        {
            DrawAnnotation(canvas, annotation, _selectedObjectId == annotation.Id);
        }
    }

    private void DrawDraft(SoftwareCanvas canvas)
    {
        if (_draftPoints.Count == 0)
        {
            return;
        }
        uint color = _activeTool == ToolKind.Wire ? 0xff55c8ff : 0xff9baab3;
        DrawRoute(canvas, _draftPoints, color, 3);
        Point2 last = _draftPoints[^1];
        DrawRoute(canvas, [last, _draftPointer], 0xffffcc66, 1);
        foreach (Point2 point in _draftPoints)
        {
            (double x, double y) = _camera.DocumentToScreen(point);
            canvas.DrawRect((int)x - 4, (int)y - 4, 9, 9, color);
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

    private void DrawAnnotation(SoftwareCanvas canvas, Annotation annotation, bool selected)
    {
        (double x, double y) = _camera.DocumentToScreen(annotation.Position);
        int width = annotation.Text.Length * 6 + 8;
        if (selected)
        {
            canvas.FillRect((int)x - 4, (int)y - 5, width, 18, 0xff26343e);
            canvas.DrawRect((int)x - 4, (int)y - 5, width, 18, 0xffffcc66);
        }
        canvas.DrawText((int)x, (int)y, annotation.Text, 0xffe6edf1);
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
        DrawChromeButton(canvas, ExportPngRect(inspectorX), "PNG", _projectDirectory is not null);
        canvas.DrawText(inspectorX + 100, 202, _dirty ? "MODIFIED" : "SAVED", _dirty ? 0xffffcc66 : 0xff61e294);
        canvas.DrawText(inspectorX + 18, 226, _statusMessage, 0xff9eb0bb);
        if (_draftPoints.Count > 0)
        {
            DrawChromeButton(canvas, FinishRect(inspectorX), "FINISH", _draftPoints.Count >= 2);
            DrawChromeButton(canvas, CancelRect(inspectorX), "CANCEL", true);
            canvas.DrawText(inspectorX + 18, 304, $"Draft points: {_draftPoints.Count}", 0xffffcc66);
        }
        else if (_activeTool == ToolKind.PlaceDevice)
        {
            DrawSymbolPalette(canvas, inspectorX);
        }
        else if (_selectedObjectId is ObjectId selected)
        {
            DrawPropertyControls(canvas, inspectorX, selected);
        }
        DrawProjectAnalysis(canvas, inspectorX);

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
            canvas.DrawText(inspectorX + 18, 86, $"Objects: {_document.Devices.Count + _document.Cables.Count + _document.Conduits.Count + _document.Annotations.Count}", 0xff9eb0bb);
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
            string actual = cable.ActualLengthMillimetres is double length ? $"  Actual {length / 1000:0.00} m" : string.Empty;
            canvas.DrawText(inspectorX + 18, 122, $"{cable.InstallationStatus}{actual}", 0xff9eb0bb);
        }
        else if (_document.Conduits.TryGetValue(selected, out Conduit? conduit))
        {
            canvas.DrawText(inspectorX + 18, 78, $"Conduit  {conduit.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"Ø {conduit.InnerDiameterMillimetres:0} mm  {conduit.Route.LengthMillimetres / 1000:0.00} m", 0xff9eb0bb);
        }
        else if (_document.Annotations.TryGetValue(selected, out Annotation? annotation))
        {
            canvas.DrawText(inspectorX + 18, 78, "Annotation", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"{annotation.Position.X:0}, {annotation.Position.Y:0} mm", 0xff9eb0bb);
        }
    }

    private void DrawSymbolPalette(SoftwareCanvas canvas, int inspectorX)
    {
        canvas.DrawText(inspectorX + 18, 260, "SYMBOL", 0xff9eb0bb);
        for (int index = 0; index < PlacementKinds.Length; index++)
        {
            DeviceKind kind = PlacementKinds[index];
            RectI rect = SymbolRect(inspectorX, index);
            bool active = kind == _placementKind;
            canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, 0xff15212a);
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, active ? 0xffffcc66 : 0xff40515e);
            canvas.DrawText(rect.X + 8, rect.Y + 12, SymbolLabel(kind), active ? 0xffffcc66 : 0xffa9bbc5);
        }
    }

    private void DrawPropertyControls(SoftwareCanvas canvas, int inspectorX, ObjectId selected)
    {
        _document.Devices.TryGetValue(selected, out Device? device);
        _document.Cables.TryGetValue(selected, out CableRoute? cable);
        _document.Conduits.TryGetValue(selected, out Conduit? conduit);
        _document.Annotations.TryGetValue(selected, out Annotation? annotation);
        string label = device is not null
            ? device.Label
            : cable is not null
                ? cable.Label
                : conduit is not null
                    ? conduit.Label
                    : annotation is not null
                        ? annotation.Text
                        : string.Empty;
        canvas.DrawText(inspectorX + 18, 258, "LABEL (ENTER TO APPLY)", 0xff9eb0bb);
        UiTextBoxResult text = _ui!.TextBox(
            new UiId($"inspector.label.{selected}"),
            new RectI(inspectorX + 18, 276, 220, 30),
            label,
            "Label",
            new UiTextBoxOptions(MaxLength: 32));
        if (text.Submitted && !string.IsNullOrWhiteSpace(text.Text) && text.Text != label)
        {
            _history.Execute(_document, new SetObjectLabelCommand(selected, text.Text));
            _dirty = true;
            _statusMessage = $"Renamed {selected}";
        }

        DrawChromeButton(canvas, DeleteRect(inspectorX), "DELETE", true);
        if (cable is not null)
        {
            canvas.DrawText(inspectorX + 18, 356, $"TYPE  {cable.Kind}", 0xff9eb0bb);
            DrawChromeButton(canvas, PropertyMinusRect(inspectorX), "<", true);
            DrawChromeButton(canvas, PropertyPlusRect(inspectorX), ">", true);
            canvas.DrawText(inspectorX + 18, 458, $"STATUS  {cable.InstallationStatus}", 0xff9eb0bb);
            DrawChromeButton(canvas, StatusMinusRect(inspectorX), "<", true);
            DrawChromeButton(canvas, StatusPlusRect(inspectorX), ">", true);
            canvas.DrawText(inspectorX + 134, 458, "ACTUAL MM", 0xff9eb0bb);
            string actualText = cable.ActualLengthMillimetres?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            UiTextBoxResult actual = _ui.TextBox(
                new UiId($"inspector.actual.{selected}"),
                ActualLengthRect(inspectorX),
                actualText,
                "unknown",
                new UiTextBoxOptions(MaxLength: 12));
            if (actual.Submitted && string.IsNullOrWhiteSpace(actual.Text) && cable.ActualLengthMillimetres is not null)
            {
                _history.Execute(_document, new SetCableInstallationCommand(cable.Id, cable.InstallationStatus, null));
                _dirty = true;
                _statusMessage = "Actual cable length cleared";
            }
            else if (actual.Submitted && TryParseNonNegative(actual.Text, out double actualLength) && actualLength != cable.ActualLengthMillimetres)
            {
                _history.Execute(_document, new SetCableInstallationCommand(cable.Id, cable.InstallationStatus, actualLength));
                _dirty = true;
                _statusMessage = $"Actual cable length: {actualLength:0.###} mm";
            }
            else if (actual.Submitted && !string.IsNullOrWhiteSpace(actual.Text) && !TryParseNonNegative(actual.Text, out _))
            {
                _statusMessage = "Actual length must be a non-negative number in millimetres";
            }
        }
        else if (conduit is not null)
        {
            canvas.DrawText(inspectorX + 18, 356, $"DIAMETER  {conduit.InnerDiameterMillimetres:0} mm", 0xff9eb0bb);
            DrawChromeButton(canvas, PropertyMinusRect(inspectorX), "-", true);
            DrawChromeButton(canvas, PropertyPlusRect(inspectorX), "+", true);
        }
        if (TryGetEditableRoute(selected, out Polyline? route))
        {
            DrawChromeButton(canvas, AddVertexRect(inspectorX), "+ POINT", true);
            DrawChromeButton(canvas, DeleteVertexRect(inspectorX), "- POINT", route!.Points.Count > 2);
        }
    }

    private void DrawProjectAnalysis(SoftwareCanvas canvas, int inspectorX)
    {
        ProjectAnalysis analysis = ProjectAnalyzer.Analyze(_document);
        canvas.DrawText(inspectorX + 18, 558, "PROJECT ANALYSIS", 0xff9eb0bb);
        canvas.DrawText(inspectorX + 18, 582, $"Cable {analysis.TotalCableLengthMillimetres / 1000:0.00} m", 0xffc7d4dc);
        canvas.DrawText(inspectorX + 18, 604, $"Order {analysis.RecommendedCableLengthMillimetres / 1000:0.00} m", 0xffc7d4dc);
        canvas.DrawText(inspectorX + 18, 626, $"Conduit {analysis.TotalConduitLengthMillimetres / 1000:0.00} m", 0xffc7d4dc);
        uint diagnosticColor = analysis.ErrorCount > 0
            ? 0xffff6b6b
            : analysis.WarningCount > 0
                ? 0xffffcc66
                : 0xff61e294;
        canvas.DrawText(inspectorX + 18, 650, $"Errors {analysis.ErrorCount}   Warnings {analysis.WarningCount}", diagnosticColor);

        if (_selectedObjectId is ObjectId selected && _document.Conduits.ContainsKey(selected))
        {
            ConduitFill? fill = analysis.ConduitFills.FirstOrDefault(item => item.ConduitId == selected);
            if (fill is not null)
            {
                canvas.DrawText(inspectorX + 18, 674, $"Selected fill {fill.FillRatio:P1}", fill.FillRatio > 0.40 ? 0xffffcc66 : 0xff9eb0bb);
            }
        }
        else
        {
            canvas.DrawText(inspectorX + 18, 674, $"Installed {analysis.CompletedInstallationCount}/{analysis.InstallationTasks.Count}", 0xff9eb0bb);
        }

        for (int index = 0; index < Math.Min(3, analysis.Diagnostics.Count); index++)
        {
            ProjectDiagnostic diagnostic = analysis.Diagnostics[index];
            uint color = diagnostic.Severity == DiagnosticSeverity.Error ? 0xffff6b6b : 0xffffcc66;
            RectI rect = DiagnosticRect(inspectorX, index);
            canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, 0xff15212a);
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, color);
            canvas.DrawText(rect.X + 6, rect.Y + 8, diagnostic.Code, color);
        }
    }

    private static bool TryParseNonNegative(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value >= 0;

    private static RectI ButtonRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index * 78, 146, 68, 32);

    private static RectI ExportPngRect(int inspectorX) => new(inspectorX + 18, 190, 68, 30);

    private static RectI FinishRect(int inspectorX) => new(inspectorX + 18, 260, 104, 32);
    private static RectI CancelRect(int inspectorX) => new(inspectorX + 134, 260, 104, 32);
    private static RectI DeleteRect(int inspectorX) => new(inspectorX + 18, 316, 104, 30);
    private static RectI PropertyMinusRect(int inspectorX) => new(inspectorX + 18, 374, 48, 30);
    private static RectI PropertyPlusRect(int inspectorX) => new(inspectorX + 76, 374, 48, 30);
    private static RectI AddVertexRect(int inspectorX) => new(inspectorX + 18, 418, 104, 30);
    private static RectI DeleteVertexRect(int inspectorX) => new(inspectorX + 134, 418, 104, 30);
    private static RectI StatusMinusRect(int inspectorX) => new(inspectorX + 18, 476, 48, 30);
    private static RectI StatusPlusRect(int inspectorX) => new(inspectorX + 76, 476, 48, 30);
    private static RectI ActualLengthRect(int inspectorX) => new(inspectorX + 134, 476, 104, 30);
    private static RectI DiagnosticRect(int inspectorX, int index) => new(inspectorX + 18, 700 + index * 30, 220, 24);
    private static RectI SymbolRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index % 2 * 112, 282 + index / 2 * 42, 102, 32);

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
        EditHandleKind.LabelAnchor => 0xffffa85c,
        _ => 0xffd7e0e5,
    };

    private Device CreatePlacedDevice(ObjectId id, Point2 position, int number)
    {
        (string prefix, Port[] ports) = _placementKind switch
        {
            DeviceKind.DistributionBoard => ("CENTRAL", new[] { new Port("feed", PortKind.MainsPower, new Point2(750, 0)) }),
            DeviceKind.Outlet => ("UTT", new[] { new Port("power", PortKind.MainsPower, new Point2(0, 0)) }),
            DeviceKind.Camera => ("KAM", new[] { new Port("eth0", PortKind.EthernetPoe, new Point2(-350, 0)) }),
            DeviceKind.PoeSwitch => ("POE-SW", new[] { new Port("uplink", PortKind.Ethernet, new Point2(-650, 0)), new Port("port-1", PortKind.EthernetPoe, new Point2(0, -350)) }),
            DeviceKind.AccessPoint => ("AP", new[] { new Port("eth0", PortKind.EthernetPoe, new Point2(0, 300)) }),
            _ => ("DOSA", new[] { new Port("generic", PortKind.Generic, new Point2(0, 0)) }),
        };
        return new Device(id, _placementKind, position, $"{prefix}-{number:00}", ports);
    }

    private static readonly DeviceKind[] PlacementKinds =
    [
        DeviceKind.JunctionBox,
        DeviceKind.Outlet,
        DeviceKind.DistributionBoard,
        DeviceKind.Camera,
        DeviceKind.PoeSwitch,
        DeviceKind.AccessPoint,
    ];

    private static string SymbolLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.JunctionBox => "DOSA",
        DeviceKind.Outlet => "UTTAG",
        DeviceKind.DistributionBoard => "CENTRAL",
        DeviceKind.Camera => "KAMERA",
        DeviceKind.PoeSwitch => "POE-SW",
        DeviceKind.AccessPoint => "AP",
        _ => kind.ToString().ToUpperInvariant(),
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
