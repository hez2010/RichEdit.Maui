#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichTextView PlatformView = null!;
    private UIColor _defaultTextColor = null!;
    internal partial Color? GetTextColor() => _defaultTextColor.ToColor();
    internal partial bool Post(Action action)
    {
        PlatformView.BeginInvokeOnMainThread(action);
        return true;
    }

    private partial void Connect()
    {
        PlatformView = _handler.PlatformView;
        _defaultTextColor = PlatformView.TextColor ?? UIColor.Label;
        Owner.TextLayout.Changed += OnTextLayoutChanged;
    }

    private partial void Disconnect()
    {
        Owner.TextLayout.Changed -= OnTextLayoutChanged;
    }

    private void OnTextLayoutChanged(object? sender, EventArgs args) => UpdateTextContainer();

    internal partial void UpdateConfiguration()
    {
        PlatformView.AllowsEditingTextAttributes = false;
        PlatformView.AutocapitalizationType = UITextAutocapitalizationType.None;
        PlatformView.SmartQuotesType = UITextSmartQuotesType.No;
        PlatformView.SmartDashesType = UITextSmartDashesType.No;
        PlatformView.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
        UpdateTextContainer();
    }

    private void UpdateTextContainer()
    {
        var container = PlatformView.TextContainer;
        if (container.WidthTracksTextView != Owner.WordWrap) container.WidthTracksTextView = Owner.WordWrap;
        var inset = PlatformView.TextContainerInset;
        var size = new CGSize(
            Owner.WordWrap ? Math.Max(0, PlatformView.Bounds.Width - inset.Left - inset.Right) : nfloat.MaxValue, nfloat.MaxValue);
        if (container.Size != size) container.Size = size;
    }

}
#endif
