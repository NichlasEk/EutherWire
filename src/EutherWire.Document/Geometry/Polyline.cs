namespace EutherWire.Document.Geometry;

public sealed class Polyline
{
    private readonly List<Point2> _points;

    public Polyline(IEnumerable<Point2> points)
    {
        _points = points.ToList();
        if (_points.Count < 2)
        {
            throw new ArgumentException("A polyline needs at least two points.", nameof(points));
        }
        if (_points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new ArgumentException("Polyline coordinates must be finite.", nameof(points));
        }
    }

    public IReadOnlyList<Point2> Points => _points;

    public Polyline WithPoint(int index, Point2 point)
    {
        if ((uint)index >= (uint)_points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        var points = new List<Point2>(_points);
        points[index] = point;
        return new Polyline(points);
    }

    public Polyline InsertPoint(int index, Point2 point)
    {
        if (index <= 0 || index >= _points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "A route vertex must be inserted between existing points.");
        }
        var points = new List<Point2>(_points);
        points.Insert(index, point);
        return new Polyline(points);
    }

    public Polyline RemovePoint(int index)
    {
        if (_points.Count <= 2)
        {
            throw new InvalidOperationException("A route must retain at least two points.");
        }
        if (index <= 0 || index >= _points.Count - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Route endpoints cannot be removed.");
        }
        var points = new List<Point2>(_points);
        points.RemoveAt(index);
        return new Polyline(points);
    }

    public double LengthMillimetres
    {
        get
        {
            double length = 0;
            for (int index = 1; index < _points.Count; index++)
            {
                double x = _points[index].X - _points[index - 1].X;
                double y = _points[index].Y - _points[index - 1].Y;
                length += Math.Sqrt(x * x + y * y);
            }
            return length;
        }
    }
}
