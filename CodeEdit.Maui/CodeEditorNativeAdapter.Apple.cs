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
    private IDisposable? _scrollObservation;
    private IDisposable? _boundsObservation;
    private CGSize _viewportSize;
    internal partial bool IsComposing => PlatformView.MarkedTextRange is not null;
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
        _scrollObservation = PlatformView.AddObserver("contentOffset", NSKeyValueObservingOptions.New, _ => Owner.InvalidateGutter());
        _boundsObservation = PlatformView.AddObserver("bounds", NSKeyValueObservingOptions.New, _ =>
        {
            if (_viewportSize != PlatformView.Bounds.Size)
            {
                _viewportSize = PlatformView.Bounds.Size;
                // UIKit adjusts the text container during its layout pass.
                PlatformView.BeginInvokeOnMainThread(() => { if (!_disposed) UpdateConfiguration(); });
            }
            Owner.InvalidateGutter();
        });
    }

    private partial void Disconnect()
    {
        _scrollObservation?.Dispose();
        _boundsObservation?.Dispose();
        _scrollObservation = null;
        _boundsObservation = null;
    }

    internal partial void UpdateConfiguration()
    {
        PlatformView.AllowsEditingTextAttributes = false;
        PlatformView.AutocapitalizationType = UITextAutocapitalizationType.None;
        PlatformView.SmartQuotesType = UITextSmartQuotesType.No;
        PlatformView.SmartDashesType = UITextSmartDashesType.No;
        PlatformView.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
        PlatformView.TextContainer.WidthTracksTextView = Owner.WordWrap;
        var inset = PlatformView.TextContainerInset;
        PlatformView.TextContainer.Size = new CGSize(
            Owner.WordWrap ? Math.Max(0, PlatformView.Bounds.Width - inset.Left - inset.Right) : nfloat.MaxValue, nfloat.MaxValue);
    }

    internal partial void ScrollToSelection() => PlatformView.ScrollRangeToVisible(PlatformView.SelectedRange);

    internal partial IReadOnlyList<VisibleCodeLine> GetVisibleLines()
    {
        var result = new List<VisibleCodeLine>();
        if (PlatformView.Bounds.Height <= 0) return result;
        var offset = PlatformView.ContentOffset;
        using var topPosition = PlatformView.GetClosestPositionToPoint(new CGPoint(PlatformView.TextContainerInset.Left, offset.Y));
        var start = topPosition is null ? 0 : (int)PlatformView.GetOffsetFromPosition(PlatformView.BeginningOfDocument, topPosition);
        var first = Owner.Lines.GetLineIndex(Math.Clamp(start, 0, Owner.Document.Length));
        for (var index = first; index < Owner.LineCount; index++)
        {
            using var position = PlatformView.GetPosition(PlatformView.BeginningOfDocument, Owner.Lines.GetRange(index).Start);
            if (position is null) break;
            var rect = PlatformView.GetCaretRectForPosition(position);
            var top = rect.Y - offset.Y;
            if (top >= PlatformView.Bounds.Height) break;
            if (top + rect.Height > 0) result.Add(new(index + 1, (float)top, (float)rect.Height));
        }
        return result;
    }
}
#endif
