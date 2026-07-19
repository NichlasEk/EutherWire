namespace EutherWire.Document.Model;

public sealed class ProjectDocument
{
    private readonly Dictionary<ObjectId, Device> _devices = [];
    private readonly Dictionary<ObjectId, CableRoute> _cables = [];
    private readonly Dictionary<ObjectId, Conduit> _conduits = [];

    public ProjectDocument(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public int SchemaVersion => 1;
    public string Name { get; set; }
    public IReadOnlyDictionary<ObjectId, Device> Devices => _devices;
    public IReadOnlyDictionary<ObjectId, CableRoute> Cables => _cables;
    public IReadOnlyDictionary<ObjectId, Conduit> Conduits => _conduits;

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

    public bool Contains(ObjectId id) =>
        _devices.ContainsKey(id) || _cables.ContainsKey(id) || _conduits.ContainsKey(id);

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

    private void RequireUniqueId(ObjectId id)
    {
        if (_devices.ContainsKey(id) || _cables.ContainsKey(id) || _conduits.ContainsKey(id))
        {
            throw new InvalidOperationException($"Object ID '{id}' already exists in this document.");
        }
    }
}
