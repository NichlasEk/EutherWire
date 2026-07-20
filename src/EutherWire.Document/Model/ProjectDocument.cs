namespace EutherWire.Document.Model;

public sealed class ProjectDocument
{
    private readonly Dictionary<ObjectId, Device> _devices = [];
    private readonly Dictionary<ObjectId, CableRoute> _cables = [];
    private readonly Dictionary<ObjectId, Conduit> _conduits = [];
    private readonly Dictionary<ObjectId, Annotation> _annotations = [];
    private readonly Dictionary<ObjectId, BuildingOpening> _openings = [];
    private readonly Dictionary<ObjectId, WallDimension> _wallDimensions = [];

    public ProjectDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public int SchemaVersion => 6;
    public string Name { get; set; }
    public PlanningSettings Planning { get; internal set; } = new();
    public SpaceVolume Space { get; internal set; } = SpaceVolume.GarageDefault;
    public IReadOnlyDictionary<ObjectId, Device> Devices => _devices;
    public IReadOnlyDictionary<ObjectId, CableRoute> Cables => _cables;
    public IReadOnlyDictionary<ObjectId, Conduit> Conduits => _conduits;
    public IReadOnlyDictionary<ObjectId, Annotation> Annotations => _annotations;
    public IReadOnlyDictionary<ObjectId, BuildingOpening> Openings => _openings;
    public IReadOnlyDictionary<ObjectId, WallDimension> WallDimensions => _wallDimensions;

    public void Add(Device device)
    {
        RequireUniqueId(device.Id);
        _devices.Add(device.Id, device);
    }

    public void Add(CableRoute cable)
    {
        RequireUniqueId(cable.Id);
        _cables.Add(cable.Id, cable);
    }

    public void Add(Conduit conduit)
    {
        RequireUniqueId(conduit.Id);
        _conduits.Add(conduit.Id, conduit);
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

    internal bool RemoveDevice(ObjectId id, out Device? device) => _devices.Remove(id, out device);
    internal bool RemoveCable(ObjectId id, out CableRoute? cable) => _cables.Remove(id, out cable);
    internal bool RemoveConduit(ObjectId id, out Conduit? conduit) => _conduits.Remove(id, out conduit);
    internal bool RemoveAnnotation(ObjectId id, out Annotation? annotation) => _annotations.Remove(id, out annotation);
    internal bool RemoveOpening(ObjectId id, out BuildingOpening? opening) => _openings.Remove(id, out opening);
    internal bool RemoveWallDimension(ObjectId id, out WallDimension? dimension) => _wallDimensions.Remove(id, out dimension);

    private void RequireUniqueId(ObjectId id)
    {
        if (Contains(id))
        {
            throw new InvalidOperationException($"Object ID '{id}' already exists in this document.");
        }
    }
}
