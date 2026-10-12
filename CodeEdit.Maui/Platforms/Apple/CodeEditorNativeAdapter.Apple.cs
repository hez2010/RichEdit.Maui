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

    internal partial Task<bool> PrepareViewportAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PlatformView.LayoutManager.EnsureLayoutForTextContainer(PlatformView.TextContainer);
        PlatformView.LayoutIfNeeded();
        return Task.FromResult(PlatformView.Window is not null);
    }

    internal partial bool Post(Action action)
    {
        PlatformView.BeginInvokeOnMainThread(action);
        return true;
    }

    private partial void Connect()
    {
        PlatformView = _handler.PlatformView;
        _defaultTextColor = PlatformView.TextColor ?? UIColor.Label;
        PlatformView.NativeSizeChanged += UpdateTextContainer;
        PlatformView.AllowsEditingTextAttributes = false;
        PlatformView.AutocapitalizationType = UITextAutocapitalizationType.None;
        PlatformView.SmartQuotesType = UITextSmartQuotesType.No;
        PlatformView.SmartDashesType = UITextSmartDashesType.No;
        PlatformView.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
    }

    private readonly List<UILabel> _lineLabels = [];

    private partial void Disconnect()
    {
        PlatformView.NativeSizeChanged -= UpdateTextContainer;
        foreach (var label in _lineLabels) { label.RemoveFromSuperview(); label.Dispose(); }
        _lineLabels.Clear();
    }

    partial void UpdateNativeLineLabels(AbsoluteLayout gutter, VisibleCodeLine[] lines, Color color, ref bool handled)
    {
        handled = true;
        if (gutter.Handler?.PlatformView is not UIView container) return;
        // The gutter has a fixed viewport. Moving its native labels must not
        // invalidate MAUI's measure of the sibling UITextView on every scroll.
        while (_lineLabels.Count < lines.Length)
        {
            var label = new UILabel { TextAlignment = UITextAlignment.Right, UserInteractionEnabled = false, Lines = 1 };
            _lineLabels.Add(label);
            container.AddSubview(label);
        }
        while (_lineLabels.Count > lines.Length)
        {
            var label = _lineLabels[^1];
            label.RemoveFromSuperview(); label.Dispose(); _lineLabels.RemoveAt(_lineLabels.Count - 1);
        }
        using var font = UIFont.FromName(Owner.FontFamily, (nfloat)Owner.FontSize) ?? UIFont.SystemFontOfSize((nfloat)Owner.FontSize);
        using var bold = UIFont.FromDescriptor(font.FontDescriptor.CreateWithTraits(UIFontDescriptorSymbolicTraits.Bold), font.PointSize);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var label = _lineLabels[index];
            label.Text = line.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            label.Font = line.Number == Owner.CaretPosition.Line + 1 ? bold : font;
            label.TextColor = color.ToPlatform();
            label.Frame = new CGRect(0, line.Top, Math.Max(0, gutter.WidthRequest - 8), line.Height);
        }
    }

    private partial void UpdateConfigurationCore() => UpdateTextContainer();

    private void UpdateTextContainer()
    {
        var container = PlatformView.TextContainer;
        if (container.WidthTracksTextView != Owner.WordWrap)
            container.WidthTracksTextView = Owner.WordWrap;

        var inset = PlatformView.TextContainerInset;
        var size = new CGSize(
            Owner.WordWrap ? Math.Max(0, PlatformView.Bounds.Width - inset.Left - inset.Right) : nfloat.MaxValue, nfloat.MaxValue);
        if (container.Size != size)
            container.Size = size;
    }

}
#endif
