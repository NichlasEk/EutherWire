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
            AddVertices(handles, cable.Id, cable.Route);
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

    private static Vector2 Rotate(Point2 point, double degrees)
    {
        double radians = degrees * Math.PI / 180;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new Vector2(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine);
    }
}
