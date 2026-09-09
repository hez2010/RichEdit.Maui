using Microsoft.Maui.Graphics;
#if WINDOWS
using Microsoft.UI.Xaml;
#endif

namespace RichEdit.Maui.TestApp;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> AdornmentCases()
    {
        yield return new("adornments preserve content selection save state and redo", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\nbody\ntail");
            editor.Document.Edit(edit => edit.InsertText(editor.Document.Length, "!"));
            editor.Undo();
            editor.Document.MarkSaved();
            editor.SelectionState = new(8, 5);
            await Verify(editor);
            var snapshot = editor.Document.CurrentSnapshot;
            var rtf = editor.Document.RtfText;
            var selection = editor.SelectionState;
            var invoked = 0;
            var button = AdornmentButton(() => invoked++);
            editor.Adornments.MarginWidth = 32;
            var item = editor.Adornments.Add(5, button, RichTextAdornmentPlacement.LeftMargin);
            await Task.Delay(150);
            Equal(true, button.Handler is not null, "native adornment handler");
            Equal(true, button.Bounds.Width > 0 && button.Bounds.X >= 0, $"visible adornment: {button.Bounds}");
            Equal(selection, editor.SelectionState, "selection after adding adornment");
            InvokeAdornment(button);
            await Task.Delay(50);
            Equal(1, invoked, "native button invocation");
            Equal(selection, editor.SelectionState, "selection after invoking adornment");
            item.Dispose();
            Equal(null, button.Parent);
            // A removed application-owned view can be attached again.
            using var replacement = editor.Adornments.Add(10, button, RichTextAdornmentPlacement.LeftMargin);
            await Task.Delay(100);
            InvokeAdornment(button);
            await Task.Delay(50);
            Equal(2, invoked, "reattached native button invocation");
            Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));
            Equal(rtf, editor.Document.RtfText);
            Equal(false, editor.Document.IsModified);
            Equal(true, editor.CanRedo);
            editor.Redo();
            await Verify(editor);
            Equal("head\nbody\ntail!", editor.Document.Text);
        });

        yield return new("adornments track native source edits and clear with document replacement", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\nbody\ntail");
            editor.Adornments.MarginWidth = 32;
            var button = AdornmentButton(() => { });
            var item = editor.Adornments.Add(5, button, RichTextAdornmentPlacement.LeftMargin);
            editor.SelectedRange = new(0, 0);
            NativeReplace(editor, "prefix\n");
            await Verify(editor);
            Equal(12, item.Position);
            editor.SelectedRange = new(12, 0);
            NativeReplace(editor, "new ");
            await Verify(editor);
            Equal(16, item.Position);
            editor.SelectedRange = new(16, 4);
            NativeReplace(editor, string.Empty);
            await Verify(editor);
            Equal(0, editor.Adornments.Count);
            Equal(null, button.Parent);
            editor.Undo();
            await Verify(editor);
            Equal(0, editor.Adornments.Count, "undo does not recreate discarded views");
            editor.Adornments.Add(editor.Document.Length, AdornmentButton(() => { }));
            editor.Document = RichTextDocument.FromPlainText("replacement");
            await Verify(editor);
            Equal(0, editor.Adornments.Count);
        });

        yield return new("adornments align with folding and native buttons explicitly expand", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\none\ntwo\ntail");
            editor.Adornments.MarginWidth = 32;
            var range = new RichTextRange(5, 8);
            var button = AdornmentButton(() => editor.Folding.Expand(range));
            using var item = editor.Adornments.Add(13, button, RichTextAdornmentPlacement.LeftMargin);
            await Task.Delay(150);
            var before = NativeAdornmentBounds(editor, button);
            editor.Folding.Collapse(range);
            await Task.Delay(150);
            var folded = NativeAdornmentBounds(editor, button);
            Equal(true, folded.Y < before.Y - 10, $"folded adornment moved: {before} -> {folded}");
            Equal(true, Math.Abs(folded.Center.Y - button.Bounds.Center.Y) < 2, "native and managed adornment alignment");
            var snapshot = editor.Document.CurrentSnapshot;
            InvokeAdornment(button);
            await Task.Delay(150);
            Equal(0, editor.Folding.CollapsedRanges.Count);
            Equal(true, Math.Abs(before.Y - NativeAdornmentBounds(editor, button).Y) < 2, "expanded adornment alignment");
            Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));
            Equal(false, editor.CanUndo);
        });

        yield return new("adornments follow scrolling and hide offscreen and collapsed anchors", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText(string.Join('\n', Enumerable.Range(0, 150).Select(index => $"Line {index}")));
            editor.Adornments.MarginWidth = 32;
            var position = editor.Document.Text.IndexOf("Line 90\n", StringComparison.Ordinal);
            var button = AdornmentButton(() => { });
            using var item = editor.Adornments.Add(position, button, RichTextAdornmentPlacement.LeftMargin);
            editor.SelectedRange = new(0, 0);
            editor.ScrollIntoView(new(0, 0));
            await Task.Delay(150);
            Equal(true, button.Bounds.X < 0, "offscreen adornment excluded from hit testing");
            ScrollAdornmentToPosition(editor, position);
            await Task.Delay(150);
            Equal(true, button.Bounds.X >= 0, $"scrolled adornment visible: {button.Bounds}; {AdornmentViewportDescription(editor, position)}");
            var native = NativeAdornmentBounds(editor, button);
            Equal(true, Math.Abs(native.Y - button.Bounds.Y) < 2, $"scroll native alignment: {native}, {button.Bounds}");
            var inline = AdornmentButton(() => { });
            var transient = editor.Adornments.Add(position, inline, offset: new Point(6, 0));
            await Task.Delay(100);
            Equal(true, inline.Bounds.X > button.Bounds.X && button.Bounds.X >= 0, "adding a text overlay preserves the scrolled viewport");
            transient.Dispose();
            await Task.Delay(100);
            Equal(true, button.Bounds.X >= 0, "removing a text overlay preserves the scrolled viewport");
            editor.Folding.Collapse(new(position, 8));
            await Task.Delay(150);
            Equal(true, button.Bounds.X < 0, "anchor inside collapsed text is hidden");
            editor.Folding.ExpandAll();
            await Task.Delay(150);
            ScrollAdornmentToPosition(editor, position);
            await Task.Delay(100);
            Equal(true, button.Bounds.X >= 0, "expanded anchor visible");
        });

        yield return new("adornments at document end remain usable when all source is folded", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("one\ntwo\nthree");
            var range = new RichTextRange(0, editor.Document.Length);
            editor.Adornments.MarginWidth = 32;
            var button = AdornmentButton(() => editor.Folding.Expand(range));
            using var item = editor.Adornments.Add(range.End, button, RichTextAdornmentPlacement.LeftMargin);
            editor.Folding.Collapse(range);
            await Task.Delay(150);
            Equal(true, button.Bounds.X >= 0 && button.Bounds.Width > 0, $"fully folded indicator: {button.Bounds}");
            InvokeAdornment(button);
            await Task.Delay(100);
            Equal(0, editor.Folding.CollapsedRanges.Count);
            Equal("one\ntwo\nthree", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });
    }

    private static Button AdornmentButton(Action action) => new()
    {
        Text = "+", WidthRequest = 24, HeightRequest = 20,
        MinimumWidthRequest = 0, MinimumHeightRequest = 0, Padding = 0, Command = new Command(action),
    };

