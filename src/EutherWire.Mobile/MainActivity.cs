using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using EutherWire.Document.Analysis;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Synchronization;
using Color = Android.Graphics.Color;
using Typeface = Android.Graphics.Typeface;
using TypefaceStyle = Android.Graphics.TypefaceStyle;

namespace EutherWire.Mobile;

[Activity(
    Label = "EutherWire",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Unspecified)]
public sealed class MainActivity : Activity
{
    private const int ImportSnapshotRequest = 1001;
    private const int ExportEventsRequest = 1002;
    private const int ExportSnapshotRequest = 1003;
    private const string PreferencesName = "eutherwire_mobile";
    private const string ActiveProjectKey = "active_project";
    private const string DeviceIdKey = "device_id";
    private const string PendingJournalName = "pending-installation-events.jsonl";

    private readonly string[] _filterNames = ["ALL", "TO DO", "DONE", "BLOCKED"];
    private readonly Dictionary<MobileMode, Button> _modeButtons = [];
    private static readonly DeviceKind[] PlacementKinds =
    [
        DeviceKind.JunctionBox,
        DeviceKind.Outlet,
        DeviceKind.DistributionBoard,
        DeviceKind.Light,
        DeviceKind.Camera,
        DeviceKind.PoeSwitch,
        DeviceKind.AccessPoint,
    ];
    private static readonly MountingSurface[] DeviceSurfaces =
    [
        MountingSurface.Free,
        MountingSurface.FloorInterior,
        MountingSurface.CeilingInterior,
        MountingSurface.NorthWallInterior,
        MountingSurface.EastWallInterior,
        MountingSurface.SouthWallInterior,
        MountingSurface.WestWallInterior,
        MountingSurface.CeilingExterior,
        MountingSurface.NorthWallExterior,
        MountingSurface.EastWallExterior,
        MountingSurface.SouthWallExterior,
        MountingSurface.WestWallExterior,
    ];
    private static readonly CableKind[] MobileCableKinds =
    [
        CableKind.Cat6,
        CableKind.Cat6A,
        CableKind.Mains3G25,
        CableKind.Mains5G6,
        CableKind.FibreDuplex,
        CableKind.Coax,
        CableKind.LowVoltageDc,
    ];
    private static readonly ConduitProfile[] MobileConduitProfiles =
    [
        new(16, 12.0, "pipelife-halovolt-750n-16", "1414864"),
        new(20, 15.8, "pipelife-halovolt-750n-20", "1414865"),
        new(25, 20.2, "pipelife-halovolt-750n-25", "1414866"),
    ];
    private ProjectDocument? _document;
    private string? _projectDirectory;
    private LinearLayout? _taskList;
    private TextView? _projectTitle;
    private TextView? _summary;
    private TextView? _message;
    private Spinner? _filter;
    private Button? _exportButton;
    private RoomPreviewView? _roomPreview;
    private TextView? _deviceSelection;
    private Button? _editDeviceButton;
    private ObjectId? _selectedDeviceId;
    private ObjectId? _selectedRouteId;
    private RouteDraftState? _routeDraft;
    private MobileMode _mode = MobileMode.Survey;
    private RoomPreviewMode _previewMode = RoomPreviewMode.Plan;

    private enum MobileMode { Survey, Design, Install }

    private sealed class RouteDraftState(RoomPreviewTool tool, string label)
    {
        public RoomPreviewTool Tool { get; } = tool;
        public string Label { get; } = label;
        public CableKind CableKind { get; init; } = CableKind.Cat6;
        public ConduitProfile? Conduit { get; init; }
        public List<Point3> Points { get; } = [];
        public PortReference? From { get; set; }
        public PortReference? To { get; set; }
    }

    private sealed record ConduitProfile(double NominalMillimetres, double InnerMillimetres, string ProductId, string ENumber)
    {
        public ConduitProduct ToProduct() => new(
            ProductId,
            "Pipelife",
            $"Halovolt PP HF 750N LF {NominalMillimetres:0}",
            ENumber,
            NominalMillimetres,
            InnerMillimetres,
            "https://www.pipelife.se/");
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        LoadActiveProject();
    }

    private void BuildUi()
    {
        var root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        root.SetBackgroundColor(Color.ParseColor("#0b1821"));
        root.SetPadding(Dp(16), Dp(14), Dp(16), Dp(10));

        var heading = Text("EUTHERWIRE · INSTALL", 21, "#55d7ff");
        heading.SetTypeface(Typeface.Monospace, TypefaceStyle.Bold);
        root.AddView(heading);
        _projectTitle = Text("NO PROJECT LOADED", 15, "#f2c94c");
        root.AddView(_projectTitle);
        _summary = Text("Import a project snapshot to begin.", 13, "#9eb0bb");
        root.AddView(_summary);

        var modes = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        modes.SetPadding(0, Dp(10), 0, 0);
        foreach (MobileMode mode in Enum.GetValues<MobileMode>())
        {
            Button button = ActionButton(mode.ToString().ToUpperInvariant());
            button.Click += (_, _) => SetMode(mode);
            modes.AddView(button, Weighted());
            _modeButtons.Add(mode, button);
        }
        root.AddView(modes);

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetPadding(0, Dp(10), 0, Dp(8));
        Button import = ActionButton("IMPORT SNAPSHOT");
        import.Click += (_, _) => BeginImport();
        actions.AddView(import, Weighted());
        _exportButton = ActionButton("EXPORT PROJECT");
        _exportButton.Click += (_, _) =>
        {
            if (_mode == MobileMode.Install) BeginExport();
            else BeginProjectExport();
        };
        actions.AddView(_exportButton, Weighted());
        root.AddView(actions);

        _filter = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, _filterNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _filter.Adapter = adapter;
        _filter.ItemSelected += (_, _) => RenderTasks();
        root.AddView(_filter, MatchWrap());

        _message = Text(string.Empty, 12, "#f2c94c");
        root.AddView(_message);

        var scroll = new ScrollView(this)
        {
            FillViewport = true,
        };
        _taskList = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        scroll.AddView(_taskList, MatchWrap());
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        SetContentView(root);
    }

    private void LoadActiveProject()
    {
        string? directory = Preferences.GetString(ActiveProjectKey, null);
        if (string.IsNullOrWhiteSpace(directory) || !File.Exists(Path.Combine(directory, ProjectToml.FileName)))
        {
            _document = null;
            _projectDirectory = null;
            RenderTasks();
            return;
        }
        try
        {
            _projectDirectory = directory;
            _document = ProjectToml.Load(directory);
            RecoverPendingEvents();
            RenderTasks();
        }
        catch (Exception exception)
        {
            _document = null;
            _projectDirectory = null;
            ShowMessage($"Could not load project: {exception.Message}", error: true);
            RenderTasks();
        }
    }

    private void RenderTasks()
    {
        if (_taskList is null || _projectTitle is null || _summary is null) return;
        _taskList.RemoveAllViews();
        ConfigureModeUi();
        if (_document is null)
        {
            _projectTitle.Text = "NO PROJECT LOADED";
            _summary.Text = _mode == MobileMode.Survey
                ? "Measure a new room or import a .eutherwire-snapshot."
                : "Import or measure a project first.";
            if (_mode == MobileMode.Survey)
            {
                Button create = ActionButton("NEW MEASURED ROOM");
                create.Click += (_, _) => ShowRoomDialog(create: true);
                _taskList.AddView(create, MatchWrap());
            }
            return;
        }

        if (_mode == MobileMode.Survey)
        {
            RenderSurvey();
            return;
        }
        if (_mode == MobileMode.Design)
        {
            RenderDesign();
            return;
        }

        ProjectAnalysis analysis = ProjectAnalyzer.Analyze(_document);
        int pendingCount = PendingEvents().Count;
        _projectTitle.Text = _document.Name.ToUpperInvariant();
        _summary.Text = $"{analysis.CompletedInstallationCount}/{analysis.InstallationTasks.Count} DONE · {pendingCount} PENDING EVENT{(pendingCount == 1 ? string.Empty : "S")}";
        List<InstallationTask> tasks = Filter(analysis.InstallationTasks, _filter?.SelectedItemPosition ?? 0).ToList();
        foreach (InstallationTask task in tasks)
        {
            Button row = new(this)
            {
                Gravity = GravityFlags.Left | GravityFlags.CenterVertical,
                Text = TaskText(task),
                TextSize = 14,
            };
            row.SetAllCaps(false);
            row.SetTextColor(StatusColour(task.Status));
            row.SetBackgroundColor(Color.ParseColor("#132632"));
            row.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
            row.Click += (_, _) => ShowTask(task.ObjectId);
            var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            parameters.SetMargins(0, Dp(5), 0, Dp(5));
            _taskList.AddView(row, parameters);
        }
        if (tasks.Count == 0)
            _taskList.AddView(Text("No tasks in this filter.", 14, "#9eb0bb"));
    }

