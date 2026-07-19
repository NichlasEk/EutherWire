using EutherWire.Document.Commands;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using EutherWire.Document.Serialization;

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

string serialized = ProjectToml.Serialize(wired);
ProjectDocument loaded = ProjectToml.Deserialize(serialized);
string serializedAgain = ProjectToml.Serialize(loaded);
Require(serializedAgain == serialized, "TOML save/load/save must be byte-identical.");
Require(loaded.RequireCable(cableId).From == new PortReference(sourceId, "out"), "TOML must preserve typed port references.");
Require(loaded.RequireConduit(pipeId).Route.Points[1] == new Point2(1000, -500), "TOML must preserve edited geometry.");

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

Console.WriteLine("EutherWire document checks passed.");
