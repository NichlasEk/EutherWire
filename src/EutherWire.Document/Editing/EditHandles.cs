using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Editing;

public enum EditHandleKind
{
    Move,
    Rotate,
    Resize,
    Vertex,
    Port,
    LabelAnchor,
}

/// <summary>
/// Stable semantic identity for an editable point. It is suitable for pointer,
/// touch, command-line, and agent-driven editing; it never contains screen
/// coordinates.
/// </summary>
public readonly record struct EditHandleId(
    ObjectId ObjectId,
    EditHandleKind Kind,
    int Index = -1,
    string? Name = null)
{
    public static EditHandleId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string[] parts = value.Split(':');
        if (parts.Length < 2 || parts.Length > 3 ||
            !Enum.TryParse(parts[1], true, out EditHandleKind kind))
        {
            throw new FormatException($"Invalid edit handle '{value}'.");
        }

        ObjectId objectId = ObjectId.Parse(parts[0]);
        if (parts.Length == 2)
        {
            return new EditHandleId(objectId, kind);
        }
        if (kind == EditHandleKind.Vertex && int.TryParse(parts[2], out int index) && index >= 0)
        {
            return new EditHandleId(objectId, kind, index);
        }
        if (kind == EditHandleKind.Port && !string.IsNullOrWhiteSpace(parts[2]))
        {
            return new EditHandleId(objectId, kind, Name: parts[2]);
        }
        throw new FormatException($"Invalid edit handle '{value}'.");
    }

    public override string ToString()
    {
        string suffix = Name is not null ? $":{Name}" : Index >= 0 ? $":{Index}" : string.Empty;
        return $"{ObjectId}:{Kind.ToString().ToLowerInvariant()}{suffix}";
    }
}

public readonly record struct EditHandle(EditHandleId Id, Point2 Position);

public static class DocumentHandles
{
    private const double RotationHandleOffsetMillimetres = 500;

    public static IReadOnlyList<EditHandle> Enumerate(ProjectDocument document)
    {
        var handles = new List<EditHandle>();
        foreach (Device device in document.Devices.Values.OrderBy(device => device.Id.Value, StringComparer.Ordinal))
        {
            handles.Add(new EditHandle(new EditHandleId(device.Id, EditHandleKind.Move), device.Position));
            handles.Add(new EditHandle(
                new EditHandleId(device.Id, EditHandleKind.Rotate),
                device.Position + new Vector2(0, -RotationHandleOffsetMillimetres)));

            for (int index = 0; index < device.Ports.Count; index++)
            {
                Port port = device.Ports[index];
                handles.Add(new EditHandle(
                    new EditHandleId(device.Id, EditHandleKind.Port, index, port.Id),
                    device.Position + Rotate(port.Position, device.RotationDegrees)));
            }
        }

        foreach (CableRoute cable in document.Cables.Values.OrderBy(cable => cable.Id.Value, StringComparer.Ordinal))
        {
            if (cable.ConduitId is null)
            {
                AddVertices(handles, cable.Id, cable.Route);
            }
        }
        foreach (Conduit conduit in document.Conduits.Values.OrderBy(conduit => conduit.Id.Value, StringComparer.Ordinal))
        {
            AddVertices(handles, conduit.Id, conduit.Route);
        }
        return handles;
    }

    private static void AddVertices(List<EditHandle> handles, ObjectId id, Polyline route)
    {
        for (int index = 0; index < route.Points.Count; index++)
        {
            handles.Add(new EditHandle(
                new EditHandleId(id, EditHandleKind.Vertex, index),
                route.Points[index]));
        }
    }

    internal static Vector2 Rotate(Point2 point, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new Vector2(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine);
    }
}

public static class DocumentHandleEditor
{
    public static Point2 RequirePosition(ProjectDocument document, EditHandleId id)
    {
        foreach (EditHandle handle in DocumentHandles.Enumerate(document))
        {
            if (handle.Id == id)
            {
                return handle.Position;
            }
        }
        throw new KeyNotFoundException($"Edit handle '{id}' does not exist.");
    }

    public static bool CanSetPosition(EditHandleId id) =>
        id.Kind is EditHandleKind.Move or EditHandleKind.Rotate or EditHandleKind.Vertex;

