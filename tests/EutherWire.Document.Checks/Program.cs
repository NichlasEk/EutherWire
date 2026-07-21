using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Xml.Linq;
using EutherWire.Document.Analysis;
using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Export;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;
using EutherWire.Document.Templates;
using EutherWire.Export;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var route = new Polyline([new Point2(0, 0), new Point2(3000, 4000), new Point2(6000, 4000)]);
Require(route.LengthMillimetres == 8000, "Polyline length must use document millimetres.");
var spatialRoute = new Polyline([new Point3(0, 0, 0), new Point3(3000, 4000, 12000)]);
Require(spatialRoute.LengthMillimetres == 13000, "Polyline length must include vertical installation distance.");

SpaceVolume wallSpace = SpaceVolume.GarageDefault;
MountingSurface[] wallSurfaces = Enum.GetValues<MountingSurface>().Where(WallCoordinateSystem.IsWall).ToArray();
Require(wallSurfaces.Length == 8, "Wall elevations must cover four inner and four outer wall faces.");
foreach (MountingSurface surface in wallSurfaces)
{
    double width = WallCoordinateSystem.Width(wallSpace, surface);
    double height = WallCoordinateSystem.Height(wallSpace, surface);
    double horizontal = width * 0.37;
    double elevation = height * 0.61;
    Point3 wallPoint = WallCoordinateSystem.FromWallCoordinates(wallSpace, surface, horizontal, elevation);
    Require(WallCoordinateSystem.IsOnWall(wallSpace, surface, wallPoint), $"{surface} coordinates must remain on their named wall plane.");
    Require(Math.Abs(WallCoordinateSystem.HorizontalCoordinate(wallSpace, surface, wallPoint) - horizontal) < 0.001, $"{surface} wall coordinates must round-trip horizontally.");
    Require(Math.Abs(wallPoint.Z - elevation) < 0.001, $"{surface} wall coordinates must preserve finished-floor elevation.");
}
Require(
    WallCoordinateSystem.Width(wallSpace, MountingSurface.NorthWallExterior) == wallSpace.WidthMillimetres + wallSpace.WallThicknessMillimetres * 2,
    "Outer wall elevations must include both corner wall thicknesses.");
Require(
    WallCoordinateSystem.Height(wallSpace, MountingSurface.NorthWallExterior) == wallSpace.HeightMillimetres + wallSpace.CeilingThicknessMillimetres,
    "Outer wall elevations must include the ceiling build-up.");

ObjectId cameraId = ObjectId.Parse("camera-north");
var camera = new Device(
    cameraId,
    DeviceKind.Camera,
    new Point2(12000, 2400),
    "KAM-N",
    [new Port("eth0", PortKind.EthernetPoe, new Point2(-21, 0))]);
var document = new ProjectDocument("Garage Draft");
document.Add(camera);

var history = new CommandHistory();
history.Execute(document, new MoveDeviceCommand(cameraId, new Point2(12600, 2500)));
Require(camera.Position == new Point2(12600, 2500), "Move must update the device.");
Require(history.Undo(document), "Move must be undoable.");
Require(camera.Position == new Point2(12000, 2400), "Undo must restore the origin.");
Require(history.Redo(document), "Move must be redoable.");
Require(camera.Position == new Point2(12600, 2500), "Redo must restore the destination.");

document.Add(new Conduit(
    ObjectId.Parse("camera-north-pipe"),
    "RÖR-R07",
    25,
    route,
    InstallationMethod.Concealed));

IReadOnlyList<EditHandle> handles = DocumentHandles.Enumerate(document);
Require(handles.Any(handle => handle.Id.ToString() == "camera-north:move"), "Devices need stable move handles.");
Require(handles.Any(handle => handle.Id.ToString() == "camera-north:port:eth0"), "Ports need named handles.");
Require(handles.Any(handle => handle.Id.ToString() == "camera-north-pipe:vertex:1"), "Routes need indexed vertex handles.");
Require(handles.Any(handle => handle.Id.ToString() == "camera-north-pipe:elevation:1"), "Routes need indexed one-axis elevation handles.");
Require(handles.Select(handle => handle.Id).Distinct().Count() == handles.Count, "Handle IDs must be unique.");

var vertexId = new EditHandleId(ObjectId.Parse("camera-north-pipe"), EditHandleKind.Vertex, 1);
history.Execute(document, new MoveEditHandleCommand(vertexId, new Point2(3400, 4200)));
Require(document.RequireConduit(ObjectId.Parse("camera-north-pipe")).Route.Points[1] == new Point2(3400, 4200), "Vertex handles must edit real routes.");
Require(history.Undo(document), "Route-handle moves must be undoable.");
Require(document.RequireConduit(ObjectId.Parse("camera-north-pipe")).Route.Points[1] == new Point2(3000, 4000), "Undo must restore route geometry.");
Require(history.Redo(document), "Route-handle moves must be redoable.");

var rotateId = new EditHandleId(cameraId, EditHandleKind.Rotate);
history.Execute(document, new MoveEditHandleCommand(rotateId, camera.Position + new Vector2(500, 0)));
Require(camera.RotationDegrees == 90, "Rotation handles must edit device rotation.");
Require(history.Undo(document), "Rotation-handle moves must be undoable.");
Require(camera.RotationDegrees == 0, "Undo must restore device rotation.");

var wired = new ProjectDocument("Connected handles");
ObjectId sourceId = ObjectId.Parse("source");
ObjectId targetId = ObjectId.Parse("target");
ObjectId pipeId = ObjectId.Parse("pipe");
ObjectId cableId = ObjectId.Parse("cable");
ObjectId lightId = ObjectId.Parse("ceiling-light");
wired.Add(new Device(sourceId, DeviceKind.PoeSwitch, new Point2(0, 0), "SOURCE", [new Port("out", PortKind.EthernetPoe, new Point2(0, 0))]));
wired.Add(new Device(targetId, DeviceKind.Camera, new Point2(2000, 0), "TARGET", [new Port("in", PortKind.EthernetPoe, new Point2(0, 0))]));
wired.Add(new Device(lightId, DeviceKind.Light, new Point2(1000, 1000), "CEILING LIGHT", [new Port("power", PortKind.MainsPower, new Point2(0, 0))], 2800, MountingSurface.CeilingInterior));
var connectedRoute = new Polyline([new Point2(0, 0), new Point2(1000, 0), new Point2(2000, 0)]);
wired.Add(new Conduit(pipeId, "PIPE", 25, connectedRoute));
wired.Add(new CableRoute(
    cableId,
    "CABLE",
    CableKind.Cat6,
    connectedRoute,
    new PortReference(sourceId, "out"),
    new PortReference(targetId, "in"),
    pipeId));

