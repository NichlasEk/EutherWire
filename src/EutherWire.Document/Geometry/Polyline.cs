namespace EutherWire.Document.Geometry;

public sealed class Polyline
{
    private readonly List<Point2> _points;
    private readonly List<double> _elevations;

    public Polyline(IEnumerable<Point2> points)
        : this(points.Select(point => new Point3(point.X, point.Y, 0)))
    {
    }

    public Polyline(IEnumerable<Point3> points)
    {
        List<Point3> spatialPoints = points.ToList();
        _points = spatialPoints.Select(point => point.Plan).ToList();
        _elevations = spatialPoints.Select(point => point.Z).ToList();
        if (_points.Count < 2)
        {
            throw new ArgumentException("A polyline needs at least two points.", nameof(points));
        }
        if (spatialPoints.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z) || point.Z < 0))
        {
            throw new ArgumentException("Polyline coordinates must be finite and elevations non-negative.", nameof(points));
        }
    }

    public IReadOnlyList<Point2> Points => _points;
    public IReadOnlyList<Point3> SpatialPoints => _points.Select((point, index) => new Point3(point.X, point.Y, _elevations[index])).ToList();

    public Polyline WithPoint(int index, Point2 point)
    {
        if ((uint)index >= (uint)_points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        var points = new List<Point2>(_points);
        points[index] = point;
        return new Polyline(points.Select((item, itemIndex) => new Point3(item.X, item.Y, _elevations[itemIndex])));
    }

    public Polyline WithElevation(int index, double elevationMillimetres)
    {
        if ((uint)index >= (uint)_points.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!double.IsFinite(elevationMillimetres) || elevationMillimetres < 0) throw new ArgumentOutOfRangeException(nameof(elevationMillimetres));
        return new Polyline(_points.Select((point, itemIndex) => new Point3(point.X, point.Y, itemIndex == index ? elevationMillimetres : _elevations[itemIndex])));
    }

    public Polyline InsertPoint(int index, Point2 point)
    {
        if (index <= 0 || index >= _points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "A route vertex must be inserted between existing points.");
        }
        return InsertPoint(index, new Point3(point.X, point.Y, (_elevations[index - 1] + _elevations[index]) / 2));
    }

    public Polyline InsertPoint(int index, Point3 point)
    {
        if (index <= 0 || index >= _points.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "A route vertex must be inserted between existing points.");
        }
        var points = new List<Point2>(_points);
        points.Insert(index, point.Plan);
        var elevations = new List<double>(_elevations);
        elevations.Insert(index, point.Z);
        return new Polyline(points.Select((item, itemIndex) => new Point3(item.X, item.Y, elevations[itemIndex])));
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
        var elevations = new List<double>(_elevations);
        elevations.RemoveAt(index);
        return new Polyline(points.Select((item, itemIndex) => new Point3(item.X, item.Y, elevations[itemIndex])));
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
                double z = _elevations[index] - _elevations[index - 1];
                length += Math.Sqrt(x * x + y * y + z * z);
            }
            return length;
        }
    }
}
