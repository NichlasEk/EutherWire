using EutherWire.Document.Geometry;
using EutherWire.Document.Model;

namespace EutherWire.Document.Editing;

public enum EditHandleKind
{
    Move,
    Rotate,
    Resize,
    Elevation,
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
        if ((kind is EditHandleKind.Port or EditHandleKind.Resize) && !string.IsNullOrWhiteSpace(parts[2]))
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
    public const double ElevationHandleOffsetMillimetres = 500;

    public static IReadOnlyList<EditHandle> Enumerate(ProjectDocument document)
    {
        var handles = new List<EditHandle>();
        foreach (Device device in document.Devices.Values.OrderBy(device => device.Id.Value, StringComparer.Ordinal))
        {
            handles.Add(new EditHandle(new EditHandleId(device.Id, EditHandleKind.Move), device.Position));
            handles.Add(new EditHandle(new EditHandleId(device.Id, EditHandleKind.Elevation), device.Position));
            handles.Add(new EditHandle(
                new EditHandleId(device.Id, EditHandleKind.Rotate),
                device.Position + new Vector2(0, -RotationHandleOffsetMillimetres)));

            for (int index = 0; index < device.Ports.Count; index++)
            {
                Port port = device.Ports[index];
                handles.Add(new EditHandle(
                    new EditHandleId(device.Id, EditHandleKind.Port, Name: port.Id),
                    device.Position + Rotate(port.Position, device.RotationDegrees)));
            }
        }
        foreach (BuildingOpening opening in document.Openings.Values.OrderBy(opening => opening.Id.Value, StringComparer.Ordinal))
        {
            handles.Add(new EditHandle(new EditHandleId(opening.Id, EditHandleKind.Move), opening.Centre.Plan));
            Point3 start = BuildingOpeningGeometry.ResizeHandle(opening, "start");
            Point3 end = BuildingOpeningGeometry.ResizeHandle(opening, "end");
            handles.Add(new EditHandle(new EditHandleId(opening.Id, EditHandleKind.Resize, Name: "start"), start.Plan));
            handles.Add(new EditHandle(new EditHandleId(opening.Id, EditHandleKind.Resize, Name: "end"), end.Plan));
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
        foreach (Annotation annotation in document.Annotations.Values.OrderBy(annotation => annotation.Id.Value, StringComparer.Ordinal))
        {
            handles.Add(new EditHandle(new EditHandleId(annotation.Id, EditHandleKind.LabelAnchor), annotation.Position));
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

    public static Point3 RequireSpatialPosition(ProjectDocument document, EditHandleId id)
    {
        Point2 position = RequirePosition(document, id);
        if (document.Devices.TryGetValue(id.ObjectId, out Device? device))
        {
            return new Point3(position.X, position.Y, device.ElevationMillimetres + (id.Kind == EditHandleKind.Elevation ? DocumentHandles.ElevationHandleOffsetMillimetres : 0));
        }
        if (document.Openings.TryGetValue(id.ObjectId, out BuildingOpening? opening))
        {
            return id.Kind == EditHandleKind.Resize
                ? BuildingOpeningGeometry.ResizeHandle(opening, id.Name ?? throw new InvalidOperationException("Resize handle has no name."))
                : opening.Centre;
        }
        if (id.Kind == EditHandleKind.Vertex && id.Index >= 0)
        {
            if (document.Conduits.TryGetValue(id.ObjectId, out Conduit? conduit)) return conduit.Route.SpatialPoints[id.Index];
            if (document.Cables.TryGetValue(id.ObjectId, out CableRoute? cable)) return cable.Route.SpatialPoints[id.Index];
        }
        return new Point3(position.X, position.Y, 0);
    }

    public static bool CanSetPosition(EditHandleId id) =>
        id.Kind is EditHandleKind.Move or EditHandleKind.Rotate or EditHandleKind.Resize or EditHandleKind.Elevation or EditHandleKind.Vertex or EditHandleKind.LabelAnchor;

    public static void SetPosition(ProjectDocument document, EditHandleId id, Point2 position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Handle coordinates must be finite.");
        }

        switch (id.Kind)
        {
            case EditHandleKind.Move:
                if (document.Openings.TryGetValue(id.ObjectId, out BuildingOpening? opening))
                {
                    opening.Centre = new Point3(position.X, position.Y, opening.Centre.Z);
                    return;
                }
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
            case EditHandleKind.Resize:
                BuildingOpening resizeOpening = document.RequireOpening(id.ObjectId);
                Point3 current = BuildingOpeningGeometry.ResizeHandle(resizeOpening, id.Name ?? throw new InvalidOperationException("Resize handle has no name."));
                BuildingOpeningGeometry.ResizeFromHandle(resizeOpening, id.Name!, new Point3(position.X, position.Y, current.Z));
                return;
            case EditHandleKind.Elevation:
                return;
            case EditHandleKind.Vertex:
                SetRouteVertex(document, id, position);
                return;
            case EditHandleKind.LabelAnchor:
                document.RequireAnnotation(id.ObjectId).Position = position;
                return;
            default:
                throw new InvalidOperationException($"Edit handle '{id}' is an anchor and cannot be moved directly.");
        }
    }

    public static void SetSpatialPosition(ProjectDocument document, EditHandleId id, Point3 position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z) || position.Z < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Spatial handle coordinates must be finite and elevation non-negative.");
        }
        if (id.Kind == EditHandleKind.Elevation)
        {
            Device elevationDevice = document.RequireDevice(id.ObjectId);
            double elevation = Math.Max(0, position.Z - DocumentHandles.ElevationHandleOffsetMillimetres);
            SetSpatialPosition(document, new EditHandleId(id.ObjectId, EditHandleKind.Move), new Point3(elevationDevice.Position.X, elevationDevice.Position.Y, elevation));
            return;
        }
        if (id.Kind == EditHandleKind.Resize && document.Openings.TryGetValue(id.ObjectId, out BuildingOpening? resizeOpening))
        {
            position = MountingSurfaceGeometry.Constrain(document.Space, resizeOpening.Surface, position);
            BuildingOpeningGeometry.ResizeFromHandle(resizeOpening, id.Name ?? throw new InvalidOperationException("Resize handle has no name."), position);
            return;
        }
        Device? spatialDevice = id.Kind == EditHandleKind.Move && document.Devices.TryGetValue(id.ObjectId, out Device? candidate)
            ? candidate
            : null;
        BuildingOpening? spatialOpening = id.Kind == EditHandleKind.Move && document.Openings.TryGetValue(id.ObjectId, out BuildingOpening? openingCandidate)
            ? openingCandidate
            : null;
        if (spatialDevice is not null)
        {
            position = MountingSurfaceGeometry.Constrain(document.Space, spatialDevice.MountingSurface, position);
        }
        else if (spatialOpening is not null)
        {
            position = BuildingOpeningGeometry.ConstrainCentre(document.Space, spatialOpening.Surface, position, spatialOpening.WidthMillimetres, spatialOpening.HeightMillimetres);
        }
        SetPosition(document, id, position.Plan);
        if (spatialDevice is not null)
        {
            spatialDevice.ElevationMillimetres = position.Z;
            foreach (CableRoute cable in document.Cables.Values.ToArray())
            {
                int endpoint = cable.From is PortReference from && from.DeviceId == spatialDevice.Id
                    ? 0
                    : cable.To is PortReference to && to.DeviceId == spatialDevice.Id
                        ? cable.Route.SpatialPoints.Count - 1
                        : -1;
                if (endpoint < 0) continue;
                Polyline route = cable.Route.WithElevation(endpoint, position.Z);
                document.Replace(cable with { Route = route });
                if (cable.ConduitId is ObjectId conduitId && document.TryGetConduit(conduitId, out Conduit? conduit) && conduit is not null)
                {
                    document.Replace(conduit with { Route = conduit.Route.WithElevation(endpoint, position.Z) });
                }
            }
            return;
        }
        if (spatialOpening is not null)
        {
            spatialOpening.Centre = position;
            return;
        }
        if (id.Kind == EditHandleKind.Vertex)
        {
            if (document.TryGetConduit(id.ObjectId, out Conduit? conduit) && conduit is not null)
            {
                Polyline route = conduit.Route.WithElevation(id.Index, position.Z);
                document.Replace(conduit with { Route = route });
                foreach (CableRoute contained in document.Cables.Values.Where(cable => cable.ConduitId == id.ObjectId).ToArray())
                {
                    document.Replace(contained with { Route = route });
                }
                return;
            }
            if (document.TryGetCable(id.ObjectId, out CableRoute? cable) && cable is not null && cable.ConduitId is null)
            {
                document.Replace(cable with { Route = cable.Route.WithElevation(id.Index, position.Z) });
            }
        }
    }

