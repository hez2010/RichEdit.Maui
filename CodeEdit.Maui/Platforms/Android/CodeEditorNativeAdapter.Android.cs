using Android.Views;
using Android.Views.InputMethods;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;

namespace CodeEdit.Maui;

internal sealed partial class CodeEditorNativeAdapter
{
    private RichEditText PlatformView = null!;
    private ViewTreeObserver? _observer;
    internal partial bool IsComposing => PlatformView.EditableText is { } text && BaseInputConnection.GetComposingSpanStart(text) >= 0;
    internal partial Color? GetTextColor() => new global::Android.Graphics.Color(PlatformView.CurrentTextColor).ToColor();
    internal partial bool Post(Action action) => PlatformView.Post(action);

    private partial void Connect()
    {
        var platformView = PlatformView = _handler.PlatformView;
        _observer = platformView.ViewTreeObserver;
        if (_observer is not null)
        {
            _observer.ScrollChanged += OnViewportChanged;
            _observer.GlobalLayout += OnViewportChanged;
        }
    }

    private partial void Disconnect()
    {
        if (_observer is { IsAlive: true })
        {
            _observer.ScrollChanged -= OnViewportChanged;
            _observer.GlobalLayout -= OnViewportChanged;
        }
        _observer = null;
    }

    internal partial void UpdateConfiguration() => PlatformView.SetHorizontallyScrolling(!Owner.WordWrap);
    internal partial void ScrollToSelection() => PlatformView.BringPointIntoView(Owner.SelectedRange.Start);

    internal partial IReadOnlyList<VisibleCodeLine> GetVisibleLines()
    {
        var result = new List<VisibleCodeLine>();
        if (PlatformView.Layout is not { } layout || PlatformView.Height <= 0) return result;
        var density = PlatformView.Resources?.DisplayMetrics?.Density ?? 1f;
        var firstVisual = layout.GetLineForVertical(PlatformView.ScrollY);
        var first = Owner.Lines.GetLineIndex(Math.Min(layout.GetLineStart(firstVisual), Owner.Document.Length));
        for (var index = first; index < Owner.LineCount; index++)
        {
            if (GetVisibleLineStart(index) is not { } start) continue;
            var visual = layout.GetLineForOffset(start);
            var top = layout.GetLineTop(visual) + PlatformView.CompoundPaddingTop - PlatformView.ScrollY;
            if (top >= PlatformView.Height) break;
            var height = layout.GetLineBottom(visual) - layout.GetLineTop(visual);
            if (top + height > 0 && (result.Count == 0 || Math.Abs(result[^1].Top - top / density) > 0.5f))
                result.Add(new(index + 1, top / density, height / density));
        }
        return result;
    }

    private void OnViewportChanged(object? sender, EventArgs args) => Owner.InvalidateGutter();
}
