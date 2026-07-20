using EutherWire.Document.Geometry;
using EutherWire.Document.Editing;
using EutherWire.Document.Model;

namespace EutherWire.Document.Commands;

public interface IDocumentCommand
{
    string Description { get; }
    void Apply(ProjectDocument document);
    void Undo(ProjectDocument document);
}

public sealed class MoveDeviceCommand(ObjectId deviceId, Point2 destination) : IDocumentCommand
{
    private Point2 _origin;
    private bool _hasOrigin;

    public string Description => "Move device";

    public void Apply(ProjectDocument document)
    {
        Device device = document.RequireDevice(deviceId);
        if (!_hasOrigin)
        {
            _origin = device.Position;
            _hasOrigin = true;
        }
        device.Position = destination;
    }

    public void Undo(ProjectDocument document)
    {
        if (!_hasOrigin)
        {
            throw new InvalidOperationException("A command cannot be undone before it has been applied.");
        }
        document.RequireDevice(deviceId).Position = _origin;
    }
}

public sealed class AddDeviceCommand(Device device) : IDocumentCommand
{
    public string Description => $"Add {device.Id}";

    public void Apply(ProjectDocument document) => document.Add(device);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveDevice(device.Id, out _))
        {
            throw new InvalidOperationException($"Device '{device.Id}' cannot be removed because it no longer exists.");
        }
    }
}

public sealed class AddCableCommand(CableRoute cable) : IDocumentCommand
{
    public string Description => $"Add {cable.Id}";

    public void Apply(ProjectDocument document) => document.Add(cable);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveCable(cable.Id, out _))
        {
            throw new InvalidOperationException($"Cable '{cable.Id}' cannot be removed because it no longer exists.");
        }
    }
}

public sealed class AddConduitCommand(Conduit conduit) : IDocumentCommand
{
    public string Description => $"Add {conduit.Id}";

    public void Apply(ProjectDocument document) => document.Add(conduit);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveConduit(conduit.Id, out _))
        {
            throw new InvalidOperationException($"Conduit '{conduit.Id}' cannot be removed because it no longer exists.");
        }
    }
}

public sealed class AddAnnotationCommand(Annotation annotation) : IDocumentCommand
{
    public string Description => $"Add {annotation.Id}";

    public void Apply(ProjectDocument document) => document.Add(annotation);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveAnnotation(annotation.Id, out _))
        {
            throw new InvalidOperationException($"Annotation '{annotation.Id}' cannot be removed because it no longer exists.");
        }
    }
}

public sealed class AddOpeningCommand(BuildingOpening opening) : IDocumentCommand
{
    public string Description => $"Add {opening.Id}";

    public void Apply(ProjectDocument document) => document.Add(opening);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveOpening(opening.Id, out _))
        {
            throw new InvalidOperationException($"Opening '{opening.Id}' no longer exists.");
        }
    }
}

public sealed class AddWallDimensionCommand(WallDimension dimension) : IDocumentCommand
{
    public string Description => $"Add {dimension.Id}";

    public void Apply(ProjectDocument document) => document.Add(dimension);

    public void Undo(ProjectDocument document)
    {
        if (!document.RemoveWallDimension(dimension.Id, out _))
        {
            throw new InvalidOperationException($"Wall dimension '{dimension.Id}' no longer exists.");
        }
    }
}

public sealed class MoveEditHandleCommand(EditHandleId handleId, Point2 destination) : IDocumentCommand
{
    private Point2 _origin;
    private bool _hasOrigin;

    public string Description => $"Move {handleId}";

    public void Apply(ProjectDocument document)
    {
        if (!_hasOrigin)
        {
            _origin = DocumentHandleEditor.RequirePosition(document, handleId);
            _hasOrigin = true;
        }
        DocumentHandleEditor.SetPosition(document, handleId, destination);
    }

