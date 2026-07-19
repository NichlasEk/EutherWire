using EutherWire.Document.Model;

namespace EutherWire.Document.Analysis;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ProjectDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    ObjectId? ObjectId,
    string Message);

public sealed record MaterialItem(
    string Category,
    string Key,
    string Description,
    double Quantity,
    string Unit);

public sealed record ConduitFill(
    ObjectId ConduitId,
    double FillRatio,
    int KnownCableCount,
    int UnknownCableCount);

public sealed record ProjectAnalysis(
    double TotalCableLengthMillimetres,
    double RecommendedCableLengthMillimetres,
    double? ActualCableLengthMillimetres,
    double TotalConduitLengthMillimetres,
    IReadOnlyList<MaterialItem> Materials,
    IReadOnlyList<ConduitFill> ConduitFills,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
}

public static class ProjectAnalyzer
{
    private const double FillWarningRatio = 0.40;

    private static readonly IReadOnlyDictionary<CableKind, double> ApproximateCableDiameters =
        new Dictionary<CableKind, double>
        {
            [CableKind.Cat6] = 6.2,
            [CableKind.Cat6A] = 7.5,
            [CableKind.Mains3G25] = 10,
            [CableKind.Mains5G6] = 16,
            [CableKind.FibreDuplex] = 5,
            [CableKind.Coax] = 7,
            [CableKind.LowVoltageDc] = 5,
        };

    public static ProjectAnalysis Analyze(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = new List<ProjectDiagnostic>();
        CheckDuplicateLabels(document, diagnostics);

        foreach (CableRoute cable in document.Cables.Values.OrderBy(cable => cable.Id.ToString(), StringComparer.Ordinal))
        {
            CheckCable(document, cable, diagnostics);
        }

        var fills = new List<ConduitFill>();
        foreach (Conduit conduit in document.Conduits.Values.OrderBy(conduit => conduit.Id.ToString(), StringComparer.Ordinal))
        {
            ConduitFill fill = CalculateFill(document, conduit);
            fills.Add(fill);
            if (fill.FillRatio > FillWarningRatio)
            {
                diagnostics.Add(new ProjectDiagnostic(
                    "conduit.fill.high",
                    DiagnosticSeverity.Warning,
                    conduit.Id,
                    $"Conduit '{conduit.Label}' has an estimated fill of {fill.FillRatio:P0}; planning threshold is {FillWarningRatio:P0}."));
            }
            if (fill.UnknownCableCount > 0)
            {
                diagnostics.Add(new ProjectDiagnostic(
                    "conduit.fill.unknown",
                    DiagnosticSeverity.Info,
                    conduit.Id,
                    $"Conduit '{conduit.Label}' contains {fill.UnknownCableCount} cable(s) without a known outside diameter."));
            }
        }

        double cableLength = document.Cables.Values.Sum(cable => cable.Route.LengthMillimetres);
        double recommendedCableLength = document.Cables.Values.Sum(cable => RecommendedLength(document, cable));
        double? actualCableLength = document.Cables.Values.Any(cable => cable.ActualLengthMillimetres.HasValue)
            ? document.Cables.Values.Sum(cable => cable.ActualLengthMillimetres ?? 0)
            : null;
        double conduitLength = document.Conduits.Values.Sum(conduit => conduit.Route.LengthMillimetres);

        return new ProjectAnalysis(
            cableLength,
            recommendedCableLength,
            actualCableLength,
            conduitLength,
            BuildMaterials(document),
            fills,
            diagnostics);
    }

