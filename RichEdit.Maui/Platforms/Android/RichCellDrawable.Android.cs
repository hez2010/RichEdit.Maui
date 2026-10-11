using Android.Graphics;
using Android.Graphics.Drawables;
using RichEdit.Core;
using NativeColor = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;

namespace RichEdit.Maui;

// Native fill plus a narrow border extension: GradientDrawable cannot assign
// different pen styles to individual sides. Text, measurement and input remain
// in the native cell controls.
internal sealed class RichCellDrawable(float density) : GradientDrawable
{
    private RichCellBorders _borders = new();
    private int _foreground;
    private Paint? _pen;
    private readonly Dictionary<(RichBorderStyle Style, float Width), DashPathEffect> _effects = [];

    internal void Configure(RichCellFormat format, int? selection, int foreground, int gridBorder)
    {
        if (_borders != format.Borders)
        {
            _pen?.SetPathEffect(null);
            foreach (var effect in _effects.Values) effect.Dispose();
            _effects.Clear();
        }
        _borders = format.Borders;
        _foreground = foreground;
        var fill = NativeColor.Transparent;
        if (format.Shading is { } shading)
        {
            var back = Native(shading.Background, NativeColor.Transparent);
            var front = Native(shading.Foreground, new(foreground));
            var amount = shading.Coverage / 10000d;
            var alpha = back.A * (1 - amount) + front.A * amount;
            fill = NativeColor.Argb((int)Math.Round(alpha), Blend(back.R, front.R), Blend(back.G, front.G), Blend(back.B, front.B));
            int Blend(byte from, byte to) => alpha == 0 ? 0 : (int)Math.Round((from * back.A * (1 - amount) + to * front.A * amount) / alpha);
        }
        SetColor(selection is { } selected ? new NativeColor(selected) : fill);
        SetStroke(_borders == new RichCellBorders() ? Math.Max(1, (int)Math.Ceiling(density * 0.5)) : 0, new NativeColor(gridBorder));
        InvalidateSelf();
    }

    public override void Draw(Canvas? canvas)
    {
        if (canvas is null) return;
        base.Draw(canvas);
        var bounds = Bounds;
        Edge(_borders.Left, bounds.Left, bounds.Top, bounds.Left, bounds.Bottom, 1, 0);
        Edge(_borders.Top, bounds.Left, bounds.Top, bounds.Right, bounds.Top, 0, 1);
        Edge(_borders.Right, bounds.Right, bounds.Top, bounds.Right, bounds.Bottom, -1, 0);
        Edge(_borders.Bottom, bounds.Left, bounds.Bottom, bounds.Right, bounds.Bottom, 0, -1);

        void Edge(RichBorderLine? border, float x1, float y1, float x2, float y2, float inwardX, float inwardY)
        {
            if (border is null) return;
            _pen ??= new(PaintFlags.AntiAlias);
            var width = border.Width.Twips == 0 ? 1 : (float)(border.Width.Points * density);
            _pen.StrokeWidth = width;
            _pen.Color = Native(border.Color, new(_foreground));
            _pen.StrokeCap = Paint.Cap.Butt;
            if (border.Style is RichBorderStyle.Dotted or RichBorderStyle.Dashed)
            {
                if (!_effects.TryGetValue((border.Style, width), out var effect))
                {
                    var dash = border.Style == RichBorderStyle.Dotted ? width : width * 3;
                    effect = new([dash, width * 2], 0);
                    _effects.Add((border.Style, width), effect);
                }
                _pen.SetPathEffect(effect);
            }
            else _pen.SetPathEffect(null);
            Line(width / 2);
            if (border.Style == RichBorderStyle.Double) Line(width * 2.5f);
            void Line(float inset) => canvas.DrawLine(x1 + inwardX * inset, y1 + inwardY * inset, x2 + inwardX * inset, y2 + inwardY * inset, _pen);
        }
    }

    private static NativeColor Native(RichColor? value, NativeColor fallback) => value is { } color ? new(color.Red, color.Green, color.Blue) : fallback;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pen?.Dispose(); _pen = null;
            foreach (var effect in _effects.Values) effect.Dispose();
            _effects.Clear();
        }
        base.Dispose(disposing);
    }
}
