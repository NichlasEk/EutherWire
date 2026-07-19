namespace EutherWire.Document.Geometry;

/// <summary>A position in document millimetres.</summary>
public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 point, Vector2 delta) =>
        new(point.X + delta.X, point.Y + delta.Y);
}

/// <summary>A displacement in document millimetres.</summary>
public readonly record struct Vector2(double X, double Y);
