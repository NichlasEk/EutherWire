using System.Globalization;
using EutherWire.Document.Commands;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Editing;

public enum PropertyValueKind
{
    Text,
    Number,
    Choice,
}

public readonly record struct PropertyHandleId(ObjectId ObjectId, string Name)
{
    private const string Marker = ":property:";

    public static PropertyHandleId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int marker = value.LastIndexOf(Marker, StringComparison.Ordinal);
        if (marker <= 0 || marker + Marker.Length >= value.Length)
        {
            throw new FormatException($"Invalid property handle '{value}'; expected object-id:property:name.");
        }
        string name = value[(marker + Marker.Length)..];
        if (name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new FormatException($"Invalid property name '{name}'.");
        }
        return new PropertyHandleId(ObjectId.Parse(value[..marker]), name);
    }

    public override string ToString() => $"{ObjectId}{Marker}{Name}";
}

public sealed record DocumentProperty(
    PropertyHandleId Id,
    PropertyValueKind Kind,
    string Value,
    IReadOnlyList<string>? Choices = null);

public static class DocumentProperties
{
    public static IReadOnlyList<DocumentProperty> Enumerate(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var properties = new List<DocumentProperty>();
        ObjectId spaceId = ObjectId.Parse("space");
        properties.Add(Number(spaceId, "width_mm", document.Space.WidthMillimetres));
        properties.Add(Number(spaceId, "depth_mm", document.Space.DepthMillimetres));
        properties.Add(Number(spaceId, "height_mm", document.Space.HeightMillimetres));
        properties.Add(Number(spaceId, "wall_thickness_mm", document.Space.WallThicknessMillimetres));
        properties.Add(Number(spaceId, "ceiling_thickness_mm", document.Space.CeilingThicknessMillimetres));
        foreach (Device device in document.Devices.Values)
        {
            properties.Add(Text(device.Id, "label", device.Label));
            properties.Add(Choice(device.Id, "kind", device.Kind));
            properties.Add(Number(device.Id, "elevation_mm", device.ElevationMillimetres));
            properties.Add(Choice(device.Id, "mounting_surface", device.MountingSurface));
            AddInstallationProperties(properties, document, device.Id);
        }
        foreach (BuildingOpening opening in document.Openings.Values)
        {
            properties.Add(Text(opening.Id, "label", opening.Label));
            properties.Add(Choice(opening.Id, "kind", opening.Kind));
            properties.Add(Choice(opening.Id, "surface", opening.Surface));
            properties.Add(Number(opening.Id, "centre_x_mm", opening.Centre.X));
            properties.Add(Number(opening.Id, "centre_y_mm", opening.Centre.Y));
            properties.Add(Number(opening.Id, "centre_z_mm", opening.Centre.Z));
            properties.Add(Number(opening.Id, "width_mm", opening.WidthMillimetres));
            properties.Add(Number(opening.Id, "height_mm", opening.HeightMillimetres));
            AddInstallationProperties(properties, document, opening.Id);
        }
        foreach (CableRoute cable in document.Cables.Values)
        {
            properties.Add(Text(cable.Id, "label", cable.Label));
            properties.Add(Choice(cable.Id, "kind", cable.Kind));
            AddInstallationProperties(properties, document, cable.Id);
            InstallationRecord installation = document.RequireInstallationRecord(cable.Id);
            properties.Add(new DocumentProperty(
                new PropertyHandleId(cable.Id, "actual_length_mm"),
                PropertyValueKind.Number,
                installation.ActualLengthMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown"));
            if (cable.ConduitId is null) AddVertexElevations(properties, cable.Id, cable.Route);
        }
        foreach (Conduit conduit in document.Conduits.Values)
        {
            properties.Add(Text(conduit.Id, "label", conduit.Label));
            properties.Add(new DocumentProperty(
                new PropertyHandleId(conduit.Id, "inner_diameter_mm"),
                PropertyValueKind.Number,
                conduit.InnerDiameterMillimetres.ToString("0.###", CultureInfo.InvariantCulture)));
            properties.Add(new DocumentProperty(
                new PropertyHandleId(conduit.Id, "nominal_diameter_mm"),
                PropertyValueKind.Number,
                conduit.NominalDiameterMillimetres?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unknown"));
            properties.Add(Choice(conduit.Id, "installation_method", conduit.InstallationMethod));
            AddInstallationProperties(properties, document, conduit.Id);
            AddVertexElevations(properties, conduit.Id, conduit.Route);
        }
        foreach (Annotation annotation in document.Annotations.Values)
        {
            properties.Add(Text(annotation.Id, "label", annotation.Text));
        }
        return properties.OrderBy(property => property.Id.ToString(), StringComparer.Ordinal).ToList();
    }

    public static IDocumentCommand CreateSetCommand(ProjectDocument document, PropertyHandleId id, string value)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = Enumerate(document).FirstOrDefault(property => property.Id == id)
            ?? throw new KeyNotFoundException($"Property handle '{id}' does not exist.");
        return id.Name switch
        {
            "label" => new SetObjectLabelCommand(id.ObjectId, value),
            "kind" when document.Devices.ContainsKey(id.ObjectId) =>
                new SetDeviceKindCommand(id.ObjectId, ParseChoice<DeviceKind>(value)),
            "kind" when document.Cables.ContainsKey(id.ObjectId) =>
                new SetCableKindCommand(id.ObjectId, ParseChoice<CableKind>(value)),
            "elevation_mm" when document.Devices.ContainsKey(id.ObjectId) =>
                new SetDeviceElevationCommand(id.ObjectId, ParseNonNegative(value)),
            "mounting_surface" when document.Devices.ContainsKey(id.ObjectId) =>
                new SetDeviceMountingSurfaceCommand(id.ObjectId, ParseChoice<MountingSurface>(value)),
            "installation_status" => UpdateInstallationStatus(document, id.ObjectId, ParseChoice<InstallationStatus>(value)),
            "installation_note" => UpdateInstallationNote(document, id.ObjectId, value),
            _ when document.Openings.ContainsKey(id.ObjectId) => CreateOpeningCommand(document, id, value),
            "actual_length_mm" => UpdateInstallationLength(document, id.ObjectId, ParseOptionalLength(value)),
            "inner_diameter_mm" => new SetConduitDiameterCommand(
                id.ObjectId,
                double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            "nominal_diameter_mm" => new SetConduitNominalDiameterCommand(
                id.ObjectId,
                ParsePositive(value)),
            "installation_method" when document.Conduits.ContainsKey(id.ObjectId) =>
                new SetConduitInstallationMethodCommand(id.ObjectId, ParseChoice<InstallationMethod>(value)),
            _ when TryParseVertexElevation(id.Name, out int vertexIndex) =>
                new SetRouteVertexElevationCommand(id.ObjectId, vertexIndex, ParseNonNegative(value)),
            _ when id.ObjectId == ObjectId.Parse("space") => CreateSpaceCommand(document, id.Name, value),
            _ => throw new InvalidOperationException($"Property handle '{id}' is not writable."),
        };
    }

    private static DocumentProperty Text(ObjectId id, string name, string value) =>
        new(new PropertyHandleId(id, name), PropertyValueKind.Text, value);

    private static DocumentProperty Number(ObjectId id, string name, double value) =>
        new(new PropertyHandleId(id, name), PropertyValueKind.Number, value.ToString("0.###", CultureInfo.InvariantCulture));

    private static void AddInstallationProperties(List<DocumentProperty> properties, ProjectDocument document, ObjectId id)
    {
        InstallationRecord record = document.RequireInstallationRecord(id);
        properties.Add(Choice(id, "installation_status", record.Status));
        properties.Add(Text(id, "installation_note", record.Note ?? string.Empty));
    }

    private static IDocumentCommand UpdateInstallationStatus(ProjectDocument document, ObjectId id, InstallationStatus status)
    {
        InstallationRecord previous = document.RequireInstallationRecord(id);
        return new SetInstallationRecordCommand(new InstallationRecord(
            id,
            status,
            DateTimeOffset.UtcNow,
            previous.Note,
            previous.ActualPosition,
            previous.ActualLengthMillimetres,
            previous.TestResult,
            previous.PhotoReferences));
    }

    private static IDocumentCommand UpdateInstallationNote(ProjectDocument document, ObjectId id, string note)
    {
        InstallationRecord previous = document.RequireInstallationRecord(id);
        return new SetInstallationRecordCommand(new InstallationRecord(id, previous.Status, DateTimeOffset.UtcNow,
            note, previous.ActualPosition, previous.ActualLengthMillimetres, previous.TestResult, previous.PhotoReferences));
    }

    private static IDocumentCommand UpdateInstallationLength(ProjectDocument document, ObjectId id, double? actualLengthMillimetres)
    {
        InstallationRecord previous = document.RequireInstallationRecord(id);
        return new SetInstallationRecordCommand(new InstallationRecord(id, previous.Status, DateTimeOffset.UtcNow,
            previous.Note, previous.ActualPosition, actualLengthMillimetres, previous.TestResult, previous.PhotoReferences));
    }

    private static void AddVertexElevations(List<DocumentProperty> properties, ObjectId id, EutherWire.Document.Geometry.Polyline route)
    {
        for (int index = 0; index < route.SpatialPoints.Count; index++)
        {
            properties.Add(Number(id, $"vertex_{index}_elevation_mm", route.SpatialPoints[index].Z));
        }
    }

    private static bool TryParseVertexElevation(string name, out int index)
    {
        const string prefix = "vertex_";
        const string suffix = "_elevation_mm";
        index = -1;
        return name.StartsWith(prefix, StringComparison.Ordinal) &&
            name.EndsWith(suffix, StringComparison.Ordinal) &&
            int.TryParse(name[prefix.Length..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    private static double ParseNonNegative(string value)
    {
        double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number) || number < 0) throw new ArgumentOutOfRangeException(nameof(value));
        return number;
    }

    private static IDocumentCommand CreateSpaceCommand(ProjectDocument document, string name, string value)
    {
        double number = ParsePositive(value);
        SpaceVolume space = document.Space;
        SpaceVolume changed = name switch
        {
            "width_mm" => space with { WidthMillimetres = number },
            "depth_mm" => space with { DepthMillimetres = number },
            "height_mm" => space with { HeightMillimetres = number },
            "wall_thickness_mm" => space with { WallThicknessMillimetres = number },
            "ceiling_thickness_mm" => space with { CeilingThicknessMillimetres = number },
            _ => throw new InvalidOperationException($"Unknown space property '{name}'."),
        };
        return new SetSpaceVolumeCommand(changed);
    }

    private static IDocumentCommand CreateOpeningCommand(ProjectDocument document, PropertyHandleId id, string value)
    {
        BuildingOpening opening = document.RequireOpening(id.ObjectId);
        OpeningKind kind = id.Name == "kind" ? ParseChoice<OpeningKind>(value) : opening.Kind;
        MountingSurface surface = id.Name == "surface" ? ParseChoice<MountingSurface>(value) : opening.Surface;
        double x = id.Name == "centre_x_mm" ? ParseFinite(value) : opening.Centre.X;
        double y = id.Name == "centre_y_mm" ? ParseFinite(value) : opening.Centre.Y;
        double z = id.Name == "centre_z_mm" ? ParseNonNegative(value) : opening.Centre.Z;
        double width = id.Name == "width_mm" ? ParsePositive(value) : opening.WidthMillimetres;
        double height = id.Name == "height_mm" ? ParsePositive(value) : opening.HeightMillimetres;
        if (id.Name is not ("kind" or "surface" or "centre_x_mm" or "centre_y_mm" or "centre_z_mm" or "width_mm" or "height_mm"))
        {
            throw new InvalidOperationException($"Unknown opening property '{id.Name}'.");
        }
        return new SetOpeningGeometryCommand(opening.Id, kind, surface, new Point3(x, y, z), width, height);
    }

    private static double ParseFinite(string value)
    {
        double number = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(number)) throw new ArgumentOutOfRangeException(nameof(value));
        return number;
    }

    private static double ParsePositive(string value)
    {
        double number = ParseNonNegative(value);
        if (number == 0) throw new ArgumentOutOfRangeException(nameof(value));
        return number;
    }

    private static DocumentProperty Choice<T>(ObjectId id, string name, T value) where T : struct, Enum =>
        new(
            new PropertyHandleId(id, name),
            PropertyValueKind.Choice,
            EnumName(value),
            Enum.GetValues<T>().Select(EnumName).ToList());

    private static T ParseChoice<T>(string value) where T : struct, Enum
    {
        string normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (T candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
        throw new ArgumentException($"Unknown {typeof(T).Name} value '{value}'.");
    }

    private static double? ParseOptionalLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        double length = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(length) || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Actual length must be non-negative.");
        }
        return length;
    }

    private static string EnumName<T>(T value) where T : struct, Enum
    {
        string name = value.ToString();
        var output = new System.Text.StringBuilder(name.Length + 4);
        for (int index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                output.Append('_');
            }
            output.Append(char.ToLowerInvariant(name[index]));
        }
        return output.ToString();
    }
}
