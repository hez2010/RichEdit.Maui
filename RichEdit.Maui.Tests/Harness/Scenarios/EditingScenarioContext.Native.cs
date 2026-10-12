#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using Microsoft.Maui.Platform;
using RichEdit.Core;
#if WINDOWS
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
#elif IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace RichEdit.Maui.Tests;

internal sealed partial class EditingScenarioContext
{
    internal static string NativeText(NativeEditorView surface)
    {
        var handler = (RichEditorHandler)surface.Handler!;
#if WINDOWS
        handler.PlatformView.Document.GetText(TextGetOptions.NoHidden, out var text); return text;
#else
        return handler.PlatformView.Text ?? "";
#endif
    }

    private string ReadNativeFont()
    {
        var offset = ReadNativeSelection().Range.Start;
#if WINDOWS
        var format = Handler.PlatformView.Document.GetRange(offset, offset + 1).CharacterFormat;
        return $"{format.Name} {format.Size}";
#elif IOS || MACCATALYST
        if (offset >= Handler.PlatformView.TextStorage.Length) return "typing";
        using var attributes = Handler.PlatformView.TextStorage.GetAttributes(offset, out _);
        return new UIStringAttributes(attributes).Font?.ToString() ?? "default";
#else
        var native = Handler.PlatformView;
        return $"{native.Typeface} {native.TextSize}; size={native.Width}x{native.Height}; nativeFocus={native.IsFocused}; viewFocus={Surface.IsFocused}; focusable={native.Focusable}/{native.FocusableInTouchMode}; attached={native.IsAttachedToWindow}";
#endif
    }

    internal void AssertNativeCharacterStyle(RichCharacterStyle style)
    {
        var range = ReadNativeSelection().Range;
#if WINDOWS
        var format = Handler.PlatformView.Document.GetRange(range.Start, range.End).CharacterFormat;
        if (style.Bold.Kind == StyleValueKind.Value) Equal(style.Bold.Value, format.Bold == FormatEffect.On, "native bold");
        if (style.Italic.Kind == StyleValueKind.Value) Equal(style.Italic.Value, format.Italic == FormatEffect.On, "native italic");
        if (style.FontSize.Kind == StyleValueKind.Value && style.FontSize.Value is { } size) Check(Math.Abs(format.Size - size.Points) < 0.1, "native font size");
        if (style.Foreground.Kind == StyleValueKind.Value && style.Foreground.Value is { } color)
            Equal(color, new RichColor(format.ForegroundColor.R, format.ForegroundColor.G, format.ForegroundColor.B), "native foreground");
#elif IOS || MACCATALYST
        using var dictionary = Handler.PlatformView.TextStorage.GetAttributes(range.Start, out _);
        var attributes = new UIStringAttributes(dictionary);
        var traits = attributes.Font!.FontDescriptor.SymbolicTraits;
        if (style.Bold.Kind == StyleValueKind.Value) Equal(style.Bold.Value, (traits & UIFontDescriptorSymbolicTraits.Bold) != 0, "native bold");
        if (style.Italic.Kind == StyleValueKind.Value) Equal(style.Italic.Value, (traits & UIFontDescriptorSymbolicTraits.Italic) != 0, "native italic");
        if (style.FontSize.Kind == StyleValueKind.Value && style.FontSize.Value is { } size) Check(Math.Abs(attributes.Font.PointSize - size.Points) < 0.1, "native font size");
#elif ANDROID
        var editable = Handler.PlatformView.EditableText!;
        var styles = editable.GetSpans(range.Start, range.End, Java.Lang.Class.FromType(typeof(Android.Text.Style.StyleSpan)))!.OfType<Android.Text.Style.StyleSpan>().ToArray();
        if (style.Bold.Kind == StyleValueKind.Value) Equal(style.Bold.Value, styles.Any(span => (span.Style & Android.Graphics.TypefaceStyle.Bold) != 0), "native bold span");
        if (style.Italic.Kind == StyleValueKind.Value) Equal(style.Italic.Value, styles.Any(span => (span.Style & Android.Graphics.TypefaceStyle.Italic) != 0), "native italic span");
#endif
    }