    public void Undo(ProjectDocument document)
    {
        if (!_hasOrigin)
        {
            throw new InvalidOperationException("A command cannot be undone before it has been applied.");
        }
        DocumentHandleEditor.SetPosition(document, handleId, _origin);
    }
}

public sealed class MoveSpatialHandleCommand(EditHandleId handleId, Point3 destination) : IDocumentCommand
{
    private Point3 _origin;
    private bool _hasOrigin;

    public string Description => $"Move {handleId} in 3D";

    public void Apply(ProjectDocument document)
    {
        if (!_hasOrigin)
        {
            _origin = DocumentHandleEditor.RequireSpatialPosition(document, handleId);
            _hasOrigin = true;
        }
        DocumentHandleEditor.SetSpatialPosition(document, handleId, destination);
    }

    public void Undo(ProjectDocument document)
    {
        if (!_hasOrigin) throw new InvalidOperationException("A command cannot be undone before it has been applied.");
        DocumentHandleEditor.SetSpatialPosition(document, handleId, _origin);
    }
}

public sealed class SetObjectLabelCommand(ObjectId objectId, string label) : IDocumentCommand
{
    private string? _previous;

    public string Description => $"Rename {objectId}";

    public void Apply(ProjectDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        _previous ??= GetLabel(document, objectId);
        SetLabel(document, objectId, label);
    }

    public void Undo(ProjectDocument document) =>
        SetLabel(document, objectId, _previous ?? throw new InvalidOperationException("Command has not been applied."));

    private static string GetLabel(ProjectDocument document, ObjectId id)
    {
        if (document.Devices.TryGetValue(id, out Device? device)) return device.Label;
        if (document.Cables.TryGetValue(id, out CableRoute? cable)) return cable.Label;
        if (document.Conduits.TryGetValue(id, out Conduit? conduit)) return conduit.Label;
        if (document.Annotations.TryGetValue(id, out Annotation? annotation)) return annotation.Text;
        if (document.Openings.TryGetValue(id, out BuildingOpening? opening)) return opening.Label;
        if (document.WallDimensions.TryGetValue(id, out WallDimension? dimension)) return dimension.Label;
        throw new KeyNotFoundException($"Object '{id}' does not exist.");
    }

    private static void SetLabel(ProjectDocument document, ObjectId id, string value)
    {
        if (document.Devices.TryGetValue(id, out Device? device))
        {
            device.Label = value;
            return;
        }
        if (document.Cables.TryGetValue(id, out CableRoute? cable))
        {
            document.Replace(cable with { Label = value });
            return;
        }
        if (document.Conduits.TryGetValue(id, out Conduit? conduit))
        {
            document.Replace(conduit with { Label = value });
            return;
        }
        if (document.Annotations.TryGetValue(id, out Annotation? annotation))
        {
            annotation.Text = value;
            return;
        }
        if (document.Openings.TryGetValue(id, out BuildingOpening? opening))
        {
            opening.Label = value;
            return;
        }
        if (document.WallDimensions.TryGetValue(id, out WallDimension? dimension))
        {
            dimension.Label = value;
            return;
        }
        throw new KeyNotFoundException($"Object '{id}' does not exist.");
    }
}

public sealed class SetDeviceKindCommand(ObjectId deviceId, DeviceKind kind) : IDocumentCommand
{
    private DeviceKind? _previous;

    public string Description => $"Set {deviceId} type";

    public void Apply(ProjectDocument document)
    {
        Device device = document.RequireDevice(deviceId);
        _previous ??= device.Kind;
        device.Kind = kind;
    }

    public void Undo(ProjectDocument document) =>
        document.RequireDevice(deviceId).Kind = _previous ?? throw new InvalidOperationException("Command has not been applied.");
}

public sealed class SetDeviceElevationCommand(ObjectId deviceId, double elevationMillimetres) : IDocumentCommand
{
    private double? _previous;

    public string Description => $"Set {deviceId} elevation";