IReadOnlyList<EditHandle> connectedHandles = DocumentHandles.Enumerate(wired);
Require(!connectedHandles.Any(handle => handle.Id.ObjectId == cableId && handle.Id.Kind == EditHandleKind.Vertex), "Contained cables must use conduit route handles.");
Require(!connectedHandles.Any(handle => handle.Id.ObjectId == cableId && handle.Id.Kind == EditHandleKind.Elevation), "Contained cables must inherit conduit elevation handles.");
Require(connectedHandles.Any(handle => handle.Id == EditHandleId.Parse("pipe:elevation:1")), "Conduit vertices need stable parseable elevation handles.");
var connectedHistory = new CommandHistory();
connectedHistory.Execute(wired, new MoveEditHandleCommand(new EditHandleId(targetId, EditHandleKind.Move), new Point2(2400, 300)));
Require(wired.RequireCable(cableId).Route.Points[^1] == new Point2(2400, 300), "Connected cable ends must follow moved device ports.");
Require(wired.RequireConduit(pipeId).Route.Points[^1] == new Point2(2400, 300), "Matching conduit ends must follow connected cable ends.");
connectedHistory.Execute(wired, new MoveSpatialHandleCommand(new EditHandleId(targetId, EditHandleKind.Move), new Point3(2500, 400, 2200)));
Require(wired.RequireDevice(targetId).ElevationMillimetres == 2200, "Spatial move handles must edit a device's exact elevation.");
Require(wired.RequireCable(cableId).Route.SpatialPoints[^1] == new Point3(2500, 400, 2200), "Connected cable ends must follow device handles in all three dimensions.");
Require(wired.RequireConduit(pipeId).Route.SpatialPoints[^1] == new Point3(2500, 400, 2200), "Contained conduit ends must follow a spatial device move.");
Require(connectedHistory.Undo(wired), "Spatial device moves must be undoable.");
Require(wired.RequireDevice(targetId).ElevationMillimetres == 0 && wired.RequireCable(cableId).Route.SpatialPoints[^1] == new Point3(2400, 300, 0), "Undo must restore exact device and route coordinates.");
connectedHistory.Execute(wired, new MoveEditHandleCommand(new EditHandleId(pipeId, EditHandleKind.Vertex, 1), new Point2(1000, -500)));
Require(wired.RequireCable(cableId).Route.Points[1] == new Point2(1000, -500), "Contained cable geometry must follow conduit vertex handles.");
connectedHistory.Execute(wired, new MoveSpatialHandleCommand(new EditHandleId(pipeId, EditHandleKind.Vertex, 1), new Point3(1200, -600, 1800)));
Require(wired.RequireConduit(pipeId).Route.SpatialPoints[1] == new Point3(1200, -600, 1800), "Spatial vertex handles must edit X, Y, and Z together.");
Require(wired.RequireCable(cableId).Route.SpatialPoints[1] == new Point3(1200, -600, 1800), "Contained cable geometry must follow spatial conduit handles.");
Require(connectedHistory.Undo(wired), "Spatial route moves must be undoable.");
var routeVertexElevationHandleId = EditHandleId.Parse("pipe:elevation:1");
Require(DocumentHandleEditor.RequireSpatialPosition(wired, routeVertexElevationHandleId) == new Point3(1000, -500, 500), "Route elevation handles must sit above their vertex without changing its plan position.");
connectedHistory.Execute(wired, new MoveSpatialHandleCommand(routeVertexElevationHandleId, new Point3(9000, 9000, 2500)));
Require(wired.RequireConduit(pipeId).Route.SpatialPoints[1] == new Point3(1000, -500, 2000), "Route elevation handles must change only Z and ignore pointer X/Y drift.");
Require(wired.RequireCable(cableId).Route.SpatialPoints[1] == new Point3(1000, -500, 2000), "Contained cable elevation must follow its conduit elevation handle.");
Require(connectedHistory.Undo(wired), "Route elevation handle moves must be undoable.");
Require(wired.RequireConduit(pipeId).Route.SpatialPoints[1] == new Point3(1000, -500, 0), "Route elevation undo must restore the exact vertex height.");

var placed = new Device(ObjectId.Parse("placed-box"), DeviceKind.JunctionBox, new Point2(500, 600), "DOSA-01");
connectedHistory.Execute(wired, new AddDeviceCommand(placed));
Require(wired.Contains(placed.Id), "Add-device commands must add stable document objects.");
Require(connectedHistory.Undo(wired), "Placed devices must be undoable.");
Require(!wired.Contains(placed.Id), "Undo must remove the placed device.");
Require(connectedHistory.Redo(wired), "Placed devices must be redoable.");
Require(wired.Contains(placed.Id), "Redo must restore the same stable object ID.");

var grouped = new ProjectDocument("Grouped deletion");
grouped.Add(new Device(sourceId, DeviceKind.PoeSwitch, new Point2(0, 0), "SOURCE", [new Port("out", PortKind.EthernetPoe, new Point2(0, 0))]));
grouped.Add(new Device(targetId, DeviceKind.Camera, new Point2(2000, 0), "TARGET", [new Port("in", PortKind.EthernetPoe, new Point2(0, 0))]));
grouped.Add(new Conduit(pipeId, "PIPE", 25, connectedRoute));
grouped.Add(new CableRoute(cableId, "CABLE", CableKind.Cat6, connectedRoute,
    new PortReference(sourceId, "out"), new PortReference(targetId, "in"), pipeId));
var groupedHistory = new CommandHistory();
groupedHistory.Execute(grouped, new DeleteObjectsCommand([sourceId, targetId, pipeId, cableId]));
Require(!grouped.Contains(sourceId) && !grouped.Contains(targetId) && !grouped.Contains(pipeId) && !grouped.Contains(cableId),
    "Grouped deletion must remove connected selections in dependency order.");
Require(groupedHistory.Undo(grouped), "Grouped deletion must be one undo operation.");
Require(grouped.Contains(sourceId) && grouped.Contains(targetId) && grouped.Contains(pipeId) && grouped.Contains(cableId),
    "Undo must restore every grouped object and its references.");
