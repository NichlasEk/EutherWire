using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public enum DeviceKind
{
    DistributionBoard,
    JunctionBox,
    Outlet,
    Camera,
    PoeSwitch,
    AccessPoint,
    PatchPanel,
    Light,
    Custom,
}

public enum PortKind
{
    Generic,
    MainsPower,
    LowVoltageDc,
    Ethernet,
    EthernetPoe,
    Fibre,
    Coax,
    ProtectiveEarth,
}

public sealed record Port(string Id, PortKind Kind, Point2 Position);

public sealed class Device
{
    public Device(ObjectId id, DeviceKind kind, Point2 position, string label, IEnumerable<Port>? ports = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = id;
        Kind = kind;
        Position = position;
        Label = label;
        Ports = ports?.ToList() ?? [];
        if (Ports.Select(port => port.Id).Distinct(StringComparer.Ordinal).Count() != Ports.Count)
        {
            throw new ArgumentException("Port IDs must be unique within a device.", nameof(ports));
        }
    }

    public ObjectId Id { get; }
    public DeviceKind Kind { get; internal set; }
    public Point2 Position { get; internal set; }
    public double RotationDegrees { get; internal set; }
    public double ElevationMillimetres { get; internal set; }
    public string Label { get; set; }
    public List<Port> Ports { get; }
}