    public void Apply(ProjectDocument document)
    {
        if (!double.IsFinite(elevationMillimetres) || elevationMillimetres < 0) throw new ArgumentOutOfRangeException(nameof(elevationMillimetres));
        Device device = document.RequireDevice(deviceId);
        _previous ??= device.ElevationMillimetres;
        device.ElevationMillimetres = elevationMillimetres;
    }

    public void Undo(ProjectDocument document) =>
        document.RequireDevice(deviceId).ElevationMillimetres = _previous ?? throw new InvalidOperationException("Command has not been applied.");
}

public sealed class SetDeviceMountingSurfaceCommand(ObjectId deviceId, MountingSurface surface) : IDocumentCommand
{
    private MountingSurface? _previous;
    private Point3 _previousPosition;

    public string Description => $"Set {deviceId} mounting surface";

    public void Apply(ProjectDocument document)
    {
        Device device = document.RequireDevice(deviceId);
        if (_previous is null)
        {
            _previous = device.MountingSurface;
            _previousPosition = new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres);
        }
        device.MountingSurface = surface;
        Point3 constrained = MountingSurfaceGeometry.Constrain(document.Space, surface, _previousPosition);
        DocumentHandleEditor.SetSpatialPosition(document, new EditHandleId(deviceId, EditHandleKind.Move), constrained);
    }

    public void Undo(ProjectDocument document)
    {
        Device device = document.RequireDevice(deviceId);
        device.MountingSurface = _previous ?? throw new InvalidOperationException("Command has not been applied.");
        DocumentHandleEditor.SetSpatialPosition(document, new EditHandleId(deviceId, EditHandleKind.Move), _previousPosition);
    }
}

public sealed class SetSpaceVolumeCommand(SpaceVolume volume) : IDocumentCommand
{
    private SpaceVolume? _previous;
    private Dictionary<ObjectId, Point3>? _previousMountedPositions;
    private Dictionary<ObjectId, Point3>? _previousOpeningPositions;
    private Dictionary<ObjectId, (Point3 Start, Point3 End)>? _previousDimensionPositions;

    public string Description => "Set room dimensions";

    public void Apply(ProjectDocument document)
    {
        volume.Validate();
        if (_previous is null)
        {
            _previous = document.Space;
            _previousMountedPositions = document.Devices.Values
                .Where(device => device.MountingSurface != MountingSurface.Free)
                .ToDictionary(device => device.Id, device => new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres));
            _previousOpeningPositions = document.Openings.Values.ToDictionary(opening => opening.Id, opening => opening.Centre);
            _previousDimensionPositions = document.WallDimensions.Values.ToDictionary(dimension => dimension.Id, dimension => (dimension.Start, dimension.End));
        }
        document.Space = volume;
        foreach (Device device in document.Devices.Values.Where(device => device.MountingSurface != MountingSurface.Free).ToArray())
        {
            var handle = new EditHandleId(device.Id, EditHandleKind.Move);
            DocumentHandleEditor.SetSpatialPosition(document, handle, DocumentHandleEditor.RequireSpatialPosition(document, handle));
        }
        foreach (BuildingOpening opening in document.Openings.Values.ToArray())
        {
            var handle = new EditHandleId(opening.Id, EditHandleKind.Move);
            DocumentHandleEditor.SetSpatialPosition(document, handle, DocumentHandleEditor.RequireSpatialPosition(document, handle));
        }
        foreach (WallDimension dimension in document.WallDimensions.Values)
        {
            dimension.Start = MountingSurfaceGeometry.Constrain(document.Space, dimension.Surface, dimension.Start);
            dimension.End = MountingSurfaceGeometry.Constrain(document.Space, dimension.Surface, dimension.End);
        }
    }

    public void Undo(ProjectDocument document)
    {
        document.Space = _previous ?? throw new InvalidOperationException("Command has not been applied.");
        foreach ((ObjectId id, Point3 position) in _previousMountedPositions ?? throw new InvalidOperationException("Command has not been applied."))
        {
            DocumentHandleEditor.SetSpatialPosition(document, new EditHandleId(id, EditHandleKind.Move), position);
        }
        foreach ((ObjectId id, Point3 position) in _previousOpeningPositions ?? throw new InvalidOperationException("Command has not been applied."))
        {
            DocumentHandleEditor.SetSpatialPosition(document, new EditHandleId(id, EditHandleKind.Move), position);
        }
        foreach ((ObjectId id, (Point3 Start, Point3 End) points) in _previousDimensionPositions ?? throw new InvalidOperationException("Command has not been applied."))
        {
            WallDimension dimension = document.RequireWallDimension(id);
            dimension.Start = points.Start;
            dimension.End = points.End;
        }
    }
}