try
{
    groupedHistory.Execute(grouped, new DeleteObjectsCommand([targetId, pipeId]));
    throw new InvalidOperationException("Grouped deletion must reject dependencies outside the selection.");
}
catch (InvalidOperationException exception)
{
    Require(exception.Message.Contains("connected", StringComparison.Ordinal) || exception.Message.Contains("contains", StringComparison.Ordinal),
        "Rejected grouped deletion needs a useful dependency message.");
}
Require(grouped.Contains(targetId) && grouped.Contains(pipeId) && grouped.Contains(cableId),
    "A rejected grouped deletion must roll back already removed objects.");
var duplicate = new DuplicateObjectsCommand([sourceId, targetId, pipeId, cableId], new Vector2(300, 300));
groupedHistory.Execute(grouped, duplicate);
Require(duplicate.CreatedIds.Count == 4 && duplicate.CreatedIds.All(grouped.Contains),
    "Grouped duplication must create a stable ID for every selected object.");
CableRoute copiedCable = grouped.Cables.Values.Single(cable => cable.Id != cableId);
Require(copiedCable.From?.DeviceId != sourceId && copiedCable.To?.DeviceId != targetId && copiedCable.ConduitId != pipeId,
    "Grouped duplication must remap internal device and conduit references.");
Require(copiedCable.Route.SpatialPoints[0] == new Point3(300, 300, 0),
    "Duplicated route geometry must use the requested spatial offset.");
Require(groupedHistory.Undo(grouped) && duplicate.CreatedIds.All(id => !grouped.Contains(id)),
    "Grouped duplication must be one undo operation.");
Require(groupedHistory.Redo(grouped) && duplicate.CreatedIds.All(grouped.Contains),
    "Redo must restore duplicated objects with the same stable IDs.");
ObjectId copiedCableId = grouped.Cables.Keys.Single(id => id != cableId);
groupedHistory.Execute(grouped, new TranslateObjectsCommand(duplicate.CreatedIds, new Vector2(200, -100)));
Require(grouped.RequireCable(copiedCableId).Route.SpatialPoints[0] == new Point3(500, 200, 0),
    "Group movement must translate complete copied route geometry.");
Require(groupedHistory.Undo(grouped) && grouped.RequireCable(copiedCableId).Route.SpatialPoints[0] == new Point3(300, 300, 0),
    "Group movement must be one undoable command.");

var additions = new ProjectDocument("Route commands");
var addedConduit = new Conduit(ObjectId.Parse("added-pipe"), "RÖR-01", 25, new Polyline([new Point2(0, 0), new Point2(1000, 0)]));
var addedCable = new CableRoute(ObjectId.Parse("added-cable"), "CAT6-01", CableKind.Cat6, new Polyline([new Point2(0, 0), new Point2(1000, 0)]));
var additionHistory = new CommandHistory();
additionHistory.Execute(additions, new AddConduitCommand(addedConduit));
additionHistory.Execute(additions, new AddCableCommand(addedCable));
Require(additions.Contains(addedConduit.Id) && additions.Contains(addedCable.Id), "Route commands must add conduits and cables.");
Require(additionHistory.Undo(additions) && !additions.Contains(addedCable.Id), "Cable placement must be undoable.");
Require(additionHistory.Undo(additions) && !additions.Contains(addedConduit.Id), "Conduit placement must be undoable.");
Require(additionHistory.Redo(additions) && additionHistory.Redo(additions), "Route placement must be redoable.");

additionHistory.Execute(additions, new SetObjectLabelCommand(addedCable.Id, "FIBER-01"));
Require(additions.RequireCable(addedCable.Id).Label == "FIBER-01", "Inspector labels must edit document objects.");
Require(additionHistory.Undo(additions) && additions.RequireCable(addedCable.Id).Label == "CAT6-01", "Label edits must be undoable.");
additionHistory.Execute(additions, new SetCableKindCommand(addedCable.Id, CableKind.FibreDuplex));
Require(additions.RequireCable(addedCable.Id).Kind == CableKind.FibreDuplex, "Cable type edits must use commands.");
Require(additions.RequireCable(addedCable.Id).Electrical?.Product == CableProductKind.Fibre,
    "Changing a legacy cable type must update its structured electrical profile.");
additionHistory.Execute(additions, new SetConduitDiameterCommand(addedConduit.Id, 32));
Require(additions.RequireConduit(addedConduit.Id).InnerDiameterMillimetres == 32, "Conduit diameter edits must use commands.");
additionHistory.Execute(additions, new DeleteObjectCommand(addedCable.Id));
Require(!additions.Contains(addedCable.Id), "Delete commands must remove unreferenced objects.");
Require(additionHistory.Undo(additions) && additions.Contains(addedCable.Id), "Delete commands must be undoable.");

try
{
    connectedHistory.Execute(wired, new DeleteObjectCommand(targetId));
    throw new InvalidOperationException("Connected devices must not be deleted without resolving their cables.");
}
catch (InvalidOperationException exception)
{
    Require(exception.Message.Contains("connected", StringComparison.Ordinal), "Blocked deletion needs a useful diagnostic.");
}

ObjectId noteId = ObjectId.Parse("note-installation");
var note = new Annotation(noteId, new Point2(750, 900), "MÄT FÖRE BORRNING");
connectedHistory.Execute(wired, new AddAnnotationCommand(note));
Require(wired.Contains(noteId), "Text tools must create real annotation objects.");
var noteHandle = new EditHandleId(noteId, EditHandleKind.LabelAnchor);
connectedHistory.Execute(wired, new MoveEditHandleCommand(noteHandle, new Point2(800, 950)));
Require(wired.RequireAnnotation(noteId).Position == new Point2(800, 950), "Annotation anchor handles must be movable.");
connectedHistory.Execute(wired, new SetObjectLabelCommand(noteId, "BORRA HÄR"));
Require(wired.RequireAnnotation(noteId).Text == "BORRA HÄR", "Annotation text must use the shared inspector command.");

int routePointCount = wired.RequireConduit(pipeId).Route.Points.Count;
connectedHistory.Execute(wired, new InsertRouteVertexCommand(pipeId, 1, new Point2(500, -250)));
Require(wired.RequireConduit(pipeId).Route.Points.Count == routePointCount + 1, "Insert-point commands must extend conduit geometry.");
Require(wired.RequireCable(cableId).Route.Points[1] == new Point2(500, -250), "Contained cables must follow inserted conduit vertices.");
Require(connectedHistory.Undo(wired), "Inserted route vertices must be undoable.");
Require(wired.RequireConduit(pipeId).Route.Points.Count == routePointCount, "Undo must remove inserted route vertices.");
connectedHistory.Execute(wired, new DeleteRouteVertexCommand(pipeId, 1));
Require(wired.RequireConduit(pipeId).Route.Points.Count == routePointCount - 1, "Delete-point commands must reduce conduit geometry.");
Require(wired.RequireCable(cableId).Route.Points.Count == routePointCount - 1, "Contained cables must follow deleted conduit vertices.");
Require(connectedHistory.Undo(wired), "Deleted route vertices must be undoable.");

