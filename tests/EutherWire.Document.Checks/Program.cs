using EutherWire.Document.Commands;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

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

Console.WriteLine("EutherWire document checks passed.");
