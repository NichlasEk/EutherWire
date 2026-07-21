using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Synchronization;

public enum InstallationEventOperation { SetRecord }

public sealed record InstallationEventPayload(
    InstallationStatus Status,
    string? Note,
    Point3? ActualPosition,
    double? ActualLengthMillimetres,
    string? TestResult,
    IReadOnlyList<string> PhotoReferences);

public sealed record InstallationEvent
{
    public InstallationEvent(
        Guid eventId,
        ObjectId objectId,
        long baseRevision,
        DateTimeOffset timestamp,
        string authorDeviceId,
        InstallationEventOperation operation,
        InstallationEventPayload payload)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID cannot be empty.", nameof(eventId));
        if (baseRevision < 0) throw new ArgumentOutOfRangeException(nameof(baseRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(authorDeviceId);
        EventId = eventId;
        ObjectId = objectId;
        BaseRevision = baseRevision;
        Timestamp = timestamp;
        AuthorDeviceId = authorDeviceId.Trim();
        Operation = operation;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public Guid EventId { get; }
    public ObjectId ObjectId { get; }
    public long BaseRevision { get; }
    public DateTimeOffset Timestamp { get; }
    public string AuthorDeviceId { get; }
    public InstallationEventOperation Operation { get; }
    public InstallationEventPayload Payload { get; }

    public static InstallationEvent CreateSetRecord(
        InstallationRecord current,
        InstallationRecord desired,
        string authorDeviceId,
        DateTimeOffset? timestamp = null,
        Guid? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(desired);
        if (current.ObjectId != desired.ObjectId)
            throw new ArgumentException("Current and desired installation records must target the same object.");
        return new InstallationEvent(
            eventId ?? Guid.NewGuid(),
            current.ObjectId,
            current.Revision,
            timestamp ?? DateTimeOffset.UtcNow,
            authorDeviceId,
            InstallationEventOperation.SetRecord,
            new InstallationEventPayload(
                desired.Status,
                desired.Note,
                desired.ActualPosition,
                desired.ActualLengthMillimetres,
                desired.TestResult,
                desired.PhotoReferences.ToList()));
    }
}

public enum InstallationEventApplyStatus { Applied, Duplicate, Conflict }

public sealed record InstallationEventApplyResult(
    InstallationEvent Event,
    InstallationEventApplyStatus Status,
    string Message);