connectedHistory.Execute(wired, new SetPlanningSettingsCommand(new PlanningSettings(15, 500)));
Require(wired.Planning == new PlanningSettings(15, 500), "Planning margins must be command-based.");
Require(connectedHistory.Undo(wired) && wired.Planning == new PlanningSettings(), "Planning margins must be undoable.");
Require(connectedHistory.Redo(wired), "Planning margins must be redoable.");
connectedHistory.Execute(wired, new SetCableInstallationCommand(cableId, InstallationStatus.Tested, 2350));
Require(wired.RequireCable(cableId).InstallationStatus == InstallationStatus.Tested, "Installation state must be stored on its cable.");
Require(wired.RequireCable(cableId).ActualLengthMillimetres == 2350, "Actual installed cable length must be preserved.");
var installedAt = new DateTimeOffset(2026, 7, 21, 12, 34, 56, TimeSpan.Zero);
connectedHistory.Execute(wired, new SetInstallationRecordCommand(new InstallationRecord(
    sourceId, InstallationStatus.Installed, installedAt, "Mounted above cabinet",
    new Point3(120, 340, 1800), testResult: "Visual OK", photoReferences: ["photos/source-01.jpg"])));
Require(wired.RequireInstallationRecord(sourceId).PhotoReferences.Single() == "photos/source-01.jpg",
    "Every installable object must support field notes, measurements, tests, and photo references.");
connectedHistory.Execute(wired, new SetSpaceVolumeCommand(wired.Space with { HeightMillimetres = 3000, WallThicknessMillimetres = 180, CeilingThicknessMillimetres = 300 }));
Require(wired.RequireDevice(lightId).ElevationMillimetres == 3000, "Ceiling-mounted devices must follow an edited room height.");

var dimensionId = ObjectId.Parse("garage-door-height");
double southY = wired.Space.Origin.Y + wired.Space.DepthMillimetres;
connectedHistory.Execute(wired, new AddWallDimensionCommand(new WallDimension(
    dimensionId,
    MountingSurface.SouthWallInterior,
    new Point3(500, southY, 0),
    new Point3(500, southY, 2200),
    "PORTÖPPNING")));
var dimensionEnd = EditHandleId.Parse("garage-door-height:resize:end");
Require(DocumentHandleEditor.RequireSpatialPosition(wired, dimensionEnd).Z == 2200, "Wall dimensions need stable endpoint handles.");
connectedHistory.Execute(wired, new MoveSpatialHandleCommand(dimensionEnd, new Point3(500, southY, 2300)));
Require(wired.RequireWallDimension(dimensionId).End.Z == 2300, "Dimension endpoints must be spatially editable.");
Require(connectedHistory.Undo(wired), "Dimension endpoint moves must be undoable.");
Require(wired.RequireWallDimension(dimensionId).End.Z == 2200, "Undo must restore dimension endpoints.");

string serialized = ProjectToml.Serialize(wired);
ProjectDocument loaded = ProjectToml.Deserialize(serialized);
string serializedAgain = ProjectToml.Serialize(loaded);
Require(serializedAgain == serialized, "TOML save/load/save must be byte-identical.");
Require(loaded.RequireCable(cableId).From == new PortReference(sourceId, "out"), "TOML must preserve typed port references.");
Require(loaded.RequireConduit(pipeId).Route.Points[1] == new Point2(1000, -500), "TOML must preserve edited geometry.");
Require(loaded.RequireAnnotation(noteId).Text == "BORRA HÄR", "TOML must preserve annotations.");
Require(loaded.SchemaVersion == 11 && loaded.Planning == new PlanningSettings(15, 500), "TOML must preserve versioned planning settings.");
Require(loaded.ElectricalRules == ElectricalRuleProfile.Sweden2026, "Schema 11 must preserve its versioned Swedish electrical rule profile.");
Require(loaded.RequireCable(cableId).Electrical is { Product: CableProductKind.Cat6, Preset: CircuitPreset.Data, Conductors.Count: 4 },
    "Schema 7 must migrate legacy CAT6 into explicit data pairs.");
Require(loaded.RequireWallDimension(dimensionId).Label == "PORTÖPPNING", "TOML must preserve wall dimensions.");
Require(loaded.RequireCable(cableId).InstallationStatus == InstallationStatus.Tested, "TOML must preserve installation state.");
Require(loaded.RequireCable(cableId).ActualLengthMillimetres == 2350, "TOML must preserve actual installed length.");
InstallationRecord loadedSourceInstallation = loaded.RequireInstallationRecord(sourceId);
Require(loadedSourceInstallation.Status == InstallationStatus.Installed &&
        loadedSourceInstallation.UpdatedAt == installedAt &&
        loadedSourceInstallation.Note == "Mounted above cabinet" &&
        loadedSourceInstallation.ActualPosition == new Point3(120, 340, 1800) &&
        loadedSourceInstallation.TestResult == "Visual OK" &&
        loadedSourceInstallation.PhotoReferences.Single() == "photos/source-01.jpg",
    "Schema 11 must round-trip unified installation evidence for non-cable objects.");
Require(loaded.Space.WallThicknessMillimetres == 180 && loaded.Space.CeilingThicknessMillimetres == 300, "TOML must preserve wall and ceiling construction thickness.");
Require(loaded.RequireDevice(lightId).ElevationMillimetres == 3000 && loaded.RequireDevice(lightId).MountingSurface == MountingSurface.CeilingInterior, "TOML must preserve a light mounted on the interior ceiling.");

const string legacySchema10 = """
[project]
name = "Legacy field project"
units = "mm"
schema_version = 10

[[devices]]
id = "legacy-device"
kind = "outlet"
label = "UTT-OLD"
position = [100.0, 200.0]
ports = []

[[cables]]
id = "legacy-cable"
label = "OLD-CABLE"
kind = "cat6"
points = [[0.0, 0.0], [1000.0, 0.0]]
installation_status = "tested"
actual_length_mm = 1234.0
""";
ProjectDocument legacyLoaded = ProjectToml.Deserialize(legacySchema10);
Require(legacyLoaded.RequireInstallationRecord(ObjectId.Parse("legacy-device")).Status == InstallationStatus.Planned,
    "Schema 10 devices must migrate to planned installation records.");