#if WINDOWS
    [WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Controls.Button))]
#endif
    private static void InvokeAdornment(Button button)
    {
#if WINDOWS
        var native = (Microsoft.UI.Xaml.Controls.Button)button.Handler!.PlatformView!;
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(native);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
#elif ANDROID
        ((global::Android.Views.View)button.Handler!.PlatformView!).PerformClick();
#elif IOS || MACCATALYST
        ((UIKit.UIButton)button.Handler!.PlatformView!).SendActionForControlEvents(UIKit.UIControlEvent.TouchUpInside);
#endif
    }

#if WINDOWS
    [WinRT.DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
#endif
    private static Rect NativeAdornmentBounds(RichEditor editor, Button button)
    {
        var handler = (RichEditorHandler)editor.Handler!;
#if WINDOWS
        var native = (Microsoft.UI.Xaml.FrameworkElement)button.Handler!.PlatformView!;
        var origin = native.TransformToVisual(handler.PlatformView).TransformPoint(new(0, 0));
        return new(origin.X, origin.Y, native.ActualWidth, native.ActualHeight);
#elif ANDROID
        var native = (global::Android.Views.View)button.Handler!.PlatformView!;
        var location = new int[2];
        var editorLocation = new int[2];
        native.GetLocationOnScreen(location);
        handler.PlatformView.GetLocationOnScreen(editorLocation);
        var density = native.Resources?.DisplayMetrics?.Density ?? 1f;
        return new((location[0] - editorLocation[0]) / density, (location[1] - editorLocation[1]) / density, native.Width / density, native.Height / density);
#else
        var native = (UIKit.UIView)button.Handler!.PlatformView!;
        var rect = native.ConvertRectToView(native.Bounds, handler.PlatformView);
        return new(rect.X - handler.PlatformView.ContentOffset.X, rect.Y - handler.PlatformView.ContentOffset.Y, rect.Width, rect.Height);
#endif
    }

    private static string AdornmentViewportDescription(RichEditor editor, int position)
    {
        var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
        return $"viewport={native.Width}x{native.Height}, scroll={native.ScrollX},{native.ScrollY}, source caret={FoldingCaretRect(editor, position)}";
#elif IOS || MACCATALYST
        return $"viewport={native.Bounds}, scroll={native.ContentOffset}, source caret={FoldingCaretRect(editor, position)}";
#else
        return $"viewport={native.ActualWidth}x{native.ActualHeight}, source caret={FoldingCaretRect(editor, position)}";
#endif
    }

    private static void ScrollAdornmentToPosition(RichEditor editor, int position)
    {
        var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
        native.ScrollTo(0, native.Layout!.GetLineTop(native.Layout.GetLineForOffset(position)));
#elif IOS || MACCATALYST
        var caret = FoldingCaretRect(editor, position);
        native.SetContentOffset(new CoreGraphics.CGPoint(0, caret.Y - native.TextContainerInset.Top), animated: false);
#else
        editor.ScrollIntoView(new(position, 0));
#endif
    }
}