    private void SetMode(MobileMode mode)
    {
        _mode = mode;
        RenderTasks();
    }

    private void ConfigureModeUi()
    {
        foreach ((MobileMode mode, Button button) in _modeButtons)
        {
            bool active = mode == _mode;
            button.SetTextColor(Color.ParseColor(active ? "#0b1821" : "#dff7ff"));
            button.SetBackgroundColor(Color.ParseColor(active ? "#55d7ff" : "#132632"));
        }
        if (_filter is not null) _filter.Visibility = _mode == MobileMode.Install ? ViewStates.Visible : ViewStates.Gone;
        if (_exportButton is not null)
        {
            _exportButton.Text = _mode == MobileMode.Install ? "EXPORT EVENTS" : "EXPORT PROJECT";
            _exportButton.Enabled = _document is not null;
        }
    }

    private void RenderSurvey()
    {
        if (_document is null || _taskList is null || _projectTitle is null || _summary is null) return;
        SpaceVolume space = _document.Space;
        _projectTitle.Text = _document.Name.ToUpperInvariant();
        _summary.Text = $"SURVEY · {Metres(space.WidthMillimetres)} × {Metres(space.DepthMillimetres)} × {Metres(space.HeightMillimetres)} · {_document.Openings.Count} OPENING{(_document.Openings.Count == 1 ? string.Empty : "S")}";

        Button editRoom = ActionButton("EDIT ROOM DIMENSIONS");
        editRoom.Click += (_, _) => ShowRoomDialog(create: false);
        _taskList.AddView(editRoom, MatchWrap());
        Button addOpening = ActionButton("+ ADD FIXED OPENING");
        addOpening.Click += (_, _) => ShowOpeningDialog(null);
        _taskList.AddView(addOpening, MatchWrap());

        _taskList.AddView(Text("MEASURED SHELL", 12, "#55d7ff"));
        _taskList.AddView(Text(
            $"✓ LENGTH  {Metres(space.WidthMillimetres)}\n✓ WIDTH   {Metres(space.DepthMillimetres)}\n✓ HEIGHT  {Metres(space.HeightMillimetres)}\n✓ WALL    {space.WallThicknessMillimetres:0} mm\n✓ CEILING {space.CeilingThicknessMillimetres:0} mm",
            14, "#dff7ff"));

        _taskList.AddView(Text("DOORS, WINDOWS AND PENETRATIONS", 12, "#55d7ff"));
        foreach (BuildingOpening opening in _document.Openings.Values.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
        {
            Button row = new(this)
            {
                Gravity = GravityFlags.Left | GravityFlags.CenterVertical,
                Text = $"{opening.Label}\n{Name(opening.Kind.ToString())} · {WallName(opening.Surface)} · {Metres(opening.WidthMillimetres)} × {Metres(opening.HeightMillimetres)}",
                TextSize = 14,
            };
            row.SetAllCaps(false);
            row.SetTextColor(Color.ParseColor("#dff7ff"));
            row.SetBackgroundColor(Color.ParseColor("#132632"));
            row.Click += (_, _) => ShowOpeningDialog(opening.Id);
            var parameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            parameters.SetMargins(0, Dp(5), 0, Dp(5));
            _taskList.AddView(row, parameters);
        }
        if (_document.Openings.Count == 0)
            _taskList.AddView(Text("○ Add every fixed opening before planning.", 14, "#f2c94c"));
        else
            _taskList.AddView(Text("Review every wall and confirm that no fixed opening or service entry is missing.", 13, "#9eb0bb"));
    }

    private void RenderDesign()
    {
        if (_document is null || _taskList is null || _projectTitle is null || _summary is null) return;
        SpaceVolume space = _document.Space;
        _projectTitle.Text = _document.Name.ToUpperInvariant();
        _summary.Text = $"DESIGN · {Metres(space.WidthMillimetres)} × {Metres(space.DepthMillimetres)} · {_document.Openings.Count} FIXED OPENING{(_document.Openings.Count == 1 ? string.Empty : "S")}";

        var viewModes = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        Button plan = PreviewModeButton("2D PLAN", RoomPreviewMode.Plan);
        Button room3D = PreviewModeButton("3D ROOM", RoomPreviewMode.Room3D);
        viewModes.AddView(plan, Weighted());
        viewModes.AddView(room3D, Weighted());
        _taskList.AddView(viewModes, MatchWrap());

        if (_selectedDeviceId is ObjectId selectedId && !_document.Devices.ContainsKey(selectedId))
            _selectedDeviceId = null;
        if (_selectedRouteId is ObjectId selectedRouteId && !_document.Cables.ContainsKey(selectedRouteId) && !_document.Conduits.ContainsKey(selectedRouteId))
            _selectedRouteId = null;
        RoomRouteDraftPreview? routePreview = _routeDraft is null
            ? null
            : new RoomRouteDraftPreview(_routeDraft.Tool, _routeDraft.Points);
        _roomPreview = new RoomPreviewView(
            this,
            _document,
            _previewMode,
            _selectedDeviceId,
            _selectedRouteId,
            routePreview,
            SelectDeviceInPreview,
            SaveDeviceMovement,
            SelectRouteInPreview,
            SaveRouteVertexMovement,
            AddRouteDraftPoint);
        var previewParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(420));
        previewParameters.SetMargins(0, Dp(8), 0, Dp(8));
        _taskList.AddView(_roomPreview, previewParameters);

        var placementActions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        Button place = ActionButton("+ PLACE DEVICE");
        place.Click += (_, _) => ShowDevicePalette();
        placementActions.AddView(place, Weighted());
        _editDeviceButton = ActionButton("EDIT SELECTED");
        _editDeviceButton.Click += (_, _) =>
        {
            if (_selectedDeviceId is ObjectId id) ShowDeviceDialog(id);
            else if (_selectedRouteId is ObjectId routeId) ShowRouteDialog(routeId);
        };
        placementActions.AddView(_editDeviceButton, Weighted());
        _taskList.AddView(placementActions, MatchWrap());

        var routingActions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        Button cable = ActionButton(_routeDraft?.Tool == RoomPreviewTool.CableRoute ? "CABLE ACTIVE" : "+ DRAW CABLE");
        cable.Enabled = _routeDraft is null;
        cable.Click += (_, _) => ShowCablePalette();
        routingActions.AddView(cable, Weighted());
        Button conduit = ActionButton(_routeDraft?.Tool == RoomPreviewTool.ConduitRoute ? "CONDUIT ACTIVE" : "+ DRAW CONDUIT");
        conduit.Enabled = _routeDraft is null;
        conduit.Click += (_, _) => ShowConduitPalette();
        routingActions.AddView(conduit, Weighted());
        _taskList.AddView(routingActions, MatchWrap());

        if (_routeDraft is not null)
        {
            var draftActions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            Button finish = ActionButton("FINISH ROUTE");
            finish.Enabled = _routeDraft.Points.Count >= 2;
            finish.Click += (_, _) => FinishRouteDraft();
            draftActions.AddView(finish, Weighted());
            Button undoPoint = ActionButton("UNDO POINT");
            undoPoint.Enabled = _routeDraft.Points.Count > 0;
            undoPoint.Click += (_, _) => UndoRouteDraftPoint();
            draftActions.AddView(undoPoint, Weighted());
            Button cancelRoute = ActionButton("CANCEL ROUTE");
            cancelRoute.Click += (_, _) => CancelRouteDraft();
            draftActions.AddView(cancelRoute, Weighted());
            _taskList.AddView(draftActions, MatchWrap());
        }
        _deviceSelection = Text(string.Empty, 12, "#f2c94c");
        _taskList.AddView(_deviceSelection);
        UpdateDeviceSelectionUi();

        var previewActions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        Button reset = ActionButton("RESET VIEW");
        reset.Click += (_, _) => _roomPreview?.ResetView();
        previewActions.AddView(reset, Weighted());
        Button survey = ActionButton("EDIT SURVEY");
        survey.Click += (_, _) => SetMode(MobileMode.Survey);
        previewActions.AddView(survey, Weighted());
        _taskList.AddView(previewActions, MatchWrap());

        string gesture = _routeDraft is not null
            ? $"{_routeDraft.Label} · TAP PORTS OR FREE POINTS · {_routeDraft.Points.Count} POINT{(_routeDraft.Points.Count == 1 ? string.Empty : "S")}"
            : _previewMode == RoomPreviewMode.Plan
                ? "DRAG TO PAN · PINCH TO ZOOM"
                : "DRAG TO ROTATE · PINCH TO ZOOM";
        _taskList.AddView(Text(gesture, 12, "#55d7ff"));
        _taskList.AddView(Text(
            _routeDraft is not null
                ? "Green circles are real device ports. Tap a start port, add free bend points, and tap the destination port; a cable finishes automatically when both endpoints are connected."
                : "Tap a device or route to select it. In 2D, drag large yellow handles to move devices or route vertices in 100 mm steps.",
            14, "#dff7ff"));
    }

