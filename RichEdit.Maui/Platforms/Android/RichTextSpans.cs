using Android.Text;
using Android.Text.Style;
using Android.Graphics.Drawables;

namespace RichEdit.Maui.Platforms.Android;

internal sealed class RichCharacterMetadataSpan(RichTextCharacterFormat format) : MetricAffectingSpan
{
    public RichTextCharacterFormat Format { get; } = format;

    public override void UpdateDrawState(TextPaint? textPaint)
    {
        if (textPaint is null)
        {
            return;
        }

        ApplyMetricState(textPaint);

        if (Format.Hidden)
        {
            textPaint.Color = global::Android.Graphics.Color.Transparent;
        }

        if (Format.Shadow)
        {
            textPaint.SetShadowLayer(1f, 1f, 1f, textPaint.Color);
        }

        if (Format.Outline)
        {
            textPaint.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
            textPaint.StrokeWidth = Math.Max(textPaint.TextSize / 16f, 1f);
        }
    }

    public override void UpdateMeasureState(TextPaint? textPaint) => ApplyMetricState(textPaint);

    private void ApplyMetricState(TextPaint? textPaint)
    {
        if (textPaint is not null && Format.SmallCaps)
        {
            textPaint.FontFeatureSettings = "'smcp' 1";
        }
    }
}

internal sealed class RichParagraphMetadataSpan(RichTextParagraphFormat format) :
    Java.Lang.Object,
    IParagraphStyle
{
    public RichTextParagraphFormat Format { get; } = format;
}

internal sealed class RichImageMetadataSpan(RichTextImage image) : CharacterStyle
{
    public RichTextImage Image { get; } = image;

    public override void UpdateDrawState(TextPaint? textPaint)
    {
    }
}

internal sealed class RichLetterSpacingSpan(float em) : MetricAffectingSpan
{
    public float Em { get; } = em;

    public override void UpdateDrawState(TextPaint? textPaint) => Apply(textPaint);

    public override void UpdateMeasureState(TextPaint? textPaint) => Apply(textPaint);

    private void Apply(TextPaint? textPaint)
    {
        if (textPaint is not null)
        {
            textPaint.LetterSpacing = Em;
        }
    }
}

internal sealed class RichBaselineOffsetSpan(int pixels) : MetricAffectingSpan
{
    public int Pixels { get; } = pixels;

    public override void UpdateDrawState(TextPaint? textPaint) => Apply(textPaint);

    public override void UpdateMeasureState(TextPaint? textPaint) => Apply(textPaint);

    private void Apply(TextPaint? textPaint)
    {
        if (textPaint is not null)
        {
            textPaint.BaselineShift = (int)Math.Clamp(
                (long)textPaint.BaselineShift + Pixels,
                int.MinValue,
                int.MaxValue);
        }
    }
}

