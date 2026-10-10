using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplySourceTabInterval()
    {
        var size = ResolveFontSize() ?? PlatformView.Document.GetDefaultCharacterFormat().Size;
        // Average many spaces because XAML rounds DesiredSize to device units.
        // A trailing sentinel prevents whitespace-only width trimming.
        var measure = new TextBlock { Text = new string(' ', 64) + "|", FontFamily = new FontFamily(ResolveFontFamily() ?? "Consolas"), FontSize = size * 4 / 3 };
        measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var withSpace = measure.DesiredSize.Width;
        measure.Text = "|";
        measure.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var advance = (withSpace - measure.DesiredSize.Width) / 64;
        // TOM uses typographic points; XAML measurements use 96-DPI units.
        PlatformView.Document.DefaultTabStop = (float)(advance * 0.75 * VirtualView.SourceTabSize);
    }
}
