using EutherWire.Document.Geometry;
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
