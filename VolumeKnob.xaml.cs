using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MicVolumeFixer;

public partial class VolumeKnob : UserControl
{
    // Arc geometry constants
    private const double StartAngle = 135;  // 0% position (bottom-left)
    private const double SweepRange = 270;  // total arc degrees
    private const double Radius = 80;       // arc radius
    private const double CenterX = 100;
    private const double CenterY = 100;

    private bool _isDragging;

    public VolumeKnob()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    // ── Dependency Properties ───────────────────────────────────────────

    public static readonly DependencyProperty TargetVolumeProperty =
        DependencyProperty.Register(nameof(TargetVolume), typeof(int), typeof(VolumeKnob),
            new PropertyMetadata(90, OnVolumeChanged));

    public static readonly DependencyProperty CurrentVolumeProperty =
        DependencyProperty.Register(nameof(CurrentVolume), typeof(int), typeof(VolumeKnob),
            new PropertyMetadata(0, OnVolumeChanged));

    public int TargetVolume
    {
        get => (int)GetValue(TargetVolumeProperty);
        set => SetValue(TargetVolumeProperty, value);
    }

    public int CurrentVolume
    {
        get => (int)GetValue(CurrentVolumeProperty);
        set => SetValue(CurrentVolumeProperty, value);
    }

    private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((VolumeKnob)d).Redraw();
    }

    // ── Event ───────────────────────────────────────────────────────────

    public event EventHandler? TargetVolumeChanged;

    // ── Drawing ─────────────────────────────────────────────────────────

    private void Redraw()
    {
        if (!IsLoaded) return;

        // Background track: full 270° arc
        trackPath.Data = CreateArcGeometry(0, 100);

        // Current volume arc
        if (CurrentVolume > 0)
        {
            volumePath.Data = CreateArcGeometry(0, CurrentVolume);
            volumePath.Visibility = Visibility.Visible;

            // Update gradient brush direction to follow the arc's bounding box
            var bounds = volumePath.Data.Bounds;
            if (bounds.Width > 0)
            {
                volumeBrush.StartPoint = new Point(0, 0.5);
                volumeBrush.EndPoint = new Point(1, 0.5);
            }
        }
        else
        {
            volumePath.Visibility = Visibility.Collapsed;
        }

        // Target indicator dot
        var dotPos = PointOnCircle(AngleFromValue(TargetVolume), Radius);
        Canvas.SetLeft(targetDot, dotPos.X - targetDot.Width / 2);
        Canvas.SetTop(targetDot, dotPos.Y - targetDot.Height / 2);

        // Labels
        lblTarget.Text = $"{TargetVolume} %";
        lblCurrent.Text = CurrentVolume >= 0
            ? $"Current: {CurrentVolume}%"
            : "Current: –";
    }

    private static Geometry CreateArcGeometry(int fromValue, int toValue)
    {
        if (toValue <= fromValue) return Geometry.Empty;

        double startDeg = AngleFromValue(fromValue);
        double endDeg = AngleFromValue(toValue);
        double sweepDeg = endDeg - startDeg;

        var start = PointOnCircle(startDeg, Radius);
        var end = PointOnCircle(endDeg, Radius);
        bool isLargeArc = sweepDeg > 180;

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    // ── Angle / Point Helpers ───────────────────────────────────────────

    private static double AngleFromValue(int value)
    {
        return StartAngle + Math.Clamp(value, 0, 100) / 100.0 * SweepRange;
    }

    private static Point PointOnCircle(double angleDeg, double radius)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(
            CenterX + radius * Math.Cos(rad),
            CenterY + radius * Math.Sin(rad));
    }

    private static int ValueFromAngle(double angleDeg)
    {
        // Normalize angle to 0-360
        angleDeg = ((angleDeg % 360) + 360) % 360;

        // The dead zone is from 45° (100%) clockwise through 135° (0%)
        // That's the 90° gap at the bottom-right to bottom-left

        // Map angle to value
        double relative;
        if (angleDeg >= StartAngle) // 135° to 360°
            relative = angleDeg - StartAngle;
        else if (angleDeg <= StartAngle - 90) // 0° to 45°
            relative = angleDeg + 360 - StartAngle;
        else
            return -1; // In the dead zone

        int value = (int)Math.Round(relative / SweepRange * 100);
        return Math.Clamp(value, 0, 100);
    }

    // ── Mouse Interaction ───────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _isDragging = true;
        CaptureMouse();
        UpdateTargetFromMouse(e.GetPosition(canvas));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        UpdateTargetFromMouse(e.GetPosition(canvas));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        TargetVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        int delta = e.Delta > 0 ? 1 : -1;
        int newVal = Math.Clamp(TargetVolume + delta, 0, 100);
        if (newVal != TargetVolume)
        {
            TargetVolume = newVal;
            TargetVolumeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateTargetFromMouse(Point mousePos)
    {
        double dx = mousePos.X - CenterX;
        double dy = mousePos.Y - CenterY;
        double angleDeg = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        int val = ValueFromAngle(angleDeg);
        if (val >= 0 && val != TargetVolume)
        {
            TargetVolume = val;
            TargetVolumeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
