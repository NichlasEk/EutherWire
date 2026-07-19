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
            },
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
        if (file.Project.SchemaVersion != 1)
        {
            throw new ProjectFormatException($"Unsupported schema_version {file.Project.SchemaVersion}; expected 1.");
        }
        if (!string.Equals(file.Project.Units, "mm", StringComparison.Ordinal))
        {
            throw new ProjectFormatException($"Unsupported units '{file.Project.Units}'; expected 'mm'.");
        }

        var document = new ProjectDocument(RequireText(file.Project.Name, "project.name"));
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
                string.IsNullOrWhiteSpace(source.Conduit) ? null : Id(source.Conduit, "conduit reference")));
        }
        foreach (AnnotationFile source in file.Annotations)
        {
            document.Add(new Annotation(
                Id(source.Id, "annotation"),
                Point(source.Position, $"annotations[{source.Id}].position"),
                RequireText(source.Text, $"annotations[{source.Id}].text")));
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
        Ports = device.Ports.Select(port => new PortFile
        {
            Id = port.Id,
            Kind = Name(port.Kind),
            Position = [port.Position.X, port.Position.Y],
        }).ToList(),
    };

    private static ConduitFile ToFile(Conduit conduit) => new()
    {
        Id = conduit.Id.Value,
        Label = conduit.Label,
        InnerDiameterMillimetres = conduit.InnerDiameterMillimetres,
        InstallationMethod = Name(conduit.InstallationMethod),
        Points = conduit.Route.Points.Select(point => new[] { point.X, point.Y }).ToList(),
    };

    private static CableFile ToFile(CableRoute cable) => new()
    {
        Id = cable.Id.Value,
        Label = cable.Label,
        Kind = Name(cable.Kind),
        Points = cable.Route.Points.Select(point => new[] { point.X, point.Y }).ToList(),
        From = cable.From is PortReference from ? $"{from.DeviceId}:{from.PortId}" : null,
        To = cable.To is PortReference to ? $"{to.DeviceId}:{to.PortId}" : null,
        Conduit = cable.ConduitId?.Value,
    };

    private static AnnotationFile ToFile(Annotation annotation) => new()
    {
        Id = annotation.Id.Value,
        Position = [annotation.Position.X, annotation.Position.Y],
        Text = annotation.Text,
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
            return new Polyline(points.Select((point, index) => Point(point, $"{field}[{index}]")));
        }
        catch (ArgumentException exception)
        {
            throw new ProjectFormatException($"Invalid polyline in {field}.", exception);
        }
    }

    private static double Positive(double value, string field)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ProjectFormatException($"{field} must be a positive finite number.");
        }
        return value;
    }

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

        [JsonPropertyName("devices")]
        public List<DeviceFile> Devices { get; set; } = [];

        [JsonPropertyName("conduits")]
        public List<ConduitFile> Conduits { get; set; } = [];

        [JsonPropertyName("cables")]
        public List<CableFile> Cables { get; set; } = [];

        [JsonPropertyName("annotations")]
        public List<AnnotationFile> Annotations { get; set; } = [];
    }

    private sealed class ProjectMetadata
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("units")]
        public string Units { get; set; } = "mm";

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; }
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

        [JsonPropertyName("ports")]
        public List<PortFile> Ports { get; set; } = [];
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
