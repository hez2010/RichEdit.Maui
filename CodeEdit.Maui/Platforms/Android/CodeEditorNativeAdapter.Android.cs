using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichEditText PlatformView = null!;
    internal partial Color? GetTextColor() => new global::Android.Graphics.Color(PlatformView.CurrentTextColor).ToColor();
    internal partial bool Post(Action action) => PlatformView.Post(action);
    private partial void Connect() => PlatformView = _handler.PlatformView;
    private partial void Disconnect() { }
    internal partial void UpdateConfiguration() => PlatformView.SetHorizontallyScrolling(!Owner.WordWrap);
}
