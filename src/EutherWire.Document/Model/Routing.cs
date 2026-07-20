using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public readonly record struct PortReference(ObjectId DeviceId, string PortId);

public enum CableKind
{
    Custom,
    Cat6,
    Cat6A,
    Mains3G25,
    Mains5G6,
    FibreDuplex,
    Coax,
    LowVoltageDc,
}

public enum InstallationStatus
{
    Planned,
    Installed,
    Tested,
    Changed,
    Blocked,
}

public sealed record CableRoute(
    ObjectId Id,
    string Label,
    CableKind Kind,
    Polyline Route,
    PortReference? From = null,
    PortReference? To = null,
    ObjectId? ConduitId = null,
    InstallationStatus InstallationStatus = InstallationStatus.Planned,
    double? ActualLengthMillimetres = null,
    ElectricalCableSpec? Electrical = null);

public enum InstallationMethod
{
    Unknown,
    Surface,
    Concealed,
    Buried,
    CableTray,
}

public sealed record Conduit(
    ObjectId Id,
    string Label,
    double InnerDiameterMillimetres,
    Polyline Route,
    InstallationMethod InstallationMethod = InstallationMethod.Unknown,
    double? NominalDiameterMillimetres = null);
