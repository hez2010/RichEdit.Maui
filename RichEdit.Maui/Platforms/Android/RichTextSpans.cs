using Android.Text;
using Android.Text.Style;

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
            textPaint.BaselineShift += Pixels;
        }
    }
}

internal sealed class RichLineHeightSpan(
    RichTextLineSpacingRule rule,
    double value,
    int? minimumHeight,
    int? maximumHeight) :
    Java.Lang.Object,
    ILineHeightSpan
{
    public RichTextLineSpacingRule Rule { get; } = rule;

    public double Value { get; } = value;

    public int? MinimumHeight { get; } = minimumHeight;

    public int? MaximumHeight { get; } = maximumHeight;

    public static bool IsNeeded(RichTextParagraphFormat format) =>
        (format.LineSpacingRule is
            RichTextLineSpacingRule.OneAndHalf or RichTextLineSpacingRule.Double) ||
        (format.LineSpacingRule is
            RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly) &&
        format.LineSpacing > 0 ||
        format.LineSpacingRule == RichTextLineSpacingRule.Multiple &&
        format.LineSpacing > 0 && format.LineSpacing != 1d ||
        format.MinimumLineHeight is > 0 ||
        format.MaximumLineHeight is > 0;

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

        var pixels = Math.Max(checked((int)Math.Round(targetHeight)), 1);
        if (pixels == naturalHeight)
        {
            return;
        }

        // Match Android's LineHeightSpan.Standard baseline-preserving scaling,
        // while remaining available on the API 26 minimum supported here.
        var descent = checked((int)Math.Round(fontMetrics.Descent * (double)pixels / naturalHeight));
        fontMetrics.Descent = descent;
        fontMetrics.Ascent = descent - pixels;
        fontMetrics.Bottom = fontMetrics.Descent;
        fontMetrics.Top = fontMetrics.Ascent;
    }
}