    private static void CheckDuplicateLabels(ProjectDocument document, List<ProjectDiagnostic> diagnostics)
    {
        IEnumerable<(ObjectId Id, string Label)> labels =
            document.Devices.Values.Select(item => (item.Id, item.Label))
                .Concat(document.Cables.Values.Select(item => (item.Id, item.Label)))
                .Concat(document.Conduits.Values.Select(item => (item.Id, item.Label)))
                .Concat(document.Annotations.Values.Select(item => (item.Id, item.Text)));

        foreach (IGrouping<string, (ObjectId Id, string Label)> group in labels
                     .Where(item => !string.IsNullOrWhiteSpace(item.Label))
                     .GroupBy(item => item.Label.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            string ids = string.Join(", ", group.Select(item => item.Id));
            foreach ((ObjectId id, _) in group)
            {
                diagnostics.Add(new ProjectDiagnostic(
                    "label.duplicate",
                    DiagnosticSeverity.Warning,
                    id,
                    $"Label '{group.Key}' is used by multiple objects: {ids}."));
            }
        }
    }

    private static void CheckCable(ProjectDocument document, CableRoute cable, List<ProjectDiagnostic> diagnostics)
    {
        Port? from = ResolvePort(document, cable, cable.From, "start", diagnostics);
        Port? to = ResolvePort(document, cable, cable.To, "end", diagnostics);

        if (from is not null && !IsCompatible(cable.Kind, from.Kind))
        {
            AddPortMismatch(cable, from, "start", diagnostics);
        }
        if (to is not null && !IsCompatible(cable.Kind, to.Kind))
        {
            AddPortMismatch(cable, to, "end", diagnostics);
        }
        if (to?.Kind == PortKind.EthernetPoe && from?.Kind != PortKind.EthernetPoe)
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.poe.source",
                DiagnosticSeverity.Warning,
                cable.Id,
                $"Cable '{cable.Label}' feeds a PoE port but does not start at a PoE-capable port."));
        }

        if (cable.ConduitId is not ObjectId conduitId)
        {
            return;
        }
        if (!document.Conduits.TryGetValue(conduitId, out Conduit? conduit))
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.conduit.missing",
                DiagnosticSeverity.Error,
                cable.Id,
                $"Cable '{cable.Label}' references missing conduit '{conduitId}'."));
            return;
        }
        if (!RoutesMatch(cable.Route, conduit.Route))
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.conduit.geometry",
                DiagnosticSeverity.Error,
                cable.Id,
                $"Cable '{cable.Label}' geometry differs from conduit '{conduit.Label}'."));
        }
    }

    private static Port? ResolvePort(
        ProjectDocument document,
        CableRoute cable,
        PortReference? reference,
        string endpoint,
        List<ProjectDiagnostic> diagnostics)
    {
        if (reference is not PortReference portReference)
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.endpoint.loose",
                DiagnosticSeverity.Warning,
                cable.Id,
                $"Cable '{cable.Label}' has a loose {endpoint} endpoint."));
            return null;
        }
        if (!document.Devices.TryGetValue(portReference.DeviceId, out Device? device))
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.device.missing",
                DiagnosticSeverity.Error,
                cable.Id,
                $"Cable '{cable.Label}' references missing device '{portReference.DeviceId}'."));
            return null;
        }
        Port? port = device.Ports.FirstOrDefault(candidate => candidate.Id == portReference.PortId);
        if (port is null)
        {
            diagnostics.Add(new ProjectDiagnostic(
                "cable.port.missing",
                DiagnosticSeverity.Error,
                cable.Id,
                $"Cable '{cable.Label}' references missing port '{portReference.PortId}' on '{device.Label}'."));
        }
        return port;
    }

    private static bool IsCompatible(CableKind cable, PortKind port) => cable switch
    {
        CableKind.Custom => true,
        CableKind.Cat6 or CableKind.Cat6A => port is PortKind.Ethernet or PortKind.EthernetPoe,
        CableKind.Mains3G25 or CableKind.Mains5G6 => port == PortKind.MainsPower,
        CableKind.FibreDuplex => port == PortKind.Fibre,
        CableKind.Coax => port == PortKind.Coax,
        CableKind.LowVoltageDc => port == PortKind.LowVoltageDc,
        _ => false,
    };

    private static void AddPortMismatch(CableRoute cable, Port port, string endpoint, List<ProjectDiagnostic> diagnostics) =>
        diagnostics.Add(new ProjectDiagnostic(
            "cable.port.type",
            DiagnosticSeverity.Error,
            cable.Id,
            $"Cable '{cable.Label}' type {cable.Kind} does not match its {endpoint} port type {port.Kind}."));

    private static bool RoutesMatch(EutherWire.Document.Geometry.Polyline first, EutherWire.Document.Geometry.Polyline second) =>
        first.Points.SequenceEqual(second.Points);

    private static ConduitFill CalculateFill(ProjectDocument document, Conduit conduit)
    {
        double occupiedDiameterSquared = 0;
        int known = 0;
        int unknown = 0;
        foreach (CableRoute cable in document.Cables.Values.Where(cable => cable.ConduitId == conduit.Id))
        {
            if (ApproximateCableDiameters.TryGetValue(cable.Kind, out double diameter))
            {
                occupiedDiameterSquared += diameter * diameter;
                known++;
            }
            else
            {
                unknown++;
            }
        }
        double ratio = conduit.InnerDiameterMillimetres > 0
            ? occupiedDiameterSquared / (conduit.InnerDiameterMillimetres * conduit.InnerDiameterMillimetres)
            : 0;
        return new ConduitFill(conduit.Id, ratio, known, unknown);
    }

    private static IReadOnlyList<MaterialItem> BuildMaterials(ProjectDocument document)
    {
        var materials = new List<MaterialItem>();
        materials.AddRange(document.Devices.Values
            .GroupBy(device => device.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new MaterialItem("device", group.Key.ToString(), group.Key.ToString(), group.Count(), "pcs")));
        materials.AddRange(document.Cables.Values
            .GroupBy(cable => cable.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new MaterialItem(
                "cable",
                group.Key.ToString(),
                group.Key.ToString(),
                group.Sum(cable => RecommendedLength(document, cable)) / 1000,
                "m")));
        materials.AddRange(document.Conduits.Values
            .GroupBy(conduit => conduit.InnerDiameterMillimetres)
            .OrderBy(group => group.Key)
            .Select(group => new MaterialItem(
                "conduit",
                $"{group.Key:0.###}",
                $"Conduit {group.Key:0.###} mm",
                group.Sum(conduit => conduit.Route.LengthMillimetres) / 1000,
                "m")));
        return materials;
    }

    private static double RecommendedLength(ProjectDocument document, CableRoute cable) =>
        cable.Route.LengthMillimetres * (1 + document.Planning.CableSlackPercent / 100) + document.Planning.ServiceLoopMillimetres;
}
