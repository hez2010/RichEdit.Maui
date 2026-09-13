namespace RichEdit.Maui;

/// <summary>
/// Reads and writes the RTF 1.9.1 controls represented by <see cref="RichTextDocument"/>.
/// Unknown controls and ignorable destinations follow the RTF reader rules.
/// </summary>
internal static partial class RtfCodec
{
    private const int MaximumListOverrideId = 2000;
    private const double DefaultRtfFontSize = 12d;
    private const int DefaultCodePage = 65001;
    private const int AnsiCodePage = 1252;
    private const double TwipsPerPoint = 20d;
    private const int SingleLineSpacingTwips = 240;

    private enum ListNumberFormat
    {
        Arabic = 0,
        UpperRoman = 1,
        LowerRoman = 2,
        UpperLetter = 3,
        LowerLetter = 4,
    }

    public static string Serialize(RichTextDocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Writer(document).Write();
    }

    public static string SerializeForNativeProjection(RichTextDocumentSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new Writer(document, includeSemanticRanges: false).Write();
    }

    public static string SerializeForNativeProjection(
        RichTextDocumentSnapshot document,
        RichTextCharacterFormat nativeDefaultCharacterFormat)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(nativeDefaultCharacterFormat);
        return new Writer(
            document,
            includeSemanticRanges: false,
            nativeDefaultCharacterFormat).Write();
    }

    public static RichTextDocumentSnapshot Parse(string rtf)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        try
        {
            return new Reader(rtf).Read();
        }
        catch (ArgumentException exception)
        {
            // Model validation failures triggered by extreme or contradictory RTF
            // values surface as the documented invalid-input exception type.
            throw new FormatException(
                "The RTF document contains a value outside the supported range.",
                exception);
        }
    }

    private readonly record struct RtfColor(byte Red, byte Green, byte Blue);
}
