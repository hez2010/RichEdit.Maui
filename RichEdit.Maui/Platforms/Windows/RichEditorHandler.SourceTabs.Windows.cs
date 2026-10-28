using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplySourceTabInterval()
    {
        // Use the same font-size quantization as the native character format.
        var format = PlatformView.Document.GetDefaultCharacterFormat().GetClone();
        format.Size = (float)(ResolveFontSize() ?? format.Size);
        var size = (double)format.Size;
        // Keep fractional advances: even averaging pixel-rounded measurements
        // can put a tab stop just beyond the corresponding text column.
        // A trailing sentinel prevents whitespace-only width trimming.
        var measure = new TextBlock
        {
            Text = new string(' ', 64) + "|",
            FontFamily = new FontFamily(ResolveFontFamily() ?? "Consolas"),
            FontSize = size * 4 / 3,
            UseLayoutRounding = false,
        };
        measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var withSpace = measure.DesiredSize.Width;
        measure.Text = "|";
        measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var advance = (withSpace - measure.DesiredSize.Width) / 64;
        // TOM takes points but stores tab stops in twips (1/20 point). Round
        // down explicitly so a tab at an exact column boundary advances to the
        // next stop instead of consuming the rounding gap at the current stop.
        var intervalInTwips = Math.Max(1, Math.Floor(advance * 15 * VirtualView.SourceTabSize));
        PlatformView.Document.DefaultTabStop = (float)(intervalInTwips / 20);
    }
}