    private void SelectDeviceInPreview(ObjectId? id)
    {
        _selectedDeviceId = id;
        if (id is not null) _selectedRouteId = null;
        UpdateDeviceSelectionUi();
    }

    private void SelectRouteInPreview(ObjectId? id)
    {
        _selectedRouteId = id;
        if (id is not null) _selectedDeviceId = null;
        UpdateDeviceSelectionUi();
    }

    private void SaveDeviceMovement(ObjectId id)
    {
        if (_document is null || _projectDirectory is null) return;
        _selectedDeviceId = id;
        ProjectToml.Save(_projectDirectory, _document);
        Device device = _document.RequireDevice(id);
        ShowMessage($"Moved {device.Label} · X {device.Position.X:0} · Y {device.Position.Y:0} · Z {device.ElevationMillimetres:0} mm");
        UpdateDeviceSelectionUi();
    }

    private void SaveRouteVertexMovement(ObjectId id, int vertexIndex)
    {
        if (_document is null || _projectDirectory is null) return;
        _selectedRouteId = id;
        _selectedDeviceId = null;
        ProjectToml.Save(_projectDirectory, _document);
        Point3 point = RoutePoints(id)[vertexIndex];
        ShowMessage($"Moved route point {vertexIndex + 1} · X {point.X:0} · Y {point.Y:0} · Z {point.Z:0} mm");
        UpdateDeviceSelectionUi();
    }

    private void UpdateDeviceSelectionUi()
    {
        Device? selectedDevice = null;
        if (_document is not null && _selectedDeviceId is ObjectId id)
            _document.Devices.TryGetValue(id, out selectedDevice);
        CableRoute? selectedCable = null;
        Conduit? selectedConduit = null;
        if (_document is not null && _selectedRouteId is ObjectId routeId)
        {
            _document.Cables.TryGetValue(routeId, out selectedCable);
            _document.Conduits.TryGetValue(routeId, out selectedConduit);
        }
        if (_editDeviceButton is not null) _editDeviceButton.Enabled = selectedDevice is not null || selectedCable is not null || selectedConduit is not null;
        if (_deviceSelection is not null)
        {
            ProjectAnalysis? analysis = _document is null ? null : ProjectAnalyzer.Analyze(_document);
            _deviceSelection.Text = selectedDevice is not null
                ? $"SELECTED · {selectedDevice.Label} · {Name(selectedDevice.Kind.ToString())} · X {selectedDevice.Position.X:0} · Y {selectedDevice.Position.Y:0} · Z {selectedDevice.ElevationMillimetres:0} mm"
                : selectedCable is not null
                    ? $"SELECTED · {selectedCable.Label} · {CableKindLabel(selectedCable.Kind)} · {selectedCable.Route.LengthMillimetres / 1000:0.##} m · {CablePlanningSummary(selectedCable, analysis!)}"
                    : selectedConduit is not null
                        ? $"SELECTED · {selectedConduit.Label} · Ø {selectedConduit.NominalDiameterMillimetres ?? selectedConduit.InnerDiameterMillimetres:0.#} mm · {selectedConduit.Route.LengthMillimetres / 1000:0.##} m · {ConduitPlanningSummary(selectedConduit, analysis!)}"
                        : "SELECT A DEVICE OR ROUTE IN THE 2D PLAN";
        }
    }

    private Button PreviewModeButton(string label, RoomPreviewMode mode)
    {
        Button button = ActionButton(label);
        bool active = mode == _previewMode;
        button.SetTextColor(Color.ParseColor(active ? "#0b1821" : "#dff7ff"));
        button.SetBackgroundColor(Color.ParseColor(active ? "#55d7ff" : "#132632"));
        button.Click += (_, _) =>
        {
            _previewMode = mode;
            RenderTasks();
        };
        return button;
    }

    private void ShowCablePalette()
    {
        if (_routeDraft is not null) return;
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Cable type");
        builder.SetItems(MobileCableKinds.Select(CableKindLabel).ToArray(), (_, args) =>
        {
            CableKind kind = MobileCableKinds[args.Which];
            int number = (_document?.Cables.Count ?? 0) + 1;
            _routeDraft = new RouteDraftState(RoomPreviewTool.CableRoute, $"{CableKindLabel(kind)}-{number:00}") { CableKind = kind };
            _previewMode = RoomPreviewMode.Plan;
            _selectedDeviceId = null;
            _selectedRouteId = null;
            ShowMessage("Cable route started. Tap its first port.");
            RenderTasks();
        });
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        builder.Show();
    }

    private void ShowConduitPalette()
    {
        if (_routeDraft is not null) return;
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Conduit size");
        builder.SetItems(MobileConduitProfiles.Select(profile => $"{profile.NominalMillimetres:0} MM · INNER {profile.InnerMillimetres:0.#} MM").ToArray(), (_, args) =>
        {
            ConduitProfile profile = MobileConduitProfiles[args.Which];
            int number = (_document?.Conduits.Count ?? 0) + 1;
            _routeDraft = new RouteDraftState(RoomPreviewTool.ConduitRoute, $"RÖR-{number:00}") { Conduit = profile };
            _previewMode = RoomPreviewMode.Plan;
            _selectedDeviceId = null;
            _selectedRouteId = null;
            ShowMessage($"{profile.NominalMillimetres:0} mm conduit started. Tap ports or free points.");
            RenderTasks();
        });
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        builder.Show();
    }

    private void AddRouteDraftPoint(Point3 requested, PortReference? port)
    {
        if (_document is null || _routeDraft is null) return;
        Point3 point;
        if (port is PortReference reference)
        {
            point = PortPosition(reference);
            if (_routeDraft.Tool == RoomPreviewTool.CableRoute)
            {
                if (_routeDraft.Points.Count == 0) _routeDraft.From = reference;
                else _routeDraft.To = reference;
            }
        }
        else
        {
            SpaceVolume space = _document.Space;
            point = new Point3(
                Math.Clamp(Math.Round(requested.X / 100) * 100, space.Origin.X, space.Origin.X + space.WidthMillimetres),
                Math.Clamp(Math.Round(requested.Y / 100) * 100, space.Origin.Y, space.Origin.Y + space.DepthMillimetres),
                Math.Round(requested.Z / 100) * 100);
        }
        if (_routeDraft.Points.Count > 0 && _routeDraft.Points[^1] == point)
        {
            ShowMessage("Choose a different route point.", error: true);
            return;
        }
        _routeDraft.Points.Add(point);
        if (_routeDraft.Tool == RoomPreviewTool.CableRoute && _routeDraft.From is not null && _routeDraft.To is not null && _routeDraft.Points.Count >= 2)
        {
            FinishRouteDraft();
            return;
        }
        ShowMessage(port is PortReference snapped
            ? $"Snapped to {DevicePortLabel(snapped)}."
            : $"Added bend point {_routeDraft.Points.Count}.");
        RenderTasks();
    }

