namespace EutherWire.Document.Model;

public sealed class ProjectDocument
{
    private readonly Dictionary<ObjectId, Device> _devices = [];
    private readonly Dictionary<ObjectId, CableRoute> _cables = [];
    private readonly Dictionary<ObjectId, Conduit> _conduits = [];
    private readonly Dictionary<ObjectId, Annotation> _annotations = [];
    private readonly Dictionary<ObjectId, BuildingOpening> _openings = [];
    private readonly Dictionary<ObjectId, WallDimension> _wallDimensions = [];
    private readonly Dictionary<ObjectId, InstallationRecord> _installationRecords = [];
    private readonly HashSet<Guid> _appliedInstallationEventIds = [];

    public ProjectDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public int SchemaVersion => 12;
    public string Name { get; set; }
    public PlanningSettings Planning { get; internal set; } = new();
    public ElectricalRuleProfile ElectricalRules { get; internal set; } = ElectricalRuleProfile.Sweden2026;
    public SpaceVolume Space { get; internal set; } = SpaceVolume.GarageDefault;
    public IReadOnlyDictionary<ObjectId, Device> Devices => _devices;
    public IReadOnlyDictionary<ObjectId, CableRoute> Cables => _cables;
    public IReadOnlyDictionary<ObjectId, Conduit> Conduits => _conduits;
    public IReadOnlyDictionary<ObjectId, Annotation> Annotations => _annotations;
    public IReadOnlyDictionary<ObjectId, BuildingOpening> Openings => _openings;
    public IReadOnlyDictionary<ObjectId, WallDimension> WallDimensions => _wallDimensions;
    public IReadOnlyDictionary<ObjectId, InstallationRecord> InstallationRecords => _installationRecords;
    public IReadOnlySet<Guid> AppliedInstallationEventIds => _appliedInstallationEventIds;

    public void Add(Device device)
    {
        RequireUniqueId(device.Id);
        _devices.Add(device.Id, device);
        AddDefaultInstallationRecord(device.Id);
    }

    public void Add(CableRoute cable)
    {
        RequireUniqueId(cable.Id);
        _cables.Add(cable.Id, cable);
        _installationRecords.Add(cable.Id, new InstallationRecord(cable.Id, cable.InstallationStatus,
            actualLengthMillimetres: cable.ActualLengthMillimetres));
    }

    public void Add(Conduit conduit)
    {
        RequireUniqueId(conduit.Id);
        _conduits.Add(conduit.Id, conduit);
        AddDefaultInstallationRecord(conduit.Id);
    }

    public void Add(Annotation annotation)
    {
        RequireUniqueId(annotation.Id);
        _annotations.Add(annotation.Id, annotation);
    }

    public void Add(BuildingOpening opening)
    {
        RequireUniqueId(opening.Id);
        _openings.Add(opening.Id, opening);
        AddDefaultInstallationRecord(opening.Id);
    }

    public void Add(WallDimension dimension)
    {
        RequireUniqueId(dimension.Id);
        _wallDimensions.Add(dimension.Id, dimension);
    }

    public bool Contains(ObjectId id) =>
        _devices.ContainsKey(id) || _cables.ContainsKey(id) || _conduits.ContainsKey(id) || _annotations.ContainsKey(id) || _openings.ContainsKey(id) || _wallDimensions.ContainsKey(id);

    public Device RequireDevice(ObjectId id) =>
        _devices.TryGetValue(id, out Device? device)
            ? device
            : throw new KeyNotFoundException($"Device '{id}' does not exist.");

    public CableRoute RequireCable(ObjectId id) =>
        _cables.TryGetValue(id, out CableRoute? cable)
            ? cable
            : throw new KeyNotFoundException($"Cable '{id}' does not exist.");

    public Conduit RequireConduit(ObjectId id) =>
        _conduits.TryGetValue(id, out Conduit? conduit)
            ? conduit
            : throw new KeyNotFoundException($"Conduit '{id}' does not exist.");

