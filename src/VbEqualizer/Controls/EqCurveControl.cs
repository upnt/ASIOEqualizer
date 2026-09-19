using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VbEqualizer.Audio;

namespace VbEqualizer.Controls;

/// <summary>
/// A parametric-EQ style frequency-response graph (think FabFilter Pro-Q / Peace for
/// Equalizer APO): draws the actual combined dB curve of all peaking bands on a log-frequency
/// axis, with a draggable node per band. Dragging a node updates the corresponding
/// EqualizerBand's Frequency/GainDb directly, which is what the audio engine already listens to
/// -- this control has no knowledge of the engine, it only reads/writes EqualizerBand.
/// </summary>
public sealed class EqCurveControl : FrameworkElement
{
    private const double MinFreq = 20;
    private const double MaxFreq = 20000;
    private const double MinGainDb = -12;
    private const double MaxGainDb = 12;
    private const double NodeRadius = 6;
    private const double HitRadius = 14;
    private const double EqQ = 1.0; // must match EqualizerSampleProvider.Q for the curve to be accurate
    private const double ReferenceSampleRate = 48000; // display-only; real filters use the live engine's rate

    private static readonly double[] GridFrequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private static readonly double[] GridGainsDb = { -12, -6, 0, 6, 12 };

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x26)));
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush ZeroLineBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB2)));
    private static readonly Brush CurveBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0x40)));
    private static readonly Brush CurveFillBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x9E, 0x40)));
    private static readonly Brush NodeFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0x40)));
    private static readonly Brush NodeHoverFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x8F)));
    private static readonly Brush DragLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
    private static readonly Pen NodeStrokePen = Freeze(new Pen(Brushes.White, 1.5));
    private static readonly Pen GridPen = Freeze(new Pen(GridBrush, 1));
    private static readonly Pen ZeroLinePen = Freeze(new Pen(ZeroLineBrush, 1));
    private static readonly Pen CurvePen = Freeze(new Pen(CurveBrush, 2));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly EqualizerBand[] _bands;
    private int _hoverIndex = -1;
    private int _dragIndex = -1;

    public EqCurveControl(EqualizerBand[] bands)
    {
        _bands = bands;
        foreach (var band in _bands)
        {
            band.FrequencyChanged += (_, _) => InvalidateVisual();
            band.GainChanged += (_, _) => InvalidateVisual();
        }

        Focusable = false;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

        DrawGrid(dc, w, h);
        DrawCurve(dc, w, h);
        DrawNodes(dc, w, h);
    }

    private void DrawGrid(DrawingContext dc, double w, double h)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (double freq in GridFrequencies)
        {
            double x = FreqToX(freq, w);
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            var text = MakeText(FormatFreqLabel(freq), dpi);
            dc.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, w - text.Width), h - text.Height - 2));
        }

        foreach (double gain in GridGainsDb)
        {
            double y = GainToY(gain, h);
            dc.DrawLine(gain == 0 ? ZeroLinePen : GridPen, new Point(0, y), new Point(w, y));
            var text = MakeText($"{gain:+0;-0;0}", dpi);
            dc.DrawText(text, new Point(3, Math.Clamp(y - text.Height - 1, 0, h - text.Height)));
        }
    }

    private void DrawCurve(DrawingContext dc, double w, double h)
    {
        var filters = new BiquadFilter[_bands.Length];
        for (int i = 0; i < _bands.Length; i++)
        {
            filters[i] = new BiquadFilter();
            filters[i].SetPeakingEq(ReferenceSampleRate, _bands[i].Frequency, EqQ, _bands[i].GainDb);
        }

        const int steps = 200;
        var points = new Point[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double x = w * i / steps;
            double freq = XToFreq(x, w);
            double totalDb = 0;
            foreach (var f in filters)
                totalDb += f.GetMagnitudeDb(ReferenceSampleRate, freq);
            points[i] = new Point(x, GainToY(Math.Clamp(totalDb, MinGainDb * 1.5, MaxGainDb * 1.5), h));
        }

        double zeroY = GainToY(0, h);

        var fillGeometry = new StreamGeometry();
        using (var ctx = fillGeometry.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, zeroY), true, true);
            ctx.PolyLineTo(points, true, false);
            ctx.LineTo(new Point(points[steps].X, zeroY), true, false);
        }
        fillGeometry.Freeze();
        dc.DrawGeometry(CurveFillBrush, null, fillGeometry);

        var strokeGeometry = new StreamGeometry();
        using (var ctx = strokeGeometry.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points, true, false);
        }
        strokeGeometry.Freeze();
        dc.DrawGeometry(null, CurvePen, strokeGeometry);
    }

    private void DrawNodes(DrawingContext dc, double w, double h)
    {
        for (int i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            var center = new Point(FreqToX(band.Frequency, w), GainToY(band.GainDb, h));
            var fill = i == _hoverIndex || i == _dragIndex ? NodeHoverFillBrush : NodeFillBrush;
            dc.DrawEllipse(fill, NodeStrokePen, center, NodeRadius, NodeRadius);
        }

        if (_dragIndex >= 0)
        {
            var band = _bands[_dragIndex];
            var center = new Point(FreqToX(band.Frequency, w), GainToY(band.GainDb, h));
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var text = MakeText($"{band.FrequencyText} Hz  {band.GainDb:+0.0;-0.0;0.0} dB", dpi, DragLabelBrush);
            double lx = Math.Clamp(center.X + 10, 0, w - text.Width - 2);
            double ly = Math.Clamp(center.Y - text.Height - 10, 0, h - text.Height);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), null,
                new Rect(lx - 3, ly - 1, text.Width + 6, text.Height + 2));
            dc.DrawText(text, new Point(lx, ly));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        int index = HitTestNode(e.GetPosition(this));
        if (index >= 0)
        {
            _dragIndex = index;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_dragIndex >= 0)
        {
            double freq = XToFreq(Math.Clamp(pos.X, 0, ActualWidth), ActualWidth);
            double gain = Math.Clamp(YToGain(Math.Clamp(pos.Y, 0, ActualHeight), ActualHeight), MinGainDb, MaxGainDb);
            _bands[_dragIndex].Frequency = freq;
            _bands[_dragIndex].GainDb = Math.Round(gain, 1);
        }
        else
        {
            int hover = HitTestNode(pos);
            if (hover != _hoverIndex)
            {
                _hoverIndex = hover;
                InvalidateVisual();
            }
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Arrow;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
        base.OnMouseLeave(e);
    }

    private int HitTestNode(Point pos)
    {
        double w = ActualWidth, h = ActualHeight;
        int best = -1;
        double bestDist = HitRadius;
        for (int i = 0; i < _bands.Length; i++)
        {
            var center = new Point(FreqToX(_bands[i].Frequency, w), GainToY(_bands[i].GainDb, h));
            double dist = (center - pos).Length;
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    private static FormattedText MakeText(string s, double dpi, Brush? brush = null) => new(
        s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 11, brush ?? LabelBrush, dpi);

    private static string FormatFreqLabel(double hz) => hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";

    private static double FreqToX(double freq, double w) =>
        (Math.Log10(freq) - Math.Log10(MinFreq)) / (Math.Log10(MaxFreq) - Math.Log10(MinFreq)) * w;

    private static double XToFreq(double x, double w) =>
        Math.Pow(10, Math.Log10(MinFreq) + x / w * (Math.Log10(MaxFreq) - Math.Log10(MinFreq)));

    private static double GainToY(double gainDb, double h) =>
        (MaxGainDb - gainDb) / (MaxGainDb - MinGainDb) * h;

    private static double YToGain(double y, double h) =>
        MaxGainDb - y / h * (MaxGainDb - MinGainDb);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
