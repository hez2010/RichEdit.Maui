using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private (string? Font, double Size, double Scale, double Ascent, double Space)? _reservationMetrics;
    private readonly Windows.UI.ViewManagement.UISettings _reservationSettings = new();

    internal bool CanUseTextReservation(RichTextAdornment item)
    {
        if (VirtualView.SourceSession is null || PlatformView.TextWrapping != TextWrapping.NoWrap ||
            item.Options.Placement != RichTextAdornmentPlacement.Inline || item.Options.Baseline is not null) return false;
        var font = ResolveFontFamily();
        var size = ResolveFontSize() ?? PlatformView.Document.GetDefaultCharacterFormat().Size;
        var scale = _reservationSettings.TextScaleFactor;
        if (_reservationMetrics is not { } cached || cached.Font != font || cached.Size != size || cached.Scale != scale)
        {
            var measure = new TextBlock { Text = new string(' ', 64) + "|", FontFamily = new FontFamily(font ?? "Consolas"), FontSize = size * 4 / 3 };
            measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var width = measure.DesiredSize.Width;
            var ascent = Math.Ceiling(measure.BaselineOffset);
            measure.Text = "|";
            measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            _reservationMetrics = (font, size, scale, ascent, (width - measure.DesiredSize.Width) / 64);
        }
        var metrics = _reservationMetrics.Value;
        if (item.MeasuredSize.Height > metrics.Ascent) return false;
        // An inkless native glyph suffices when content fits above the existing
        // baseline. Tall widgets, explicit baselines and wrapped text retain
        // native attachments. This never removes requested reservations.
        item.NativeReservationSpace = metrics.Space;
        return true;
    }

    internal static RichTextCharacterFormat TextReservationFormat(RichTextAdornment item, double width) =>
        RichTextCharacterFormat.Default with
        { CharacterSpacing = Math.Round((Math.Min(item.MeasuredSize.Width, width) - item.NativeReservationSpace) * 15) / 20 };

    internal void InvalidateNativeReservationPolicy()
    {
        _reservationMetrics = null;
        _displayAdornmentVersion = -1;
        QueueAdornmentLayout();
    }

    private static bool IsTextReservation(RichTextDocumentSnapshot snapshot, RichTextImage image) =>
        image.Adornment is not null && snapshot.TextBuffer[image.Position] == '\u00A0';
}
