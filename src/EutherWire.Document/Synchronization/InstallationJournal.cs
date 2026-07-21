using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EutherWire.Document.Commands;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Synchronization;

public static class InstallationJournal
{
    public const string FileName = "installation-events.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static string Serialize(InstallationEvent installationEvent)
    {
        ArgumentNullException.ThrowIfNull(installationEvent);
        return JsonSerializer.Serialize(ToFile(installationEvent), JsonOptions);
    }

    public static InstallationEvent Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            EventFile source = JsonSerializer.Deserialize<EventFile>(json, JsonOptions)
                ?? throw new InvalidDataException("Installation event is empty.");
            if (!Guid.TryParseExact(source.EventId, "D", out Guid eventId))
                throw new InvalidDataException($"Invalid event_id '{source.EventId}'.");
            if (!DateTimeOffset.TryParse(source.Timestamp, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
                throw new InvalidDataException($"Invalid timestamp '{source.Timestamp}'.");
            PayloadFile payloadSource = source.Payload ?? throw new InvalidDataException("Installation event payload is missing.");
            long baseRevision = source.BaseRevision ?? throw new InvalidDataException("Installation event base_revision is missing.");
            InstallationEventOperation operation = source.Operation ?? throw new InvalidDataException("Installation event operation is missing.");
            InstallationStatus status = payloadSource.Status ?? throw new InvalidDataException("Installation event payload status is missing.");
            Point3? position = payloadSource.ActualPosition is { Length: > 0 }
                ? payloadSource.ActualPosition.Length == 3
                    ? new Point3(payloadSource.ActualPosition[0], payloadSource.ActualPosition[1], payloadSource.ActualPosition[2])
                    : throw new InvalidDataException("actual_position must contain exactly three numbers.")
                : null;
            var payload = new InstallationEventPayload(
                status,
                payloadSource.Note,
                position,
                payloadSource.ActualLengthMillimetres,
                payloadSource.TestResult,
                payloadSource.PhotoReferences ?? []);
            return new InstallationEvent(eventId, ObjectId.Parse(source.ObjectId), baseRevision,
                timestamp, source.AuthorDeviceId, operation, payload);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Invalid installation event JSON.", exception);
        }
    }

    public static IReadOnlyList<InstallationEvent> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) return [];
        var events = new List<InstallationEvent>();
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                events.Add(Deserialize(line));
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"Invalid installation journal '{path}' at line {lineNumber}: {exception.Message}", exception);
            }
        }
        return events;
    }

    public static int AppendUnique(string path, IEnumerable<InstallationEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(events);
        var known = Read(path).ToDictionary(item => item.EventId);
        var additions = new List<InstallationEvent>();
        foreach (InstallationEvent installationEvent in events)
        {
            if (known.TryGetValue(installationEvent.EventId, out InstallationEvent? existing))
            {
                if (!string.Equals(Serialize(existing), Serialize(installationEvent), StringComparison.Ordinal))
                    throw new InvalidDataException($"Event ID collision for '{installationEvent.EventId:D}' with different content.");
                continue;
            }
            known.Add(installationEvent.EventId, installationEvent);
            additions.Add(installationEvent);
        }
        if (additions.Count == 0) return 0;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (InstallationEvent installationEvent in additions) writer.WriteLine(Serialize(installationEvent));
        return additions.Count;
    }

    public static InstallationEventApplyResult Apply(ProjectDocument document, InstallationEvent installationEvent)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(installationEvent);
        if (document.AppliedInstallationEventIds.Contains(installationEvent.EventId))
            return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Duplicate, "Event was already applied.");
        if (!document.InstallationRecords.TryGetValue(installationEvent.ObjectId, out InstallationRecord? current))
            return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Conflict, "Target object does not exist or is not installable.");
        if (current.Revision != installationEvent.BaseRevision)
            return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Conflict,
                $"Base revision {installationEvent.BaseRevision} does not match current revision {current.Revision}.");
        if (installationEvent.Operation != InstallationEventOperation.SetRecord)
            return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Conflict,
                $"Unsupported operation '{installationEvent.Operation}'.");

        InstallationEventPayload payload = installationEvent.Payload;
        var updated = new InstallationRecord(
            installationEvent.ObjectId,
            payload.Status,
            installationEvent.Timestamp,
            payload.Note,
            payload.ActualPosition,
            payload.ActualLengthMillimetres,
            payload.TestResult,
            payload.PhotoReferences,
            current.Revision + 1);
        new SetInstallationRecordCommand(updated, preserveRevision: true).Apply(document);
        document.MarkInstallationEventApplied(installationEvent.EventId);
        return new InstallationEventApplyResult(installationEvent, InstallationEventApplyStatus.Applied,
            $"Applied revision {updated.Revision}.");
    }

    private static EventFile ToFile(InstallationEvent installationEvent) => new()
    {
        EventId = installationEvent.EventId.ToString("D"),
        ObjectId = installationEvent.ObjectId.Value,
        BaseRevision = installationEvent.BaseRevision,
        Timestamp = installationEvent.Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        AuthorDeviceId = installationEvent.AuthorDeviceId,
        Operation = installationEvent.Operation,
        Payload = new PayloadFile
        {
            Status = installationEvent.Payload.Status,
            Note = installationEvent.Payload.Note,
            ActualPosition = installationEvent.Payload.ActualPosition is Point3 position ? [position.X, position.Y, position.Z] : null,
            ActualLengthMillimetres = installationEvent.Payload.ActualLengthMillimetres,
            TestResult = installationEvent.Payload.TestResult,
            PhotoReferences = installationEvent.Payload.PhotoReferences.Count > 0 ? installationEvent.Payload.PhotoReferences.ToList() : null,
        },
    };

    private sealed class EventFile
    {
        public string EventId { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
        public long? BaseRevision { get; set; }
        public string Timestamp { get; set; } = string.Empty;
        public string AuthorDeviceId { get; set; } = string.Empty;
        public InstallationEventOperation? Operation { get; set; }
        public PayloadFile? Payload { get; set; }
    }

    private sealed class PayloadFile
    {
        public InstallationStatus? Status { get; set; }
        public string? Note { get; set; }
        public double[]? ActualPosition { get; set; }
        public double? ActualLengthMillimetres { get; set; }
        public string? TestResult { get; set; }
        public List<string>? PhotoReferences { get; set; }
    }
}
