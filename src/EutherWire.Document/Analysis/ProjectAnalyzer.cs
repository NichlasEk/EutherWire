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
    int UnknownCableCount,
    int KnownConductorCount = 0,
    int UnknownConductorCount = 0);

public enum DesignCheckStatus { Pass, Warning, Unknown }

public sealed record ElectricalDesignCheck(
    ObjectId CableId,
    DesignCheckStatus Status,
    double? DesignCurrentAmperes,
    double? ProtectiveDeviceAmperes,
    double? CorrectedCurrentCarryingCapacityAmperes,
    string RuleProfileId,
    string Message);

public sealed record InstallationTask(
    ObjectId CableId,
    string Label,
    InstallationStatus Status,
    string From,
    string To,
    double PlannedLengthMillimetres,
    double? ActualLengthMillimetres);

public sealed record ProjectAnalysis(
    double TotalCableLengthMillimetres,
    double RecommendedCableLengthMillimetres,
    double? ActualCableLengthMillimetres,
    double TotalConduitLengthMillimetres,
    IReadOnlyList<MaterialItem> Materials,
    IReadOnlyList<ConduitFill> ConduitFills,
    IReadOnlyList<ElectricalDesignCheck> ElectricalDesignChecks,
    IReadOnlyList<InstallationTask> InstallationTasks,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
    public int CompletedInstallationCount => InstallationTasks.Count(task => task.Status is InstallationStatus.Installed or InstallationStatus.Tested);
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
            int unknownFillItems = fill.UnknownCableCount + fill.UnknownConductorCount;
            if (unknownFillItems > 0)
            {
                diagnostics.Add(new ProjectDiagnostic(
                    "conduit.fill.unknown",
                    DiagnosticSeverity.Info,
                    conduit.Id,
                    $"Conduit '{conduit.Label}' contains {unknownFillItems} cable(s) or conductor(s) without a known outside diameter."));
            }
        }

        IReadOnlyList<ElectricalDesignCheck> designChecks = BuildElectricalDesignChecks(document, diagnostics);

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
            designChecks,
            BuildInstallationTasks(document),
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
        CheckElectrical(cable, diagnostics);
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
        if (cable.InstallationStatus is InstallationStatus.Installed or InstallationStatus.Tested && cable.ActualLengthMillimetres is null)
        {
            diagnostics.Add(new ProjectDiagnostic(
                "installation.length.missing",
                DiagnosticSeverity.Warning,
                cable.Id,
                $"Cable '{cable.Label}' is {cable.InstallationStatus.ToString().ToLowerInvariant()} but has no measured installed length."));
        }
        if (cable.InstallationStatus == InstallationStatus.Blocked)
        {
            diagnostics.Add(new ProjectDiagnostic(
                "installation.blocked",
                DiagnosticSeverity.Info,
                cable.Id,
                $"Cable '{cable.Label}' is blocked and needs field follow-up."));
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

    private static void CheckElectrical(CableRoute cable, List<ProjectDiagnostic> diagnostics)
    {
        ElectricalCableSpec electrical = cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind);
        bool Has(ConductorFunction function) => electrical.Conductors.Any(item => item.Function == function);
        void Require(ConductorFunction function, string code)
        {
            if (!Has(function)) diagnostics.Add(new ProjectDiagnostic(code, DiagnosticSeverity.Error, cable.Id,
                $"Cable '{cable.Label}' uses {electrical.Preset} but has no {function} conductor."));
        }
        if (electrical.Preset is CircuitPreset.SinglePhase or CircuitPreset.Lighting)
        {
            Require(ConductorFunction.Line1, "electrical.line.missing");
            Require(ConductorFunction.Neutral, "electrical.neutral.missing");
            Require(ConductorFunction.ProtectiveEarth, "electrical.pe.missing");
        }
        if (electrical.Preset is CircuitPreset.ThreePhase or CircuitPreset.ThreePhaseNeutral)
        {
            Require(ConductorFunction.Line1, "electrical.l1.missing");
            Require(ConductorFunction.Line2, "electrical.l2.missing");
            Require(ConductorFunction.Line3, "electrical.l3.missing");
            Require(ConductorFunction.ProtectiveEarth, "electrical.pe.missing");
            if (electrical.Preset == CircuitPreset.ThreePhaseNeutral) Require(ConductorFunction.Neutral, "electrical.neutral.missing");
        }
        foreach (ConductorSpec conductor in electrical.Conductors)
        {
            if (conductor.Function == ConductorFunction.Neutral && !string.Equals(conductor.Colour, "blue", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new ProjectDiagnostic("electrical.neutral.colour", DiagnosticSeverity.Warning, cable.Id, $"Neutral conductor '{conductor.Id}' in '{cable.Label}' is not blue."));
            if (conductor.Function == ConductorFunction.ProtectiveEarth && !string.Equals(conductor.Colour, "green_yellow", StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new ProjectDiagnostic("electrical.pe.colour", DiagnosticSeverity.Error, cable.Id, $"Protective-earth conductor '{conductor.Id}' in '{cable.Label}' is not green/yellow."));
            if (conductor.Function == ConductorFunction.SwitchedLive && string.IsNullOrWhiteSpace(conductor.TerminalLabel))
                diagnostics.Add(new ProjectDiagnostic("electrical.switched_live.label", DiagnosticSeverity.Warning, cable.Id, $"Switched live '{conductor.Id}' in '{cable.Label}' needs a control label."));
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
        int knownConductors = 0;
        int unknownConductors = 0;
        foreach (CableRoute cable in document.Cables.Values.Where(cable => cable.ConduitId == conduit.Id))
        {
            ElectricalCableSpec electrical = cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind);
            if (electrical.Product is CableProductKind.Fk or CableProductKind.Rk)
            {
                foreach (ConductorSpec conductor in electrical.Conductors)
                {
                    if (conductor.OutsideDiameterMillimetres is double conductorDiameter)
                    {
                        occupiedDiameterSquared += conductorDiameter * conductorDiameter;
                        knownConductors++;
                    }
                    else
                    {
                        unknownConductors++;
                    }
                }
            }
            else if (electrical.OutsideDiameterMillimetres is double exactDiameter)
            {
                occupiedDiameterSquared += exactDiameter * exactDiameter;
                known++;
            }
            else if (ApproximateCableDiameters.TryGetValue(cable.Kind, out double diameter))
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
        return new ConduitFill(conduit.Id, ratio, known, unknown, knownConductors, unknownConductors);
    }

    private static IReadOnlyList<ElectricalDesignCheck> BuildElectricalDesignChecks(ProjectDocument document, List<ProjectDiagnostic> diagnostics)
    {
        var checks = new List<ElectricalDesignCheck>();
        foreach (CableRoute cable in document.Cables.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            ElectricalCableSpec electrical = cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind);
            if (electrical.Preset is CircuitPreset.Data or CircuitPreset.Custom || electrical.Design is null)
            {
                if (electrical.Preset is not (CircuitPreset.Data or CircuitPreset.Custom))
                {
                    AddUnknown(cable, document, diagnostics, checks, "Circuit design current, protective device and verified reference capacity are required.");
                }
                continue;
            }

            CircuitDesign design = electrical.Design;
            if (design.DesignCurrentAmperes is not double ib ||
                design.ProtectiveDeviceAmperes is not double protectiveDevice ||
                design.CorrectedCurrentCarryingCapacityAmperes is not double iz ||
                string.IsNullOrWhiteSpace(design.ReferenceSource))
            {
                AddUnknown(cable, document, diagnostics, checks, "Thermal result is unknown until Ib, protective device, reference capacity and its source are supplied.");
                continue;
            }

            bool passes = ib <= protectiveDevice && protectiveDevice <= iz;
            string firstRelation = ib <= protectiveDevice ? "≤" : ">";
            string secondRelation = protectiveDevice <= iz ? "≤" : ">";
            string relation = $"Ib {ib:0.##} A {firstRelation} In {protectiveDevice:0.##} A {secondRelation} Iz {iz:0.##} A";
            var check = new ElectricalDesignCheck(cable.Id, passes ? DesignCheckStatus.Pass : DesignCheckStatus.Warning,
                ib, protectiveDevice, iz, document.ElectricalRules.Id,
                $"{relation}; source: {design.ReferenceSource}.");
            checks.Add(check);
            if (!passes)
            {
                diagnostics.Add(new ProjectDiagnostic("sizing.current_capacity.failed", DiagnosticSeverity.Warning, cable.Id,
                    $"Cable '{cable.Label}' fails the planning current-capacity relation: {relation}. Electrician verification required."));
            }
        }
        return checks;
    }

    private static void AddUnknown(CableRoute cable, ProjectDocument document, List<ProjectDiagnostic> diagnostics, List<ElectricalDesignCheck> checks, string message)
    {
        checks.Add(new ElectricalDesignCheck(cable.Id, DesignCheckStatus.Unknown, null, null, null, document.ElectricalRules.Id, message));
        diagnostics.Add(new ProjectDiagnostic("sizing.current_capacity.unknown", DiagnosticSeverity.Info, cable.Id,
            $"Cable '{cable.Label}': {message}"));
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
        materials.AddRange(document.Cables.Values
            .Select(cable => (Cable: cable, Electrical: cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)))
            .Where(item => item.Electrical.Product is CableProductKind.Rk or CableProductKind.Fk)
            .SelectMany(item => item.Electrical.Conductors.Select(conductor => (item.Cable, item.Electrical.Product, Conductor: conductor)))
            .GroupBy(item => (item.Product, item.Conductor.AreaSquareMillimetres, item.Conductor.Colour, item.Conductor.Function))
            .OrderBy(group => group.Key.Product).ThenBy(group => group.Key.AreaSquareMillimetres).ThenBy(group => group.Key.Colour)
            .Select(group => new MaterialItem(
                "conductor",
                $"{group.Key.Product}-{group.Key.AreaSquareMillimetres:0.###}-{group.Key.Colour}-{group.Key.Function}",
                $"{group.Key.Product} {group.Key.AreaSquareMillimetres:0.###} mm² {group.Key.Colour} · {group.Key.Function}",
                group.Sum(item => RecommendedLength(document, item.Cable)) / 1000,
                "m")));
        return materials;
    }

    private static IReadOnlyList<InstallationTask> BuildInstallationTasks(ProjectDocument document) =>
        document.Cables.Values
            .OrderBy(cable => cable.Id.Value, StringComparer.Ordinal)
            .Select(cable => new InstallationTask(
                cable.Id,
                cable.Label,
                cable.InstallationStatus,
                Endpoint(document, cable.From),
                Endpoint(document, cable.To),
                RecommendedLength(document, cable),
                cable.ActualLengthMillimetres))
            .ToList();

    private static string Endpoint(ProjectDocument document, PortReference? reference)
    {
        if (reference is not PortReference endpoint) return "loose";
        return document.Devices.TryGetValue(endpoint.DeviceId, out Device? device)
            ? $"{device.Label}:{endpoint.PortId}"
            : $"{endpoint.DeviceId}:{endpoint.PortId}";
    }

    private static double RecommendedLength(ProjectDocument document, CableRoute cable) =>
        cable.Route.LengthMillimetres * (1 + document.Planning.CableSlackPercent / 100) + document.Planning.ServiceLoopMillimetres;
}