public sealed class SetOpeningGeometryCommand(
    ObjectId openingId,
    OpeningKind kind,
    MountingSurface surface,
    Point3 centre,
    double widthMillimetres,
    double heightMillimetres) : IDocumentCommand
{
    private (OpeningKind Kind, MountingSurface Surface, Point3 Centre, double Width, double Height)? _previous;

    public string Description => $"Edit {openingId}";

    public void Apply(ProjectDocument document)
    {
        BuildingOpening opening = document.RequireOpening(openingId);
        _previous ??= (opening.Kind, opening.Surface, opening.Centre, opening.WidthMillimetres, opening.HeightMillimetres);
        ApplyValues(document, opening, kind, surface, centre, widthMillimetres, heightMillimetres);
    }

    public void Undo(ProjectDocument document)
    {
        var previous = _previous ?? throw new InvalidOperationException("Command has not been applied.");
        ApplyValues(document, document.RequireOpening(openingId), previous.Kind, previous.Surface, previous.Centre, previous.Width, previous.Height);
    }

    private static void ApplyValues(ProjectDocument document, BuildingOpening opening, OpeningKind nextKind, MountingSurface nextSurface, Point3 nextCentre, double width, double height)
    {
        _ = new BuildingOpening(opening.Id, nextKind, nextSurface, nextCentre, width, height, opening.Label);
        opening.Kind = nextKind;
        opening.Surface = nextSurface;
        opening.Centre = BuildingOpeningGeometry.ConstrainCentre(document.Space, nextSurface, nextCentre, width, height);
        opening.WidthMillimetres = width;
        opening.HeightMillimetres = height;
    }
}

public sealed class SetCableKindCommand(ObjectId cableId, CableKind kind) : IDocumentCommand
{
    private CableKind? _previous;

    public string Description => $"Set {cableId} cable type";

    public void Apply(ProjectDocument document)
    {
        CableRoute cable = document.RequireCable(cableId);
        _previous ??= cable.Kind;
        document.Replace(cable with { Kind = kind });
    }

    public void Undo(ProjectDocument document)
    {
        CableRoute cable = document.RequireCable(cableId);
        document.Replace(cable with { Kind = _previous ?? throw new InvalidOperationException("Command has not been applied.") });
    }
}

public sealed class SetConduitDiameterCommand(ObjectId conduitId, double diameterMillimetres) : IDocumentCommand
{
    private double? _previous;

    public string Description => $"Set {conduitId} diameter";

    public void Apply(ProjectDocument document)
    {
        if (!double.IsFinite(diameterMillimetres) || diameterMillimetres <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameterMillimetres));
        }
        Conduit conduit = document.RequireConduit(conduitId);
        _previous ??= conduit.InnerDiameterMillimetres;
        document.Replace(conduit with { InnerDiameterMillimetres = diameterMillimetres });
    }

    public void Undo(ProjectDocument document)
    {
        Conduit conduit = document.RequireConduit(conduitId);
        document.Replace(conduit with { InnerDiameterMillimetres = _previous ?? throw new InvalidOperationException("Command has not been applied.") });
    }
}