internal sealed class RichLineHeightSpan(
    int paragraphStart,
    int paragraphEnd,
    RichTextLineSpacingRule rule,
    double value,
    int? minimumHeight,
    int? maximumHeight,
    int spaceBefore,
    int spaceAfter) :
    Java.Lang.Object,
    ILineHeightSpan
{
    public int ParagraphStart { get; } = paragraphStart;

    public int ParagraphEnd { get; } = paragraphEnd;

    public RichTextLineSpacingRule Rule { get; } = rule;

    public double Value { get; } = value;

    public int? MinimumHeight { get; } = minimumHeight;

    public int? MaximumHeight { get; } = maximumHeight;

    public int SpaceBefore { get; } = spaceBefore;

    public int SpaceAfter { get; } = spaceAfter;

    public static bool IsNeeded(RichTextParagraphFormat format) =>
        (format.LineSpacingRule is
            RichTextLineSpacingRule.OneAndHalf or RichTextLineSpacingRule.Double) ||
        (format.LineSpacingRule is
            RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly) &&
        format.LineSpacing > 0 ||
        format.LineSpacingRule == RichTextLineSpacingRule.Multiple &&
        format.LineSpacing > 0 && format.LineSpacing != 1d ||
        format.MinimumLineHeight is > 0 ||
        format.MaximumLineHeight is > 0 ||
        format.SpaceBefore > 0 ||
        format.SpaceAfter > 0;

    public void ChooseHeight(
        Java.Lang.ICharSequence? text,
        int start,
        int end,
        int spanStartVertical,
        int lineHeight,
        global::Android.Graphics.Paint.FontMetricsInt? fontMetrics)
    {
        if (fontMetrics is null)
        {
            return;
        }

        var naturalHeight = fontMetrics.Descent - fontMetrics.Ascent;
        if (naturalHeight <= 0)
        {
            return;
        }

        var targetHeight = Rule switch
        {
            RichTextLineSpacingRule.OneAndHalf => naturalHeight * 1.5d,
            RichTextLineSpacingRule.Double => naturalHeight * 2d,
            RichTextLineSpacingRule.Multiple when Value > 0 => naturalHeight * Value,
            RichTextLineSpacingRule.AtLeast when Value > 0 => Math.Max(naturalHeight, Value),
            RichTextLineSpacingRule.Exactly when Value > 0 => Value,
            _ => naturalHeight,
        };
        if (MinimumHeight is { } minimum)
        {
            targetHeight = Math.Max(targetHeight, minimum);
        }

        if (MaximumHeight is { } maximum)
        {
            targetHeight = Math.Min(targetHeight, maximum);
        }

        var pixels = Math.Max(ToSaturatedInt32(targetHeight), 1);
        if (pixels != naturalHeight)
        {
            // Match Android's LineHeightSpan.Standard baseline-preserving scaling,
            // while remaining available on the API 26 minimum supported here.
            var descent = ToSaturatedInt32(
                fontMetrics.Descent * (double)pixels / naturalHeight);
            fontMetrics.Descent = descent;
            fontMetrics.Ascent = SaturatingSubtract(descent, pixels);
            fontMetrics.Bottom = fontMetrics.Descent;
            fontMetrics.Top = fontMetrics.Ascent;
        }

        if (start <= ParagraphStart && SpaceBefore > 0)
        {
            fontMetrics.Ascent = SaturatingSubtract(fontMetrics.Ascent, SpaceBefore);
            fontMetrics.Top = fontMetrics.Ascent;
        }

        if (end >= ParagraphEnd && SpaceAfter > 0)
        {
            fontMetrics.Descent = (int)Math.Clamp(
                (long)fontMetrics.Descent + SpaceAfter,
                int.MinValue,
                int.MaxValue);
            fontMetrics.Bottom = fontMetrics.Descent;
        }
    }

    private static int ToSaturatedInt32(double value) => value switch
    {
        >= int.MaxValue => int.MaxValue,
        <= int.MinValue => int.MinValue,
        _ => (int)Math.Round(value),
    };

    private static int SaturatingSubtract(int left, int right) =>
        (int)Math.Clamp((long)left - right, int.MinValue, int.MaxValue);
}

