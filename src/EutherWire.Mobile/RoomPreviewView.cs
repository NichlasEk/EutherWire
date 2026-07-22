using Android.Content;
using Android.Graphics;
using Android.Views;
using EutherWire.Document.Editing;
using EutherWire.Document.Geometry;
using EutherWire.Document.Model;
using GraphicsPath = Android.Graphics.Path;
using PaintStyle = Android.Graphics.Paint.Style;

namespace EutherWire.Mobile;

public enum RoomPreviewMode
{
    Plan,
    Room3D,
}

public sealed class RoomPreviewView : View
{
    private readonly ProjectDocument _document;
    private readonly Action<ObjectId?> _selectionChanged;
    private readonly Action<ObjectId> _deviceMoved;
    private readonly ScaleGestureDetector _scaleDetector;
    private readonly Paint _paint = new(PaintFlags.AntiAlias);
    private float _lastX;
    private float _lastY;
    private float _panX;
    private float _panY;
    private float _zoom = 1;
    private double _yaw = -0.72;
    private double _pitch = 0.58;
    private ObjectId? _draggedDeviceId;
    private bool _dragMoved;

    public RoomPreviewView(
        Context context,
        ProjectDocument document,
        RoomPreviewMode mode,
        ObjectId? selectedDeviceId,
        Action<ObjectId?> selectionChanged,
        Action<ObjectId> deviceMoved) : base(context)
    {
        _document = document;
        _selectionChanged = selectionChanged;
        _deviceMoved = deviceMoved;
        Mode = mode;
        SelectedDeviceId = selectedDeviceId;
        _scaleDetector = new ScaleGestureDetector(context, new ScaleListener(this));
        SetBackgroundColor(Color.ParseColor("#07131a"));
        ContentDescription = mode == RoomPreviewMode.Plan
            ? "Touch-driven room plan preview"
            : "Touch-driven three-dimensional room preview";
    }

    public RoomPreviewMode Mode { get; }
    public ObjectId? SelectedDeviceId { get; private set; }

    public void ResetView()
    {
        _panX = 0;
        _panY = 0;
        _zoom = 1;
        _yaw = -0.72;
        _pitch = 0.58;
        Invalidate();
    }

    public override bool OnTouchEvent(MotionEvent? motionEvent)
    {
        if (motionEvent is null) return false;
        _scaleDetector.OnTouchEvent(motionEvent);
        switch (motionEvent.ActionMasked)
        {
            case MotionEventActions.Down:
                Parent?.RequestDisallowInterceptTouchEvent(true);
                _lastX = motionEvent.GetX();
                _lastY = motionEvent.GetY();
                _dragMoved = false;
                if (Mode == RoomPreviewMode.Plan && HitPlanDevice(_lastX, _lastY) is ObjectId deviceId)
                {
                    SelectedDeviceId = deviceId;
                    _draggedDeviceId = deviceId;
                    _selectionChanged(deviceId);
                    Invalidate();
                }
                else
                {
                    _draggedDeviceId = null;
                }
                return true;
            case MotionEventActions.Move when !_scaleDetector.IsInProgress && motionEvent.PointerCount == 1:
                float x = motionEvent.GetX();
                float y = motionEvent.GetY();
                float dx = x - _lastX;
                float dy = y - _lastY;
                if (Mode == RoomPreviewMode.Plan && _draggedDeviceId is ObjectId draggedDeviceId)
                {
                    Point3 destination = PlanScreenToDocument(x, y, _document.RequireDevice(draggedDeviceId).ElevationMillimetres);
                    destination = new Point3(
                        Math.Round(destination.X / 100) * 100,
                        Math.Round(destination.Y / 100) * 100,
                        Math.Round(destination.Z / 100) * 100);
                    DocumentHandleEditor.SetSpatialPosition(
                        _document,
                        new EditHandleId(draggedDeviceId, EditHandleKind.Move),
                        destination);
                    _dragMoved = true;
                }
                else if (Mode == RoomPreviewMode.Plan)
                {
                    _panX += dx;
                    _panY += dy;
                }
                else
                {
                    _yaw += dx / Math.Max(Width, 1) * Math.PI * 1.6;
                    _pitch = Math.Clamp(_pitch - dy / Math.Max(Height, 1) * 1.7, 0.12, 1.35);
                }
                _lastX = x;
                _lastY = y;
                Invalidate();
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                Parent?.RequestDisallowInterceptTouchEvent(false);
                if (_draggedDeviceId is ObjectId movedDeviceId && _dragMoved)
                    _deviceMoved(movedDeviceId);
                _draggedDeviceId = null;
                PerformClick();
                return true;
            default:
                return true;
        }
    }