public sealed class SetPlanningSettingsCommand(PlanningSettings settings) : IDocumentCommand
{
    private PlanningSettings? _previous;

    public string Description => "Set project planning margins";

    public void Apply(ProjectDocument document)
    {
        settings.Validate();
        _previous ??= document.Planning;
        document.Planning = settings;
    }

    public void Undo(ProjectDocument document) =>
        document.Planning = _previous ?? throw new InvalidOperationException("Command has not been applied.");
}

public sealed class SetCableInstallationCommand(
    ObjectId cableId,
    InstallationStatus status,
    double? actualLengthMillimetres) : IDocumentCommand
{
    private CableRoute? _previous;

    public string Description => $"Set {cableId} installation state";

    public void Apply(ProjectDocument document)
    {
        if (actualLengthMillimetres is double length && (!double.IsFinite(length) || length < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(actualLengthMillimetres));
        }
        CableRoute cable = document.RequireCable(cableId);
        _previous ??= cable;
        document.Replace(cable with { InstallationStatus = status, ActualLengthMillimetres = actualLengthMillimetres });
    }

    public void Undo(ProjectDocument document)
    {
        _ = document.RequireCable(cableId);
        document.Replace(_previous ?? throw new InvalidOperationException("Command has not been applied."));
    }
}

public sealed class DeleteObjectCommand(ObjectId objectId) : IDocumentCommand
{
    private object? _removed;

    public string Description => $"Delete {objectId}";

    public void Apply(ProjectDocument document)
    {
        _removed ??= Capture(document);
        Remove(document);
    }

    public void Undo(ProjectDocument document)
    {
        switch (_removed)
        {
            case Device device:
                document.Add(device);
                break;
            case CableRoute cable:
                document.Add(cable);
                break;
            case Conduit conduit:
                document.Add(conduit);
                break;
            case Annotation annotation:
                document.Add(annotation);
                break;
            case BuildingOpening opening:
                document.Add(opening);
                break;
            case WallDimension dimension:
                document.Add(dimension);
                break;
            default:
                throw new InvalidOperationException("Command has not been applied.");
        }
    }

    private object Capture(ProjectDocument document)
    {
        if (document.Devices.TryGetValue(objectId, out Device? device))
        {
            if (document.Cables.Values.Any(cable => cable.From?.DeviceId == objectId || cable.To?.DeviceId == objectId))
            {
                throw new InvalidOperationException($"Device '{objectId}' is connected to a cable and cannot be deleted.");
            }
            return device;
        }
        if (document.Cables.TryGetValue(objectId, out CableRoute? cable)) return cable;
        if (document.Conduits.TryGetValue(objectId, out Conduit? conduit))
        {
            if (document.Cables.Values.Any(cable => cable.ConduitId == objectId))
            {
                throw new InvalidOperationException($"Conduit '{objectId}' contains a cable and cannot be deleted.");
            }
            return conduit;
        }
        if (document.Annotations.TryGetValue(objectId, out Annotation? annotation)) return annotation;
        if (document.Openings.TryGetValue(objectId, out BuildingOpening? opening)) return opening;
        if (document.WallDimensions.TryGetValue(objectId, out WallDimension? dimension)) return dimension;
        throw new KeyNotFoundException($"Object '{objectId}' does not exist.");
    }

    private void Remove(ProjectDocument document)
    {
        bool removed = _removed switch
        {
            Device => document.RemoveDevice(objectId, out _),
            CableRoute => document.RemoveCable(objectId, out _),
            Conduit => document.RemoveConduit(objectId, out _),
            Annotation => document.RemoveAnnotation(objectId, out _),
            BuildingOpening => document.RemoveOpening(objectId, out _),
            WallDimension => document.RemoveWallDimension(objectId, out _),
            _ => false,
        };
        if (!removed)
        {
            throw new InvalidOperationException($"Object '{objectId}' no longer exists.");
        }
    }
}

