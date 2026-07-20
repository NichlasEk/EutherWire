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

var placed = new Device(ObjectId.Parse("placed-box"), DeviceKind.JunctionBox, new Point2(500, 600), "DOSA-01");
connectedHistory.Execute(wired, new AddDeviceCommand(placed));
Require(wired.Contains(placed.Id), "Add-device commands must add stable document objects.");
Require(connectedHistory.Undo(wired), "Placed devices must be undoable.");
Require(!wired.Contains(placed.Id), "Undo must remove the placed device.");
Require(connectedHistory.Redo(wired), "Placed devices must be redoable.");
Require(wired.Contains(placed.Id), "Redo must restore the same stable object ID.");

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
connectedHistory.Execute(wired, new SetSpaceVolumeCommand(wired.Space with { HeightMillimetres = 3000, WallThicknessMillimetres = 180, CeilingThicknessMillimetres = 300 }));
Require(wired.RequireDevice(lightId).ElevationMillimetres == 3000, "Ceiling-mounted devices must follow an edited room height.");

string serialized = ProjectToml.Serialize(wired);
ProjectDocument loaded = ProjectToml.Deserialize(serialized);
string serializedAgain = ProjectToml.Serialize(loaded);
Require(serializedAgain == serialized, "TOML save/load/save must be byte-identical.");
Require(loaded.RequireCable(cableId).From == new PortReference(sourceId, "out"), "TOML must preserve typed port references.");
Require(loaded.RequireConduit(pipeId).Route.Points[1] == new Point2(1000, -500), "TOML must preserve edited geometry.");
Require(loaded.RequireAnnotation(noteId).Text == "BORRA HÄR", "TOML must preserve annotations.");
Require(loaded.SchemaVersion == 5 && loaded.Planning == new PlanningSettings(15, 500), "TOML must preserve versioned planning settings.");
Require(loaded.RequireCable(cableId).InstallationStatus == InstallationStatus.Tested, "TOML must preserve installation state.");
Require(loaded.RequireCable(cableId).ActualLengthMillimetres == 2350, "TOML must preserve actual installed length.");
Require(loaded.Space.WallThicknessMillimetres == 180 && loaded.Space.CeilingThicknessMillimetres == 300, "TOML must preserve wall and ceiling construction thickness.");
Require(loaded.RequireDevice(lightId).ElevationMillimetres == 3000 && loaded.RequireDevice(lightId).MountingSurface == MountingSurface.CeilingInterior, "TOML must preserve a light mounted on the interior ceiling.");

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
Require(Math.Abs(garageFill.FillRatio - (6.2 * 6.2 / (25 * 25))) < 0.000001, "Conduit fill must use the planning diameter catalogue.");
Require(garageAnalysis.ErrorCount == 0 && garageAnalysis.WarningCount == 0, "The garage template must be semantically sound.");
Require(garageAnalysis.Materials.Any(item => item.Category == "cable" && item.Key == nameof(CableKind.Cat6) && Math.Abs(item.Quantity - 9.14) < 0.001), "Material list must aggregate recommended cable metres.");
InstallationTask garageTask = garageAnalysis.InstallationTasks.Single();
Require(garageTask.From == "POE-SW:port-1" && garageTask.To == "KAM-N:eth0", "Installation tasks must resolve readable endpoint labels.");
Require(garageTask.PlannedLengthMillimetres == 9140 && garageTask.ActualLengthMillimetres is null, "Installation tasks need planned and measured lengths.");

PropertyHandleId statusPropertyId = PropertyHandleId.Parse("camera-north-cat6:property:installation_status");
IReadOnlyList<DocumentProperty> garageProperties = DocumentProperties.Enumerate(garageDocument);
Require(garageProperties.Any(property => property.Id == statusPropertyId && property.Value == "planned"), "Objects need stable semantic property handles.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north-pipe:property:inner_diameter_mm"), "Conduit dimensions need property handles.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north:property:elevation_mm" && property.Value == "2200"), "Device elevation needs a property handle.");
Require(garageProperties.Any(property => property.Id.ToString() == "camera-north:property:mounting_surface"), "Devices need a semantic mounting-surface handle.");
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
