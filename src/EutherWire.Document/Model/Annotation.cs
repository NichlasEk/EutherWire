using EutherWire.Document.Geometry;

namespace EutherWire.Document.Model;

public sealed class Annotation
{
    public Annotation(ObjectId id, Point2 position, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Id = id;
        Position = position;
        Text = text;
    }

    public ObjectId Id { get; }
    public Point2 Position { get; internal set; }
    public string Text { get; internal set; }
}
