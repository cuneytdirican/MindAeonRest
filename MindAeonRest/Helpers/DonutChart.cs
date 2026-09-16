using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MindAeonRest.Helpers;

/// <summary>Basit donut/pasta grafiği (dashboard için).</summary>
public static class DonutChart
{
    public static FrameworkElement Build(IEnumerable<(double Value, string Color)> slices, double size = 190, double holeRatio = 0.58, Brush? holeBrush = null)
    {
        var data = slices.Where(s => s.Value > 0).ToList();
        var canvas = new Canvas { Width = size, Height = size };
        double total = data.Sum(s => s.Value);
        double cx = size / 2, cy = size / 2, r = size / 2;

        if (total <= 0)
        {
            canvas.Children.Add(new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xEE)) });
        }
        else if (data.Count == 1)
        {
            canvas.Children.Add(new Ellipse { Width = size, Height = size, Fill = ToBrush(data[0].Color) });
        }
        else
        {
            double angle = -Math.PI / 2;
            foreach (var s in data)
            {
                double sweep = s.Value / total * 2 * Math.PI;
                double a1 = angle + sweep;
                var p0 = new Point(cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
                var p1 = new Point(cx + r * Math.Cos(a1), cy + r * Math.Sin(a1));
                var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
                fig.Segments.Add(new LineSegment(p0, true));
                fig.Segments.Add(new ArcSegment(p1, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, true));
                canvas.Children.Add(new Path { Fill = ToBrush(s.Color), Data = new PathGeometry(new[] { fig }) });
                angle = a1;
            }
        }

        double hole = size * holeRatio;
        var center = new Ellipse { Width = hole, Height = hole, Fill = holeBrush ?? Brushes.White };
        Canvas.SetLeft(center, (size - hole) / 2);
        Canvas.SetTop(center, (size - hole) / 2);
        canvas.Children.Add(center);
        return canvas;
    }

    private static Brush ToBrush(string hex)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { return Brushes.Gray; }
    }
}
