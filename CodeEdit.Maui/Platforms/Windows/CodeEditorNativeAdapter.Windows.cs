using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Maui.Platform;
using ScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using WinRT;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichEditBox PlatformView = null!;
    [DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Media.SolidColorBrush))]
    internal partial Color? GetTextColor() => (PlatformView.Foreground as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color.ToColor();
    internal partial bool Post(Action action) => PlatformView.DispatcherQueue.TryEnqueue(() => action());
    private partial void Connect() => PlatformView = _handler.PlatformView;
    private partial void Disconnect() { }
    internal partial void UpdateConfiguration()
    {
        PlatformView.TextWrapping = Owner.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(PlatformView, Owner.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }
}