    public static void InsertVertex(ProjectDocument document, ObjectId routeId, int index, Point2 position)
    {
        if (document.TryGetCable(routeId, out CableRoute? cable) && cable is not null)
        {
            if (cable.ConduitId is not null)
            {
                throw new InvalidOperationException($"Cable '{routeId}' inherits vertices from conduit '{cable.ConduitId}'.");
            }
            document.Replace(cable with { Route = cable.Route.InsertPoint(index, position) });
            return;
        }
        if (document.TryGetConduit(routeId, out Conduit? conduit) && conduit is not null)
        {
            document.Replace(conduit with { Route = conduit.Route.InsertPoint(index, position) });
            foreach (CableRoute contained in document.Cables.Values.Where(item => item.ConduitId == routeId).ToArray())
            {
                document.Replace(contained with { Route = contained.Route.InsertPoint(index, position) });
            }
            return;
        }
        throw new KeyNotFoundException($"Route '{routeId}' does not exist.");
    }

    public static void InsertVertex(ProjectDocument document, ObjectId routeId, int index, Point3 position)
    {
        if (document.TryGetCable(routeId, out CableRoute? cable) && cable is not null)
        {
            if (cable.ConduitId is not null) throw new InvalidOperationException($"Cable '{routeId}' inherits vertices from conduit '{cable.ConduitId}'.");
            document.Replace(cable with { Route = cable.Route.InsertPoint(index, position) });
            return;
        }
        if (document.TryGetConduit(routeId, out Conduit? conduit) && conduit is not null)
        {
            document.Replace(conduit with { Route = conduit.Route.InsertPoint(index, position) });
            foreach (CableRoute contained in document.Cables.Values.Where(item => item.ConduitId == routeId).ToArray())
            {
                document.Replace(contained with { Route = contained.Route.InsertPoint(index, position) });
            }
            return;
        }
        throw new KeyNotFoundException($"Route '{routeId}' does not exist.");
    }

    public static Point3 DeleteVertex(ProjectDocument document, ObjectId routeId, int index)
    {
        if (document.TryGetCable(routeId, out CableRoute? cable) && cable is not null)
        {
            if (cable.ConduitId is not null)
            {
                throw new InvalidOperationException($"Cable '{routeId}' inherits vertices from conduit '{cable.ConduitId}'.");
            }
            Point3 removed = cable.Route.SpatialPoints[index];
            document.Replace(cable with { Route = cable.Route.RemovePoint(index) });
            return removed;
        }
        if (document.TryGetConduit(routeId, out Conduit? conduit) && conduit is not null)
        {
            Point3 removed = conduit.Route.SpatialPoints[index];
            document.Replace(conduit with { Route = conduit.Route.RemovePoint(index) });
            foreach (CableRoute contained in document.Cables.Values.Where(item => item.ConduitId == routeId).ToArray())
            {
                document.Replace(contained with { Route = contained.Route.RemovePoint(index) });
            }
            return removed;
        }
        throw new KeyNotFoundException($"Route '{routeId}' does not exist.");
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