InstallationRecord migratedCableRecord = legacyLoaded.RequireInstallationRecord(ObjectId.Parse("legacy-cable"));
Require(migratedCableRecord.Status == InstallationStatus.Tested && migratedCableRecord.ActualLengthMillimetres == 1234,
    "Schema 10 cable status and measured length must migrate into the unified record.");

var electricalDocument = new ProjectDocument("Electrical conductors");
var fkSpec = ElectricalCableProfiles.Lighting(CableProductKind.Fk, 1.5, 2);
electricalDocument.Add(new CableRoute(ObjectId.Parse("lighting-fk"), "BELYSNING", CableKind.Custom,
    new Polyline([new Point2(0, 0), new Point2(10000, 0)]), Electrical: fkSpec));
ProjectDocument electricalLoaded = ProjectToml.Deserialize(ProjectToml.Serialize(electricalDocument));
ElectricalCableSpec loadedFk = electricalLoaded.RequireCable(ObjectId.Parse("lighting-fk")).Electrical!;
Require(loadedFk.Conductors.Count == 5 && loadedFk.Conductors.Count(item => item.Function == ConductorFunction.SwitchedLive) == 2,
    "Schema 7 must preserve explicit FK conductor functions.");
Require(loadedFk.Conductors.All(item => item.AreaSquareMillimetres == 1.5), "Schema 7 must preserve conductor area in square millimetres.");
ProjectAnalysis electricalAnalysis = ProjectAnalyzer.Analyze(electricalLoaded);
Require(electricalAnalysis.Materials.Count(item => item.Category == "conductor") == 5,
    "Loose FK conductors must produce material rows grouped by function and colour.");
Require(!electricalAnalysis.Diagnostics.Any(item => item.Code.StartsWith("electrical.", StringComparison.Ordinal)),
    "A complete lighting preset must pass basic electrical diagnostics.");

var sizingDocument = new ProjectDocument("16 mm conduit sizing");
ObjectId sizingConduitId = ObjectId.Parse("pipe-16");
Polyline sizingRoute = new([new Point2(0, 0), new Point2(5000, 0)]);
sizingDocument.Add(new Conduit(sizingConduitId, "RÖR-16", 10.7, sizingRoute, InstallationMethod.Concealed, 16));
var sizedFk = new ElectricalCableSpec(CableProductKind.Fk, CircuitPreset.SinglePhase,
    [
        new("l1", ConductorFunction.Line1, "brown", 2.5, 3.4),
        new("n", ConductorFunction.Neutral, "blue", 2.5, 3.4),
        new("pe", ConductorFunction.ProtectiveEarth, "green_yellow", 2.5, 3.4),
    ], design: new CircuitDesign(230, 1, loadedConductorCount: 2, designCurrentAmperes: 13,
        protectiveDeviceAmperes: 16, protectiveDeviceCharacteristic: "B",
        referenceCurrentCarryingCapacityAmperes: 20, ambientCorrectionFactor: 1,
        groupingCorrectionFactor: 0.9, thermalInsulationCorrectionFactor: 1,
        referenceSource: "licensed test fixture"));
sizingDocument.Add(new CableRoute(ObjectId.Parse("fk-group"), "UTTAG", CableKind.Custom, sizingRoute,
    ConduitId: sizingConduitId, Electrical: sizedFk));
ProjectDocument sizingLoaded = ProjectToml.Deserialize(ProjectToml.Serialize(sizingDocument));
ProjectAnalysis sizingAnalysis = ProjectAnalyzer.Analyze(sizingLoaded);
ConduitFill sizingFill = sizingAnalysis.ConduitFills.Single();
Require(sizingFill.KnownConductorCount == 3 && sizingFill.UnknownConductorCount == 0,
    "Loose FK/RK fill must count each insulated conductor using its exact outside diameter.");
Require(Math.Abs(sizingFill.FillRatio - (3 * 3.4 * 3.4 / (10.7 * 10.7))) < 0.000001,
    "Conduit fill must use actual inner diameter and insulated conductor diameters.");
ElectricalDesignCheck sizingCheck = sizingAnalysis.ElectricalDesignChecks.Single();
Require(sizingCheck.Status == DesignCheckStatus.Pass && Math.Abs(sizingCheck.CorrectedCurrentCarryingCapacityAmperes!.Value - 18) < 0.001,
    "Current-capacity planning must apply explicit correction factors and verify Ib <= In <= Iz.");
Require(sizingLoaded.RequireCable(ObjectId.Parse("fk-group")).Electrical!.Design!.ReferenceSource == "licensed test fixture",
    "Schema 10 must preserve traceable thermal sizing inputs.");
Require(sizingLoaded.RequireConduit(sizingConduitId).NominalDiameterMillimetres == 16,
    "Schema 10 must keep nominal conduit size separate from actual inner diameter.");
var sizingHistory = new CommandHistory();
sizingHistory.Execute(sizingLoaded, new SetConduitNominalDiameterCommand(sizingConduitId, 20));
Require(sizingLoaded.RequireConduit(sizingConduitId).NominalDiameterMillimetres == 20 && sizingLoaded.RequireConduit(sizingConduitId).InnerDiameterMillimetres == 10.7,
    "Changing nominal conduit size must never silently change the measured inner diameter.");
Require(sizingHistory.Undo(sizingLoaded) && sizingLoaded.RequireConduit(sizingConduitId).NominalDiameterMillimetres == 16,
    "Nominal conduit size edits must be undoable.");
ElectricalCableSpec replacementElectrical = ElectricalCableProfiles.SinglePhase(CableProductKind.Fk, 1.5);
sizingHistory.Execute(sizingLoaded, new SetCableElectricalCommand(ObjectId.Parse("fk-group"), CableKind.Custom, replacementElectrical));
Require(sizingLoaded.RequireCable(ObjectId.Parse("fk-group")).Electrical == replacementElectrical,
    "Electrical profile edits must use the undoable command layer.");
Require(sizingHistory.Undo(sizingLoaded) && sizingLoaded.RequireCable(ObjectId.Parse("fk-group")).Electrical!.Design is not null,
    "Undo must restore the complete electrical design.");