    private void FinishRouteDraft()
    {
        if (_document is null || _projectDirectory is null || _routeDraft is null || _routeDraft.Points.Count < 2) return;
        try
        {
            var route = new Polyline(_routeDraft.Points);
            ObjectId id = ObjectId.New();
            if (_routeDraft.Tool == RoomPreviewTool.CableRoute)
            {
                CableKind kind = _routeDraft.CableKind;
                new AddCableCommand(new CableRoute(
                    id,
                    _routeDraft.Label,
                    kind,
                    route,
                    _routeDraft.From,
                    _routeDraft.To,
                    Electrical: ElectricalCableProfiles.Infer(kind))).Apply(_document);
            }
            else
            {
                ConduitProfile profile = _routeDraft.Conduit ?? throw new InvalidOperationException("Conduit profile is missing.");
                new AddConduitCommand(new Conduit(
                    id,
                    _routeDraft.Label,
                    profile.InnerMillimetres,
                    route,
                    InstallationMethod.Concealed,
                    profile.NominalMillimetres,
                    profile.ProductId)).Apply(_document);
            }
            string label = _routeDraft.Label;
            _routeDraft = null;
            _selectedRouteId = id;
            _selectedDeviceId = null;
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage($"Created {label}.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not finish route: {exception.Message}", error: true);
        }
    }

    private void UndoRouteDraftPoint()
    {
        if (_routeDraft is null || _routeDraft.Points.Count == 0) return;
        if (_routeDraft.To is not null) _routeDraft.To = null;
        _routeDraft.Points.RemoveAt(_routeDraft.Points.Count - 1);
        if (_routeDraft.Points.Count == 0) _routeDraft.From = null;
        ShowMessage("Removed the last route point.");
        RenderTasks();
    }

    private void CancelRouteDraft()
    {
        _routeDraft = null;
        ShowMessage("Route cancelled.");
        RenderTasks();
    }

    private Point3 PortPosition(PortReference reference)
    {
        if (_document is null) throw new InvalidOperationException("No project is loaded.");
        Point2 position = DocumentHandleEditor.RequirePosition(
            _document,
            new EditHandleId(reference.DeviceId, EditHandleKind.Port, Name: reference.PortId));
        return new Point3(position.X, position.Y, _document.RequireDevice(reference.DeviceId).ElevationMillimetres);
    }

    private string DevicePortLabel(PortReference reference) =>
        $"{_document!.RequireDevice(reference.DeviceId).Label}:{reference.PortId}";

    private void ShowRouteDialog(ObjectId id)
    {
        if (_document is null) return;
        CableRoute? cable = _document.Cables.TryGetValue(id, out CableRoute? foundCable) ? foundCable : null;
        Conduit? conduit = _document.Conduits.TryGetValue(id, out Conduit? foundConduit) ? foundConduit : null;
        if (cable is null && conduit is null) return;
        var form = Form();
        EditText label = NumberOrTextField(cable?.Label ?? conduit!.Label, numeric: false);
        AddField(form, "LABEL", label);
        var profile = new Spinner(this);
        ObjectId?[] conduitOptions = [null];
        Spinner? conduitPicker = null;
        ProjectAnalysis analysis = ProjectAnalyzer.Analyze(_document);
        if (cable is not null)
        {
            profile.Adapter = SpinnerAdapter(MobileCableKinds.Select(CableKindLabel).ToArray());
            profile.SetSelection(Math.Max(Array.IndexOf(MobileCableKinds, cable.Kind), 0));
            AddField(form, "CABLE TYPE", profile);
            Conduit[] conduits = _document.Conduits.Values.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
            conduitOptions = [null, .. conduits.Select(item => (ObjectId?)item.Id)];
            string[] conduitLabels = ["NO CONDUIT", .. conduits.Select(item => $"{item.Label} · Ø {item.NominalDiameterMillimetres ?? item.InnerDiameterMillimetres:0.#} MM")];
            conduitPicker = new Spinner(this) { Adapter = SpinnerAdapter(conduitLabels) };
            int conduitIndex = Array.FindIndex(conduitOptions, item => item == cable.ConduitId);
            conduitPicker.SetSelection(Math.Max(conduitIndex, 0));
            AddField(form, "RUN CABLE IN", conduitPicker);
            form.AddView(Text(CablePlanningSummary(cable, analysis), 12, "#f2c94c"));
        }
        else
        {
            Conduit selectedConduit = conduit!;
            profile.Adapter = SpinnerAdapter(MobileConduitProfiles.Select(item => $"{item.NominalMillimetres:0} MM").ToArray());
            int index = Array.FindIndex(MobileConduitProfiles, item => item.NominalMillimetres == selectedConduit.NominalDiameterMillimetres);
            profile.SetSelection(Math.Max(index, 0));
            AddField(form, "CONDUIT SIZE", profile);
            ConduitFill fill = analysis.ConduitFills.Single(item => item.ConduitId == selectedConduit.Id);
            CableRoute[] contents = _document.Cables.Values.Where(item => item.ConduitId == selectedConduit.Id)
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ToArray();
            form.AddView(Text(contents.Length == 0 ? "CONTENTS · EMPTY" : $"CONTENTS · {string.Join(", ", contents.Select(item => item.Label))}", 12, "#dff7ff"));
            form.AddView(Text($"PHYSICAL FILL · {fill.FillRatio:P1} / 40% PLANNING LIMIT", 12, fill.FillRatio > 0.40 ? "#ff7b72" : "#66e6a5"));
            var fillBar = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
            {
                Max = 100,
                Progress = Math.Clamp((int)Math.Round(fill.FillRatio * 100), 0, 100),
            };
            form.AddView(fillBar, MatchWrap());
            form.AddView(Text(ThermalSummary(selectedConduit.Id, analysis), 12, ThermalColour(selectedConduit.Id, analysis)));
        }
        form.AddView(Text($"ROUTE LENGTH · {(cable?.Route ?? conduit!.Route).LengthMillimetres / 1000:0.##} m", 12, "#9eb0bb"));
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(cable is not null ? "Edit cable" : "Edit conduit");
        builder.SetView(form);
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        builder.SetNeutralButton("DELETE", (_, _) => DeleteRoute(id));
        builder.SetPositiveButton("SAVE", (_, _) => SaveRouteDetails(
            id,
            label.Text ?? string.Empty,
            profile.SelectedItemPosition,
            conduitPicker is null ? null : conduitOptions[conduitPicker.SelectedItemPosition]));
        builder.Show();
    }