public sealed class InsertRouteVertexCommand(ObjectId routeId, int index, Point2 position) : IDocumentCommand
{
    public string Description => $"Insert {routeId}:vertex:{index}";

    public void Apply(ProjectDocument document) => DocumentHandleEditor.InsertVertex(document, routeId, index, position);

    public void Undo(ProjectDocument document) => _ = DocumentHandleEditor.DeleteVertex(document, routeId, index);
}

public sealed class DeleteRouteVertexCommand(ObjectId routeId, int index) : IDocumentCommand
{
    private Point3 _removed;
    private bool _hasRemoved;

    public string Description => $"Delete {routeId}:vertex:{index}";

    public void Apply(ProjectDocument document)
    {
        Point3 removed = DocumentHandleEditor.DeleteVertex(document, routeId, index);
        if (!_hasRemoved)
        {
            _removed = removed;
            _hasRemoved = true;
        }
    }

    public void Undo(ProjectDocument document)
    {
        if (!_hasRemoved)
        {
            throw new InvalidOperationException("Command has not been applied.");
        }
        DocumentHandleEditor.InsertVertex(document, routeId, index, _removed);
    }
}

public sealed class SetRouteVertexElevationCommand(ObjectId routeId, int index, double elevationMillimetres) : IDocumentCommand
{
    private double? _previous;

    public string Description => $"Set {routeId}:vertex:{index} elevation";

    public void Apply(ProjectDocument document)
    {
        if (!double.IsFinite(elevationMillimetres) || elevationMillimetres < 0) throw new ArgumentOutOfRangeException(nameof(elevationMillimetres));
        Polyline route = RequireRoute(document);
        _previous ??= route.SpatialPoints[index].Z;
        SetRoute(document, elevationMillimetres);
    }

    public void Undo(ProjectDocument document) =>
        SetRoute(document, _previous ?? throw new InvalidOperationException("Command has not been applied."));

    private Polyline RequireRoute(ProjectDocument document)
    {
        if (document.Conduits.TryGetValue(routeId, out Conduit? conduit)) return conduit.Route;
        if (document.Cables.TryGetValue(routeId, out CableRoute? cable) && cable.ConduitId is null) return cable.Route;
        throw new KeyNotFoundException($"Editable route '{routeId}' does not exist.");
    }

    private void SetRoute(ProjectDocument document, double elevation)
    {
        if (document.Conduits.TryGetValue(routeId, out Conduit? conduit))
        {
            Polyline route = conduit.Route.WithElevation(index, elevation);
            document.Replace(conduit with { Route = route });
            foreach (CableRoute cable in document.Cables.Values.Where(cable => cable.ConduitId == routeId).ToList())
            {
                document.Replace(cable with { Route = route });
            }
            return;
        }
        CableRoute standalone = document.RequireCable(routeId);
        if (standalone.ConduitId is not null) throw new InvalidOperationException("Contained cable elevation is controlled by its conduit.");
        document.Replace(standalone with { Route = standalone.Route.WithElevation(index, elevation) });
    }
}

public sealed class CommandHistory
{
    private readonly Stack<IDocumentCommand> _undo = [];
    private readonly Stack<IDocumentCommand> _redo = [];

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Execute(ProjectDocument document, IDocumentCommand command)
    {
        command.Apply(document);
        _undo.Push(command);
        _redo.Clear();
    }

    public bool Undo(ProjectDocument document)
    {
        if (!_undo.TryPop(out IDocumentCommand? command))
        {
            return false;
        }
        command.Undo(document);
        _redo.Push(command);
        return true;
    }

    public bool Redo(ProjectDocument document)
    {
        if (!_redo.TryPop(out IDocumentCommand? command))
        {
            return false;
        }
        command.Apply(document);
        _undo.Push(command);
        return true;
    }
}