ConduitLoad sizingLoad = ProjectAnalyzer.Analyze(sizingLoaded).ConduitLoads.Single();
Require(sizingLoad is { CableCount: 1, PowerCircuitCount: 1, ConductorCount: 3, KnownLoadedConductorCount: 2, UnknownLoadedCircuitCount: 0 },
    "Conduit analysis must distinguish cables, power circuits, physical conductors, and loaded conductors.");
var secondSizedFk = new ElectricalCableSpec(CableProductKind.Fk, CircuitPreset.SinglePhase,
    [
        new("l1", ConductorFunction.Line1, "brown", 2.5, 3.4),
        new("n", ConductorFunction.Neutral, "blue", 2.5, 3.4),
        new("pe", ConductorFunction.ProtectiveEarth, "green_yellow", 2.5, 3.4),
    ], design: new CircuitDesign(230, 1, loadedConductorCount: 2));
sizingLoaded.Add(new CableRoute(ObjectId.Parse("fk-group-2"), "UTTAG-2", CableKind.Custom, sizingRoute,
    ConduitId: sizingConduitId, Electrical: secondSizedFk));
ProjectAnalysis groupedAnalysis = ProjectAnalyzer.Analyze(sizingLoaded);
Require(groupedAnalysis.ConduitLoads.Single() is { PowerCircuitCount: 2, ConductorCount: 6, KnownLoadedConductorCount: 4 },
    "Shared conduits must aggregate every contained power circuit.");
Require(groupedAnalysis.Diagnostics.Any(item => item.Code == "conduit.power_circuits.grouped" && item.Severity == DiagnosticSeverity.Warning),
    "Multiple power circuits in one conduit need a visible grouping warning.");
var methodHistory = new CommandHistory();
methodHistory.Execute(sizingLoaded, new SetConduitInstallationMethodCommand(sizingConduitId, InstallationMethod.Surface));
Require(sizingLoaded.RequireConduit(sizingConduitId).InstallationMethod == InstallationMethod.Surface,
    "Conduit installation-method edits must use commands.");
Require(methodHistory.Undo(sizingLoaded) && sizingLoaded.RequireConduit(sizingConduitId).InstallationMethod == InstallationMethod.Concealed,
    "Conduit installation-method edits must be undoable.");
ElectricalProductCatalog productCatalog = ElectricalProductCatalog.Load(Path.Combine("catalog", "electrical-products.toml"));
Require(productCatalog.Conduits.Select(item => item.NominalDiameterMillimetres).SequenceEqual([16d, 20d, 25d]),
    "The initial Swedish conduit catalog must stay focused on 16, 20, and 25 mm products.");
ConduitProduct selectedProduct = productCatalog.RequireConduit("pipelife-halovolt-750n-16");
Require(selectedProduct.InnerDiameterMillimetres == 12 && selectedProduct.ENumber == "1414864",
    "Catalog dimensions and E-number must come from the traceable product record.");
sizingHistory.Execute(sizingLoaded, new SetConduitProductCommand(sizingConduitId, selectedProduct));
Require(sizingLoaded.RequireConduit(sizingConduitId) is { ProductId: "pipelife-halovolt-750n-16", NominalDiameterMillimetres: 16, InnerDiameterMillimetres: 12 },
    "Selecting a conduit product must atomically apply its nominal and inner diameters.");
Require(sizingHistory.Undo(sizingLoaded) && sizingLoaded.RequireConduit(sizingConduitId).InnerDiameterMillimetres == 10.7,
    "Conduit product selection must be undoable.");

string dangling = serialized.Replace("target:in", "missing:in", StringComparison.Ordinal);
try
{
    _ = ProjectToml.Deserialize(dangling);
    throw new InvalidOperationException("Dangling TOML references must be rejected.");
}
catch (ProjectFormatException exception)
{
    Require(exception.Message.Contains("missing device", StringComparison.Ordinal), "Dangling references need useful diagnostics.");
}

ProjectDocument garageDocument = ProjectTemplates.CreateGarageDraft();
ProjectAnalysis garageAnalysis = ProjectAnalyzer.Analyze(garageDocument);
Require(garageDocument.Space == SpaceVolume.GarageDefault, "Garage Draft needs a real editable 3D space volume.");
BuildingOpening garageDoor = garageDocument.RequireOpening(ObjectId.Parse("garage-door-south"));
Require(garageDoor.Kind == OpeningKind.GarageDoor && garageDoor.Surface == MountingSurface.SouthWallInterior, "Garage Draft needs a first-class garage-door opening on a real wall.");
Require(garageDoor.WidthMillimetres == 5000 && garageDoor.HeightMillimetres == 2200, "Building openings need exact dimensions.");
EditHandleId garageDoorResizeEnd = EditHandleId.Parse("garage-door-south:resize:end");
Require(DocumentHandles.Enumerate(garageDocument).Any(handle => handle.Id == garageDoorResizeEnd), "Building openings need stable named resize handles.");
var openingHistory = new CommandHistory();
openingHistory.Execute(garageDocument, new MoveSpatialHandleCommand(garageDoorResizeEnd, new Point3(8500, 2500, 2400)));
Require(garageDoor.WidthMillimetres == 5500 && garageDoor.HeightMillimetres == 2400, "A spatial resize handle must edit opening width and height together.");
Require(garageDoor.Centre == new Point3(5750, 2500, 1200), "Opening resize handles must retain the opposite corner and move the centre.");
Require(openingHistory.Undo(garageDocument), "Opening resize handles must be undoable.");
Require(garageDoor.WidthMillimetres == 5000 && garageDoor.HeightMillimetres == 2200 && garageDoor.Centre == new Point3(5500, 2500, 1100), "Opening resize undo must restore exact geometry.");
EditHandleId cameraElevationHandle = EditHandleId.Parse("camera-north:elevation");
Require(DocumentHandleEditor.RequireSpatialPosition(garageDocument, cameraElevationHandle) == new Point3(11200, -2600, 2700), "Devices need a stable vertical handle above their installation elevation.");
var elevationHistory = new CommandHistory();
elevationHistory.Execute(garageDocument, new MoveSpatialHandleCommand(cameraElevationHandle, new Point3(11200, -2600, 3100)));
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).ElevationMillimetres == 2600, "Vertical handles must edit device elevation independently of X and Y.");
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).Route.SpatialPoints[^1].Z == 2600, "Connected cable endpoints must follow a device's vertical handle.");
Require(elevationHistory.Undo(garageDocument), "Vertical device moves must be undoable.");
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).ElevationMillimetres == 2200, "Vertical move undo must restore exact elevation.");
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).ElevationMillimetres == 2200, "Devices need installation elevation in 3D space.");
Require(garageDocument.RequireConduit(ObjectId.Parse("camera-north-pipe")).Route.SpatialPoints.All(point => point.Z == 2200), "Routes need per-vertex 3D elevation.");
Require(garageAnalysis.TotalCableLengthMillimetres == 7400, "Analysis must sum cable geometry in document millimetres.");
Require(garageAnalysis.TotalConduitLengthMillimetres == 7400, "Analysis must sum conduit geometry.");
Require(Math.Abs(garageAnalysis.RecommendedCableLengthMillimetres - 9140) < 0.001, "Cable orders need margin and a service loop.");
ConduitFill garageFill = garageAnalysis.ConduitFills.Single();
Require(Math.Abs(garageFill.FillRatio - (6.2 * 6.2 / (20.2 * 20.2))) < 0.000001, "Conduit fill must use the selected product's actual inner diameter.");
Require(garageDocument.RequireConduit(ObjectId.Parse("camera-north-pipe")).ProductId == "pipelife-halovolt-750n-25",
    "Garage Draft must retain its traceable conduit product selection.");