    public Annotation RequireAnnotation(ObjectId id) =>
        _annotations.TryGetValue(id, out Annotation? annotation)
            ? annotation
            : throw new KeyNotFoundException($"Annotation '{id}' does not exist.");

    public BuildingOpening RequireOpening(ObjectId id) =>
        _openings.TryGetValue(id, out BuildingOpening? opening)
            ? opening
            : throw new KeyNotFoundException($"Building opening '{id}' does not exist.");

    public WallDimension RequireWallDimension(ObjectId id) =>
        _wallDimensions.TryGetValue(id, out WallDimension? dimension)
            ? dimension
            : throw new KeyNotFoundException($"Wall dimension '{id}' does not exist.");

    public InstallationRecord RequireInstallationRecord(ObjectId id) =>
        _installationRecords.TryGetValue(id, out InstallationRecord? record)
            ? record
            : throw new KeyNotFoundException($"Installation record for '{id}' does not exist.");

    public InstallationObjectKind RequireInstallationObjectKind(ObjectId id) =>
        _devices.ContainsKey(id) ? InstallationObjectKind.Device :
        _openings.ContainsKey(id) ? InstallationObjectKind.Opening :
        _conduits.ContainsKey(id) ? InstallationObjectKind.Conduit :
        _cables.ContainsKey(id) ? InstallationObjectKind.Cable :
        throw new KeyNotFoundException($"Object '{id}' is not installable.");

    internal bool TryGetCable(ObjectId id, out CableRoute? cable) => _cables.TryGetValue(id, out cable);
    internal bool TryGetConduit(ObjectId id, out Conduit? conduit) => _conduits.TryGetValue(id, out conduit);

    internal void Replace(CableRoute cable)
    {
        _ = RequireCable(cable.Id);
        _cables[cable.Id] = cable;
    }

    internal void Replace(Conduit conduit)
    {
        _ = RequireConduit(conduit.Id);
        _conduits[conduit.Id] = conduit;
    }

    internal void ReplaceInstallationRecord(InstallationRecord record)
    {
        _ = RequireInstallationObjectKind(record.ObjectId);
        _ = RequireInstallationRecord(record.ObjectId);
        _installationRecords[record.ObjectId] = record;
        if (_cables.TryGetValue(record.ObjectId, out CableRoute? cable))
            _cables[record.ObjectId] = cable with { InstallationStatus = record.Status, ActualLengthMillimetres = record.ActualLengthMillimetres };
    }

    internal void MarkInstallationEventApplied(Guid eventId)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event ID cannot be empty.", nameof(eventId));
        _appliedInstallationEventIds.Add(eventId);
    }

    internal bool RemoveDevice(ObjectId id, out Device? device) => RemoveInstallable(_devices, id, out device);
    internal bool RemoveCable(ObjectId id, out CableRoute? cable) => RemoveInstallable(_cables, id, out cable);
    internal bool RemoveConduit(ObjectId id, out Conduit? conduit) => RemoveInstallable(_conduits, id, out conduit);
    internal bool RemoveAnnotation(ObjectId id, out Annotation? annotation) => _annotations.Remove(id, out annotation);
    internal bool RemoveOpening(ObjectId id, out BuildingOpening? opening) => RemoveInstallable(_openings, id, out opening);
    internal bool RemoveWallDimension(ObjectId id, out WallDimension? dimension) => _wallDimensions.Remove(id, out dimension);

    private void RequireUniqueId(ObjectId id)
    {
        if (Contains(id))
        {
            throw new InvalidOperationException($"Object ID '{id}' already exists in this document.");
        }
    }

    private void AddDefaultInstallationRecord(ObjectId id) => _installationRecords.Add(id, new InstallationRecord(id));

    private bool RemoveInstallable<T>(Dictionary<ObjectId, T> objects, ObjectId id, out T? value)
    {
        bool removed = objects.Remove(id, out value);
        if (removed) _installationRecords.Remove(id);
        return removed;
    }
}