    public static void SetPosition(ProjectDocument document, EditHandleId id, Point2 position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Handle coordinates must be finite.");
        }

        switch (id.Kind)
        {
            case EditHandleKind.Move:
                MoveDevice(document, id.ObjectId, position);
                return;
            case EditHandleKind.Rotate:
                Device device = document.RequireDevice(id.ObjectId);
                double x = position.X - device.Position.X;
                double y = position.Y - device.Position.Y;
                if (x == 0 && y == 0)
                {
                    return;
                }
                device.RotationDegrees = NormalizeDegrees(Math.Atan2(y, x) * 180 / Math.PI + 90);
                return;
            case EditHandleKind.Vertex:
                SetRouteVertex(document, id, position);
                return;
            default:
                throw new InvalidOperationException($"Edit handle '{id}' is an anchor and cannot be moved directly.");
        }
    }

    private static void SetRouteVertex(ProjectDocument document, EditHandleId id, Point2 position)
    {
        if (document.TryGetCable(id.ObjectId, out CableRoute? cable) && cable is not null)
        {
            document.Replace(cable with { Route = cable.Route.WithPoint(id.Index, position) });
            return;
        }
        if (document.TryGetConduit(id.ObjectId, out Conduit? conduit) && conduit is not null)
        {
            Point2 previous = conduit.Route.Points[id.Index];
            document.Replace(conduit with { Route = conduit.Route.WithPoint(id.Index, position) });
            foreach (CableRoute containedCable in document.Cables.Values.Where(cable => cable.ConduitId == id.ObjectId).ToArray())
            {
                if (id.Index < containedCable.Route.Points.Count && containedCable.Route.Points[id.Index] == previous)
                {
                    document.Replace(containedCable with { Route = containedCable.Route.WithPoint(id.Index, position) });
                }
            }
            return;
        }
        throw new KeyNotFoundException($"Route for edit handle '{id}' does not exist.");
    }

    private static void MoveDevice(ProjectDocument document, ObjectId deviceId, Point2 position)
    {
        Device device = document.RequireDevice(deviceId);
        var oldEndpoints = new Dictionary<ObjectId, (Point2 First, Point2 Last)>();
        foreach (CableRoute cable in document.Cables.Values)
        {
            oldEndpoints[cable.Id] = (cable.Route.Points[0], cable.Route.Points[^1]);
        }

        device.Position = position;
        foreach (CableRoute original in document.Cables.Values.ToArray())
        {
            Polyline route = original.Route;
            if (original.From is PortReference from && from.DeviceId == deviceId)
            {
                route = route.WithPoint(0, RequirePortPosition(device, from.PortId));
            }
            if (original.To is PortReference to && to.DeviceId == deviceId)
            {
                route = route.WithPoint(route.Points.Count - 1, RequirePortPosition(device, to.PortId));
            }
            if (ReferenceEquals(route, original.Route))
            {
                continue;
            }

            var updated = original with { Route = route };
            document.Replace(updated);
            SyncConduitEndpoints(document, updated, oldEndpoints[original.Id]);
        }
    }

    private static Point2 RequirePortPosition(Device device, string portId)
    {
        Port port = device.Ports.FirstOrDefault(port => port.Id == portId)
            ?? throw new InvalidOperationException($"Device '{device.Id}' has no port '{portId}'.");
        return device.Position + DocumentHandles.Rotate(port.Position, device.RotationDegrees);
    }

    private static void SyncConduitEndpoints(
        ProjectDocument document,
        CableRoute cable,
        (Point2 First, Point2 Last) previous)
    {
        if (cable.ConduitId is not ObjectId conduitId ||
            !document.TryGetConduit(conduitId, out Conduit? conduit) ||
            conduit is null)
        {
            return;
        }

        Polyline route = conduit.Route;
        if (route.Points[0] == previous.First)
        {
            route = route.WithPoint(0, cable.Route.Points[0]);
        }
        if (route.Points[^1] == previous.Last)
        {
            route = route.WithPoint(route.Points.Count - 1, cable.Route.Points[^1]);
        }
        if (!ReferenceEquals(route, conduit.Route))
        {
            document.Replace(conduit with { Route = route });
        }
    }

    private static double NormalizeDegrees(double degrees)
    {
        double normalized = degrees % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