Require(garageAnalysis.ErrorCount == 0 && garageAnalysis.WarningCount == 0, "The garage template must be semantically sound.");
Require(garageAnalysis.Materials.Any(item => item.Category == "cable" && item.Key == nameof(CableKind.Cat6) && Math.Abs(item.Quantity - 9.14) < 0.001), "Material list must aggregate recommended cable metres.");
Require(garageAnalysis.Materials.Any(item => item.Category == "conduit" && item.Description == "Conduit 25 mm (inner 20.2 mm)"),
    "Material lists must order the nominal conduit product while retaining its actual inner diameter.");
Require(garageAnalysis.InstallationTasks.Count == 6, "Every device, opening, conduit, and cable must become a field task.");
InstallationTask garageTask = garageAnalysis.InstallationTasks.Single(task => task.Kind == InstallationObjectKind.Cable);
Require(garageTask.From == "POE-SW:port-1" && garageTask.To == "KAM-N:eth0", "Installation tasks must resolve readable endpoint labels.");
Require(garageTask.PlannedLengthMillimetres == 9140 && garageTask.ActualLengthMillimetres is null, "Installation tasks need planned and measured lengths.");

PropertyHandleId statusPropertyId = PropertyHandleId.Parse("camera-north-cat6:property:installation_status");
IReadOnlyList<DocumentProperty> garageProperties = DocumentProperties.Enumerate(garageDocument);
Require(garageProperties.Any(property => property.Id == statusPropertyId && property.Value == "planned"), "Objects need stable semantic property handles.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north-pipe:property:inner_diameter_mm"), "Conduit dimensions need property handles.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north-pipe:property:installation_method" && property.Value == "concealed"),
    "Conduit installation methods need stable property handles.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north:property:elevation_mm" && property.Value == "2200"), "Device elevation needs a property handle.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north:property:mounting_surface"), "Devices need a semantic mounting-surface handle.");
PropertyHandleId deviceInstallationStatusId = PropertyHandleId.Parse("camera-north:property:installation_status");
PropertyHandleId conduitInstallationNoteId = PropertyHandleId.Parse("camera-north-pipe:property:installation_note");
Require(garageProperties.Any(property => property.Id == deviceInstallationStatusId && property.Value == "planned"),
    "Devices need the same installation-status property as cables.");
Require(garageProperties.Any(property => property.Id == conduitInstallationNoteId),
    "Conduits need a writable installation note property.");
PropertyHandleId roomWidthId = PropertyHandleId.Parse("space:property:width_mm");
Require(garageProperties.Any(property => property.Id == roomWidthId && property.Value == "14000"), "Room dimensions need stable property handles.");
PropertyHandleId garageDoorWidthId = PropertyHandleId.Parse("garage-door-south:property:width_mm");
Require(garageProperties.Any(property => property.Id == garageDoorWidthId && property.Value == "5000"), "Building openings need stable dimension property handles.");
PropertyHandleId routeElevationId = PropertyHandleId.Parse("camera-north-pipe:property:vertex_1_elevation_mm");
Require(garageProperties.Any(property => property.Id == routeElevationId && property.Value == "2200"), "Every route vertex needs a stable elevation property handle.");
var propertyHistory = new CommandHistory();
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, statusPropertyId, "tested"));
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).InstallationStatus == InstallationStatus.Tested, "Property handles must create real document commands.");
Require(propertyHistory.Undo(garageDocument), "Property-handle edits must be undoable.");
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).InstallationStatus == InstallationStatus.Planned, "Property undo must restore the previous value.");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, deviceInstallationStatusId, "installed"));
Require(garageDocument.RequireInstallationRecord(ObjectId.Parse("camera-north")).Status == InstallationStatus.Installed,
    "Generic installation properties must update non-cable field tasks.");
Require(propertyHistory.Undo(garageDocument), "Generic installation-state edits must be undoable.");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, conduitInstallationNoteId, "Leave pull wire"));
Require(garageDocument.RequireInstallationRecord(ObjectId.Parse("camera-north-pipe")).Note == "Leave pull wire",
    "Installation notes must be editable through stable property handles.");
