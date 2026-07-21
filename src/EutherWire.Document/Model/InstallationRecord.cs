using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public enum InstallationObjectKind { Device, Opening, Conduit, Cable }

public sealed record InstallationRecord
{
    public InstallationRecord(
        ObjectId objectId,
        InstallationStatus status = InstallationStatus.Planned,
        DateTimeOffset? updatedAt = null,
        string? note = null,
        Point3? actualPosition = null,
        double? actualLengthMillimetres = null,
        string? testResult = null,
        IEnumerable<string>? photoReferences = null,
        long revision = 0)
    {
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (actualLengthMillimetres is double length && (!double.IsFinite(length) || length < 0))
            throw new ArgumentOutOfRangeException(nameof(actualLengthMillimetres));
        if (actualPosition is Point3 position &&
            (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z) || position.Z < 0))
            throw new ArgumentOutOfRangeException(nameof(actualPosition));
        ObjectId = objectId;
        Status = status;
        UpdatedAt = updatedAt;
        Note = Normalize(note);
        ActualPosition = actualPosition;
        ActualLengthMillimetres = actualLengthMillimetres;
        TestResult = Normalize(testResult);
        PhotoReferences = (photoReferences ?? []).Select(reference =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reference);
            return reference.Trim();
        }).Distinct(StringComparer.Ordinal).ToList();
        Revision = revision;
    }

    public ObjectId ObjectId { get; }
    public InstallationStatus Status { get; }
    public DateTimeOffset? UpdatedAt { get; }
    public string? Note { get; }
    public Point3? ActualPosition { get; }
    public double? ActualLengthMillimetres { get; }
    public string? TestResult { get; }
    public IReadOnlyList<string> PhotoReferences { get; }
    public long Revision { get; }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
