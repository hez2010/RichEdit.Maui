using Android.Text;
using Android.Text.Style;

namespace RichEdit.Maui.Platforms.Android;

internal sealed class RichCharacterMetadataSpan(RichTextCharacterFormat format) : CharacterStyle
{
    public RichTextCharacterFormat Format { get; } = format;

    public override void UpdateDrawState(TextPaint? textPaint)
    {
        if (textPaint is null)
        {
            return;
        }

        if (Format.Hidden)
        {
            textPaint.Color = global::Android.Graphics.Color.Transparent;
        }

        if (Format.Shadow)
        {
            textPaint.SetShadowLayer(1f, 1f, 1f, textPaint.Color);
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