internal sealed class RichParagraphDecorationSpan(
    int paragraphStart,
    int paragraphEnd,
    global::Android.Graphics.Color? backgroundColor,
    RichTextBorderSides borderSides,
    RichTextBorderStyle borderStyle,
    float borderWidth,
    global::Android.Graphics.Color borderColor) :
    Java.Lang.Object,
    ILineBackgroundSpan
{
    private readonly global::Android.Graphics.DashPathEffect? _pathEffect = borderStyle switch
    {
        RichTextBorderStyle.Dotted => new([borderWidth, borderWidth * 2], 0),
        RichTextBorderStyle.Dashed => new([borderWidth * 4, borderWidth * 2], 0),
        _ => null,
    };

    public void DrawBackground(
        global::Android.Graphics.Canvas? canvas,
        global::Android.Graphics.Paint? paint,
        int left,
        int right,
        int top,
        int baseline,
        int bottom,
        Java.Lang.ICharSequence? text,
        int start,
        int end,
        int lineNumber)
    {
        if (canvas is null || paint is null)
        {
            return;
        }

        var previousColor = paint.Color;
        var previousStyle = paint.GetStyle();
        var previousStrokeWidth = paint.StrokeWidth;
        var previousPathEffect = paint.PathEffect;
        var previousStrokeCap = paint.StrokeCap;
        try
        {
            if (backgroundColor is { } background)
            {
                paint.Color = background;
                paint.SetStyle(global::Android.Graphics.Paint.Style.Fill);
                paint.SetPathEffect(null);
                canvas.DrawRect(left, top, right, bottom, paint);
            }

            if (borderSides == RichTextBorderSides.None ||
                borderStyle == RichTextBorderStyle.None)
            {
                return;
            }

            paint.Color = borderColor;
            paint.SetStyle(global::Android.Graphics.Paint.Style.Stroke);
            paint.StrokeCap = borderStyle == RichTextBorderStyle.Dotted
                ? global::Android.Graphics.Paint.Cap.Round
                : global::Android.Graphics.Paint.Cap.Butt;
            paint.SetPathEffect(_pathEffect);
            paint.StrokeWidth = borderWidth;

            if ((borderSides & RichTextBorderSides.Left) != 0)
            {
                DrawVerticalBorder(canvas, paint, left, top, bottom, inward: 1);
            }

            if ((borderSides & RichTextBorderSides.Right) != 0)
            {
                DrawVerticalBorder(canvas, paint, right, top, bottom, inward: -1);
            }

            if (start <= paragraphStart && (borderSides & RichTextBorderSides.Top) != 0)
            {
                DrawHorizontalBorder(canvas, paint, top, left, right, inward: 1);
            }

            if (end >= paragraphEnd && (borderSides & RichTextBorderSides.Bottom) != 0)
            {
                DrawHorizontalBorder(canvas, paint, bottom, left, right, inward: -1);
            }
        }
        finally
        {
            paint.Color = previousColor;
            paint.SetStyle(previousStyle);
            paint.StrokeWidth = previousStrokeWidth;
            paint.SetPathEffect(previousPathEffect);
            paint.StrokeCap = previousStrokeCap;
        }
    }

    private void DrawVerticalBorder(
        global::Android.Graphics.Canvas canvas,
        global::Android.Graphics.Paint paint,
        float edge,
        float top,
        float bottom,
        int inward)
    {
        if (borderStyle != RichTextBorderStyle.Double || borderWidth < 3)
        {
            var x = edge + inward * borderWidth / 2;
            canvas.DrawLine(x, top, x, bottom, paint);
            return;
        }

        var strokeWidth = Math.Max(borderWidth / 3, 1);
        paint.StrokeWidth = strokeWidth;
        paint.SetPathEffect(null);
        var first = strokeWidth / 2;
        var second = borderWidth - first;
        canvas.DrawLine(
            edge + inward * first,
            top,
            edge + inward * first,
            bottom,
            paint);
        canvas.DrawLine(
            edge + inward * second,
            top,
            edge + inward * second,
            bottom,
            paint);
    }

    private void DrawHorizontalBorder(
        global::Android.Graphics.Canvas canvas,
        global::Android.Graphics.Paint paint,
        float edge,
        float left,
        float right,
        int inward)
    {
        if (borderStyle != RichTextBorderStyle.Double || borderWidth < 3)
        {
            var y = edge + inward * borderWidth / 2;
            canvas.DrawLine(left, y, right, y, paint);
            return;
        }

        var strokeWidth = Math.Max(borderWidth / 3, 1);
        paint.StrokeWidth = strokeWidth;
        paint.SetPathEffect(null);
        var first = strokeWidth / 2;
        var second = borderWidth - first;
        canvas.DrawLine(left, edge + inward * first, right, edge + inward * first, paint);
        canvas.DrawLine(left, edge + inward * second, right, edge + inward * second, paint);
    }
}

internal sealed class RichListMarkerSpan(
    RichTextListFormat listFormat,
    string marker,
    Drawable? picture,
    int markerWidth,
    int gapWidth,
    int levelIndent) :
    Java.Lang.Object,
    ILeadingMarginSpan
{
    public RichTextListFormat ListFormat { get; } = listFormat;

    public string Marker { get; } = marker;

    public int GetLeadingMargin(bool first) => (int)Math.Min(
        (long)levelIndent + markerWidth + gapWidth,
        int.MaxValue);

    public void DrawLeadingMargin(
        global::Android.Graphics.Canvas? canvas,
        global::Android.Graphics.Paint? paint,
        int x,
        int direction,
        int top,
        int baseline,
        int bottom,
        Java.Lang.ICharSequence? text,
        int start,
        int end,
        bool first,
        global::Android.Text.Layout? layout)
    {
        if (!first || canvas is null || paint is null)
        {
            return;
        }

        var previousAlignment = paint.TextAlign;
        try
        {
            paint.TextAlign = direction < 0
                ? global::Android.Graphics.Paint.Align.Right
                : global::Android.Graphics.Paint.Align.Left;
            if (picture is null)
            {
                canvas.DrawText(Marker, x + direction * levelIndent, baseline, paint);
            }
            else
            {
                var saveCount = canvas.Save();
                try
                {
                    var left = direction < 0
                        ? x - levelIndent - picture.Bounds.Width()
                        : x + levelIndent;
                    canvas.Translate(left, baseline - picture.Bounds.Height());
                    picture.Draw(canvas);
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                }
            }
        }
        finally
        {
            paint.TextAlign = previousAlignment;
        }
    }
}
