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

bool startupIn3D = args.Contains("--3d", StringComparer.Ordinal);
bool startupInWall = args.Contains("--wall", StringComparer.Ordinal);
string? startupCamera = args.FirstOrDefault(argument => argument.StartsWith("--camera=", StringComparison.OrdinalIgnoreCase))?["--camera=".Length..];
string? requestedDirectory = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
string? startupDirectory = requestedDirectory is not null
    ? requestedDirectory
    : Directory.Exists(Path.Combine("examples", "garage.eutherwire"))
        ? Path.Combine("examples", "garage.eutherwire")
        : null;
ProjectDocument startupDocument = startupDirectory is not null
    ? ProjectToml.Load(startupDirectory)
    : ProjectTemplates.CreateGarageDraft();

return ForgeApplicationHost.Run(
    new EutherWireApplication(startupDocument, startupDirectory, startupIn3D, startupInWall, startupCamera),
    new ForgeWindowOptions(1280, 800, $"EutherWire - {startupDocument.Name}"));

internal sealed class EutherWireApplication : IForgeApplication
{
    private const int ToolbarWidth = 72;
    private const int InspectorWidth = 280;
    private const int StatusHeight = 28;

    private readonly CanvasCamera _camera = new();
    private readonly IsometricCamera _camera3D = new();
    private readonly WallCamera _cameraWall = new();
    private readonly ProjectDocument _document;
    private readonly string? _projectDirectory;
    private readonly CommandHistory _history = new();
    private PointerState _previousPointer;
    private uint _handledScrollSerial;
    private EditHandleId? _activeHandle;
    private ObjectId? _selectedObjectId;
    private ToolKind _activeTool = ToolKind.Select;
    private Point2 _dragOrigin;
    private Point3 _dragOriginSpatial;
    private string _statusMessage = "Ready";
    private bool _dirty;
    private readonly List<Point3> _draftPoints = [];
    private PortReference? _draftFrom;
    private PortReference? _draftTo;
    private Point3 _draftPointer;
    private UiContext? _ui;
    private DeviceKind _placementKind = DeviceKind.JunctionBox;
    private OpeningKind _openingKind = OpeningKind.GarageDoor;
    private ViewMode _viewMode;
    private MountingSurface _activeSurface = MountingSurface.FloorInterior;
    private double? _wallSnapHeightMillimetres;
    private Point3? _dimensionStart;

    public EutherWireApplication(ProjectDocument document, string? projectDirectory, bool startIn3D = false, bool startInWall = false, string? startupCamera = null)
    {
        _document = document;
        _projectDirectory = projectDirectory;
        _viewMode = startInWall ? ViewMode.Wall : startIn3D ? ViewMode.ThreeD : ViewMode.Plan;
        if (startInWall) _activeSurface = MountingSurface.SouthWallInterior;
        if (!string.IsNullOrWhiteSpace(startupCamera)) _camera3D.SetNamedView(startupCamera);
    }

    public void Render(in ForgeFrame frame)
    {
        _ui ??= new UiContext(frame.Canvas, UiTheme.Default);
        _ui.BeginFrame(frame.Pointer, _previousPointer, frame.TextInput, frame.ScrollInput);
        int canvasRight = Math.Max(ToolbarWidth + 1, frame.Canvas.Width - InspectorWidth);
        int canvasBottom = Math.Max(1, frame.Canvas.Height - StatusHeight);
        var work = new RectI(ToolbarWidth, 0, canvasRight - ToolbarWidth, canvasBottom);
        _camera3D.Configure(work, _document.Space);
        _cameraWall.Configure(work, _document.Space, WallSurfaceOrDefault());
        bool chromeAction = HandleChromeActions(frame);
        if (!chromeAction)
        {
            HandleEditing(frame, work);
        }
        if (work.Contains(frame.Pointer.X, frame.Pointer.Y))
        {
            _draftPointer = SnapRoutePoint(ScreenToSpatial(frame.Pointer.X, frame.Pointer.Y));
        }
        HandleNavigation(frame);
        SoftwareCanvas canvas = frame.Canvas;
        canvas.Clear(0xff111820);

        using (canvas.PushClip(work))
        {
            canvas.FillRect(work.X, work.Y, work.Width, work.Height, 0xff17212a);
            if (_viewMode == ViewMode.ThreeD)
            {
                Draw3DScene(canvas, work);
            }
            else if (_viewMode == ViewMode.Wall)
            {
                DrawWallScene(canvas, work);
            }
            else
            {
                DrawGrid(canvas, work);
                DrawDocument(canvas);
                DrawDraft(canvas);
                DrawHandles(canvas);
            }
        }

        DrawChrome(canvas, work, frame.Pointer);
        _previousPointer = frame.Pointer;
    }

    public void Key(in ForgeKeyEvent input)
    {
        const uint keyF9 = 67;
        const uint keyF10 = 68;
        if (!input.Pressed) return;
        if (input.KeyCode == keyF9)
        {
            SetViewMode(_viewMode switch
            {
                ViewMode.Plan => ViewMode.ThreeD,
                ViewMode.ThreeD => ViewMode.Wall,
                _ => ViewMode.Plan,
            });
        }
        else if (input.KeyCode == keyF10 && _viewMode == ViewMode.ThreeD)
        {
            _camera3D.CycleView();
            _statusMessage = $"Camera: {_camera3D.ViewLabel}";
        }
    }