    public override bool PerformClick()
    {
        base.PerformClick();
        return true;
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (Width <= 0 || Height <= 0) return;
        if (Mode == RoomPreviewMode.Plan) DrawPlan(canvas);
        else DrawRoom3D(canvas);
    }

    private void DrawPlan(Canvas canvas)
    {
        SpaceVolume space = _document.Space;
        float margin = Dp(38);
        float availableWidth = Math.Max(Width - margin * 2, Dp(40));
        float availableHeight = Math.Max(Height - margin * 2, Dp(40));
        float scale = (float)Math.Min(availableWidth / space.WidthMillimetres, availableHeight / space.DepthMillimetres) * _zoom;
        float left = Width / 2f - (float)space.WidthMillimetres * scale / 2 + _panX;
        float top = Height / 2f - (float)space.DepthMillimetres * scale / 2 + _panY;
        float right = left + (float)space.WidthMillimetres * scale;
        float bottom = top + (float)space.DepthMillimetres * scale;
        float wall = Math.Max((float)space.WallThicknessMillimetres * scale, Dp(4));

        FillRect(canvas, left - wall, top - wall, right + wall, bottom + wall, "#263b47");
        FillRect(canvas, left, top, right, bottom, "#0d202a");
        DrawPlanGrid(canvas, left, top, right, bottom, scale);
        StrokeRect(canvas, left, top, right, bottom, "#72dfff", Dp(1.4f));

        foreach (BuildingOpening opening in _document.Openings.Values.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            DrawPlanOpening(canvas, opening, space, left, top, scale, wall);
        foreach (Device device in _document.Devices.Values.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            DrawPlanDevice(canvas, device, space, left, top, scale);

        DrawText(canvas, $"{space.WidthMillimetres / 1000:0.###} m", (left + right) / 2, top - Dp(13), "#dff7ff", Dp(12), Paint.Align.Center!);
        DrawText(canvas, $"{space.DepthMillimetres / 1000:0.###} m", left - Dp(15), (top + bottom) / 2, "#dff7ff", Dp(12), Paint.Align.Center!, -90);
        DrawText(canvas, "N", right + Dp(18), top + Dp(8), "#f2c94c", Dp(13), Paint.Align.Center!);
        DrawLine(canvas, right + Dp(18), top + Dp(14), right + Dp(18), top + Dp(31), "#f2c94c", Dp(2));
        DrawLine(canvas, right + Dp(18), top + Dp(14), right + Dp(13), top + Dp(21), "#f2c94c", Dp(2));
        DrawLine(canvas, right + Dp(18), top + Dp(14), right + Dp(23), top + Dp(21), "#f2c94c", Dp(2));
    }

    private void DrawPlanGrid(Canvas canvas, float left, float top, float right, float bottom, float scale)
    {
        float step = 1000 * scale;
        if (step < Dp(16)) return;
        for (float x = left; x <= right; x += step) DrawLine(canvas, x, top, x, bottom, "#173542", Dp(0.7f));
        for (float y = top; y <= bottom; y += step) DrawLine(canvas, left, y, right, y, "#173542", Dp(0.7f));
    }

    private void DrawPlanOpening(Canvas canvas, BuildingOpening opening, SpaceVolume space, float left, float top, float scale, float wall)
    {
        bool horizontal = BuildingOpeningGeometry.UsesXAxis(opening.Surface);
        float centre = horizontal
            ? left + (float)(opening.Centre.X - space.Origin.X) * scale
            : top + (float)(opening.Centre.Y - space.Origin.Y) * scale;
        float half = (float)opening.WidthMillimetres * scale / 2;
        string colour = OpeningColour(opening.Kind);
        float x1;
        float y1;
        float x2;
        float y2;
        if (opening.Surface is MountingSurface.NorthWallInterior or MountingSurface.NorthWallExterior)
        {
            x1 = centre - half; x2 = centre + half; y1 = top - wall; y2 = top + Dp(2);
        }
        else if (opening.Surface is MountingSurface.SouthWallInterior or MountingSurface.SouthWallExterior)
        {
            x1 = centre - half; x2 = centre + half; y1 = top + (float)space.DepthMillimetres * scale - Dp(2); y2 = y1 + wall;
        }
        else if (opening.Surface is MountingSurface.WestWallInterior or MountingSurface.WestWallExterior)
        {
            x1 = left - wall; x2 = left + Dp(2); y1 = centre - half; y2 = centre + half;
        }
        else
        {
            x1 = left + (float)space.WidthMillimetres * scale - Dp(2); x2 = x1 + wall; y1 = centre - half; y2 = centre + half;
        }
        FillRect(canvas, x1, y1, x2, y2, colour);
        StrokeRect(canvas, x1, y1, x2, y2, "#07131a", Dp(1));
        float labelX = (x1 + x2) / 2;
        float labelY = (y1 + y2) / 2 - Dp(7);
        DrawText(canvas, opening.Label.ToUpperInvariant(), labelX, labelY, colour, Dp(9), Paint.Align.Center!);
    }

    private void DrawPlanDevice(Canvas canvas, Device device, SpaceVolume space, float left, float top, float scale)
    {
        float x = left + (float)(device.Position.X - space.Origin.X) * scale;
        float y = top + (float)(device.Position.Y - space.Origin.Y) * scale;
        bool selected = SelectedDeviceId == device.Id;
        if (selected)
        {
            DrawCircle(canvas, x, y, Dp(19), "#f2c94c", PaintStyle.Stroke!, Dp(3));
            DrawLine(canvas, x - Dp(26), y, x + Dp(26), y, "#f2c94c", Dp(1));
            DrawLine(canvas, x, y - Dp(26), x, y + Dp(26), "#f2c94c", Dp(1));
        }
        DrawCircle(canvas, x, y, Dp(selected ? 12 : 10), DeviceColour(device.Kind), PaintStyle.Fill!, 0);
        DrawCircle(canvas, x, y, Dp(selected ? 12 : 10), "#07131a", PaintStyle.Stroke!, Dp(1.5f));
        DrawText(canvas, DeviceGlyph(device.Kind), x, y + Dp(3.5f), "#07131a", Dp(8), Paint.Align.Center!);
        DrawText(canvas, device.Label.ToUpperInvariant(), x, y - Dp(17), selected ? "#f2c94c" : "#dff7ff", Dp(8.5f), Paint.Align.Center!);
    }

    private ObjectId? HitPlanDevice(float screenX, float screenY)
    {
        PlanTransform transform = PlanCoordinates();
        return _document.Devices.Values
            .Select(device => new
            {
                device.Id,
                Distance = Math.Sqrt(
                    Math.Pow(screenX - (transform.Left + (device.Position.X - _document.Space.Origin.X) * transform.Scale), 2) +
                    Math.Pow(screenY - (transform.Top + (device.Position.Y - _document.Space.Origin.Y) * transform.Scale), 2)),
            })
            .Where(hit => hit.Distance <= Dp(30))
            .OrderBy(hit => hit.Distance)
            .Select(hit => (ObjectId?)hit.Id)
            .FirstOrDefault();
    }

    private Point3 PlanScreenToDocument(float screenX, float screenY, double elevation)
    {
        PlanTransform transform = PlanCoordinates();
        return new Point3(
            _document.Space.Origin.X + (screenX - transform.Left) / transform.Scale,
            _document.Space.Origin.Y + (screenY - transform.Top) / transform.Scale,
            elevation);
    }

    private PlanTransform PlanCoordinates()
    {
        SpaceVolume space = _document.Space;
        float margin = Dp(38);
        float availableWidth = Math.Max(Width - margin * 2, Dp(40));
        float availableHeight = Math.Max(Height - margin * 2, Dp(40));
        float scale = (float)Math.Min(availableWidth / space.WidthMillimetres, availableHeight / space.DepthMillimetres) * _zoom;
        return new PlanTransform(
            Width / 2f - (float)space.WidthMillimetres * scale / 2 + _panX,
            Height / 2f - (float)space.DepthMillimetres * scale / 2 + _panY,
            scale);
    }

    private void DrawRoom3D(Canvas canvas)
    {
        SpaceVolume space = _document.Space;
        Vec3[] corners = RoomCorners(space);
        RawPoint[] rawCorners = corners.Select(ProjectRaw).ToArray();
        double minX = rawCorners.Min(point => point.X);
        double maxX = rawCorners.Max(point => point.X);
        double minY = rawCorners.Min(point => point.Y);
        double maxY = rawCorners.Max(point => point.Y);
        double rangeX = Math.Max(maxX - minX, 1);
        double rangeY = Math.Max(maxY - minY, 1);
        float scale = (float)(Math.Min((Width - Dp(42)) / rangeX, (Height - Dp(62)) / rangeY) * 0.88 * _zoom);
        double centreX = (minX + maxX) / 2;
        double centreY = (minY + maxY) / 2;
        ScreenPoint ToScreen(Vec3 point)
        {
            RawPoint raw = ProjectRaw(point);
            return new ScreenPoint(Width / 2f + (float)(raw.X - centreX) * scale, Height / 2f + (float)(raw.Y - centreY) * scale, raw.Depth);
        }

        var faces = new[]
        {
            new Face([corners[0], corners[1], corners[2], corners[3]], "#16313d"),
            new Face([corners[0], corners[1], corners[5], corners[4]], "#294754"),
            new Face([corners[1], corners[2], corners[6], corners[5]], "#203d49"),
            new Face([corners[2], corners[3], corners[7], corners[6]], "#294754"),
            new Face([corners[3], corners[0], corners[4], corners[7]], "#203d49"),
        };
        foreach (Face face in faces.OrderBy(face => face.Points.Average(point => ProjectRaw(point).Depth)))
            DrawPolygon(canvas, face.Points.Select(ToScreen).ToArray(), face.Colour, "#436a7c", alpha: 150);

        int[][] edges =
        [
            [0, 1], [1, 2], [2, 3], [3, 0],
            [4, 5], [5, 6], [6, 7], [7, 4],
            [0, 4], [1, 5], [2, 6], [3, 7],
        ];
        foreach (int[] edge in edges)
        {
            ScreenPoint start = ToScreen(corners[edge[0]]);
            ScreenPoint end = ToScreen(corners[edge[1]]);
            DrawLine(canvas, start.X, start.Y, end.X, end.Y, "#72dfff", Dp(1.3f));
        }

        foreach (BuildingOpening opening in _document.Openings.Values.OrderBy(opening => ProjectRaw(LocalPoint(opening.Centre, space)).Depth))
        {
            ScreenPoint[] points = BuildingOpeningGeometry.Corners(opening)
                .Select(point => ToScreen(LocalPoint(point, space)))
                .ToArray();
            string colour = OpeningColour(opening.Kind);
            DrawPolygon(canvas, points, colour, "#07131a", alpha: 220);
            float labelX = points.Average(point => point.X);
            float labelY = points.Average(point => point.Y);
            DrawText(canvas, opening.Label.ToUpperInvariant(), labelX, labelY - Dp(5), "#ffffff", Dp(9), Paint.Align.Center!);
        }

        foreach (Device device in _document.Devices.Values.OrderBy(device => ProjectRaw(LocalPoint(new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres), space)).Depth))
        {
            ScreenPoint point = ToScreen(LocalPoint(new Point3(device.Position.X, device.Position.Y, device.ElevationMillimetres), space));
            bool selected = SelectedDeviceId == device.Id;
            if (selected) DrawCircle(canvas, point.X, point.Y, Dp(17), "#f2c94c", PaintStyle.Stroke!, Dp(3));
            DrawCircle(canvas, point.X, point.Y, Dp(selected ? 11 : 9), DeviceColour(device.Kind), PaintStyle.Fill!, 0);
            DrawCircle(canvas, point.X, point.Y, Dp(selected ? 11 : 9), "#07131a", PaintStyle.Stroke!, Dp(1.5f));
            DrawText(canvas, DeviceGlyph(device.Kind), point.X, point.Y + Dp(3), "#07131a", Dp(7), Paint.Align.Center!);
            DrawText(canvas, device.Label.ToUpperInvariant(), point.X, point.Y - Dp(15), selected ? "#f2c94c" : "#dff7ff", Dp(8), Paint.Align.Center!);
        }

        DrawText(canvas, $"{space.WidthMillimetres / 1000:0.###} × {space.DepthMillimetres / 1000:0.###} × {space.HeightMillimetres / 1000:0.###} m", Dp(12), Height - Dp(13), "#dff7ff", Dp(11), Paint.Align.Left!);
    }

    private Vec3[] RoomCorners(SpaceVolume space) =>
    [
        new(0, 0, 0),
        new(space.WidthMillimetres, 0, 0),
        new(space.WidthMillimetres, space.DepthMillimetres, 0),
        new(0, space.DepthMillimetres, 0),
        new(0, 0, space.HeightMillimetres),
        new(space.WidthMillimetres, 0, space.HeightMillimetres),
        new(space.WidthMillimetres, space.DepthMillimetres, space.HeightMillimetres),
        new(0, space.DepthMillimetres, space.HeightMillimetres),
    ];

    private RawPoint ProjectRaw(Vec3 point)
    {
        SpaceVolume space = _document.Space;
        double x = point.X - space.WidthMillimetres / 2;
        double y = point.Y - space.DepthMillimetres / 2;
        double z = point.Z - space.HeightMillimetres / 2;
        double cosine = Math.Cos(_yaw);
        double sine = Math.Sin(_yaw);
        double horizontal = cosine * x - sine * y;
        double depth = sine * x + cosine * y;
        double vertical = -(z * Math.Cos(_pitch) - depth * Math.Sin(_pitch));
        double cameraDepth = depth * Math.Cos(_pitch) + z * Math.Sin(_pitch);
        return new RawPoint(horizontal, vertical, cameraDepth);
    }

    private static Vec3 LocalPoint(Point3 point, SpaceVolume space) =>
        new(point.X - space.Origin.X, point.Y - space.Origin.Y, point.Z);

    private void DrawPolygon(Canvas canvas, ScreenPoint[] points, string fill, string stroke, int alpha)
    {
        if (points.Length == 0) return;
        using var path = new GraphicsPath();
        path.MoveTo(points[0].X, points[0].Y);
        foreach (ScreenPoint point in points.Skip(1)) path.LineTo(point.X, point.Y);
        path.Close();
        _paint.SetStyle(PaintStyle.Fill);
        _paint.Color = Color.ParseColor(fill);
        _paint.Alpha = alpha;
        canvas.DrawPath(path, _paint);
        _paint.SetStyle(PaintStyle.Stroke);
        _paint.Color = Color.ParseColor(stroke);
        _paint.Alpha = 255;
        _paint.StrokeWidth = Dp(1.1f);
        canvas.DrawPath(path, _paint);
    }

    private void FillRect(Canvas canvas, float left, float top, float right, float bottom, string colour)
    {
        _paint.SetStyle(PaintStyle.Fill);
        _paint.Color = Color.ParseColor(colour);
        _paint.Alpha = 255;
        canvas.DrawRect(left, top, right, bottom, _paint);
    }

    private void StrokeRect(Canvas canvas, float left, float top, float right, float bottom, string colour, float width)
    {
        _paint.SetStyle(PaintStyle.Stroke);
        _paint.Color = Color.ParseColor(colour);
        _paint.Alpha = 255;
        _paint.StrokeWidth = width;
        canvas.DrawRect(left, top, right, bottom, _paint);
    }

    private void DrawLine(Canvas canvas, float x1, float y1, float x2, float y2, string colour, float width)
    {
        _paint.SetStyle(PaintStyle.Stroke);
        _paint.Color = Color.ParseColor(colour);
        _paint.Alpha = 255;
        _paint.StrokeWidth = width;
        canvas.DrawLine(x1, y1, x2, y2, _paint);
    }

    private void DrawCircle(Canvas canvas, float x, float y, float radius, string colour, PaintStyle style, float width)
    {
        _paint.SetStyle(style);
        _paint.Color = Color.ParseColor(colour);
        _paint.Alpha = 255;
        _paint.StrokeWidth = width;
        canvas.DrawCircle(x, y, radius, _paint);
    }

    private void DrawText(Canvas canvas, string text, float x, float y, string colour, float size, Paint.Align align, float rotation = 0)
    {
        _paint.SetStyle(PaintStyle.Fill);
        _paint.Color = Color.ParseColor(colour);
        _paint.Alpha = 255;
        _paint.TextSize = size;
        _paint.TextAlign = align;
        _paint.SetTypeface(Typeface.Monospace);
        if (rotation != 0)
        {
            canvas.Save();
            canvas.Rotate(rotation, x, y);
            canvas.DrawText(text, x, y, _paint);
            canvas.Restore();
        }
        else canvas.DrawText(text, x, y, _paint);
    }

    private float Dp(float value) => value * Resources!.DisplayMetrics!.Density;

    private static string OpeningColour(OpeningKind kind) => kind switch
    {
        OpeningKind.Door => "#f2c94c",
        OpeningKind.GarageDoor => "#ff9f43",
        OpeningKind.Window => "#55d7ff",
        _ => "#c792ea",
    };

    private static string DeviceColour(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => "#ff9f43",
        DeviceKind.JunctionBox => "#f2c94c",
        DeviceKind.Outlet => "#66e6a5",
        DeviceKind.Light => "#fff176",
        DeviceKind.Camera => "#c792ea",
        DeviceKind.PoeSwitch => "#55d7ff",
        DeviceKind.AccessPoint => "#72dfff",
        DeviceKind.PatchPanel => "#8fb7c9",
        _ => "#dff7ff",
    };

    private static string DeviceGlyph(DeviceKind kind) => kind switch
    {
        DeviceKind.DistributionBoard => "DB",
        DeviceKind.JunctionBox => "JB",
        DeviceKind.Outlet => "O",
        DeviceKind.Light => "L",
        DeviceKind.Camera => "C",
        DeviceKind.PoeSwitch => "SW",
        DeviceKind.AccessPoint => "AP",
        DeviceKind.PatchPanel => "PP",
        _ => "+",
    };

    private sealed class ScaleListener(RoomPreviewView owner) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScale(ScaleGestureDetector detector)
        {
            owner._zoom = Math.Clamp(owner._zoom * detector.ScaleFactor, 0.6f, 5f);
            owner.Invalidate();
            return true;
        }
    }

    private readonly record struct Vec3(double X, double Y, double Z);
    private readonly record struct RawPoint(double X, double Y, double Depth);
    private readonly record struct ScreenPoint(float X, float Y, double Depth);
    private readonly record struct PlanTransform(float Left, float Top, float Scale);
    private sealed record Face(Vec3[] Points, string Colour);
}