Require(propertyHistory.Undo(garageDocument), "Installation-note edits must be undoable.");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, routeElevationId, "2500"));
Require(garageDocument.RequireConduit(ObjectId.Parse("camera-north-pipe")).Route.SpatialPoints[1].Z == 2500, "Route elevation handles must edit 3D conduit geometry.");
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).Route.SpatialPoints[1].Z == 2500, "Contained cable elevation must follow its conduit.");
Require(propertyHistory.Undo(garageDocument), "Route elevation edits must be undoable.");
propertyHistory.Execute(garageDocument, new DeleteRouteVertexCommand(ObjectId.Parse("camera-north-pipe"), 1));
Require(propertyHistory.Undo(garageDocument), "Deleted 3D route vertices must be undoable.");
Require(garageDocument.RequireConduit(ObjectId.Parse("camera-north-pipe")).Route.SpatialPoints[1].Z == 2200, "Undo must restore the exact route vertex elevation.");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, roomWidthId, "15000"));
Require(garageDocument.Space.WidthMillimetres == 15000, "Room size properties must edit the real space volume.");
Require(propertyHistory.Undo(garageDocument) && garageDocument.Space == SpaceVolume.GarageDefault, "Room dimension changes must be undoable.");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, garageDoorWidthId, "5200"));
Require(garageDoor.WidthMillimetres == 5200, "Opening dimension properties must edit real geometry.");
Require(propertyHistory.Undo(garageDocument) && garageDoor.WidthMillimetres == 5000, "Opening dimension edits must be undoable.");
PropertyHandleId mountingSurfaceId = PropertyHandleId.Parse("camera-north:property:mounting_surface");
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, mountingSurfaceId, "north_wall_exterior"));
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).MountingSurface == MountingSurface.NorthWallExterior, "Mounting-surface handles must attach devices to named building surfaces.");
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).Position.Y == garageDocument.Space.Origin.Y - garageDocument.Space.WallThicknessMillimetres, "Exterior-wall attachment must constrain device geometry to the real outside face.");
Require(propertyHistory.Undo(garageDocument), "Mounting-surface changes must be undoable.");
Require(garageDocument.RequireDevice(ObjectId.Parse("camera-north")).Position == new Point2(11200, -2600), "Undo must restore the device's exact free position.");
var missingMeasurementHistory = new CommandHistory();
missingMeasurementHistory.Execute(garageDocument, new SetCableInstallationCommand(ObjectId.Parse("camera-north-cat6"), InstallationStatus.Installed, null));
ProjectAnalysis missingMeasurement = ProjectAnalyzer.Analyze(garageDocument);
Require(missingMeasurement.CompletedInstallationCount == 1, "Installed and tested cables must count as completed field tasks.");
Require(missingMeasurement.Diagnostics.Any(item => item.Code == "installation.length.missing"), "Completed cable tasks without measurements need a warning.");
Require(missingMeasurementHistory.Undo(garageDocument), "Installation task state must remain undoable.");

var deletionDocument = new ProjectDocument("Installation deletion undo");
ObjectId deletionDeviceId = ObjectId.Parse("delete-device");
deletionDocument.Add(new Device(deletionDeviceId, DeviceKind.Outlet, new Point2(100, 200), "UTT-01"));
var deletionHistory = new CommandHistory();
deletionHistory.Execute(deletionDocument, new SetInstallationRecordCommand(new InstallationRecord(
    deletionDeviceId, InstallationStatus.Tested, installedAt, "Measured in field",
    new Point3(110, 205, 1050), testResult: "Passed", photoReferences: ["photos/utt-01.jpg"])));
deletionHistory.Execute(deletionDocument, new DeleteObjectCommand(deletionDeviceId));
Require(!deletionDocument.InstallationRecords.ContainsKey(deletionDeviceId), "Deleting an object must remove its installation record.");
Require(deletionHistory.Undo(deletionDocument), "Deleted installable objects must be undoable.");
InstallationRecord restoredInstallation = deletionDocument.RequireInstallationRecord(deletionDeviceId);
Require(restoredInstallation.Status == InstallationStatus.Tested && restoredInstallation.Note == "Measured in field" &&
        restoredInstallation.TestResult == "Passed" && restoredInstallation.PhotoReferences.Single() == "photos/utt-01.jpg",
    "Delete undo must restore all installation evidence.");

string garageSvg = SvgProjectExporter.Export(garageDocument);
Require(SvgProjectExporter.Export(garageDocument) == garageSvg, "SVG export must be deterministic.");
XDocument svgXml = XDocument.Parse(garageSvg);
XNamespace svgNamespace = "http://www.w3.org/2000/svg";
Require(svgXml.Descendants(svgNamespace + "polyline").Any(element => (string?)element.Attribute("id") == "camera-north-cat6"), "SVG export must contain cable geometry with stable object IDs.");
Require(svgXml.Descendants(svgNamespace + "polyline").Any(element => (string?)element.Attribute("id") == "garage-door-south"), "SVG export must contain building openings with stable object IDs.");
Require(svgXml.Descendants(svgNamespace + "g").Any(element => (string?)element.Attribute("id") == "camera-north"), "SVG export must contain device symbols with stable object IDs.");

byte[] garagePng = PngProjectExporter.Export(garageDocument);
Require(PngProjectExporter.Export(garageDocument).SequenceEqual(garagePng), "PNG export must be byte-deterministic.");
Require(garagePng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG export needs a valid PNG signature.");
Require(BinaryPrimitives.ReadInt32BigEndian(garagePng.AsSpan(16, 4)) == 1600, "Wide projects must use the configured maximum PNG width.");
Require(BinaryPrimitives.ReadInt32BigEndian(garagePng.AsSpan(20, 4)) > 0, "PNG export needs a positive calculated height.");
Require(Convert.ToHexString(SHA256.HashData(garagePng)).ToLowerInvariant() == "57896fc36da3b5904bbb3a262886f2956af8d8c4aa2b3e510f36524aa3df7319", "Garage Draft PNG must retain its reference render hash.");

var invalid = new ProjectDocument("Analysis diagnostics");
ObjectId mainsId = ObjectId.Parse("mains");
ObjectId poeId = ObjectId.Parse("poe-load");
invalid.Add(new Device(mainsId, DeviceKind.DistributionBoard, new Point2(0, 0), "DUPLICATE", [new Port("out", PortKind.MainsPower, new Point2(0, 0))]));
invalid.Add(new Device(poeId, DeviceKind.Camera, new Point2(1000, 0), "DUPLICATE", [new Port("in", PortKind.EthernetPoe, new Point2(0, 0))]));
invalid.Add(new CableRoute(
    ObjectId.Parse("bad-cat6"),
    "BAD-CAT6",
    CableKind.Cat6,
    new Polyline([new Point2(0, 0), new Point2(1000, 0)]),
    new PortReference(mainsId, "out"),
    new PortReference(poeId, "in")));
ProjectAnalysis invalidAnalysis = ProjectAnalyzer.Analyze(invalid);
Require(invalidAnalysis.Diagnostics.Count(item => item.Code == "label.duplicate") == 2, "Duplicate labels must identify every conflicting object.");
Require(invalidAnalysis.Diagnostics.Any(item => item.Code == "cable.port.type" && item.Severity == DiagnosticSeverity.Error), "Cable and port type mismatches must be errors.");
Require(invalidAnalysis.Diagnostics.Any(item => item.Code == "cable.poe.source"), "PoE loads need a PoE-capable source warning.");

Console.WriteLine("EutherWire document checks passed.");