    internal void Type(string text)
    {
#if WINDOWS
        Handler.PlatformView.Document.Selection.TypeText(text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r'));
#elif IOS || MACCATALYST
        Handler.PlatformView.InsertText(text);
#else
        using var info = new Android.Views.InputMethods.EditorInfo();
        using var input = Handler.PlatformView.OnCreateInputConnection(info)!;
        if (Surface.IsReadOnly) { Check(input is null, "Read-only native editor has no input connection"); return; }
        Check(input is not null, "Editable native editor has an input connection");
        using var value = new Java.Lang.String(text);
        Check(input!.CommitText(value, 1), "Native input accepted");
#endif
    }

#if ANDROID || IOS || MACCATALYST
    internal void Compose(string marked, string committed)
    {
#if ANDROID
        using var info = new Android.Views.InputMethods.EditorInfo();
        using var input = Handler.PlatformView.OnCreateInputConnection(info)!;
        using var first = new Java.Lang.String(marked); using var final = new Java.Lang.String(committed);
        Check(input.SetComposingText(first, 1), "marked input accepted");
        Check(input.CommitText(final, 1), "composition committed"); input.FinishComposingText();
#else
        Handler.PlatformView.SetMarkedText(marked, new(marked.Length, 0));
        Handler.PlatformView.SetMarkedText(committed, new(committed.Length, 0));
        Handler.PlatformView.UnmarkText();
#endif
    }
#endif

    internal void Backspace()
    {
#if WINDOWS
        if (!Surface.SendKeyDown(EditorKey.Backspace, EditorKeyModifiers.None))
        { Handler.PrepareNativeSourceKey(EditorKey.Backspace); Handler.PlatformView.Document.Selection.Delete(TextRangeUnit.Character, -1); }
#elif IOS || MACCATALYST
        Handler.PlatformView.DeleteBackward();
#else
        using var info = new Android.Views.InputMethods.EditorInfo();
        using var input = Handler.PlatformView.OnCreateInputConnection(info)!;
        Check(input.DeleteSurroundingText(1, 0), "Native Backspace accepted");
#endif
    }

    internal TextSelectionState ReadNativeSelection()
    {
#if WINDOWS
        var selected = Handler.PlatformView.Document.Selection;
        return (selected.Options & SelectionOptions.StartActive) != 0 ? new(selected.EndPosition, selected.StartPosition) : new(selected.StartPosition, selected.EndPosition);
#elif IOS || MACCATALYST
        var selected = Handler.PlatformView.SelectedRange;
        return new((int)selected.Location, (int)(selected.Location + selected.Length));
#else
        return new(Handler.PlatformView.SelectionStart, Handler.PlatformView.SelectionEnd);
#endif
    }

    internal bool NativeFocused()
    {
#if WINDOWS
        return Handler.PlatformView.FocusState != FocusState.Unfocused;
#elif IOS || MACCATALYST
        return Handler.PlatformView.IsFirstResponder;
#else
        return Handler.PlatformView.IsFocused;
#endif
    }

    internal Point ScrollOffset()
    {
#if WINDOWS
        var scroll = Scroller(Handler.PlatformView)!;
        return new(scroll.HorizontalOffset, scroll.VerticalOffset);
#elif IOS || MACCATALYST
        var value = Handler.PlatformView.ContentOffset;
        return new(value.X, value.Y);
#else
        var view = ScrollOwner();
        var density = view.Resources!.DisplayMetrics!.Density;
        return new(view.ScrollX / density, view.ScrollY / density);
#endif
    }

    internal void PositionCaretInViewport(double y)
    {
        var selected = ReadNativeSelection();
#if WINDOWS
        var scroll = Scroller(Handler.PlatformView)!;
        Handler.PlatformView.Document.GetRange(selected.Active, selected.Active).GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
        scroll.ChangeView(null, Math.Max(0, rect.Y - y), null, true);
#elif IOS || MACCATALYST
        var native = Handler.PlatformView;
        using var point = native.GetDisplayPosition(native.BeginningOfDocument, selected.Active)!;
        var rect = native.GetDisplayCaret(point);
        native.SetContentOffset(new(native.ContentOffset.X, Math.Max(-native.AdjustedContentInset.Top, rect.Y - y)), false);
#else
        var native = Handler.PlatformView;
        if (native.Layout is not { } layout) return;
        var owner = ScrollOwner();
        var location = new int[2]; var origin = new int[2];
        native.GetLocationInWindow(location); owner.GetLocationInWindow(origin);
        var top = location[1] - origin[1] + owner.ScrollY - native.ScrollY + native.TotalPaddingTop + layout.GetLineTop(layout.GetLineForOffset(selected.Active));
        owner.ScrollTo(owner.ScrollX, Math.Max(0, top - (int)(y * native.Resources!.DisplayMetrics!.Density)));
#endif
    }

#if WINDOWS
    [WinRT.DynamicWindowsRuntimeCast(typeof(ScrollViewer))]
    private static ScrollViewer? Scroller(DependencyObject view)
    {
        if (view is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(view); i++)
            if (Scroller(VisualTreeHelper.GetChild(view, i)) is { } child) return child;
        return null;
    }
#elif ANDROID
    private Android.Views.View ScrollOwner() => Rich?.Content?.Handler?.PlatformView is RichDocumentHost host ? host : Handler.PlatformView;
#endif
}
#endif