    private void SaveRouteDetails(ObjectId id, string label, int profileIndex, ObjectId? conduitId)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Route label is required.");
            new SetObjectLabelCommand(id, label.Trim()).Apply(_document);
            if (_document.Cables.ContainsKey(id))
            {
                CableKind selectedKind = MobileCableKinds[profileIndex];
                if (_document.RequireCable(id).Kind != selectedKind)
                    new SetCableKindCommand(id, selectedKind).Apply(_document);
                if (_document.RequireCable(id).ConduitId != conduitId)
                    new SetCableConduitCommand(id, conduitId).Apply(_document);
            }
            else
            {
                ConduitProfile profile = MobileConduitProfiles[profileIndex];
                new SetConduitProductCommand(id, profile.ToProduct()).Apply(_document);
            }
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage($"Updated {label.Trim()}.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not update route: {exception.Message}", error: true);
        }
    }

    private void DeleteRoute(ObjectId id)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            string label = _document.Cables.TryGetValue(id, out CableRoute? cable) ? cable.Label : _document.RequireConduit(id).Label;
            new DeleteObjectCommand(id).Apply(_document);
            _selectedRouteId = null;
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage($"Deleted {label}.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not delete route: {exception.Message}", error: true);
        }
    }

    private IReadOnlyList<Point3> RoutePoints(ObjectId id)
    {
        if (_document!.Cables.TryGetValue(id, out CableRoute? cable)) return cable.Route.SpatialPoints;
        return _document.RequireConduit(id).Route.SpatialPoints;
    }

    private static string CableKindLabel(CableKind kind) => kind switch
    {
        CableKind.Cat6 => "CAT6",
        CableKind.Cat6A => "CAT6A",
        CableKind.Mains3G25 => "EKRK 3G2.5",
        CableKind.Mains5G6 => "EKRK 5G6",
        CableKind.FibreDuplex => "FIBRE DUPLEX",
        CableKind.Coax => "COAX",
        CableKind.LowVoltageDc => "12 V DC",
        _ => "CUSTOM",
    };

    private string CablePlanningSummary(CableRoute cable, ProjectAnalysis analysis)
    {
        if (cable.ConduitId is not ObjectId conduitId) return $"NO CONDUIT · {CableThermalSummary(cable.Id, analysis)}";
        Conduit conduit = _document!.RequireConduit(conduitId);
        ConduitFill fill = analysis.ConduitFills.Single(item => item.ConduitId == conduitId);
        return $"{conduit.Label} · FILL {fill.FillRatio:P1} · {CableThermalSummary(cable.Id, analysis)}";
    }

    private string ConduitPlanningSummary(Conduit conduit, ProjectAnalysis analysis)
    {
        ConduitFill fill = analysis.ConduitFills.Single(item => item.ConduitId == conduit.Id);
        ConduitLoad load = analysis.ConduitLoads.Single(item => item.ConduitId == conduit.Id);
        string count = load.CableCount == 1 ? "1 CABLE" : $"{load.CableCount} CABLES";
        return $"{count} · FILL {fill.FillRatio:P1} · {ThermalSummary(conduit.Id, analysis)}";
    }

    private string ThermalSummary(ObjectId conduitId, ProjectAnalysis analysis)
    {
        ConduitLoad load = analysis.ConduitLoads.Single(item => item.ConduitId == conduitId);
        if (load.PowerCircuitCount == 0) return "THERMAL N/A";
        ElectricalDesignCheck[] checks = analysis.ElectricalDesignChecks
            .Where(check => _document!.RequireCable(check.CableId).ConduitId == conduitId).ToArray();
        if (checks.Any(check => check.Status == DesignCheckStatus.Warning)) return "THERMAL WARNING";
        if (load.PowerCircuitCount > 1) return "THERMAL VERIFY GROUPING";
        return checks.Length < load.PowerCircuitCount || checks.Any(check => check.Status == DesignCheckStatus.Unknown)
            ? "THERMAL UNKNOWN"
            : "THERMAL PASS";
    }

    private string CableThermalSummary(ObjectId cableId, ProjectAnalysis analysis)
    {
        ElectricalDesignCheck? check = analysis.ElectricalDesignChecks.FirstOrDefault(item => item.CableId == cableId);
        return check?.Status switch
        {
            DesignCheckStatus.Pass => "THERMAL PASS",
            DesignCheckStatus.Warning => "THERMAL WARNING",
            DesignCheckStatus.Unknown => "THERMAL UNKNOWN",
            _ => "THERMAL N/A",
        };
    }

    private string ThermalColour(ObjectId conduitId, ProjectAnalysis analysis) => ThermalSummary(conduitId, analysis) switch
    {
        "THERMAL PASS" or "THERMAL N/A" => "#66e6a5",
        "THERMAL WARNING" => "#ff7b72",
        _ => "#f2c94c",
    };

    private void ShowDevicePalette()
    {
        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Place installation object");
        builder.SetItems(PlacementKinds.Select(kind => Name(kind.ToString())).ToArray(), (_, args) =>
            ShowDeviceDialog(null, PlacementKinds[args.Which]));
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        builder.Show();
    }

    private void ShowDeviceDialog(ObjectId? deviceId, DeviceKind? placementKind = null)
    {
        if (_document is null) return;
        Device? device = deviceId is ObjectId id ? _document.RequireDevice(id) : null;
        DeviceKind initialKind = device?.Kind ?? placementKind ?? DeviceKind.JunctionBox;
        (MountingSurface defaultSurface, Point3 defaultPosition) = DefaultDevicePosition(initialKind, _document.Space);
        MountingSurface initialSurface = device?.MountingSurface ?? defaultSurface;
        Point3 initialPosition = device is null
            ? defaultPosition
            : new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres);

        var form = Form();
        EditText label = NumberOrTextField(device?.Label ?? NextDeviceLabel(initialKind), numeric: false);
        AddField(form, "LABEL", label);
        var kind = new Spinner(this) { Adapter = SpinnerAdapter(PlacementKinds.Select(value => Name(value.ToString())).ToArray()) };
        int kindIndex = Array.IndexOf(PlacementKinds, initialKind);
        kind.SetSelection(Math.Max(kindIndex, 0));
        kind.Enabled = device is null;
        AddField(form, device is null ? "TYPE" : "TYPE (FIXED AFTER PLACEMENT)", kind);
        var surface = new Spinner(this) { Adapter = SpinnerAdapter(DeviceSurfaces.Select(SurfaceName).ToArray()) };
        surface.SetSelection(Math.Max(Array.IndexOf(DeviceSurfaces, initialSurface), 0));
        AddField(form, "MOUNTING SURFACE", surface);
        EditText x = NumberOrTextField(initialPosition.X.ToString("0.###", CultureInfo.InvariantCulture));
        EditText y = NumberOrTextField(initialPosition.Y.ToString("0.###", CultureInfo.InvariantCulture));
        EditText z = NumberOrTextField(initialPosition.Z.ToString("0.###", CultureInfo.InvariantCulture));
        AddField(form, "X POSITION (MM)", x);
        AddField(form, "Y POSITION (MM)", y);
        AddField(form, "Z / HEIGHT (MM)", z);
        form.AddView(Text("The selected mounting surface constrains the final position to the room shell.", 12, "#9eb0bb"));

        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(device is null ? $"Place {Name(initialKind.ToString())}" : $"Edit {device.Label}");
        builder.SetView(InScroll(form));
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        if (device is not null) builder.SetNeutralButton("DELETE", (_, _) => DeleteDevice(device.Id));
        builder.SetPositiveButton("SAVE", (_, _) => SaveDevice(
            device?.Id,
            label.Text ?? string.Empty,
            kind.SelectedItemPosition,
            surface.SelectedItemPosition,
            x.Text!,
            y.Text!,
            z.Text!));
        builder.Show();
    }

    private void SaveDevice(ObjectId? deviceId, string label, int kindIndex, int surfaceIndex, string xText, string yText, string zText)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Device label is required.");
            DeviceKind kind = deviceId is ObjectId existingId
                ? _document.RequireDevice(existingId).Kind
                : PlacementKinds[kindIndex];
            MountingSurface surface = DeviceSurfaces[surfaceIndex];
            var requested = new Point3(ParseCoordinate(xText, "X"), ParseCoordinate(yText, "Y"), ParseNonNegative(zText, "Z / height"));
            Point3 constrained = MountingSurfaceGeometry.Constrain(_document.Space, surface, requested);
            ObjectId id;
            if (deviceId is ObjectId currentId)
            {
                id = currentId;
                new SetObjectLabelCommand(id, label.Trim()).Apply(_document);
                new SetDeviceMountingSurfaceCommand(id, surface).Apply(_document);
                DocumentHandleEditor.SetSpatialPosition(_document, new EditHandleId(id, EditHandleKind.Move), constrained);
            }
            else
            {
                id = ObjectId.New();
                var device = new Device(id, kind, constrained.Plan, label.Trim(), DefaultPorts(kind), constrained.Z, surface);
                new AddDeviceCommand(device).Apply(_document);
            }
            _selectedDeviceId = id;
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage(deviceId is null ? $"Placed {label.Trim()}." : $"Updated {label.Trim()}.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not save device: {exception.Message}", error: true);
        }
    }

    private void DeleteDevice(ObjectId id)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            string label = _document.RequireDevice(id).Label;
            new DeleteObjectCommand(id).Apply(_document);
            if (_selectedDeviceId == id) _selectedDeviceId = null;
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage($"Deleted {label}.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not delete device: {exception.Message}", error: true);
        }
    }

    private string NextDeviceLabel(DeviceKind kind)
    {
        int number = (_document?.Devices.Values.Count(device => device.Kind == kind) ?? 0) + 1;
        return $"{DevicePrefix(kind)}-{number:00}";
    }

    private static (MountingSurface Surface, Point3 Position) DefaultDevicePosition(DeviceKind kind, SpaceVolume space)
    {
        double centreX = space.Origin.X + space.WidthMillimetres / 2;
        double centreY = space.Origin.Y + space.DepthMillimetres / 2;
        return kind switch
        {
            DeviceKind.Light or DeviceKind.AccessPoint => (MountingSurface.CeilingInterior, new Point3(centreX, centreY, space.HeightMillimetres)),
            DeviceKind.Outlet => (MountingSurface.NorthWallInterior, new Point3(centreX, space.Origin.Y, 300)),
            DeviceKind.Camera => (MountingSurface.NorthWallInterior, new Point3(centreX, space.Origin.Y, Math.Min(2200, space.HeightMillimetres))),
            DeviceKind.DistributionBoard => (MountingSurface.NorthWallInterior, new Point3(centreX, space.Origin.Y, Math.Min(1200, space.HeightMillimetres))),
            _ => (MountingSurface.NorthWallInterior, new Point3(centreX, space.Origin.Y, Math.Min(1500, space.HeightMillimetres))),
        };
    }

    private static Port[] DefaultPorts(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => [new Port("feed", PortKind.MainsPower, new Point2(750, 0))],
        DeviceKind.Outlet => [new Port("power", PortKind.MainsPower, new Point2(0, 0))],
        DeviceKind.Camera => [new Port("eth0", PortKind.EthernetPoe, new Point2(-350, 0))],
        DeviceKind.PoeSwitch => [new Port("uplink", PortKind.Ethernet, new Point2(-650, 0)), new Port("port-1", PortKind.EthernetPoe, new Point2(0, -350))],
        DeviceKind.AccessPoint => [new Port("eth0", PortKind.EthernetPoe, new Point2(0, 300))],
        DeviceKind.Light => [new Port("power", PortKind.MainsPower, new Point2(0, 0))],
        _ => [new Port("generic", PortKind.Generic, new Point2(0, 0))],
    };

    private static string DevicePrefix(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => "CENTRAL",
        DeviceKind.JunctionBox => "DOSA",
        DeviceKind.Outlet => "UTT",
        DeviceKind.Camera => "KAM",
        DeviceKind.PoeSwitch => "POE-SW",
        DeviceKind.AccessPoint => "AP",
        DeviceKind.Light => "LAMPA",
        _ => "APPARAT",
    };

    private static string SurfaceName(MountingSurface surface) => Name(surface.ToString());

    private void ShowRoomDialog(bool create)
    {
        SpaceVolume current = create || _document is null
            ? new SpaceVolume(new Point2(0, 0), 6000, 4000, 2400, 200, 250)
            : _document.Space;
        var form = Form();
        EditText name = NumberOrTextField(create ? "GARAGE" : _document!.Name, numeric: false);
        if (create) AddField(form, "PROJECT / ROOM NAME", name);
        EditText length = NumberOrTextField(current.WidthMillimetres.ToString("0.###", CultureInfo.InvariantCulture));
        EditText width = NumberOrTextField(current.DepthMillimetres.ToString("0.###", CultureInfo.InvariantCulture));
        EditText height = NumberOrTextField(current.HeightMillimetres.ToString("0.###", CultureInfo.InvariantCulture));
        EditText wall = NumberOrTextField(current.WallThicknessMillimetres.ToString("0.###", CultureInfo.InvariantCulture));
        EditText ceiling = NumberOrTextField(current.CeilingThicknessMillimetres.ToString("0.###", CultureInfo.InvariantCulture));
        AddField(form, "LENGTH (MM)", length);
        AddField(form, "WIDTH (MM)", width);
        AddField(form, "WALL HEIGHT (MM)", height);
        AddField(form, "WALL THICKNESS (MM)", wall);
        AddField(form, "CEILING THICKNESS (MM)", ceiling);

        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(create ? "New measured room" : "Room dimensions");
        builder.SetView(InScroll(form));
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        builder.SetPositiveButton("SAVE", (_, _) => SaveRoom(create, name.Text ?? string.Empty, length.Text!, width.Text!, height.Text!, wall.Text!, ceiling.Text!));
        builder.Show();
    }

    private void SaveRoom(bool create, string name, string length, string width, string height, string wall, string ceiling)
    {
        try
        {
            if (create && string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Room name is required.");
            SpaceVolume origin = create || _document is null
                ? new SpaceVolume(new Point2(0, 0), 1, 1, 1)
                : _document.Space;
            var volume = new SpaceVolume(origin.Origin, ParseLength(length), ParseLength(width), ParseLength(height), ParseLength(wall), ParseLength(ceiling)).Validate();
            if (create)
            {
                _document = new ProjectDocument(name.Trim());
                _projectDirectory = Path.Combine(FilesDir!.AbsolutePath, "projects", $"survey-{Guid.NewGuid():N}.eutherwire");
                Directory.CreateDirectory(_projectDirectory);
                Preferences.Edit()!.PutString(ActiveProjectKey, _projectDirectory)!.Apply();
            }
            new SetSpaceVolumeCommand(volume).Apply(_document!);
            ProjectToml.Save(_projectDirectory!, _document!);
            ShowMessage(create ? "Measured room created." : "Room dimensions saved.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not save room: {exception.Message}", error: true);
        }
    }

    private void ShowOpeningDialog(ObjectId? openingId)
    {
        if (_document is null) return;
        BuildingOpening? opening = openingId is ObjectId id ? _document.RequireOpening(id) : null;
        var form = Form();
        EditText label = NumberOrTextField(opening?.Label ?? "DOOR", numeric: false);
        AddField(form, "LABEL", label);

        var kind = new Spinner(this);
        string[] kinds = Enum.GetNames<OpeningKind>().Select(Name).ToArray();
        kind.Adapter = SpinnerAdapter(kinds);
        if (opening is not null) kind.SetSelection(Array.IndexOf(Enum.GetValues<OpeningKind>(), opening.Kind));
        else kind.SetSelection(Array.IndexOf(Enum.GetValues<OpeningKind>(), OpeningKind.Door));
        AddField(form, "TYPE", kind);

        var wall = new Spinner(this);
        string[] walls = ["NORTH WALL", "EAST WALL", "SOUTH WALL", "WEST WALL"];
        wall.Adapter = SpinnerAdapter(walls);
        if (opening is not null) wall.SetSelection(WallIndex(opening.Surface));
        AddField(form, "WALL", wall);

        double openingWidth = opening?.WidthMillimetres ?? 900;
        double openingHeight = opening?.HeightMillimetres ?? 2100;
        EditText offset = NumberOrTextField((opening is null ? 0 : OpeningOffset(opening, _document.Space)).ToString("0.###", CultureInfo.InvariantCulture));
        EditText sill = NumberOrTextField((opening is null ? 0 : opening.Centre.Z - openingHeight / 2).ToString("0.###", CultureInfo.InvariantCulture));
        EditText width = NumberOrTextField(openingWidth.ToString("0.###", CultureInfo.InvariantCulture));
        EditText height = NumberOrTextField(openingHeight.ToString("0.###", CultureInfo.InvariantCulture));
        AddField(form, "OFFSET FROM WALL START (MM)", offset);
        AddField(form, "SILL / BOTTOM ABOVE FLOOR (MM)", sill);
        AddField(form, "OPENING WIDTH (MM)", width);
        AddField(form, "OPENING HEIGHT (MM)", height);

        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(opening is null ? "Add fixed opening" : "Edit fixed opening");
        builder.SetView(InScroll(form));
        builder.SetNegativeButton("CANCEL", (_, _) => { });
        if (opening is not null)
            builder.SetNeutralButton("DELETE", (_, _) => DeleteOpening(opening.Id));
        builder.SetPositiveButton("SAVE", (_, _) => SaveOpening(opening?.Id, label.Text ?? string.Empty,
            kind.SelectedItemPosition, wall.SelectedItemPosition, offset.Text!, sill.Text!, width.Text!, height.Text!));
        builder.Show();
    }

    private void SaveOpening(ObjectId? openingId, string label, int kindIndex, int wallIndex, string offsetText, string sillText, string widthText, string heightText)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Opening label is required.");
            OpeningKind kind = Enum.GetValues<OpeningKind>()[kindIndex];
            MountingSurface surface = WallSurface(wallIndex);
            double offset = ParseNonNegative(offsetText, "Offset");
            double sill = ParseNonNegative(sillText, "Sill height");
            double width = ParseLength(widthText);
            double height = ParseLength(heightText);
            double wallLength = BuildingOpeningGeometry.UsesXAxis(surface) ? _document.Space.WidthMillimetres : _document.Space.DepthMillimetres;
            if (offset + width > wallLength) throw new ArgumentException("Opening extends beyond the selected wall.");
            if (sill + height > _document.Space.HeightMillimetres) throw new ArgumentException("Opening extends above the ceiling.");
            Point3 centre = OpeningCentre(_document.Space, surface, offset, sill, width, height);
            if (openingId is ObjectId id)
            {
                new SetOpeningGeometryCommand(id, kind, surface, centre, width, height).Apply(_document);
                _document.RequireOpening(id).Label = label.Trim();
            }
            else
            {
                var opening = new BuildingOpening(ObjectId.New(), kind, surface, centre, width, height, label.Trim());
                new AddOpeningCommand(opening).Apply(_document);
            }
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage("Fixed opening saved.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not save opening: {exception.Message}", error: true);
        }
    }

    private void DeleteOpening(ObjectId id)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            new DeleteObjectCommand(id).Apply(_document);
            ProjectToml.Save(_projectDirectory, _document);
            ShowMessage("Opening deleted.");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not delete opening: {exception.Message}", error: true);
        }
    }

    private void BeginProjectExport()
    {
        if (_projectDirectory is null || _document is null)
        {
            ShowMessage("Measure or import a project first.");
            return;
        }
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/zip");
        intent.PutExtra(Intent.ExtraTitle, $"{SafeFileName(_document.Name)}.eutherwire-snapshot");
        StartActivityForResult(intent, ExportSnapshotRequest);
    }

    private void ExportProjectSnapshot(Android.Net.Uri uri)
    {
        if (_projectDirectory is null) return;
        string temporary = Path.Combine(CacheDir!.AbsolutePath, $"export-{Guid.NewGuid():N}.eutherwire-snapshot");
        try
        {
            ProjectToml.Save(_projectDirectory, _document!);
            ProjectSnapshotInfo info = ProjectSnapshot.Export(_projectDirectory, temporary);
            using Stream input = File.OpenRead(temporary);
            using Stream output = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("Could not create snapshot file.");
            input.CopyTo(output);
            ShowMessage($"Exported project snapshot · {info.FileCount} files");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void ShowTask(ObjectId objectId)
    {
        if (_document is null) return;
        InstallationTask task = ProjectAnalyzer.Analyze(_document).InstallationTasks.Single(item => item.ObjectId == objectId);
        InstallationRecord record = _document.RequireInstallationRecord(objectId);
        var form = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
        };
        form.SetPadding(Dp(20), Dp(8), Dp(20), 0);
        form.AddView(Text($"{task.Kind.ToString().ToUpperInvariant()} · {objectId}", 12, "#55d7ff"));
        form.AddView(Text(task.Label, 20, "#ffffff"));
        form.AddView(Text(Details(task), 13, "#9eb0bb"));

        var statusSpinner = new Spinner(this);
        string[] statuses = Enum.GetNames<InstallationStatus>().Select(Name).ToArray();
        var statusAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, statuses);
        statusAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        statusSpinner.Adapter = statusAdapter;
        statusSpinner.SetSelection(Array.IndexOf(Enum.GetValues<InstallationStatus>(), record.Status));
        form.AddView(Label("STATUS"));
        form.AddView(statusSpinner);

        var note = new EditText(this)
        {
            Text = record.Note ?? string.Empty,
            Hint = "What was installed or changed?",
            Gravity = GravityFlags.Top,
        };
        note.SetMinLines(2);
        note.SetTextColor(Color.White);
        note.SetHintTextColor(Color.ParseColor("#718590"));
        form.AddView(Label("FIELD NOTE"));
        form.AddView(note);

        EditText? actualLength = null;
        if (task.Kind is InstallationObjectKind.Cable or InstallationObjectKind.Conduit)
        {
            actualLength = new EditText(this)
            {
                Text = record.ActualLengthMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                Hint = "millimetres",
                InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal,
            };
            actualLength.SetTextColor(Color.White);
            actualLength.SetHintTextColor(Color.ParseColor("#718590"));
            form.AddView(Label("ACTUAL LENGTH (MM)"));
            form.AddView(actualLength);
        }

        var scroll = new ScrollView(this);
        scroll.AddView(form);
        var dialogBuilder = new AlertDialog.Builder(this);
        dialogBuilder.SetView(scroll);
        dialogBuilder.SetNegativeButton("CANCEL", (_, _) => { });
        dialogBuilder.SetPositiveButton("SAVE OFFLINE", (_, _) => SaveTask(objectId, statusSpinner, note, actualLength));
        AlertDialog dialog = dialogBuilder.Create() ?? throw new InvalidOperationException("Could not create task dialog.");
        dialog.Show();
        dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize);
    }

    private void SaveTask(ObjectId objectId, Spinner statusSpinner, EditText note, EditText? actualLength)
    {
        if (_document is null || _projectDirectory is null) return;
        try
        {
            InstallationRecord current = _document.RequireInstallationRecord(objectId);
            InstallationStatus status = Enum.GetValues<InstallationStatus>()[statusSpinner.SelectedItemPosition];
            double? length = string.IsNullOrWhiteSpace(actualLength?.Text)
                ? null
                : ParseLength(actualLength.Text);
            var desired = new InstallationRecord(objectId, status, note: note.Text,
                actualPosition: current.ActualPosition,
                actualLengthMillimetres: actualLength is null ? current.ActualLengthMillimetres : length,
                testResult: current.TestResult,
                photoReferences: current.PhotoReferences,
                revision: current.Revision);
            InstallationEvent installationEvent = InstallationEvent.CreateSetRecord(current, desired, AuthorDeviceId);
            string pendingPath = Path.Combine(_projectDirectory, PendingJournalName);
            InstallationJournal.AppendUnique(pendingPath, [installationEvent]);
            InstallationEventApplyResult result = InstallationJournal.Apply(_document, installationEvent);
            if (result.Status != InstallationEventApplyStatus.Applied) throw new InvalidOperationException(result.Message);
            ProjectToml.Save(_projectDirectory, _document);
            InstallationJournal.AppendUnique(Path.Combine(_projectDirectory, InstallationJournal.FileName), [installationEvent]);
            ShowMessage($"Saved offline · revision {_document.RequireInstallationRecord(objectId).Revision}");
            RenderTasks();
        }
        catch (Exception exception)
        {
            ShowMessage($"Could not save task: {exception.Message}", error: true);
        }
    }

    private void RecoverPendingEvents()
    {
        if (_document is null || _projectDirectory is null) return;
        IReadOnlyList<InstallationEvent> pending = PendingEvents();
        bool changed = false;
        foreach (InstallationEvent installationEvent in pending)
        {
            InstallationEventApplyResult result = InstallationJournal.Apply(_document, installationEvent);
            changed |= result.Status == InstallationEventApplyStatus.Applied;
        }
        if (changed) ProjectToml.Save(_projectDirectory, _document);
        if (pending.Count > 0)
            InstallationJournal.AppendUnique(Path.Combine(_projectDirectory, InstallationJournal.FileName), pending);
    }

    private void BeginImport()
    {
        void OpenPicker()
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            StartActivityForResult(intent, ImportSnapshotRequest);
        }

        if (PendingEvents().Count == 0)
        {
            OpenPicker();
            return;
        }
        var importBuilder = new AlertDialog.Builder(this);
        importBuilder.SetTitle("Pending field work");
        importBuilder.SetMessage("This project has events that have not been exported. Import another snapshot anyway?");
        importBuilder.SetNegativeButton("CANCEL", (_, _) => { });
        importBuilder.SetPositiveButton("IMPORT", (_, _) => OpenPicker());
        importBuilder.Show();
    }

    private void BeginExport()
    {
        if (_projectDirectory is null || PendingEvents().Count == 0)
        {
            ShowMessage("There are no pending events to export.");
            return;
        }
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/x-ndjson");
        intent.PutExtra(Intent.ExtraTitle, $"eutherwire-{DateTime.UtcNow:yyyyMMdd-HHmm}-events.jsonl");
        StartActivityForResult(intent, ExportEventsRequest);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is null) return;
        try
        {
            if (requestCode == ImportSnapshotRequest) ImportSnapshot(data.Data);
            if (requestCode == ExportEventsRequest) ExportPendingEvents(data.Data);
            if (requestCode == ExportSnapshotRequest) ExportProjectSnapshot(data.Data);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, error: true);
        }
    }

    private void ImportSnapshot(Android.Net.Uri uri)
    {
        string temporarySnapshot = Path.Combine(CacheDir!.AbsolutePath, $"import-{Guid.NewGuid():N}.eutherwire-snapshot");
        try
        {
            using (Stream input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Could not open selected snapshot."))
            using (Stream output = File.Create(temporarySnapshot)) input.CopyTo(output);
            string projectDirectory = Path.Combine(FilesDir!.AbsolutePath, "projects", $"project-{Guid.NewGuid():N}.eutherwire");
            ProjectSnapshotInfo info = ProjectSnapshot.Import(temporarySnapshot, projectDirectory);
            Preferences.Edit()!.PutString(ActiveProjectKey, projectDirectory)!.Apply();
            _projectDirectory = projectDirectory;
            _document = ProjectToml.Load(projectDirectory);
            ShowMessage($"Imported {info.FileCount} files · schema {info.ProjectSchemaVersion}");
            RenderTasks();
        }
        finally
        {
            if (File.Exists(temporarySnapshot)) File.Delete(temporarySnapshot);
        }
    }

    private void ExportPendingEvents(Android.Net.Uri uri)
    {
        if (_projectDirectory is null) return;
        string pendingPath = Path.Combine(_projectDirectory, PendingJournalName);
        using Stream input = File.OpenRead(pendingPath);
        using Stream output = ContentResolver!.OpenOutputStream(uri, "wt") ?? throw new IOException("Could not create event file.");
        input.CopyTo(output);
        ShowMessage($"Exported {PendingEvents().Count} pending event(s). Import a refreshed snapshot to clear them.");
    }

    private IReadOnlyList<InstallationEvent> PendingEvents() =>
        _projectDirectory is null ? [] : InstallationJournal.Read(Path.Combine(_projectDirectory, PendingJournalName));

    private IEnumerable<InstallationTask> Filter(IEnumerable<InstallationTask> tasks, int selected) => selected switch
    {
        1 => tasks.Where(task => task.Status is not (InstallationStatus.Installed or InstallationStatus.Tested)),
        2 => tasks.Where(task => task.Status is InstallationStatus.Installed or InstallationStatus.Tested),
        3 => tasks.Where(task => task.Status == InstallationStatus.Blocked),
        _ => tasks,
    };

    private static string TaskText(InstallationTask task)
    {
        string marker = task.Status switch
        {
            InstallationStatus.Installed => "✓",
            InstallationStatus.Tested => "✓✓",
            InstallationStatus.Blocked => "!",
            InstallationStatus.Changed => "Δ",
            _ => "○",
        };
        return $"{marker}  {task.Label}\n{task.Kind.ToString().ToUpperInvariant()} · {Name(task.Status.ToString())}";
    }

    private static string Details(InstallationTask task)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.From)) lines.Add($"FROM  {task.From}");
        if (!string.IsNullOrWhiteSpace(task.To)) lines.Add($"TO    {task.To}");
        if (task.PlannedLengthMillimetres is double planned) lines.Add($"PLAN  {planned / 1000:0.##} m");
        if (task.ActualLengthMillimetres is double actual) lines.Add($"ACTUAL {actual / 1000:0.##} m");
        if (!string.IsNullOrWhiteSpace(task.Note)) lines.Add($"NOTE  {task.Note}");
        return string.Join("\n", lines);
    }

    private void ShowMessage(string message, bool error = false)
    {
        if (_message is not null)
        {
            _message.Text = message;
            _message.SetTextColor(Color.ParseColor(error ? "#ff6b6b" : "#f2c94c"));
        }
        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }

    private TextView Text(string value, float size, string colour)
    {
        var text = new TextView(this)
        {
            Text = value,
            TextSize = size,
        };
        text.SetTextColor(Color.ParseColor(colour));
        return text;
    }

    private TextView Label(string value)
    {
        TextView label = Text(value, 11, "#55d7ff");
        label.SetPadding(0, Dp(12), 0, 0);
        return label;
    }

    private LinearLayout Form()
    {
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(20), 0, Dp(20), Dp(8));
        return form;
    }

    private ScrollView InScroll(View view)
    {
        var scroll = new ScrollView(this);
        scroll.AddView(view);
        return scroll;
    }

    private EditText NumberOrTextField(string value, bool numeric = true)
    {
        var field = new EditText(this)
        {
            Text = value,
            InputType = numeric
                ? Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal | Android.Text.InputTypes.NumberFlagSigned
                : Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextFlagCapSentences,
        };
        field.SetTextColor(Color.White);
        field.SetHintTextColor(Color.ParseColor("#718590"));
        return field;
    }

    private void AddField(LinearLayout form, string label, View field)
    {
        form.AddView(Label(label));
        form.AddView(field, MatchWrap());
    }

    private ArrayAdapter<string> SpinnerAdapter(string[] values)
    {
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, values);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        return adapter;
    }

    private Button ActionButton(string label)
    {
        var button = new Button(this)
        {
            Text = label,
            TextSize = 12,
        };
        button.SetTextColor(Color.ParseColor("#dff7ff"));
        return button;
    }

    private LinearLayout.LayoutParams Weighted()
    {
        var parameters = new LinearLayout.LayoutParams(0, Dp(52), 1);
        parameters.SetMargins(Dp(3), 0, Dp(3), 0);
        return parameters;
    }

    private static LinearLayout.LayoutParams MatchWrap() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private ISharedPreferences Preferences => GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    private string AuthorDeviceId
    {
        get
        {
            string? existing = Preferences.GetString(DeviceIdKey, null);
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
            string created = $"android-{Guid.NewGuid():N}";
            Preferences.Edit()!.PutString(DeviceIdKey, created)!.Apply();
            return created;
        }
    }

    private static string Name(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {char.ToUpperInvariant(character)}" : char.ToUpperInvariant(character).ToString()));

    private static string Metres(double millimetres) => $"{millimetres / 1000:0.###} m";

    private static string SafeFileName(string value)
    {
        string result = new(value.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray());
        string collapsed = string.Join('-', result.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? "project" : collapsed;
    }

    private static int WallIndex(MountingSurface surface) => surface switch
    {
        MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior => 0,
        MountingSurface.EastWallInterior or MountingSurface.EastWallExterior => 1,
        MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior => 2,
        MountingSurface.WestWallInterior or MountingSurface.WestWallExterior => 3,
        _ => throw new ArgumentException("Opening is not mounted on a wall."),
    };

    private static MountingSurface WallSurface(int index) => index switch
    {
        0 => MountingSurface.NorthWallInterior,
        1 => MountingSurface.EastWallInterior,
        2 => MountingSurface.SouthWallInterior,
        3 => MountingSurface.WestWallInterior,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static string WallName(MountingSurface surface) => WallIndex(surface) switch
    {
        0 => "NORTH WALL",
        1 => "EAST WALL",
        2 => "SOUTH WALL",
        _ => "WEST WALL",
    };

    private static double OpeningOffset(BuildingOpening opening, SpaceVolume space) => BuildingOpeningGeometry.UsesXAxis(opening.Surface)
        ? opening.Centre.X - space.Origin.X - opening.WidthMillimetres / 2
        : opening.Centre.Y - space.Origin.Y - opening.WidthMillimetres / 2;

    private static Point3 OpeningCentre(SpaceVolume space, MountingSurface surface, double offset, double sill, double width, double height)
    {
        double x = BuildingOpeningGeometry.UsesXAxis(surface) ? space.Origin.X + offset + width / 2 : space.Origin.X;
        double y = BuildingOpeningGeometry.UsesXAxis(surface) ? space.Origin.Y : space.Origin.Y + offset + width / 2;
        if (surface == MountingSurface.SouthWallInterior) y = space.Origin.Y + space.DepthMillimetres;
        if (surface == MountingSurface.EastWallInterior) x = space.Origin.X + space.WidthMillimetres;
        return new Point3(x, y, sill + height / 2);
    }

    private static double ParseNonNegative(string value, string label)
    {
        double number = ParseLength(value);
        if (number < 0) throw new ArgumentException($"{label} cannot be negative.");
        return number;
    }

    private static double ParseCoordinate(string value, string label)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double local) && double.IsFinite(local)) return local;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant) && double.IsFinite(invariant)) return invariant;
        throw new ArgumentException($"{label} must be a valid number.");
    }

    private static double ParseLength(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double local)) return local;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant)) return invariant;
        string alternate = value.Contains(',') ? value.Replace(',', '.') : value.Replace('.', ',');
        if (double.TryParse(alternate, NumberStyles.Float, CultureInfo.CurrentCulture, out double converted)) return converted;
        throw new FormatException("Length must be a number, for example 12500 or 12500.5.");
    }

    private static Color StatusColour(InstallationStatus status) => Color.ParseColor(status switch
    {
        InstallationStatus.Installed or InstallationStatus.Tested => "#66e6a5",
        InstallationStatus.Blocked => "#ff6b6b",
        InstallationStatus.Changed => "#f2c94c",
        _ => "#dff7ff",
    });
}
