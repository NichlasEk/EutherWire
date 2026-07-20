using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Templates;

public static class ProjectTemplates
{
    public static ProjectDocument CreateGarageDraft()
    {
        var document = new ProjectDocument("Garage Draft");
        document.Add(new BuildingOpening(
            ObjectId.Parse("garage-door-south"),
            OpeningKind.GarageDoor,
            MountingSurface.SouthWallInterior,
            new Point3(5500, 2500, 1100),
            5000,
            2200,
            "GARAGEPORT"));
        document.Add(new Device(
            ObjectId.Parse("main-board"),
            DeviceKind.DistributionBoard,
            new Point2(0, 0),
            "CENTRAL",
            [new Port("garage-feed", PortKind.MainsPower, new Point2(750, 0))]) { ElevationMillimetres = 1000 });
        document.Add(new Device(
            ObjectId.Parse("poe-switch"),
            DeviceKind.PoeSwitch,
            new Point2(6500, 800),
            "POE-SW",
            [
                new Port("uplink", PortKind.Ethernet, new Point2(-650, 0)),
                new Port("port-1", PortKind.EthernetPoe, new Point2(0, -350)),
            ]) { ElevationMillimetres = 2200 });
        document.Add(new Device(
            ObjectId.Parse("camera-north"),
            DeviceKind.Camera,
            new Point2(11200, -2600),
            "KAM-N",
            [new Port("eth0", PortKind.EthernetPoe, new Point2(-350, 0))]) { ElevationMillimetres = 2200 });

        Point3[] route = [new(6500, 450, 2200), new(6500, -2600, 2200), new(10850, -2600, 2200)];
        ObjectId conduitId = ObjectId.Parse("camera-north-pipe");
        document.Add(new Conduit(
            conduitId,
            "RÖR-R07",
            20.2,
            new Polyline(route),
            InstallationMethod.Concealed,
            25,
            "pipelife-halovolt-750n-25"));
        document.Add(new CableRoute(
            ObjectId.Parse("camera-north-cat6"),
            "KAM-N-CAT6",
            CableKind.Cat6,
            new Polyline(route),
            new PortReference(ObjectId.Parse("poe-switch"), "port-1"),
            new PortReference(ObjectId.Parse("camera-north"), "eth0"),
            conduitId));
        return document;
    }
}
