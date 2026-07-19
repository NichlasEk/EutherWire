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

    public Device RequireDevice(ObjectId id) =>
        _devices.TryGetValue(id, out Device? device)
            ? device
            : throw new KeyNotFoundException($"Device '{id}' does not exist.");

    private void RequireUniqueId(ObjectId id)
    {
        if (_devices.ContainsKey(id) || _cables.ContainsKey(id) || _conduits.ContainsKey(id))
        {
            throw new InvalidOperationException($"Object ID '{id}' already exists in this document.");
        }
    }
}
