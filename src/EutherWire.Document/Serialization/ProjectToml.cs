using System.Text.Json.Serialization;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using Tomlyn;

namespace EutherWire.Document.Serialization;

public static class ProjectToml
{
    public const string FileName = "project.toml";

    public static string Serialize(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var file = new ProjectFile
        {
            Project = new ProjectMetadata
            {
                Name = document.Name,
                Units = "mm",
                SchemaVersion = document.SchemaVersion,
                CableSlackPercent = document.Planning.CableSlackPercent,
                ServiceLoopMillimetres = document.Planning.ServiceLoopMillimetres,
            },
            ElectricalRules = new ElectricalRulesFile
            {
                Id = document.ElectricalRules.Id,
                InstallationStandard = document.ElectricalRules.InstallationStandard,
                InstallationStandardEdition = document.ElectricalRules.InstallationStandardEdition,
                CableSizingGuide = document.ElectricalRules.CableSizingGuide,
                CableSizingGuideEdition = document.ElectricalRules.CableSizingGuideEdition,
            },
            Space = new SpaceFile
            {
                Origin = [document.Space.Origin.X, document.Space.Origin.Y],
                WidthMillimetres = document.Space.WidthMillimetres,
                DepthMillimetres = document.Space.DepthMillimetres,
                HeightMillimetres = document.Space.HeightMillimetres,
                WallThicknessMillimetres = document.Space.WallThicknessMillimetres,
                CeilingThicknessMillimetres = document.Space.CeilingThicknessMillimetres,
            },
            Openings = document.Openings.Values
                .OrderBy(opening => opening.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
            Devices = document.Devices.Values
                .OrderBy(device => device.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
            Conduits = document.Conduits.Values
                .OrderBy(conduit => conduit.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
            Cables = document.Cables.Values
                .OrderBy(cable => cable.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
            Annotations = document.Annotations.Values
                .OrderBy(annotation => annotation.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
            WallDimensions = document.WallDimensions.Values
                .OrderBy(dimension => dimension.Id.Value, StringComparer.Ordinal)
                .Select(ToFile)
                .ToList(),
        };
        return TomlSerializer.Serialize(file).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static ProjectDocument Deserialize(string toml)
    {
        ArgumentNullException.ThrowIfNull(toml);
        ProjectFile file;
        try
        {
            file = TomlSerializer.Deserialize<ProjectFile>(toml)
                ?? throw new ProjectFormatException("The TOML document is empty.");
        }
        catch (ProjectFormatException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProjectFormatException("Could not parse project.toml.", exception);
        }

        if (file.Project is null)
        {
            throw new ProjectFormatException("Missing [project] table.");
        }
        if (file.Project.SchemaVersion is < 1 or > 8)
        {
            throw new ProjectFormatException($"Unsupported schema_version {file.Project.SchemaVersion}; expected 1 through 8.");
        }
        if (!string.Equals(file.Project.Units, "mm", StringComparison.Ordinal))
        {
            throw new ProjectFormatException($"Unsupported units '{file.Project.Units}'; expected 'mm'.");
        }

        var document = new ProjectDocument(RequireText(file.Project.Name, "project.name"))
        {
            Planning = new PlanningSettings(
                NonNegative(file.Project.CableSlackPercent, "project.cable_slack_percent", 100),
                NonNegative(file.Project.ServiceLoopMillimetres, "project.service_loop_mm")),
        };
        if (file.ElectricalRules is not null)
        {
            document.ElectricalRules = new ElectricalRuleProfile(
                RequireText(file.ElectricalRules.Id, "electrical_rules.id"),
                RequireText(file.ElectricalRules.InstallationStandard, "electrical_rules.installation_standard"),
                RequireText(file.ElectricalRules.InstallationStandardEdition, "electrical_rules.installation_standard_edition"),
                RequireText(file.ElectricalRules.CableSizingGuide, "electrical_rules.cable_sizing_guide"),
                RequireText(file.ElectricalRules.CableSizingGuideEdition, "electrical_rules.cable_sizing_guide_edition"));
        }
        if (file.Space is not null)
        {
            document.Space = new SpaceVolume(
                Point(file.Space.Origin, "space.origin"),
                Positive(file.Space.WidthMillimetres, "space.width_mm"),
                Positive(file.Space.DepthMillimetres, "space.depth_mm"),
                Positive(file.Space.HeightMillimetres, "space.height_mm"),
                Positive(file.Space.WallThicknessMillimetres, "space.wall_thickness_mm"),
                Positive(file.Space.CeilingThicknessMillimetres, "space.ceiling_thickness_mm")).Validate();
        }
        foreach (OpeningFile source in file.Openings)
        {
            document.Add(new BuildingOpening(
                Id(source.Id, "opening"),
                ParseEnum<OpeningKind>(source.Kind, "opening kind"),
                ParseEnum<MountingSurface>(source.Surface, "opening surface"),
                SpatialPoint(source.Centre, $"openings[{source.Id}].centre"),
                Positive(source.WidthMillimetres, $"openings[{source.Id}].width_mm"),
                Positive(source.HeightMillimetres, $"openings[{source.Id}].height_mm"),
                RequireText(source.Label, $"openings[{source.Id}].label")));
        }
        foreach (DeviceFile source in file.Devices)
        {
            var ports = source.Ports.Select(port => new Port(
                RequireText(port.Id, $"devices[{source.Id}].ports.id"),
                ParseEnum<PortKind>(port.Kind, "port kind"),
                Point(port.Position, $"devices[{source.Id}].ports[{port.Id}].position")));
            var device = new Device(
                Id(source.Id, "device"),
                ParseEnum<DeviceKind>(source.Kind, "device kind"),
                Point(source.Position, $"devices[{source.Id}].position"),
                RequireText(source.Label, $"devices[{source.Id}].label"),
                ports)
            {
                RotationDegrees = source.RotationDegrees,
                ElevationMillimetres = NonNegative(source.ElevationMillimetres, $"devices[{source.Id}].elevation_mm"),
                MountingSurface = ParseEnum<MountingSurface>(source.MountingSurface, "mounting surface"),
            };
            document.Add(device);
        }
        foreach (ConduitFile source in file.Conduits)
        {
            document.Add(new Conduit(
                Id(source.Id, "conduit"),
                RequireText(source.Label, $"conduits[{source.Id}].label"),
                Positive(source.InnerDiameterMillimetres, $"conduits[{source.Id}].inner_diameter_mm"),
                Polyline(source.Points, $"conduits[{source.Id}].points"),
                ParseEnum<InstallationMethod>(source.InstallationMethod, "installation method")));
        }
        foreach (CableFile source in file.Cables)
        {
            document.Add(new CableRoute(
                Id(source.Id, "cable"),
                RequireText(source.Label, $"cables[{source.Id}].label"),
                ParseEnum<CableKind>(source.Kind, "cable kind"),
                Polyline(source.Points, $"cables[{source.Id}].points"),
                PortReference(source.From),
                PortReference(source.To),
                string.IsNullOrWhiteSpace(source.Conduit) ? null : Id(source.Conduit, "conduit reference"),
                ParseEnum<InstallationStatus>(source.InstallationStatus, "installation status"),
                OptionalNonNegative(source.ActualLengthMillimetres, $"cables[{source.Id}].actual_length_mm"),
                Electrical(source)));
        }
        foreach (AnnotationFile source in file.Annotations)
        {
            document.Add(new Annotation(
                Id(source.Id, "annotation"),
                Point(source.Position, $"annotations[{source.Id}].position"),
                RequireText(source.Text, $"annotations[{source.Id}].text")));
        }
        foreach (WallDimensionFile source in file.WallDimensions)
        {
            MountingSurface surface = ParseEnum<MountingSurface>(source.Surface, "wall dimension surface");
            Point3 start = SpatialPoint(source.Start, $"wall_dimensions[{source.Id}].start");
            Point3 end = SpatialPoint(source.End, $"wall_dimensions[{source.Id}].end");
            if (!WallCoordinateSystem.IsOnWall(document.Space, surface, start) || !WallCoordinateSystem.IsOnWall(document.Space, surface, end))
            {
                throw new ProjectFormatException($"Wall dimension '{source.Id}' endpoints must lie on {surface}.");
            }
            document.Add(new WallDimension(
                Id(source.Id, "wall dimension"),
                surface,
                start,
                end,
                source.Label ?? string.Empty));
        }
        ValidateReferences(document);
        return document;
    }

    public static void Save(string projectDirectory, ProjectDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        Directory.CreateDirectory(projectDirectory);
        string path = Path.Combine(projectDirectory, FileName);
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(document));
        File.Move(temporaryPath, path, true);
    }

    public static ProjectDocument Load(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        return Deserialize(File.ReadAllText(Path.Combine(projectDirectory, FileName)));
    }

    private static DeviceFile ToFile(Device device) => new()
    {
        Id = device.Id.Value,
        Kind = Name(device.Kind),
        Label = device.Label,
        Position = [device.Position.X, device.Position.Y],
        RotationDegrees = device.RotationDegrees,
        ElevationMillimetres = device.ElevationMillimetres,
        MountingSurface = Name(device.MountingSurface),
        Ports = device.Ports.Select(port => new PortFile
        {
            Id = port.Id,
            Kind = Name(port.Kind),
            Position = [port.Position.X, port.Position.Y],
        }).ToList(),
    };

    private static OpeningFile ToFile(BuildingOpening opening) => new()
    {
        Id = opening.Id.Value,
        Kind = Name(opening.Kind),
        Surface = Name(opening.Surface),
        Centre = [opening.Centre.X, opening.Centre.Y, opening.Centre.Z],
        WidthMillimetres = opening.WidthMillimetres,
        HeightMillimetres = opening.HeightMillimetres,
        Label = opening.Label,
    };

    private static ConduitFile ToFile(Conduit conduit) => new()
    {
        Id = conduit.Id.Value,
        Label = conduit.Label,
        InnerDiameterMillimetres = conduit.InnerDiameterMillimetres,
        InstallationMethod = Name(conduit.InstallationMethod),
        Points = conduit.Route.SpatialPoints.Select(point => new[] { point.X, point.Y, point.Z }).ToList(),
    };

    private static CableFile ToFile(CableRoute cable) => new()
    {
        Id = cable.Id.Value,
        Label = cable.Label,
        Kind = Name(cable.Kind),
        Points = cable.Route.SpatialPoints.Select(point => new[] { point.X, point.Y, point.Z }).ToList(),
        From = cable.From is PortReference from ? $"{from.DeviceId}:{from.PortId}" : null,
        To = cable.To is PortReference to ? $"{to.DeviceId}:{to.PortId}" : null,
        Conduit = cable.ConduitId?.Value,
        InstallationStatus = Name(cable.InstallationStatus),
        ActualLengthMillimetres = cable.ActualLengthMillimetres,
        Product = Name((cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).Product),
        Circuit = Name((cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).Preset),
        Shielding = (cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).Shielding,
        PoeCapable = (cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).PoeCapable,
        OutsideDiameterMillimetres = (cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).OutsideDiameterMillimetres,
        Design = ToFile((cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).Design),
        Conductors = (cable.Electrical ?? ElectricalCableProfiles.Infer(cable.Kind)).Conductors.Select(item => new ConductorFile
        {
            Id = item.Id,
            Function = Name(item.Function),
            Colour = item.Colour,
            AreaSquareMillimetres = item.AreaSquareMillimetres,
            OutsideDiameterMillimetres = item.OutsideDiameterMillimetres,
            TerminalLabel = item.TerminalLabel,
        }).ToList(),
    };

    private static ElectricalCableSpec Electrical(CableFile source)
    {
        CableKind legacyKind = ParseEnum<CableKind>(source.Kind, "cable kind");
        if (string.IsNullOrWhiteSpace(source.Product) || source.Conductors.Count == 0) return ElectricalCableProfiles.Infer(legacyKind);
        return new ElectricalCableSpec(
            ParseEnum<CableProductKind>(source.Product, "cable product"),
            ParseEnum<CircuitPreset>(source.Circuit, "circuit preset"),
            source.Conductors.Select(item => new ConductorSpec(
                RequireText(item.Id, "conductor id"),
                ParseEnum<ConductorFunction>(item.Function, "conductor function"),
                RequireText(item.Colour, "conductor colour"),
                Positive(item.AreaSquareMillimetres, "conductor area_mm2"),
                item.OutsideDiameterMillimetres is double diameter ? Positive(diameter, "conductor outside_diameter_mm") : null,
                item.TerminalLabel)),
            string.IsNullOrWhiteSpace(source.Shielding) ? "none" : source.Shielding,
            source.PoeCapable,
            source.OutsideDiameterMillimetres is double outsideDiameter ? Positive(outsideDiameter, "cable outside_diameter_mm") : null,
            CircuitDesign(source.Design));
    }

    private static CircuitDesignFile? ToFile(CircuitDesign? design) => design is null ? null : new CircuitDesignFile
    {
        NominalVoltageVolts = design.NominalVoltageVolts,
        PhaseCount = design.PhaseCount,
        ConductorMaterial = Name(design.ConductorMaterial),
        LoadedConductorCount = design.LoadedConductorCount,
        DesignCurrentAmperes = design.DesignCurrentAmperes,
        ProtectiveDeviceAmperes = design.ProtectiveDeviceAmperes,
        ProtectiveDeviceCharacteristic = design.ProtectiveDeviceCharacteristic,
        ReferenceCurrentCarryingCapacityAmperes = design.ReferenceCurrentCarryingCapacityAmperes,
        AmbientCorrectionFactor = design.AmbientCorrectionFactor,
        GroupingCorrectionFactor = design.GroupingCorrectionFactor,
        ThermalInsulationCorrectionFactor = design.ThermalInsulationCorrectionFactor,
        ReferenceSource = design.ReferenceSource,
    };

    private static CircuitDesign? CircuitDesign(CircuitDesignFile? source) => source is null ? null : new CircuitDesign(
        Positive(source.NominalVoltageVolts, "design.nominal_voltage_v"),
        source.PhaseCount,
        ParseEnum<ConductorMaterial>(source.ConductorMaterial, "design.conductor_material"),
        source.LoadedConductorCount,
        OptionalPositive(source.DesignCurrentAmperes, "design.design_current_a"),
        OptionalPositive(source.ProtectiveDeviceAmperes, "design.protective_device_a"),
        source.ProtectiveDeviceCharacteristic,
        OptionalPositive(source.ReferenceCurrentCarryingCapacityAmperes, "design.reference_capacity_a"),
        Positive(source.AmbientCorrectionFactor, "design.ambient_factor"),
        Positive(source.GroupingCorrectionFactor, "design.grouping_factor"),
        Positive(source.ThermalInsulationCorrectionFactor, "design.thermal_insulation_factor"),
        source.ReferenceSource);

    private static AnnotationFile ToFile(Annotation annotation) => new()
    {
        Id = annotation.Id.Value,
        Position = [annotation.Position.X, annotation.Position.Y],
        Text = annotation.Text,
    };

    private static WallDimensionFile ToFile(WallDimension dimension) => new()
    {
        Id = dimension.Id.Value,
        Surface = Name(dimension.Surface),
        Start = [dimension.Start.X, dimension.Start.Y, dimension.Start.Z],
        End = [dimension.End.X, dimension.End.Y, dimension.End.Z],
        Label = dimension.Label,
    };

    private static void ValidateReferences(ProjectDocument document)
    {
        foreach (CableRoute cable in document.Cables.Values)
        {
            ValidatePort(document, cable.Id, cable.From, "from");
            ValidatePort(document, cable.Id, cable.To, "to");
            if (cable.ConduitId is ObjectId conduitId && !document.Conduits.ContainsKey(conduitId))
            {
                throw new ProjectFormatException($"Cable '{cable.Id}' references missing conduit '{conduitId}'.");
            }
        }
    }

    private static void ValidatePort(ProjectDocument document, ObjectId cableId, PortReference? reference, string end)
    {
        if (reference is not PortReference portReference)
        {
            return;
        }
        Device device;
        try
        {
            device = document.RequireDevice(portReference.DeviceId);
        }
        catch (KeyNotFoundException exception)
        {
            throw new ProjectFormatException($"Cable '{cableId}' {end} references missing device '{portReference.DeviceId}'.", exception);
        }
        if (!device.Ports.Any(port => port.Id == portReference.PortId))
        {
            throw new ProjectFormatException($"Cable '{cableId}' {end} references missing port '{portReference}'.");
        }
    }

    private static PortReference? PortReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        int separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ProjectFormatException($"Invalid port reference '{value}'; expected 'device-id:port-id'.");
        }
        return new PortReference(ObjectId.Parse(value[..separator]), value[(separator + 1)..]);
    }

    private static ObjectId Id(string? value, string kind)
    {
        try
        {
            return ObjectId.Parse(RequireText(value, $"{kind} id"));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ProjectFormatException($"Invalid {kind} id '{value}'.", exception);
        }
    }

    private static Point2 Point(IReadOnlyList<double> values, string field)
    {
        if (values.Count != 2 || !double.IsFinite(values[0]) || !double.IsFinite(values[1]))
        {
            throw new ProjectFormatException($"{field} must contain exactly two finite numbers.");
        }
        return new Point2(values[0], values[1]);
    }

    private static Polyline Polyline(IReadOnlyList<double[]> points, string field)
    {
        try
        {
            return new Polyline(points.Select((point, index) => SpatialPoint(point, $"{field}[{index}]")));
        }
        catch (ArgumentException exception)
        {
            throw new ProjectFormatException($"Invalid polyline in {field}.", exception);
        }
    }

    private static Point3 SpatialPoint(IReadOnlyList<double> values, string field)
    {
        if (values.Count is not (2 or 3) || values.Any(value => !double.IsFinite(value)))
        {
            throw new ProjectFormatException($"{field} must contain two or three finite numbers.");
        }
        double elevation = values.Count == 3 ? NonNegative(values[2], $"{field}[2]") : 0;
        return new Point3(values[0], values[1], elevation);
    }

    private static double Positive(double value, string field)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ProjectFormatException($"{field} must be a positive finite number.");
        }
        return value;
    }

    private static double NonNegative(double value, string field, double? maximum = null)
    {
        if (!double.IsFinite(value) || value < 0 || maximum is double limit && value > limit)
        {
            throw new ProjectFormatException($"{field} must be a non-negative finite number{(maximum is double max ? $" no greater than {max}" : string.Empty)}.");
        }
        return value;
    }

    private static double? OptionalNonNegative(double? value, string field) =>
        value is double number ? NonNegative(number, field) : null;

    private static double? OptionalPositive(double? value, string field) =>
        value is double number ? Positive(number, field) : null;

    private static string RequireText(string? value, string field) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw new ProjectFormatException($"Missing or empty {field}.");

    private static T ParseEnum<T>(string? value, string field) where T : struct, Enum
    {
        string pascal = string.Concat(RequireText(value, field)
            .Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return Enum.TryParse(pascal, out T result)
            ? result
            : throw new ProjectFormatException($"Unknown {field} '{value}'.");
    }

    private static string Name<T>(T value) where T : struct, Enum
    {
        string name = value.ToString();
        var output = new System.Text.StringBuilder(name.Length + 4);
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (index > 0 && char.IsUpper(character))
            {
                output.Append('_');
            }
            output.Append(char.ToLowerInvariant(character));
        }
        return output.ToString();
    }

    private sealed class ProjectFile
    {
        [JsonPropertyName("project")]
        public ProjectMetadata? Project { get; set; }

        [JsonPropertyName("electrical_rules")]
        public ElectricalRulesFile? ElectricalRules { get; set; }

        [JsonPropertyName("space")]
        public SpaceFile? Space { get; set; }

        [JsonPropertyName("openings")]
        public List<OpeningFile> Openings { get; set; } = [];

        [JsonPropertyName("devices")]
        public List<DeviceFile> Devices { get; set; } = [];

        [JsonPropertyName("conduits")]
        public List<ConduitFile> Conduits { get; set; } = [];

        [JsonPropertyName("cables")]
        public List<CableFile> Cables { get; set; } = [];

        [JsonPropertyName("annotations")]
        public List<AnnotationFile> Annotations { get; set; } = [];

        [JsonPropertyName("wall_dimensions")]
        public List<WallDimensionFile> WallDimensions { get; set; } = [];
    }

    private sealed class ElectricalRulesFile
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("installation_standard")] public string? InstallationStandard { get; set; }
        [JsonPropertyName("installation_standard_edition")] public string? InstallationStandardEdition { get; set; }
        [JsonPropertyName("cable_sizing_guide")] public string? CableSizingGuide { get; set; }
        [JsonPropertyName("cable_sizing_guide_edition")] public string? CableSizingGuideEdition { get; set; }
    }

    private sealed class SpaceFile
    {
        [JsonPropertyName("origin")]
        public double[] Origin { get; set; } = [];

        [JsonPropertyName("width_mm")]
        public double WidthMillimetres { get; set; }

        [JsonPropertyName("depth_mm")]
        public double DepthMillimetres { get; set; }

        [JsonPropertyName("height_mm")]
        public double HeightMillimetres { get; set; }

        [JsonPropertyName("wall_thickness_mm")]
        public double WallThicknessMillimetres { get; set; } = 200;

        [JsonPropertyName("ceiling_thickness_mm")]
        public double CeilingThicknessMillimetres { get; set; } = 250;
    }

    private sealed class ProjectMetadata
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("units")]
        public string Units { get; set; } = "mm";

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("cable_slack_percent")]
        public double CableSlackPercent { get; set; } = 10;

        [JsonPropertyName("service_loop_mm")]
        public double ServiceLoopMillimetres { get; set; } = 1000;
    }

    private sealed class DeviceFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("position")]
        public double[] Position { get; set; } = [];

        [JsonPropertyName("rotation_degrees")]
        public double RotationDegrees { get; set; }

        [JsonPropertyName("elevation_mm")]
        public double ElevationMillimetres { get; set; }

        [JsonPropertyName("mounting_surface")]
        public string MountingSurface { get; set; } = "free";

        [JsonPropertyName("ports")]
        public List<PortFile> Ports { get; set; } = [];
    }

    private sealed class OpeningFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("surface")]
        public string? Surface { get; set; }

        [JsonPropertyName("centre")]
        public double[] Centre { get; set; } = [];

        [JsonPropertyName("width_mm")]
        public double WidthMillimetres { get; set; }

        [JsonPropertyName("height_mm")]
        public double HeightMillimetres { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    private sealed class PortFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("position")]
        public double[] Position { get; set; } = [];
    }

    private sealed class ConduitFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("inner_diameter_mm")]
        public double InnerDiameterMillimetres { get; set; }

        [JsonPropertyName("installation_method")]
        public string? InstallationMethod { get; set; }

        [JsonPropertyName("points")]
        public List<double[]> Points { get; set; } = [];
    }

    private sealed class CableFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("points")]
        public List<double[]> Points { get; set; } = [];

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("conduit")]
        public string? Conduit { get; set; }

        [JsonPropertyName("installation_status")]
        public string InstallationStatus { get; set; } = "planned";

        [JsonPropertyName("actual_length_mm")]
        public double? ActualLengthMillimetres { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("circuit")]
        public string? Circuit { get; set; }

        [JsonPropertyName("shielding")]
        public string? Shielding { get; set; }

        [JsonPropertyName("poe_capable")]
        public bool PoeCapable { get; set; }

        [JsonPropertyName("outside_diameter_mm")]
        public double? OutsideDiameterMillimetres { get; set; }

        [JsonPropertyName("design")]
        public CircuitDesignFile? Design { get; set; }

        [JsonPropertyName("conductors")]
        public List<ConductorFile> Conductors { get; set; } = [];
    }

    private sealed class CircuitDesignFile
    {
        [JsonPropertyName("nominal_voltage_v")] public double NominalVoltageVolts { get; set; }
        [JsonPropertyName("phase_count")] public int PhaseCount { get; set; }
        [JsonPropertyName("conductor_material")] public string ConductorMaterial { get; set; } = "copper";
        [JsonPropertyName("loaded_conductor_count")] public int? LoadedConductorCount { get; set; }
        [JsonPropertyName("design_current_a")] public double? DesignCurrentAmperes { get; set; }
        [JsonPropertyName("protective_device_a")] public double? ProtectiveDeviceAmperes { get; set; }
        [JsonPropertyName("protective_device_characteristic")] public string? ProtectiveDeviceCharacteristic { get; set; }
        [JsonPropertyName("reference_capacity_a")] public double? ReferenceCurrentCarryingCapacityAmperes { get; set; }
        [JsonPropertyName("ambient_factor")] public double AmbientCorrectionFactor { get; set; } = 1;
        [JsonPropertyName("grouping_factor")] public double GroupingCorrectionFactor { get; set; } = 1;
        [JsonPropertyName("thermal_insulation_factor")] public double ThermalInsulationCorrectionFactor { get; set; } = 1;
        [JsonPropertyName("reference_source")] public string? ReferenceSource { get; set; }
    }

    private sealed class ConductorFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("function")]
        public string? Function { get; set; }
        [JsonPropertyName("colour")]
        public string? Colour { get; set; }
        [JsonPropertyName("area_mm2")]
        public double AreaSquareMillimetres { get; set; }
        [JsonPropertyName("outside_diameter_mm")]
        public double? OutsideDiameterMillimetres { get; set; }
        [JsonPropertyName("terminal_label")]
        public string? TerminalLabel { get; set; }
    }

    private sealed class AnnotationFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("position")]
        public double[] Position { get; set; } = [];

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class WallDimensionFile
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("surface")]
        public string? Surface { get; set; }

        [JsonPropertyName("start")]
        public double[] Start { get; set; } = [];

        [JsonPropertyName("end")]
        public double[] End { get; set; } = [];

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
}

public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException(string message) : base(message)
    {
    }

    public ProjectFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