    private void HandleNavigation(in ForgeFrame frame)
    {
        PointerState pointer = frame.Pointer;
        if (_viewMode == ViewMode.ThreeD)
        {
            if (_previousPointer.IsInside)
            {
                double deltaX = pointer.X - _previousPointer.X;
                double deltaY = pointer.Y - _previousPointer.Y;
                if (pointer.Buttons.HasFlag(PointerButtons.Right)) _camera3D.Orbit(deltaX, deltaY);
                if (pointer.Buttons.HasFlag(PointerButtons.Middle)) _camera3D.Pan(deltaX, deltaY);
            }
            if (frame.ScrollInput.Serial != 0 && frame.ScrollInput.Serial != _handledScrollSerial)
            {
                _handledScrollSerial = frame.ScrollInput.Serial;
                _camera3D.Zoom(frame.ScrollInput.Delta);
            }
            return;
        }
        if (_viewMode == ViewMode.Wall)
        {
            bool wallPanning = pointer.Buttons.HasFlag(PointerButtons.Middle) ||
                pointer.Buttons.HasFlag(PointerButtons.Right) ||
                (_activeTool == ToolKind.Pan && pointer.LeftPressed);
            if (wallPanning && _previousPointer.IsInside)
            {
                _cameraWall.Pan(pointer.X - _previousPointer.X, pointer.Y - _previousPointer.Y);
            }
            if (frame.ScrollInput.Serial != 0 && frame.ScrollInput.Serial != _handledScrollSerial)
            {
                _handledScrollSerial = frame.ScrollInput.Serial;
                _cameraWall.ZoomAt(pointer.X, pointer.Y, frame.ScrollInput.Delta);
            }
            return;
        }
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
                PlaceDevice(ScreenToSpatial(pointer.X, pointer.Y));
                return;
            }
            if (_activeTool == ToolKind.Opening)
            {
                PlaceOpening(ScreenToSpatial(pointer.X, pointer.Y));
                return;
            }
            if (_activeTool == ToolKind.Text)
            {
                if (_viewMode == ViewMode.Wall)
                {
                    _statusMessage = "Wall annotations are not implemented yet";
                    return;
                }
                PlaceAnnotation(ScreenToDocument(pointer.X, pointer.Y));
                return;
            }
            if (_activeTool == ToolKind.Dimension)
            {
                PlaceWallDimensionPoint(ScreenToSpatial(pointer.X, pointer.Y));
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
                _dragOriginSpatial = DocumentHandleEditor.RequireSpatialPosition(_document, handleId);
            }
            else
            {
                _selectedObjectId = HitObject(pointer.X, pointer.Y);
                if (_selectedObjectId is ObjectId selectedId &&
                    _document.Devices.TryGetValue(selectedId, out Device? selectedDevice) &&
                    selectedDevice.MountingSurface != MountingSurface.Free)
                {
                    _activeSurface = selectedDevice.MountingSurface;
                }
                else if (_selectedObjectId is ObjectId openingId && _document.Openings.TryGetValue(openingId, out BuildingOpening? selectedOpening))
                {
                    _activeSurface = selectedOpening.Surface;
                }
                _statusMessage = _selectedObjectId is ObjectId selected ? $"Selected {selected}" : "Selection cleared";
            }
        }

        if (pressed && _activeHandle is EditHandleId active)
        {
            if (_viewMode != ViewMode.Plan && active.Kind is EditHandleKind.Move or EditHandleKind.Resize or EditHandleKind.Elevation or EditHandleKind.Vertex)
            {
                Point3 destination = active.Kind == EditHandleKind.Elevation && _viewMode == ViewMode.ThreeD
                    ? SnapElevationHandle(active, pointer.Y)
                    : SnapSpatialHandle(active, ScreenToSpatial(pointer.X, pointer.Y));
                DocumentHandleEditor.SetSpatialPosition(_document, active, destination);
            }
            else
            {
                DocumentHandleEditor.SetPosition(_document, active, ScreenToDocument(pointer.X, pointer.Y));
            }
        }

        if (released && _activeHandle is EditHandleId completed)
        {
            if (_viewMode != ViewMode.Plan && completed.Kind is EditHandleKind.Move or EditHandleKind.Resize or EditHandleKind.Elevation or EditHandleKind.Vertex)
            {
                Point3 destination = DocumentHandleEditor.RequireSpatialPosition(_document, completed);
                DocumentHandleEditor.SetSpatialPosition(_document, completed, _dragOriginSpatial);
                if (destination != _dragOriginSpatial)
                {
                    _history.Execute(_document, new MoveSpatialHandleCommand(completed, destination));
                    _dirty = true;
                    _statusMessage = $"Moved {completed} to {destination.X:0}, {destination.Y:0}, Z {destination.Z:0} mm";
                }
            }
            else
            {
                Point2 destination = DocumentHandleEditor.RequirePosition(_document, completed);
                DocumentHandleEditor.SetPosition(_document, completed, _dragOrigin);
                if (destination != _dragOrigin)
                {
                    _history.Execute(_document, new MoveEditHandleCommand(completed, destination));
                    _dirty = true;
                    _statusMessage = $"Moved {completed}";
                }
            }
            _activeHandle = null;
            SyncSelectedLabelEditor();
        }
    }

    private Point3 SnapSpatialHandle(EditHandleId handle, Point3 point)
    {
        Point3 snapped = new(
            Math.Round(point.X / 100) * 100,
            Math.Round(point.Y / 100) * 100,
            Math.Round(point.Z / 100) * 100);
        bool deviceHeightHandle = (handle.Kind is EditHandleKind.Move or EditHandleKind.Elevation) &&
            _document.Devices.ContainsKey(handle.ObjectId);
        bool routeElevationHandle = handle.Kind == EditHandleKind.Elevation && handle.Index >= 0;
        return _viewMode == ViewMode.Wall && (deviceHeightHandle || routeElevationHandle) && _wallSnapHeightMillimetres is double height
            ? new Point3(snapped.X, snapped.Y, handle.Kind == EditHandleKind.Elevation ? height + DocumentHandles.ElevationHandleOffsetMillimetres : height)
            : snapped;
    }

    private Point3 SnapElevationHandle(EditHandleId handle, double screenY)
    {
        Point2 anchor = DocumentHandleEditor.RequirePosition(_document, handle);
        Point3 point = _camera3D.UnprojectElevation(screenY, anchor);
        return new Point3(point.X, point.Y, Math.Round(point.Z / 100) * 100);
    }

    private void AddDraftPoint(int screenX, int screenY)
    {
        Point3 point = SnapRoutePoint(ScreenToSpatial(screenX, screenY));
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
            _statusMessage = $"{ToolLabel(_activeTool)} point {_draftPoints.Count}: {point.X:0}, {point.Y:0}, Z {point.Z:0} mm";
        }
    }

    private Point3 SnapRoutePoint(Point3 point)
    {
        Point3 snapped = new(Math.Round(point.X / 100) * 100, Math.Round(point.Y / 100) * 100, Math.Round(point.Z / 100) * 100);
        if (_viewMode == ViewMode.Wall && _wallSnapHeightMillimetres is double wallHeight)
        {
            snapped = new Point3(snapped.X, snapped.Y, wallHeight);
        }
        if (_draftPoints.Count == 0)
        {
            return snapped;
        }
        Point3 origin = _draftPoints[^1];
        bool fixedX = _activeSurface is MountingSurface.WestWallInterior or MountingSurface.WestWallExterior or MountingSurface.EastWallInterior or MountingSurface.EastWallExterior;
        bool fixedY = _activeSurface is MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior;
        double first = fixedX ? snapped.Y : snapped.X;
        double second = fixedX || fixedY ? snapped.Z : snapped.Y;
        double originFirst = fixedX ? origin.Y : origin.X;
        double originSecond = fixedX || fixedY ? origin.Z : origin.Y;
        double deltaFirst = first - originFirst;
        double deltaSecond = second - originSecond;
        if (deltaFirst == 0 && deltaSecond == 0)
        {
            return snapped;
        }
        double angle = Math.Atan2(deltaSecond, deltaFirst);
        double snappedAngle = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        double length = Math.Sqrt(deltaFirst * deltaFirst + deltaSecond * deltaSecond);
        first = Math.Round((originFirst + Math.Cos(snappedAngle) * length) / 100) * 100;
        second = Math.Round((originSecond + Math.Sin(snappedAngle) * length) / 100) * 100;
        return fixedX
            ? new Point3(snapped.X, first, second)
            : fixedY
                ? new Point3(first, snapped.Y, second)
                : new Point3(first, second, snapped.Z);
    }

    private PortReference? HitPort(int screenX, int screenY)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document))
        {
            if (handle.Id.Kind != EditHandleKind.Port || handle.Id.Name is null)
            {
                continue;
            }
            if (_viewMode == ViewMode.Wall && !HandleVisibleInWall(handle)) continue;
            (double x, double y) = HandleToScreen(handle);
            if (Math.Abs(screenX - x) <= 10 && Math.Abs(screenY - y) <= 10)
            {
                return new PortReference(handle.Id.ObjectId, handle.Id.Name);
            }
        }
        return null;
    }

    private Point3 PortPosition(PortReference reference)
    {
        var handleId = new EditHandleId(reference.DeviceId, EditHandleKind.Port, Name: reference.PortId);
        Point2 position = DocumentHandleEditor.RequirePosition(_document, handleId);
        return new Point3(position.X, position.Y, _document.RequireDevice(reference.DeviceId).ElevationMillimetres);
    }

    private void PlaceDevice(Point3 position)
    {
        Point3 snapped = new(Math.Round(position.X / 100) * 100, Math.Round(position.Y / 100) * 100, Math.Round(position.Z / 100) * 100);
        if (_viewMode == ViewMode.Wall && _wallSnapHeightMillimetres is double wallHeight)
        {
            snapped = new Point3(snapped.X, snapped.Y, wallHeight);
        }
        ObjectId id = ObjectId.Parse($"junction-{Guid.NewGuid():N}");
        int number = _document.Devices.Values.Count(device => device.Kind == _placementKind) + 1;
        MountingSurface surface = _viewMode == ViewMode.Plan ? MountingSurface.Free : _activeSurface;
        var device = CreatePlacedDevice(id, snapped.Plan, number, snapped.Z, surface);
        _history.Execute(_document, new AddDeviceCommand(device));
        _selectedObjectId = id;
        _dirty = true;
        _statusMessage = $"Placed {id} on {device.MountingSurface} at {snapped.X:0}, {snapped.Y:0}, Z {snapped.Z:0} mm";
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

    private void PlaceOpening(Point3 position)
    {
        if (!IsWallSurface(_activeSurface))
        {
            _statusMessage = "Choose an inner or outer wall before placing an opening";
            return;
        }
        (double width, double height, string prefix) = _openingKind switch
        {
            OpeningKind.GarageDoor => (5000, 2200, "GARAGEPORT"),
            OpeningKind.Door => (900, 2100, "DÖRR"),
            OpeningKind.Window => (1200, 1000, "FÖNSTER"),
            _ => (200, 200, "GENOMFÖRING"),
        };
        if (_openingKind is OpeningKind.GarageDoor or OpeningKind.Door) position = new Point3(position.X, position.Y, height / 2);
        position = BuildingOpeningGeometry.ConstrainCentre(_document.Space, _activeSurface, position, width, height);
        ObjectId id = ObjectId.Parse($"opening-{Guid.NewGuid():N}");
        int number = _document.Openings.Values.Count(opening => opening.Kind == _openingKind) + 1;
        var opening = new BuildingOpening(id, _openingKind, _activeSurface, position, width, height, $"{prefix}-{number:00}");
        _history.Execute(_document, new AddOpeningCommand(opening));
        _selectedObjectId = id;
        _activeTool = ToolKind.Select;
        _dirty = true;
        _statusMessage = $"Placed {opening.Label} on {_activeSurface}; switched to SEL";
    }

    private void PlaceWallDimensionPoint(Point3 position)
    {
        if (_viewMode != ViewMode.Wall || !IsWallSurface(_activeSurface))
        {
            _statusMessage = "DIM is available in WALL mode";
            return;
        }
        Point3 snapped = MountingSurfaceGeometry.Constrain(_document.Space, _activeSurface, new Point3(
            Math.Round(position.X / 100) * 100,
            Math.Round(position.Y / 100) * 100,
            Math.Round(position.Z / 100) * 100));
        if (_dimensionStart is null)
        {
            _dimensionStart = snapped;
            _statusMessage = "Dimension start set; click the second point";
            return;
        }
        Point3 start = _dimensionStart.Value;
        if (start == snapped)
        {
            _statusMessage = "Choose a different second point";
            return;
        }
        ObjectId id = ObjectId.Parse($"dimension-{Guid.NewGuid():N}");
        var dimension = new WallDimension(id, _activeSurface, start, snapped);
        _history.Execute(_document, new AddWallDimensionCommand(dimension));
        _selectedObjectId = id;
        _dimensionStart = null;
        _activeTool = ToolKind.Select;
        _dirty = true;
        _statusMessage = $"Created {id}; drag either endpoint to adjust";
    }

    private static bool IsWallSurface(MountingSurface surface) => surface is
        MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or
        MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior or
        MountingSurface.WestWallInterior or MountingSurface.WestWallExterior or
        MountingSurface.EastWallInterior or MountingSurface.EastWallExterior;

    private MountingSurface WallSurfaceOrDefault() => IsWallSurface(_activeSurface)
        ? _activeSurface
        : MountingSurface.NorthWallInterior;

    private void SetViewMode(ViewMode mode)
    {
        _viewMode = mode;
        if (mode == ViewMode.Wall && !IsWallSurface(_activeSurface))
        {
            _activeSurface = MountingSurface.NorthWallInterior;
        }
        _activeHandle = null;
        CancelDraft("View changed; draft cancelled");
        _statusMessage = mode switch
        {
            ViewMode.ThreeD => $"3D view · drawing surface: {_activeSurface}",
            ViewMode.Wall => $"Wall elevation: {_activeSurface}",
            _ => "Plan view",
        };
    }

    private static bool IsExteriorWall(MountingSurface surface) => surface is
        MountingSurface.NorthWallExterior or MountingSurface.SouthWallExterior or
        MountingSurface.WestWallExterior or MountingSurface.EastWallExterior;

    private int CurrentWallDirection() => _activeSurface switch
    {
        MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior => 0,
        MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior => 1,
        MountingSurface.EastWallInterior or MountingSurface.EastWallExterior => 2,
        MountingSurface.WestWallInterior or MountingSurface.WestWallExterior => 3,
        _ => 0,
    };

    private void SetOpeningWall(int direction, bool exterior)
    {
        _activeSurface = (direction, exterior) switch
        {
            (0, false) => MountingSurface.NorthWallInterior,
            (0, true) => MountingSurface.NorthWallExterior,
            (1, false) => MountingSurface.SouthWallInterior,
            (1, true) => MountingSurface.SouthWallExterior,
            (2, false) => MountingSurface.EastWallInterior,
            (2, true) => MountingSurface.EastWallExterior,
            (3, false) => MountingSurface.WestWallInterior,
            _ => MountingSurface.WestWallExterior,
        };
        _statusMessage = _viewMode == ViewMode.Wall
            ? $"Wall elevation: {_activeSurface}"
            : $"Opening target: {_activeSurface}";
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
        for (int index = 0; index < 3; index++)
        {
            if (ViewRect(index).Contains(pointer.X, pointer.Y))
            {
                SetViewMode((ViewMode)index);
                return true;
            }
        }
        if (_viewMode == ViewMode.Wall)
        {
            for (int index = 0; index < 4; index++)
            {
                if (WallOverlayDirectionRect(index).Contains(pointer.X, pointer.Y))
                {
                    SetOpeningWall(index, IsExteriorWall(_activeSurface));
                    return true;
                }
            }
            if (WallOverlayFaceRect(false).Contains(pointer.X, pointer.Y))
            {
                SetOpeningWall(CurrentWallDirection(), false);
                return true;
            }
            if (WallOverlayFaceRect(true).Contains(pointer.X, pointer.Y))
            {
                SetOpeningWall(CurrentWallDirection(), true);
                return true;
            }
            for (int index = 0; index < WallSnapHeights.Length; index++)
            {
                if (WallHeightSnapRect(index).Contains(pointer.X, pointer.Y))
                {
                    _wallSnapHeightMillimetres = WallSnapHeights[index];
                    _statusMessage = _wallSnapHeightMillimetres is double height
                        ? $"Wall mounting height: {height:0} mm"
                        : "Wall mounting height: free 100 mm grid";
                    return true;
                }
            }
        }
        if (_viewMode == ViewMode.ThreeD && CameraRect().Contains(pointer.X, pointer.Y))
        {
            _camera3D.CycleView();
            _statusMessage = $"Camera: {_camera3D.ViewLabel}";
            return true;
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
        if (_activeTool == ToolKind.Opening)
        {
            for (int index = 0; index < OpeningKinds.Length; index++)
            {
                if (OpeningKindRect(inspectorX, index).Contains(pointer.X, pointer.Y))
                {
                    _openingKind = OpeningKinds[index];
                    _statusMessage = $"Opening: {_openingKind}";
                    return true;
                }
            }
            for (int index = 0; index < 4; index++)
            {
                if (WallDirectionRect(inspectorX, index).Contains(pointer.X, pointer.Y))
                {
                    SetOpeningWall(index, IsExteriorWall(_activeSurface));
                    return true;
                }
            }
            if (WallFaceRect(inspectorX, false).Contains(pointer.X, pointer.Y))
            {
                SetOpeningWall(CurrentWallDirection(), false);
                return true;
            }
            if (WallFaceRect(inspectorX, true).Contains(pointer.X, pointer.Y))
            {
                SetOpeningWall(CurrentWallDirection(), true);
                return true;
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
            if (_document.Devices.TryGetValue(selected, out Device? heightDevice) && ElevationMinusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                AdjustDeviceElevation(heightDevice, -100);
                return true;
            }
            if (_document.Devices.TryGetValue(selected, out heightDevice) && ElevationPlusRect(inspectorX).Contains(pointer.X, pointer.Y))
            {
                AdjustDeviceElevation(heightDevice, 100);
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
        if (ViewToggleRect(inspectorX).Contains(pointer.X, pointer.Y))
        {
            SetViewMode(_viewMode switch
            {
                ViewMode.Plan => ViewMode.Wall,
                ViewMode.Wall => ViewMode.ThreeD,
                _ => ViewMode.Plan,
            });
            return true;
        }
        if (_viewMode != ViewMode.Plan && SurfaceRect(inspectorX).Contains(pointer.X, pointer.Y))
        {
            CycleSurface();
            return true;
        }
        if (ButtonRect(inspectorX, 1).Contains(pointer.X, pointer.Y))
        {
            if (_history.Undo(_document))
            {
                EnsureSelectionExists();
                SyncSelectedLabelEditor();
                SyncRoomEditors();
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
                SyncRoomEditors();
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

    private void AdjustDeviceElevation(Device device, double delta)
    {
        double target = Math.Max(0, device.ElevationMillimetres + delta);
        var handle = new EditHandleId(device.Id, EditHandleKind.Move);
        _history.Execute(_document, new MoveSpatialHandleCommand(handle, new Point3(device.Position.X, device.Position.Y, target)));
        _dirty = true;
        _statusMessage = $"Set {device.Id} Z to {device.ElevationMillimetres:0} mm";
        SyncSelectedLabelEditor();
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

    private void CycleSurface()
    {
        MountingSurface[] surfaces = _viewMode == ViewMode.Wall
            ? Enum.GetValues<MountingSurface>().Where(IsWallSurface).ToArray()
            : Enum.GetValues<MountingSurface>().Where(surface => surface != MountingSurface.Free).ToArray();
        int index = (Array.IndexOf(surfaces, _activeSurface) + 1) % surfaces.Length;
        _activeSurface = surfaces[index];
        CancelDraft("Drawing surface changed; draft cancelled");
        _statusMessage = _viewMode == ViewMode.Wall
            ? $"Wall elevation: {_activeSurface}"
            : $"3D drawing surface: {_activeSurface}";
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
        if (_draftPoints.Count == 0 && _dimensionStart is null)
        {
            return;
        }
        ClearDraft();
        _dimensionStart = null;
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
                        : _document.Openings.TryGetValue(selected, out BuildingOpening? opening)
                            ? opening.Label
                            : _document.WallDimensions.TryGetValue(selected, out WallDimension? dimension)
                                ? dimension.Label
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
        if (_document.Devices.TryGetValue(selected, out Device? selectedDevice))
        {
            SetDeviceCoordinateEditor(selected, "x", selectedDevice.Position.X);
            SetDeviceCoordinateEditor(selected, "y", selectedDevice.Position.Y);
            SetDeviceCoordinateEditor(selected, "z", selectedDevice.ElevationMillimetres);
        }
        if (_document.Openings.TryGetValue(selected, out BuildingOpening? selectedOpening))
        {
            SetOpeningDimensionEditor(selected, "width", selectedOpening.WidthMillimetres);
            SetOpeningDimensionEditor(selected, "height", selectedOpening.HeightMillimetres);
        }
    }

    private void SetDeviceCoordinateEditor(ObjectId id, string axis, double value) =>
        _ui!.SetText(new UiId($"inspector.coordinate.{id}.{axis}"), value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

    private void SetOpeningDimensionEditor(ObjectId id, string name, double value) =>
        _ui!.SetText(new UiId($"inspector.opening.{id}.{name}"), value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

    private void SyncRoomEditors()
    {
        if (_ui is null) return;
        SpaceVolume space = _document.Space;
        SetRoomEditor("width", space.WidthMillimetres);
        SetRoomEditor("depth", space.DepthMillimetres);
        SetRoomEditor("height", space.HeightMillimetres);
        SetRoomEditor("wall", space.WallThicknessMillimetres);
        SetRoomEditor("ceiling", space.CeilingThicknessMillimetres);
    }

    private void SetRoomEditor(string name, double value) =>
        _ui!.SetText(new UiId($"room.{name}"), value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

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
            if (_viewMode == ViewMode.Plan && handle.Id.Kind == EditHandleKind.Elevation) continue;
            if (_viewMode == ViewMode.Wall && !HandleVisibleInWall(handle)) continue;
            if (!DocumentHandleEditor.CanSetPosition(handle.Id))
            {
                continue;
            }
            (double x, double y) = HandleToScreen(handle);
            if (Math.Abs(screenX - x) <= 9 && Math.Abs(screenY - y) <= 9)
            {
                return handle.Id;
            }
        }
        return null;
    }

    private ObjectId? HitObject(int screenX, int screenY)
    {
        if (_viewMode == ViewMode.ThreeD) return HitObject3D(screenX, screenY);
        if (_viewMode == ViewMode.Wall) return HitObjectWall(screenX, screenY);
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
        foreach (BuildingOpening opening in _document.Openings.Values.Reverse())
        {
            Point2[] edge = OpeningPlanEdge(opening);
            if (HitRoute(screenX, screenY, edge, 10)) return opening.Id;
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

    private Point2 ScreenToDocument(double screenX, double screenY)
        => ScreenToSpatial(screenX, screenY).Plan;

    private Point3 ScreenToSpatial(double screenX, double screenY)
    {
        if (_viewMode == ViewMode.Plan)
        {
            Point2 plan = _camera.ScreenToDocument(screenX, screenY);
            return new Point3(plan.X, plan.Y, 0);
        }
        if (_viewMode == ViewMode.Wall) return _cameraWall.Unproject(screenX, screenY);
        return _camera3D.UnprojectSurface(screenX, screenY, _activeSurface);
    }

    private (double X, double Y) HandleToScreen(EditHandle handle)
    {
        if (_viewMode == ViewMode.Plan) return _camera.DocumentToScreen(handle.Position);
        var point = new Point3(handle.Position.X, handle.Position.Y, HandleElevation(handle));
        return _viewMode == ViewMode.Wall ? _cameraWall.Project(point) : _camera3D.Project(point);
    }

    private bool HandleVisibleInWall(EditHandle handle)
    {
        if (_document.Devices.TryGetValue(handle.Id.ObjectId, out Device? device))
        {
            return device.MountingSurface == _activeSurface;
        }
        if (_document.Openings.TryGetValue(handle.Id.ObjectId, out BuildingOpening? opening))
        {
            return opening.Surface == _activeSurface;
        }
        if (_document.WallDimensions.TryGetValue(handle.Id.ObjectId, out WallDimension? dimension))
        {
            return dimension.Surface == _activeSurface;
        }
        if ((handle.Id.Kind is EditHandleKind.Vertex or EditHandleKind.Elevation) && handle.Id.Index is int index)
        {
            Point3 point = _document.Conduits.TryGetValue(handle.Id.ObjectId, out Conduit? conduit)
                ? conduit.Route.SpatialPoints[index]
                : _document.Cables.TryGetValue(handle.Id.ObjectId, out CableRoute? cable)
                    ? cable.Route.SpatialPoints[index]
                    : new Point3(double.NaN, double.NaN, double.NaN);
            return _cameraWall.IsOnWall(point);
        }
        return false;
    }

    private double HandleElevation(EditHandle handle)
    {
        if (_document.Devices.TryGetValue(handle.Id.ObjectId, out Device? device))
        {
            return device.ElevationMillimetres + (handle.Id.Kind == EditHandleKind.Elevation ? DocumentHandles.ElevationHandleOffsetMillimetres : 0);
        }
        if (_document.Openings.TryGetValue(handle.Id.ObjectId, out BuildingOpening? opening))
        {
            return handle.Id.Kind == EditHandleKind.Resize
                ? BuildingOpeningGeometry.ResizeHandle(opening, handle.Id.Name ?? throw new InvalidOperationException("Resize handle has no name.")).Z
                : opening.Centre.Z;
        }
        if (_document.WallDimensions.TryGetValue(handle.Id.ObjectId, out WallDimension? dimension))
        {
            return handle.Id.Name == "start" ? dimension.Start.Z : dimension.End.Z;
        }
        if ((handle.Id.Kind is EditHandleKind.Vertex or EditHandleKind.Elevation) && handle.Id.Index is int index)
        {
            double elevation = _document.Conduits.TryGetValue(handle.Id.ObjectId, out Conduit? conduit)
                ? conduit.Route.SpatialPoints[index].Z
                : _document.Cables.TryGetValue(handle.Id.ObjectId, out CableRoute? cable)
                    ? cable.Route.SpatialPoints[index].Z
                    : 0;
            return elevation + (handle.Id.Kind == EditHandleKind.Elevation ? DocumentHandles.ElevationHandleOffsetMillimetres : 0);
        }
        return 0;
    }

    private ObjectId? HitObject3D(int screenX, int screenY)
    {
        foreach (BuildingOpening opening in _document.Openings.Values.Reverse())
        {
            Point3[] corners = OpeningCorners(opening);
            if (HitRoute3D(screenX, screenY, [.. corners, corners[0]], 10)) return opening.Id;
        }
        foreach (Device device in _document.Devices.Values.Reverse())
        {
            (double x, double y) = _camera3D.Project(new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres));
            if (Math.Abs(screenX - x) <= 44 && Math.Abs(screenY - y) <= 28) return device.Id;
        }
        foreach (CableRoute cable in _document.Cables.Values.Reverse())
        {
            if (HitRoute3D(screenX, screenY, cable.Route.SpatialPoints, 8)) return cable.Id;
        }
        foreach (Conduit conduit in _document.Conduits.Values.Reverse())
        {
            if (HitRoute3D(screenX, screenY, conduit.Route.SpatialPoints, 10)) return conduit.Id;
        }
        return null;
    }

    private ObjectId? HitObjectWall(int screenX, int screenY)
    {
        foreach (WallDimension dimension in _document.WallDimensions.Values.Reverse())
        {
            if (dimension.Surface != _activeSurface) continue;
            if (HitRouteWall(screenX, screenY, [dimension.Start, dimension.End], 10)) return dimension.Id;
        }
        foreach (BuildingOpening opening in _document.Openings.Values.Reverse())
        {
            if (opening.Surface != _activeSurface) continue;
            Point3[] corners = OpeningCorners(opening);
            if (HitRouteWall(screenX, screenY, [.. corners, corners[0]], 10)) return opening.Id;
        }
        foreach (Device device in _document.Devices.Values.Reverse())
        {
            if (device.MountingSurface != _activeSurface) continue;
            Point3 centre = new(device.Position.X, device.Position.Y, device.ElevationMillimetres);
            (double x, double y) = _cameraWall.Project(centre);
            (double width, double height, _) = DeviceStyle(device.Kind);
            double halfWidth = Math.Max(12, width * _cameraWall.PixelsPerMillimetre / 2);
            double halfHeight = Math.Max(10, height * _cameraWall.PixelsPerMillimetre / 2);
            if (Math.Abs(screenX - x) <= halfWidth && Math.Abs(screenY - y) <= halfHeight) return device.Id;
        }
        foreach (CableRoute cable in _document.Cables.Values.Reverse())
        {
            if (HitRouteWall(screenX, screenY, cable.Route.SpatialPoints, 8)) return cable.Id;
        }
        foreach (Conduit conduit in _document.Conduits.Values.Reverse())
        {
            if (HitRouteWall(screenX, screenY, conduit.Route.SpatialPoints, 10)) return conduit.Id;
        }
        return null;
    }

    private bool HitRouteWall(int screenX, int screenY, IReadOnlyList<Point3> points, double tolerance)
    {
        for (int index = 1; index < points.Count; index++)
        {
            if (!_cameraWall.IsOnWall(points[index - 1]) || !_cameraWall.IsOnWall(points[index])) continue;
            (double x0, double y0) = _cameraWall.Project(points[index - 1]);
            (double x1, double y1) = _cameraWall.Project(points[index]);
            if (DistanceToSegment(screenX, screenY, x0, y0, x1, y1) <= tolerance) return true;
        }
        return false;
    }

    private bool HitRoute3D(int screenX, int screenY, IReadOnlyList<Point3> points, double tolerance)
    {
        for (int index = 1; index < points.Count; index++)
        {
            (double x0, double y0) = _camera3D.Project(points[index - 1]);
            (double x1, double y1) = _camera3D.Project(points[index]);
            if (DistanceToSegment(screenX, screenY, x0, y0, x1, y1) <= tolerance) return true;
        }
        return false;
    }

    private void Draw3DScene(SoftwareCanvas canvas, RectI work)
    {
        SpaceVolume space = _document.Space;
        double x0 = space.Origin.X;
        double x1 = x0 + space.WidthMillimetres;
        double y0 = space.Origin.Y;
        double y1 = y0 + space.DepthMillimetres;
        double top = space.HeightMillimetres;
        double wall = space.WallThicknessMillimetres;
        double outerX0 = x0 - wall;
        double outerX1 = x1 + wall;
        double outerY0 = y0 - wall;
        double outerY1 = y1 + wall;
        double outerTop = top + space.CeilingThicknessMillimetres;

        for (double x = Math.Ceiling(x0 / 1000) * 1000; x <= x1; x += 1000)
        {
            Draw3DLine(canvas, new Point3(x, y0, 0), new Point3(x, y1, 0), 0xff2b414e, 1);
        }
        for (double y = Math.Ceiling(y0 / 1000) * 1000; y <= y1; y += 1000)
        {
            Draw3DLine(canvas, new Point3(x0, y, 0), new Point3(x1, y, 0), 0xff2b414e, 1);
        }

        Point3[] floor = [new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0)];
        Point3[] ceiling = [new(x0, y0, top), new(x1, y0, top), new(x1, y1, top), new(x0, y1, top)];
        Point3[] outerFloor = [new(outerX0, outerY0, 0), new(outerX1, outerY0, 0), new(outerX1, outerY1, 0), new(outerX0, outerY1, 0)];
        Point3[] outerCeiling = [new(outerX0, outerY0, outerTop), new(outerX1, outerY0, outerTop), new(outerX1, outerY1, outerTop), new(outerX0, outerY1, outerTop)];
        for (int index = 0; index < 4; index++)
        {
            Draw3DLine(canvas, floor[index], floor[(index + 1) % 4], 0xff7394a5, 2);
            Draw3DLine(canvas, ceiling[index], ceiling[(index + 1) % 4], 0xff405c6b, 1);
            Draw3DLine(canvas, floor[index], ceiling[index], 0xff526e7d, 1);
            Draw3DLine(canvas, outerFloor[index], outerFloor[(index + 1) % 4], 0xff6c5335, 2);
            Draw3DLine(canvas, outerCeiling[index], outerCeiling[(index + 1) % 4], 0xff8b6e43, 2);
            Draw3DLine(canvas, outerFloor[index], outerCeiling[index], 0xff6c5335, 2);
            Draw3DLine(canvas, ceiling[index], outerCeiling[index], 0xff8b6e43, 1);
        }
        DrawActiveSurface(canvas, space);

        foreach (BuildingOpening opening in _document.Openings.Values)
        {
            Draw3DOpening(canvas, opening, _selectedObjectId == opening.Id);
        }

        foreach (Conduit conduit in _document.Conduits.Values)
        {
            Draw3DRoute(canvas, conduit.Route.SpatialPoints, _selectedObjectId == conduit.Id ? 0xffffcc66 : 0xff708898, _selectedObjectId == conduit.Id ? 7 : 5);
        }
        foreach (CableRoute cable in _document.Cables.Values)
        {
            Draw3DRoute(canvas, cable.Route.SpatialPoints, _selectedObjectId == cable.Id ? 0xffffcc66 : 0xff55c8ff, _selectedObjectId == cable.Id ? 5 : 2);
        }
        foreach (Device device in _document.Devices.Values)
        {
            Point3 position = new(device.Position.X, device.Position.Y, device.ElevationMillimetres);
            (double x, double y) = _camera3D.Project(position);
            (double floorX, double floorY) = _camera3D.Project(new Point3(device.Position.X, device.Position.Y, 0));
            (_, _, uint color) = DeviceStyle(device.Kind);
            canvas.DrawLine((int)floorX, (int)floorY, (int)x, (int)y, 0xff40515e);
            int width = device.Kind == DeviceKind.DistributionBoard ? 90 : 70;
            int height = device.Kind == DeviceKind.DistributionBoard ? 48 : 36;
            canvas.FillRect((int)x - width / 2, (int)y - height / 2, width, height, 0xff20303b);
            canvas.DrawRect((int)x - width / 2, (int)y - height / 2, width, height, _selectedObjectId == device.Id ? 0xffffcc66 : color);
            canvas.DrawText((int)x - width / 2 + 5, (int)y - 4, device.Label, color);
        }
        foreach (WallDimension dimension in _document.WallDimensions.Values)
        {
            if (dimension.Surface == _activeSurface) DrawWallDimension(canvas, dimension, _selectedObjectId == dimension.Id);
        }
        if (_activeTool == ToolKind.Dimension && _dimensionStart is Point3 dimensionStart)
        {
            DrawWallDimension(canvas, new WallDimension(ObjectId.Parse("dimension-preview"), _activeSurface, dimensionStart, _draftPointer), false);
        }

        if (_draftPoints.Count > 0)
        {
            IReadOnlyList<Point3> draft = _draftPoints.Append(_draftPointer).ToList();
            Draw3DRoute(canvas, draft, 0xffffcc66, 2);
        }
        Draw3DHandles(canvas);
        canvas.DrawText(work.X + 14, work.Y + 14, $"3D GARAGE · {_activeSurface} · CAMERA {_camera3D.ViewLabel}", 0xffffcc66);
    }

    private void DrawActiveSurface(SoftwareCanvas canvas, SpaceVolume space)
    {
        Point3[] corners = SurfaceCorners(space, _activeSurface);
        for (int index = 0; index < corners.Length; index++)
        {
            Draw3DLine(canvas, corners[index], corners[(index + 1) % corners.Length], 0xffffcc66, 3);
        }
    }

    private static Point3[] SurfaceCorners(SpaceVolume space, MountingSurface surface)
    {
        double x0 = space.Origin.X;
        double x1 = x0 + space.WidthMillimetres;
        double y0 = space.Origin.Y;
        double y1 = y0 + space.DepthMillimetres;
        double top = space.HeightMillimetres;
        double wall = space.WallThicknessMillimetres;
        bool exterior = surface.ToString().EndsWith("Exterior", StringComparison.Ordinal);
        double spanX0 = exterior ? x0 - wall : x0;
        double spanX1 = exterior ? x1 + wall : x1;
        double spanY0 = exterior ? y0 - wall : y0;
        double spanY1 = exterior ? y1 + wall : y1;
        double wallTop = exterior ? top + space.CeilingThicknessMillimetres : top;
        return surface switch
        {
            MountingSurface.CeilingInterior => [new(x0, y0, top), new(x1, y0, top), new(x1, y1, top), new(x0, y1, top)],
            MountingSurface.CeilingExterior => [new(spanX0, spanY0, wallTop), new(spanX1, spanY0, wallTop), new(spanX1, spanY1, wallTop), new(spanX0, spanY1, wallTop)],
            MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior => [new(spanX0, exterior ? y0 - wall : y0, 0), new(spanX1, exterior ? y0 - wall : y0, 0), new(spanX1, exterior ? y0 - wall : y0, wallTop), new(spanX0, exterior ? y0 - wall : y0, wallTop)],
            MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior => [new(spanX0, exterior ? y1 + wall : y1, 0), new(spanX1, exterior ? y1 + wall : y1, 0), new(spanX1, exterior ? y1 + wall : y1, wallTop), new(spanX0, exterior ? y1 + wall : y1, wallTop)],
            MountingSurface.WestWallInterior or MountingSurface.WestWallExterior => [new(exterior ? x0 - wall : x0, spanY0, 0), new(exterior ? x0 - wall : x0, spanY1, 0), new(exterior ? x0 - wall : x0, spanY1, wallTop), new(exterior ? x0 - wall : x0, spanY0, wallTop)],
            MountingSurface.EastWallInterior or MountingSurface.EastWallExterior => [new(exterior ? x1 + wall : x1, spanY0, 0), new(exterior ? x1 + wall : x1, spanY1, 0), new(exterior ? x1 + wall : x1, spanY1, wallTop), new(exterior ? x1 + wall : x1, spanY0, wallTop)],
            _ => [new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0)],
        };
    }

    private void Draw3DHandles(SoftwareCanvas canvas)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document))
        {
            if (_selectedObjectId != handle.Id.ObjectId) continue;
            (double x, double y) = HandleToScreen(handle);
            if (handle.Id.Kind == EditHandleKind.Elevation)
            {
                Point3 elevationPoint = DocumentHandleEditor.RequireSpatialPosition(_document, handle.Id);
                Point3 basePoint = new(elevationPoint.X, elevationPoint.Y, Math.Max(0, elevationPoint.Z - DocumentHandles.ElevationHandleOffsetMillimetres));
                (double baseX, double baseY) = _camera3D.Project(basePoint);
                canvas.DrawLine((int)baseX, (int)baseY, (int)x, (int)y, HandleColor(EditHandleKind.Elevation));
            }
            int size = _activeHandle == handle.Id ? 11 : 7;
            canvas.FillRect((int)x - size / 2, (int)y - size / 2, size, size, 0xff101820);
            canvas.DrawRect((int)x - size / 2, (int)y - size / 2, size, size, HandleColor(handle.Id.Kind));
        }
    }

    private void Draw3DOpening(SoftwareCanvas canvas, BuildingOpening opening, bool selected)
    {
        Point3[] corners = OpeningCorners(opening);
        uint color = selected ? 0xffffcc66 : opening.Kind == OpeningKind.Window ? 0xff78c7e8 : 0xffd8895b;
        for (int index = 0; index < corners.Length; index++) Draw3DLine(canvas, corners[index], corners[(index + 1) % corners.Length], color, selected ? 5 : 3);
        Draw3DLine(canvas, corners[0], corners[2], 0xff5f4a3e, 1);
        Draw3DLine(canvas, corners[1], corners[3], 0xff5f4a3e, 1);
        (double x, double y) = _camera3D.Project(opening.Centre);
        canvas.DrawText((int)x - opening.Label.Length * 3, (int)y - 4, opening.Label, color);
    }

    private static Point3[] OpeningCorners(BuildingOpening opening)
        => BuildingOpeningGeometry.Corners(opening);

    private static Point2[] OpeningPlanEdge(BuildingOpening opening)
    {
        double half = opening.WidthMillimetres / 2;
        bool xAxis = opening.Surface is MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior or MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior;
        return xAxis
            ? [new Point2(opening.Centre.X - half, opening.Centre.Y), new Point2(opening.Centre.X + half, opening.Centre.Y)]
            : [new Point2(opening.Centre.X, opening.Centre.Y - half), new Point2(opening.Centre.X, opening.Centre.Y + half)];
    }

    private void Draw3DRoute(SoftwareCanvas canvas, IReadOnlyList<Point3> points, uint color, int thickness)
    {
        for (int index = 1; index < points.Count; index++) Draw3DLine(canvas, points[index - 1], points[index], color, thickness);
    }

    private void Draw3DLine(SoftwareCanvas canvas, Point3 start, Point3 end, uint color, int thickness)
    {
        (double x0, double y0) = _camera3D.Project(start);
        (double x1, double y1) = _camera3D.Project(end);
        for (int offset = -(thickness / 2); offset <= thickness / 2; offset++)
        {
            canvas.DrawLine((int)x0 + offset, (int)y0, (int)x1 + offset, (int)y1, color);
        }
    }

    private void DrawWallScene(SoftwareCanvas canvas, RectI work)
    {
        double widthMillimetres = _cameraWall.WallWidthMillimetres;
        double heightMillimetres = _cameraWall.WallHeightMillimetres;
        Point3 lowerLeft = _cameraWall.PointFromWallCoordinates(0, 0);
        Point3 upperRight = _cameraWall.PointFromWallCoordinates(widthMillimetres, heightMillimetres);
        (double left, double bottom) = _cameraWall.Project(lowerLeft);
        (double right, double top) = _cameraWall.Project(upperRight);
        int wallLeft = (int)Math.Round(Math.Min(left, right));
        int wallTop = (int)Math.Round(Math.Min(top, bottom));
        int wallWidth = Math.Max(1, (int)Math.Round(Math.Abs(right - left)));
        int wallHeight = Math.Max(1, (int)Math.Round(Math.Abs(bottom - top)));
        canvas.FillRect(wallLeft, wallTop, wallWidth, wallHeight, IsExteriorWall(_activeSurface) ? 0xff202a31 : 0xff1c2a33);

        for (double horizontal = 0; horizontal <= widthMillimetres; horizontal += 500)
        {
            bool major = Math.Abs(horizontal / 1000 - Math.Round(horizontal / 1000)) < 0.00001;
            DrawWallLine(
                canvas,
                _cameraWall.PointFromWallCoordinates(horizontal, 0),
                _cameraWall.PointFromWallCoordinates(horizontal, heightMillimetres),
                major ? 0xff354b58 : 0xff293943,
                1);
        }
        for (double elevation = 0; elevation <= heightMillimetres; elevation += 500)
        {
            bool major = Math.Abs(elevation / 1000 - Math.Round(elevation / 1000)) < 0.00001;
            DrawWallLine(
                canvas,
                _cameraWall.PointFromWallCoordinates(0, elevation),
                _cameraWall.PointFromWallCoordinates(widthMillimetres, elevation),
                major ? 0xff354b58 : 0xff293943,
                1);
        }
        canvas.DrawRect(wallLeft, wallTop, wallWidth, wallHeight, 0xffffcc66);
        DrawWallLine(canvas, lowerLeft, _cameraWall.PointFromWallCoordinates(widthMillimetres, 0), 0xff8ca4af, 3);

        foreach (BuildingOpening opening in _document.Openings.Values)
        {
            if (opening.Surface == _activeSurface) DrawWallOpening(canvas, opening, _selectedObjectId == opening.Id);
        }
        foreach (Conduit conduit in _document.Conduits.Values)
        {
            DrawWallRoute(canvas, conduit.Route.SpatialPoints, _selectedObjectId == conduit.Id ? 0xffffcc66 : 0xff708898, _selectedObjectId == conduit.Id ? 7 : 5);
        }
        foreach (CableRoute cable in _document.Cables.Values)
        {
            DrawWallRoute(canvas, cable.Route.SpatialPoints, _selectedObjectId == cable.Id ? 0xffffcc66 : 0xff55c8ff, _selectedObjectId == cable.Id ? 5 : 2);
        }
        foreach (Device device in _document.Devices.Values)
        {
            if (device.MountingSurface == _activeSurface) DrawWallDevice(canvas, device, _selectedObjectId == device.Id);
        }
        if (_draftPoints.Count > 0)
        {
            DrawWallRoute(canvas, _draftPoints.Append(_draftPointer).ToList(), 0xffffcc66, 2);
        }
        DrawWallHandles(canvas);
        DrawWallSelectionDimensions(canvas);
        DrawWallOverlay(canvas);
        canvas.DrawText(work.X + 14, work.Y + 14, $"WALL ELEVATION · {_activeSurface}", 0xffffcc66);
    }

    private void DrawWallOpening(SoftwareCanvas canvas, BuildingOpening opening, bool selected)
    {
        Point3[] corners = OpeningCorners(opening);
        uint color = selected ? 0xffffcc66 : opening.Kind == OpeningKind.Window ? 0xff78c7e8 : 0xffd8895b;
        (double X, double Y)[] projected = corners.Select(_cameraWall.Project).ToArray();
        int cutoutLeft = (int)Math.Round(projected.Min(point => point.X));
        int cutoutTop = (int)Math.Round(projected.Min(point => point.Y));
        int cutoutRight = (int)Math.Round(projected.Max(point => point.X));
        int cutoutBottom = (int)Math.Round(projected.Max(point => point.Y));
        uint cutoutColor = opening.Kind == OpeningKind.Window ? 0xff102833 : 0xff17212a;
        canvas.FillRect(
            cutoutLeft,
            cutoutTop,
            Math.Max(1, cutoutRight - cutoutLeft),
            Math.Max(1, cutoutBottom - cutoutTop),
            cutoutColor);
        for (int index = 0; index < corners.Length; index++)
        {
            DrawWallLine(canvas, corners[index], corners[(index + 1) % corners.Length], color, selected ? 5 : 3);
        }
        DrawWallLine(canvas, corners[0], corners[2], 0xff5f4a3e, 1);
        DrawWallLine(canvas, corners[1], corners[3], 0xff5f4a3e, 1);
        (double x, double y) = _cameraWall.Project(opening.Centre);
        canvas.DrawText((int)x - opening.Label.Length * 3, (int)y - 4, opening.Label, color);
    }

    private void DrawWallDevice(SoftwareCanvas canvas, Device device, bool selected)
    {
        Point3 centre = new(device.Position.X, device.Position.Y, device.ElevationMillimetres);
        (double x, double y) = _cameraWall.Project(centre);
        (double widthMillimetres, double heightMillimetres, uint color) = DeviceStyle(device.Kind);
        int width = Math.Max(24, (int)Math.Round(widthMillimetres * _cameraWall.PixelsPerMillimetre));
        int height = Math.Max(20, (int)Math.Round(heightMillimetres * _cameraWall.PixelsPerMillimetre));
        int left = (int)Math.Round(x) - width / 2;
        int top = (int)Math.Round(y) - height / 2;
        canvas.FillRect(left, top, width, height, 0xff20303b);
        canvas.DrawRect(left, top, width, height, color);
        if (selected) canvas.DrawRect(left - 3, top - 3, width + 6, height + 6, 0xffffcc66);
        canvas.DrawText(left + 5, (int)y - 4, device.Label, color);
    }

    private void DrawWallRoute(SoftwareCanvas canvas, IReadOnlyList<Point3> points, uint color, int thickness)
    {
        for (int index = 1; index < points.Count; index++)
        {
            if (!_cameraWall.IsOnWall(points[index - 1]) || !_cameraWall.IsOnWall(points[index])) continue;
            DrawWallLine(canvas, points[index - 1], points[index], color, thickness);
        }
    }

    private void DrawWallDimension(SoftwareCanvas canvas, WallDimension dimension, bool selected)
    {
        uint color = selected ? 0xffffcc66 : 0xff61e294;
        DrawWallLine(canvas, dimension.Start, dimension.End, color, selected ? 3 : 1);
        (double startX, double startY) = _cameraWall.Project(dimension.Start);
        (double endX, double endY) = _cameraWall.Project(dimension.End);
        double dx = endX - startX;
        double dy = endY - startY;
        double lengthPixels = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        double nx = -dy / lengthPixels;
        double ny = dx / lengthPixels;
        for (int sign = -1; sign <= 1; sign += 2)
        {
            double x = sign < 0 ? startX : endX;
            double y = sign < 0 ? startY : endY;
            canvas.DrawLine((int)(x - nx * 7), (int)(y - ny * 7), (int)(x + nx * 7), (int)(y + ny * 7), color);
        }
        double horizontal = _cameraWall.HorizontalCoordinate(dimension.End) - _cameraWall.HorizontalCoordinate(dimension.Start);
        double elevation = dimension.End.Z - dimension.Start.Z;
        double millimetres = Math.Sqrt(horizontal * horizontal + elevation * elevation);
        string text = string.IsNullOrWhiteSpace(dimension.Label) ? $"{millimetres:0} mm" : $"{dimension.Label} · {millimetres:0} mm";
        canvas.DrawText((int)((startX + endX) / 2) - text.Length * 3, (int)((startY + endY) / 2) - 15, text, color);
    }

    private void DrawWallLine(SoftwareCanvas canvas, Point3 start, Point3 end, uint color, int thickness)
    {
        (double x0, double y0) = _cameraWall.Project(start);
        (double x1, double y1) = _cameraWall.Project(end);
        for (int offset = -(thickness / 2); offset <= thickness / 2; offset++)
        {
            canvas.DrawLine((int)x0 + offset, (int)y0, (int)x1 + offset, (int)y1, color);
        }
    }

    private void DrawWallHandles(SoftwareCanvas canvas)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(_document))
        {
            if (_selectedObjectId != handle.Id.ObjectId || !HandleVisibleInWall(handle)) continue;
            (double x, double y) = HandleToScreen(handle);
            if (handle.Id.Kind == EditHandleKind.Elevation)
            {
                Point3 elevationPoint = DocumentHandleEditor.RequireSpatialPosition(_document, handle.Id);
                Point3 basePoint = new(elevationPoint.X, elevationPoint.Y, Math.Max(0, elevationPoint.Z - DocumentHandles.ElevationHandleOffsetMillimetres));
                (double baseX, double baseY) = _cameraWall.Project(basePoint);
                canvas.DrawLine((int)baseX, (int)baseY, (int)x, (int)y, HandleColor(EditHandleKind.Elevation));
            }
            int size = _activeHandle == handle.Id ? 11 : handle.Id.Kind == EditHandleKind.Port ? 5 : 7;
            canvas.FillRect((int)x - size / 2, (int)y - size / 2, size, size, 0xff101820);
            canvas.DrawRect((int)x - size / 2, (int)y - size / 2, size, size, HandleColor(handle.Id.Kind));
        }
    }

    private void DrawWallSelectionDimensions(SoftwareCanvas canvas)
    {
        Point3? selectedPoint = _selectedObjectId is ObjectId selected && _document.Devices.TryGetValue(selected, out Device? device) && device.MountingSurface == _activeSurface
            ? new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres)
            : _selectedObjectId is ObjectId openingId && _document.Openings.TryGetValue(openingId, out BuildingOpening? opening) && opening.Surface == _activeSurface
                ? opening.Centre
                : null;
        if (selectedPoint is not Point3 point) return;

        double horizontal = _cameraWall.HorizontalCoordinate(point);
        double wallWidth = _cameraWall.WallWidthMillimetres;
        bool fromLeft = horizontal <= wallWidth / 2;
        double cornerOffset = fromLeft ? horizontal : wallWidth - horizontal;
        Point3 floorPoint = _cameraWall.PointFromWallCoordinates(horizontal, 0);
        Point3 cornerPoint = _cameraWall.PointFromWallCoordinates(fromLeft ? 0 : wallWidth, point.Z);
        DrawWallLine(canvas, floorPoint, point, 0xff61e294, 1);
        DrawWallLine(canvas, cornerPoint, point, 0xff61e294, 1);
        (double x, double y) = _cameraWall.Project(point);
        canvas.DrawText((int)x + 10, (int)y + 12, $"FLOOR {point.Z:0} mm", 0xff61e294);
        canvas.DrawText((int)x + 10, (int)y + 28, $"{(fromLeft ? "LEFT" : "RIGHT")} {cornerOffset:0} mm", 0xff61e294);
    }

    private void DrawWallOverlay(SoftwareCanvas canvas)
    {
        string[] directions = ["N", "S", "E", "W"];
        int current = CurrentWallDirection();
        for (int index = 0; index < directions.Length; index++)
        {
            RectI rect = WallOverlayDirectionRect(index);
            DrawChromeButton(canvas, rect, directions[index], true);
            if (index == current) canvas.DrawRect(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4, 0xffffcc66);
        }
        RectI inside = WallOverlayFaceRect(false);
        RectI outside = WallOverlayFaceRect(true);
        DrawChromeButton(canvas, inside, "INSIDE", true);
        DrawChromeButton(canvas, outside, "OUTSIDE", true);
        RectI active = IsExteriorWall(_activeSurface) ? outside : inside;
        canvas.DrawRect(active.X - 2, active.Y - 2, active.Width + 4, active.Height + 4, 0xffffcc66);

        canvas.DrawText(ToolbarWidth + 16, 82, "HEIGHT", 0xff9eb0bb);
        for (int index = 0; index < WallSnapHeights.Length; index++)
        {
            RectI rect = WallHeightSnapRect(index);
            DrawChromeButton(canvas, rect, WallSnapHeightLabels[index], true);
            if (WallSnapHeights[index] == _wallSnapHeightMillimetres)
            {
                canvas.DrawRect(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4, 0xff61e294);
            }
        }
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
        foreach (BuildingOpening opening in _document.Openings.Values)
        {
            Point2[] edge = OpeningPlanEdge(opening);
            DrawRoute(canvas, edge, _selectedObjectId == opening.Id ? 0xffffcc66 : 0xffd8895b, _selectedObjectId == opening.Id ? 9 : 5);
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
        DrawRoute(canvas, _draftPoints.Select(point => point.Plan).ToList(), color, 3);
        Point3 last = _draftPoints[^1];
        DrawRoute(canvas, [last.Plan, _draftPointer.Plan], 0xffffcc66, 1);
        foreach (Point3 point in _draftPoints)
        {
            (double x, double y) = _camera.DocumentToScreen(point.Plan);
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
            if (handle.Id.Kind == EditHandleKind.Elevation) continue;
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
        canvas.DrawText(14, 408, "VIEW", 0xff728996);
        for (int index = 0; index < 3; index++)
        {
            RectI rect = ViewRect(index);
            bool active = (int)_viewMode == index;
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, active ? 0xffffcc66 : 0xff3c5261);
            string label = index switch { 0 => "PLAN", 1 => "3D", _ => "WALL" };
            canvas.DrawText(rect.X + 8, rect.Y + 13, label, active ? 0xffffcc66 : 0xff9eb0bb);
        }
        canvas.DrawText(14, 566, "CAMERA", 0xff728996);
        RectI cameraRect = CameraRect();
        canvas.DrawRect(cameraRect.X, cameraRect.Y, cameraRect.Width, cameraRect.Height, _viewMode == ViewMode.ThreeD ? 0xffffcc66 : 0xff3c5261);
        canvas.DrawText(cameraRect.X + 7, cameraRect.Y + 13, _camera3D.ViewLabel, _viewMode == ViewMode.ThreeD ? 0xffffcc66 : 0xff667680);

        int inspectorX = work.Right;
        canvas.FillRect(inspectorX, 0, canvas.Width - inspectorX, canvas.Height, 0xff101820);
        canvas.DrawLine(inspectorX, 0, inspectorX, canvas.Height, 0xff40515e);
        canvas.DrawText(inspectorX + 18, 20, _document.Name.ToUpperInvariant(), 0xffe3edf2, 2);
        DrawSelectionInspector(canvas, inspectorX);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 0), "SAVE", _projectDirectory is not null);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 1), "UNDO", _history.UndoCount > 0);
        DrawChromeButton(canvas, ButtonRect(inspectorX, 2), "REDO", _history.RedoCount > 0);
        DrawChromeButton(canvas, ExportPngRect(inspectorX), "PNG", _projectDirectory is not null);
        DrawChromeButton(canvas, ViewToggleRect(inspectorX), NextViewLabel(), true);
        DrawChromeButton(canvas, SurfaceRect(inspectorX), SurfaceLabel(_activeSurface), _viewMode != ViewMode.Plan);
        canvas.DrawText(inspectorX + 18, 226, _dirty ? $"MODIFIED · {_statusMessage}" : _statusMessage, _dirty ? 0xffffcc66 : 0xff9eb0bb);
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
        else if (_activeTool == ToolKind.Opening)
        {
            DrawOpeningPalette(canvas, inspectorX);
        }
        else if (_selectedObjectId is ObjectId selected)
        {
            DrawPropertyControls(canvas, inspectorX, selected);
        }
        else if (_viewMode == ViewMode.ThreeD)
        {
            DrawRoomControls(canvas, inspectorX);
        }
        DrawProjectAnalysis(canvas, inspectorX);

        canvas.FillRect(ToolbarWidth, work.Bottom, work.Width, StatusHeight, 0xff0b1117);
        Point3 spatialPoint = ScreenToSpatial(pointer.X, pointer.Y);
        string status = _viewMode switch
        {
            ViewMode.ThreeD => $"3D {_activeSurface}   X {spatialPoint.X:0}   Y {spatialPoint.Y:0}   Z {spatialPoint.Z:0} mm   RMB orbit · MMB pan · wheel zoom",
            ViewMode.Wall => $"WALL {_activeSurface}   ALONG {_cameraWall.HorizontalCoordinate(spatialPoint):0}   FLOOR {spatialPoint.Z:0} mm   SNAP {(_wallSnapHeightMillimetres is double snapHeight ? $"{snapHeight:0}" : "FREE")}   ZOOM {_cameraWall.ZoomPercent}%",
            _ => $"X {spatialPoint.X:0} mm   Y {spatialPoint.Y:0} mm   Zoom {_camera.PixelsPerMillimetre * 1000:0} px/m   MMB/RMB pan   Wheel zoom",
        };
        canvas.DrawText(ToolbarWidth + 12, work.Bottom + 10, status, 0xff91a6b3);
    }

    private void DrawSelectionInspector(SoftwareCanvas canvas, int inspectorX)
    {
        if (_selectedObjectId is not ObjectId selected)
        {
            canvas.DrawText(inspectorX + 18, 60, "Selected: none", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 86, $"Objects: {_document.Devices.Count + _document.Cables.Count + _document.Conduits.Count + _document.Annotations.Count + _document.Openings.Count + _document.WallDimensions.Count}", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 112, $"Undo: {_history.UndoCount}   Redo: {_history.RedoCount}", 0xff9eb0bb);
            return;
        }
        canvas.DrawText(inspectorX + 18, 56, selected.ToString(), 0xff63d4ff);
        if (_document.Devices.TryGetValue(selected, out Device? device))
        {
            canvas.DrawText(inspectorX + 18, 78, $"{device.Kind}  {device.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"X {device.Position.X:0}  Y {device.Position.Y:0}  Z {device.ElevationMillimetres:0} mm", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 122, $"{device.MountingSurface}  R {device.RotationDegrees:0}°", 0xff9eb0bb);
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
        else if (_document.Openings.TryGetValue(selected, out BuildingOpening? opening))
        {
            canvas.DrawText(inspectorX + 18, 78, $"{opening.Kind}  {opening.Label}", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, $"{opening.WidthMillimetres:0} × {opening.HeightMillimetres:0} mm", 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 122, opening.Surface.ToString(), 0xff9eb0bb);
        }
        else if (_document.WallDimensions.TryGetValue(selected, out WallDimension? dimension))
        {
            double horizontal = _cameraWall.HorizontalCoordinate(dimension.End) - _cameraWall.HorizontalCoordinate(dimension.Start);
            double vertical = dimension.End.Z - dimension.Start.Z;
            canvas.DrawText(inspectorX + 18, 78, $"Wall dimension  {Math.Sqrt(horizontal * horizontal + vertical * vertical):0} mm", 0xffc7d4dc);
            canvas.DrawText(inspectorX + 18, 100, dimension.Surface.ToString(), 0xff9eb0bb);
            canvas.DrawText(inspectorX + 18, 122, "Drag START / END handles", 0xff9eb0bb);
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

    private void DrawOpeningPalette(SoftwareCanvas canvas, int inspectorX)
    {
        canvas.DrawText(inspectorX + 18, 260, "WALL OPENING", 0xff9eb0bb);
        for (int index = 0; index < OpeningKinds.Length; index++)
        {
            OpeningKind kind = OpeningKinds[index];
            RectI rect = OpeningKindRect(inspectorX, index);
            bool active = kind == _openingKind;
            canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, 0xff15212a);
            canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, active ? 0xffffcc66 : 0xff40515e);
            canvas.DrawText(rect.X + 7, rect.Y + 12, OpeningLabel(kind), active ? 0xffffcc66 : 0xffa9bbc5);
        }
        canvas.DrawText(inspectorX + 18, 378, "WALL", 0xff9eb0bb);
        string[] directions = ["N", "S", "E", "W"];
        int currentDirection = CurrentWallDirection();
        for (int index = 0; index < directions.Length; index++)
        {
            RectI rect = WallDirectionRect(inspectorX, index);
            DrawChromeButton(canvas, rect, directions[index], true);
            if (IsWallSurface(_activeSurface) && index == currentDirection)
            {
                canvas.DrawRect(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4, 0xffffcc66);
            }
        }
        bool exterior = IsExteriorWall(_activeSurface);
        RectI interiorRect = WallFaceRect(inspectorX, false);
        RectI exteriorRect = WallFaceRect(inspectorX, true);
        DrawChromeButton(canvas, interiorRect, "INSIDE", true);
        DrawChromeButton(canvas, exteriorRect, "OUTSIDE", true);
        RectI faceRect = exterior ? exteriorRect : interiorRect;
        if (IsWallSurface(_activeSurface))
        {
            canvas.DrawRect(faceRect.X - 2, faceRect.Y - 2, faceRect.Width + 4, faceRect.Height + 4, 0xffffcc66);
        }
        canvas.DrawText(inspectorX + 18, 500, IsWallSurface(_activeSurface) ? $"TARGET {_activeSurface}" : "CHOOSE N / S / E / W", IsWallSurface(_activeSurface) ? 0xff61e294 : 0xffffcc66);
    }

    private void DrawPropertyControls(SoftwareCanvas canvas, int inspectorX, ObjectId selected)
    {
        _document.Devices.TryGetValue(selected, out Device? device);
        _document.Cables.TryGetValue(selected, out CableRoute? cable);
        _document.Conduits.TryGetValue(selected, out Conduit? conduit);
        _document.Annotations.TryGetValue(selected, out Annotation? annotation);
        _document.Openings.TryGetValue(selected, out BuildingOpening? opening);
        _document.WallDimensions.TryGetValue(selected, out WallDimension? dimension);
        string label = device is not null
            ? device.Label
            : cable is not null
                ? cable.Label
                : conduit is not null
                    ? conduit.Label
                    : annotation is not null
                        ? annotation.Text
                        : opening is not null
                            ? opening.Label
                            : dimension?.Label ?? string.Empty;
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
        if (device is not null)
        {
            canvas.DrawText(inspectorX + 18, 356, "EXACT POSITION (MM)", 0xff9eb0bb);
            DrawDeviceCoordinate(canvas, inspectorX, selected, device, 0, "X");
            DrawDeviceCoordinate(canvas, inspectorX, selected, device, 1, "Y");
            DrawDeviceCoordinate(canvas, inspectorX, selected, device, 2, "Z");
            canvas.DrawText(inspectorX + 18, 502, $"SURFACE  {device.MountingSurface}", 0xff9eb0bb);
            DrawChromeButton(canvas, ElevationMinusRect(inspectorX), "Z -100", true);
            DrawChromeButton(canvas, ElevationPlusRect(inspectorX), "Z +100", true);
        }
        else if (cable is not null)
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
        else if (opening is not null)
        {
            canvas.DrawText(inspectorX + 18, 356, "EXACT OPENING SIZE (MM)", 0xff9eb0bb);
            DrawOpeningDimension(canvas, inspectorX, selected, opening, 0, "WIDTH", "width");
            DrawOpeningDimension(canvas, inspectorX, selected, opening, 1, "HEIGHT", "height");
            canvas.DrawText(inspectorX + 18, 466, $"CENTRE Z  {opening.Centre.Z:0} mm", 0xff9eb0bb);
        }
        if (TryGetEditableRoute(selected, out Polyline? route))
        {
            DrawChromeButton(canvas, AddVertexRect(inspectorX), "+ POINT", true);
            DrawChromeButton(canvas, DeleteVertexRect(inspectorX), "- POINT", route!.Points.Count > 2);
        }
    }

    private void DrawOpeningDimension(SoftwareCanvas canvas, int inspectorX, ObjectId selected, BuildingOpening opening, int row, string label, string name)
    {
        int y = 374 + row * 42;
        canvas.DrawText(inspectorX + 18, y + 10, label, 0xffc7d4dc);
        double current = name == "width" ? opening.WidthMillimetres : opening.HeightMillimetres;
        UiTextBoxResult text = _ui!.TextBox(
            new UiId($"inspector.opening.{selected}.{name}"),
            new RectI(inspectorX + 88, y, 150, 30),
            current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            label,
            new UiTextBoxOptions(Numeric: true, MaxLength: 10));
        if (!text.Submitted) return;
        if (!TryParsePositive(text.Text, out double value))
        {
            _statusMessage = $"{label} must be greater than zero";
            SetOpeningDimensionEditor(selected, name, current);
            return;
        }
        string propertyName = name == "width" ? "width_mm" : "height_mm";
        _history.Execute(_document, DocumentProperties.CreateSetCommand(_document, new PropertyHandleId(selected, propertyName), value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        _dirty = true;
        _statusMessage = $"Set {opening.Label} {name} to {value:0.###} mm";
        SyncSelectedLabelEditor();
    }

    private void DrawDeviceCoordinate(SoftwareCanvas canvas, int inspectorX, ObjectId selected, Device device, int row, string axis)
    {
        int y = 374 + row * 42;
        canvas.DrawText(inspectorX + 18, y + 10, axis, 0xffc7d4dc);
        double current = axis switch { "X" => device.Position.X, "Y" => device.Position.Y, _ => device.ElevationMillimetres };
        string key = axis.ToLowerInvariant();
        UiTextBoxResult text = _ui!.TextBox(
            new UiId($"inspector.coordinate.{selected}.{key}"),
            new RectI(inspectorX + 50, y, 188, 30),
            current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            axis,
            new UiTextBoxOptions(Numeric: true, MaxLength: 12));
        if (!text.Submitted) return;
        bool valid = axis == "Z" ? TryParseNonNegative(text.Text, out double value) : TryParseFinite(text.Text, out value);
        if (!valid)
        {
            _statusMessage = axis == "Z" ? "Z must be non-negative" : $"{axis} must be a number";
            SetDeviceCoordinateEditor(selected, key, current);
            return;
        }
        Point3 destination = axis switch
        {
            "X" => new Point3(value, device.Position.Y, device.ElevationMillimetres),
            "Y" => new Point3(device.Position.X, value, device.ElevationMillimetres),
            _ => new Point3(device.Position.X, device.Position.Y, value),
        };
        _history.Execute(_document, new MoveSpatialHandleCommand(new EditHandleId(selected, EditHandleKind.Move), destination));
        _dirty = true;
        _statusMessage = $"Set {selected} {axis} to {value:0.###} mm";
        SyncSelectedLabelEditor();
    }

    private void DrawRoomControls(SoftwareCanvas canvas, int inspectorX)
    {
        canvas.DrawText(inspectorX + 18, 258, "ROOM DIMENSIONS (MM)", 0xff9eb0bb);
        DrawRoomDimension(canvas, inspectorX, 0, "WIDTH", "width", _document.Space.WidthMillimetres);
        DrawRoomDimension(canvas, inspectorX, 1, "DEPTH", "depth", _document.Space.DepthMillimetres);
        DrawRoomDimension(canvas, inspectorX, 2, "HEIGHT", "height", _document.Space.HeightMillimetres);
        DrawRoomDimension(canvas, inspectorX, 3, "WALL", "wall", _document.Space.WallThicknessMillimetres);
        DrawRoomDimension(canvas, inspectorX, 4, "CEILING", "ceiling", _document.Space.CeilingThicknessMillimetres);
    }

    private void DrawRoomDimension(SoftwareCanvas canvas, int inspectorX, int row, string label, string name, double current)
    {
        int y = 280 + row * 45;
        canvas.DrawText(inspectorX + 18, y + 10, label, 0xffc7d4dc);
        UiTextBoxResult text = _ui!.TextBox(
            new UiId($"room.{name}"),
            new RectI(inspectorX + 112, y, 126, 30),
            current.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "millimetres",
            new UiTextBoxOptions(Numeric: true, MaxLength: 10));
        if (!text.Submitted) return;
        if (!TryParsePositive(text.Text, out double value))
        {
            _statusMessage = $"{label} must be greater than zero";
            SetRoomEditor(name, current);
            return;
        }

        SpaceVolume space = _document.Space;
        SpaceVolume updated = name switch
        {
            "width" => space with { WidthMillimetres = value },
            "depth" => space with { DepthMillimetres = value },
            "height" => space with { HeightMillimetres = value },
            "wall" => space with { WallThicknessMillimetres = value },
            "ceiling" => space with { CeilingThicknessMillimetres = value },
            _ => space,
        };
        _history.Execute(_document, new SetSpaceVolumeCommand(updated));
        _dirty = true;
        _statusMessage = $"Room {name} set to {value:0.###} mm";
        SyncRoomEditors();
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

    private static bool TryParseFinite(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);

    private static bool TryParsePositive(string text, out double value) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value > 0;

    private static RectI ButtonRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index * 78, 146, 68, 32);

    private static RectI ExportPngRect(int inspectorX) => new(inspectorX + 18, 190, 68, 30);
    private static RectI ViewToggleRect(int inspectorX) => new(inspectorX + 96, 190, 68, 30);
    private static RectI SurfaceRect(int inspectorX) => new(inspectorX + 174, 190, 88, 30);

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
    private static RectI ElevationMinusRect(int inspectorX) => new(inspectorX + 18, 520, 104, 30);
    private static RectI ElevationPlusRect(int inspectorX) => new(inspectorX + 134, 520, 104, 30);
    private static RectI DiagnosticRect(int inspectorX, int index) => new(inspectorX + 18, 700 + index * 30, 220, 24);
    private static RectI SymbolRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index % 2 * 112, 282 + index / 2 * 42, 102, 32);
    private static RectI OpeningKindRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index % 2 * 112, 282 + index / 2 * 42, 102, 32);
    private static RectI WallDirectionRect(int inspectorX, int index) =>
        new(inspectorX + 18 + index * 56, 398, 48, 30);
    private static RectI WallFaceRect(int inspectorX, bool exterior) =>
        new(inspectorX + 18 + (exterior ? 116 : 0), 444, 104, 30);
    private static RectI WallOverlayDirectionRect(int index) =>
        new(ToolbarWidth + 16 + index * 46, 38, 40, 30);
    private static RectI WallOverlayFaceRect(bool exterior) =>
        new(ToolbarWidth + 208 + (exterior ? 88 : 0), 38, 80, 30);
    private static RectI WallHeightSnapRect(int index) =>
        new(ToolbarWidth + 68 + index * 66, 74, 60, 30);

    private static void DrawChromeButton(SoftwareCanvas canvas, RectI rect, string label, bool enabled)
    {
        uint border = enabled ? 0xff63d4ff : 0xff34434d;
        uint text = enabled ? 0xffccebf7 : 0xff667680;
        canvas.FillRect(rect.X, rect.Y, rect.Width, rect.Height, 0xff15212a);
        canvas.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, border);
        canvas.DrawText(rect.X + 14, rect.Y + 12, label, text);
    }

    private static RectI ToolRect(int index) => new(10, 64 + index * 44, 52, 34);
    private static RectI ViewRect(int index) => new(10, 428 + index * 44, 52, 32);
    private static RectI CameraRect() => new(10, 586, 52, 32);

    private const int ToolCount = 8;

    private string NextViewLabel() => _viewMode switch
    {
        ViewMode.Plan => "WALL",
        ViewMode.Wall => "3D",
        _ => "PLAN",
    };

    private static string ToolLabel(ToolKind tool) => tool switch
    {
        ToolKind.Select => "SEL",
        ToolKind.Pan => "PAN",
        ToolKind.PlaceDevice => "DEV",
        ToolKind.Wire => "WIRE",
        ToolKind.Conduit => "PIPE",
        ToolKind.Text => "TEXT",
        ToolKind.Opening => "OPEN",
        ToolKind.Dimension => "DIM",
        _ => "?",
    };

    private static string SurfaceLabel(MountingSurface surface) => surface switch
    {
        MountingSurface.FloorInterior => "FLOOR",
        MountingSurface.CeilingInterior => "CEIL-IN",
        MountingSurface.CeilingExterior => "CEIL-OUT",
        MountingSurface.NorthWallInterior => "N-IN",
        MountingSurface.NorthWallExterior => "N-OUT",
        MountingSurface.SouthWallInterior => "S-IN",
        MountingSurface.SouthWallExterior => "S-OUT",
        MountingSurface.WestWallInterior => "W-IN",
        MountingSurface.WestWallExterior => "W-OUT",
        MountingSurface.EastWallInterior => "E-IN",
        MountingSurface.EastWallExterior => "E-OUT",
        _ => "FREE",
    };

    private static (double Width, double Height, uint Color) DeviceStyle(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => (1500, 900, 0xffd7a84a),
        DeviceKind.PoeSwitch => (1300, 700, 0xff4aa6d7),
        DeviceKind.Camera => (700, 500, 0xff64c987),
        DeviceKind.Light => (600, 600, 0xffffd46a),
        _ => (800, 600, 0xffb4c0c8),
    };

    private static uint HandleColor(EditHandleKind kind) => kind switch
    {
        EditHandleKind.Move => 0xffffcc66,
        EditHandleKind.Rotate => 0xffff70d2,
        EditHandleKind.Elevation => 0xffff9f43,
        EditHandleKind.Vertex => 0xff63d4ff,
        EditHandleKind.Port => 0xff61e294,
        EditHandleKind.LabelAnchor => 0xffffa85c,
        _ => 0xffd7e0e5,
    };

    private Device CreatePlacedDevice(
        ObjectId id,
        Point2 position,
        int number,
        double elevationMillimetres,
        MountingSurface mountingSurface)
    {
        (string prefix, Port[] ports) = _placementKind switch
        {
            DeviceKind.DistributionBoard => ("CENTRAL", new[] { new Port("feed", PortKind.MainsPower, new Point2(750, 0)) }),
            DeviceKind.Outlet => ("UTT", new[] { new Port("power", PortKind.MainsPower, new Point2(0, 0)) }),
            DeviceKind.Camera => ("KAM", new[] { new Port("eth0", PortKind.EthernetPoe, new Point2(-350, 0)) }),
            DeviceKind.PoeSwitch => ("POE-SW", new[] { new Port("uplink", PortKind.Ethernet, new Point2(-650, 0)), new Port("port-1", PortKind.EthernetPoe, new Point2(0, -350)) }),
            DeviceKind.AccessPoint => ("AP", new[] { new Port("eth0", PortKind.EthernetPoe, new Point2(0, 300)) }),
            DeviceKind.Light => ("LAMPA", new[] { new Port("power", PortKind.MainsPower, new Point2(0, 0)) }),
            _ => ("DOSA", new[] { new Port("generic", PortKind.Generic, new Point2(0, 0)) }),
        };
        return new Device(id, _placementKind, position, $"{prefix}-{number:00}", ports, elevationMillimetres, mountingSurface);
    }

    private static readonly DeviceKind[] PlacementKinds =
    [
        DeviceKind.JunctionBox,
        DeviceKind.Outlet,
        DeviceKind.DistributionBoard,
        DeviceKind.Camera,
        DeviceKind.PoeSwitch,
        DeviceKind.AccessPoint,
        DeviceKind.Light,
    ];

    private static readonly OpeningKind[] OpeningKinds =
    [
        OpeningKind.GarageDoor,
        OpeningKind.Door,
        OpeningKind.Window,
        OpeningKind.Penetration,
    ];

    private static readonly double?[] WallSnapHeights = [null, 300, 1100, 2200, 2400];
    private static readonly string[] WallSnapHeightLabels = ["FREE", "300", "1100", "2200", "2400"];

    private static string OpeningLabel(OpeningKind kind) => kind switch
    {
        OpeningKind.GarageDoor => "PORT",
        OpeningKind.Door => "DÖRR",
        OpeningKind.Window => "FÖNSTER",
        _ => "GENOMF",
    };

    private static string SymbolLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.JunctionBox => "DOSA",
        DeviceKind.Outlet => "UTTAG",
        DeviceKind.DistributionBoard => "CENTRAL",
        DeviceKind.Camera => "KAMERA",
        DeviceKind.PoeSwitch => "POE-SW",
        DeviceKind.AccessPoint => "AP",
        DeviceKind.Light => "LAMPA",
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
        Opening,
        Dimension,
    }

    private enum ViewMode
    {
        Plan,
        ThreeD,
        Wall,
    }

}
