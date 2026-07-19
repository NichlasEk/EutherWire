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
wired.Add(new Device(sourceId, DeviceKind.PoeSwitch, new Point2(0, 0), "SOURCE", [new Port("out", PortKind.EthernetPoe, new Point2(0, 0))]));
wired.Add(new Device(targetId, DeviceKind.Camera, new Point2(2000, 0), "TARGET", [new Port("in", PortKind.EthernetPoe, new Point2(0, 0))]));
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
connectedHistory.Execute(wired, new MoveEditHandleCommand(new EditHandleId(pipeId, EditHandleKind.Vertex, 1), new Point2(1000, -500)));
Require(wired.RequireCable(cableId).Route.Points[1] == new Point2(1000, -500), "Contained cable geometry must follow conduit vertex handles.");

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

string serialized = ProjectToml.Serialize(wired);
ProjectDocument loaded = ProjectToml.Deserialize(serialized);
string serializedAgain = ProjectToml.Serialize(loaded);
Require(serializedAgain == serialized, "TOML save/load/save must be byte-identical.");
Require(loaded.RequireCable(cableId).From == new PortReference(sourceId, "out"), "TOML must preserve typed port references.");
Require(loaded.RequireConduit(pipeId).Route.Points[1] == new Point2(1000, -500), "TOML must preserve edited geometry.");
Require(loaded.RequireAnnotation(noteId).Text == "BORRA HÄR", "TOML must preserve annotations.");
Require(loaded.SchemaVersion == 2 && loaded.Planning == new PlanningSettings(15, 500), "TOML must preserve versioned planning settings.");
Require(loaded.RequireCable(cableId).InstallationStatus == InstallationStatus.Tested, "TOML must preserve installation state.");
Require(loaded.RequireCable(cableId).ActualLengthMillimetres == 2350, "TOML must preserve actual installed length.");

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
var propertyHistory = new CommandHistory();
propertyHistory.Execute(garageDocument, DocumentProperties.CreateSetCommand(garageDocument, statusPropertyId, "tested"));
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).InstallationStatus == InstallationStatus.Tested, "Property handles must create real document commands.");
Require(propertyHistory.Undo(garageDocument), "Property-handle edits must be undoable.");
Require(garageDocument.RequireCable(ObjectId.Parse("camera-north-cat6")).InstallationStatus == InstallationStatus.Planned, "Property undo must restore the previous value.");
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
Require(svgXml.Descendants(svgNamespace + "g").Any(element => (string?)element.Attribute("id") == "camera-north"), "SVG export must contain device symbols with stable object IDs.");

byte[] garagePng = PngProjectExporter.Export(garageDocument);
Require(PngProjectExporter.Export(garageDocument).SequenceEqual(garagePng), "PNG export must be byte-deterministic.");
Require(garagePng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG export needs a valid PNG signature.");
Require(BinaryPrimitives.ReadInt32BigEndian(garagePng.AsSpan(16, 4)) == 1600, "Wide projects must use the configured maximum PNG width.");
Require(BinaryPrimitives.ReadInt32BigEndian(garagePng.AsSpan(20, 4)) > 0, "PNG export needs a positive calculated height.");
Require(Convert.ToHexString(SHA256.HashData(garagePng)).ToLowerInvariant() == "8c7db562b106a83b69f19e88f42998506b76dea138a667de23c55b1337d5184d", "Garage Draft PNG must retain its reference render hash.");

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
