using Microsoft.Maui.Graphics;

namespace RichEdit.Maui.TestApp;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> LayoutCases()
    {
#if ANDROID
        yield return new("layout Android fill width retains native layout across edits", async editor =>
        {
            var alignment = editor.HorizontalOptions;
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            try
            {
                editor.Document = RichTextDocument.FromPlainText("short\nnext\nlast");
                editor.HorizontalOptions = LayoutOptions.Fill;
                await Task.Delay(100);
                var width = global::Android.Views.View.MeasureSpec.MakeMeasureSpec(native.Width, global::Android.Views.MeasureSpecMode.AtMost);
                var height = global::Android.Views.View.MeasureSpec.MakeMeasureSpec(native.Height, global::Android.Views.MeasureSpecMode.Exactly);
                native.ForceLayout();
                native.Measure(width, height);
                Equal(native.Width, native.MeasuredWidth, "fill consumes the bounded viewport");
                var layout = native.Layout!;
                editor.SelectedRange = new(0, 0);
                NativeReplace(editor, "x");
                native.ForceLayout();
                native.Measure(width, height);
                Equal(true, layout.Equals(native.Layout), "unchanged width keeps the incremental native layout");
                editor.HorizontalOptions = LayoutOptions.Start;
                native.ForceLayout();
                native.Measure(width, height);
                Equal(true, native.MeasuredWidth < global::Android.Views.View.MeasureSpec.GetSize(width), "start alignment keeps intrinsic measurement");
            }
            finally { editor.HorizontalOptions = alignment; native.RequestLayout(); }
        });
#endif

        yield return new("layout captures visible source and round trips caret points", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("A😀 B\nnext\n");
            editor.SelectionState = new(3, 1);
            await Task.Delay(120);
            var source = editor.Document.CurrentSnapshot;
            var selection = editor.SelectionState;
            var layout = editor.TextLayout.Capture() ?? throw new InvalidOperationException("Missing native layout.");
            Equal(source.Revision, layout.DocumentRevision);
            Equal(3, layout.Lines.Count, "visible logical lines");
            foreach (var line in layout.Lines)
            {
                Equal(true, line.Bounds.Height > 0, "line height");
                Equal(true, line.Baseline >= line.Bounds.Top && line.Baseline <= line.Bounds.Bottom + 1, $"baseline {line.Baseline} in {line.Bounds}");
            }
            foreach (var position in new[] { 0, 1, 3, 4, 6, source.Length })
            {
                var caret = editor.TextLayout.GetCaretBounds(layout, position) ?? throw new InvalidOperationException($"Missing caret {position}.");
                var hit = editor.TextLayout.HitTest(layout, new(caret.X + Math.Min(0.25, caret.Width / 2), caret.Y + caret.Height / 2));
                Equal(position, hit?.Position, $"caret round trip {position}: {caret}");
            }
            Equal(true, editor.TextLayout.GetRangeBounds(layout, new(0, 8)).Count >= 2, "multiline rectangles");
            Equal(true, ReferenceEquals(source, editor.Document.CurrentSnapshot), "geometry preserves source");
            Equal(selection, editor.SelectionState, "geometry preserves selection");
            Equal(false, editor.CanUndo);
        });

        yield return new("layout rejects stale captures and hides folded source", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\none\ntwo\ntail");
            await Task.Delay(100);
            var before = editor.TextLayout.Capture()!;
            editor.Folding.Collapse(new(5, 8));
            Equal(null, editor.TextLayout.GetCaretBounds(before, 0), "fold invalidates capture");
            await Task.Delay(100);
            var folded = editor.TextLayout.Capture()!;
            Equal(null, editor.TextLayout.GetCaretBounds(folded, 7));
            Equal(0, editor.TextLayout.GetRangeBounds(folded, new(5, 8)).Count);
            Equal(true, folded.Lines.SelectMany(line => line.SourceRanges).All(range => range.End <= 5 || range.Start >= 13), "hidden source intervals");
            editor.Document.Edit(edit => edit.InsertText(0, "x"));
            Equal(null, editor.TextLayout.HitTest(folded, new(20, 20)), "edit invalidates capture");
            await Task.Delay(100);
            var current = editor.TextLayout.Capture()!;
            editor.Document = RichTextDocument.FromPlainText("same length text");
            Equal(null, editor.TextLayout.GetCaretBounds(current, 0), "replacement invalidates capture");
            await Task.Delay(100);
            var replacement = editor.TextLayout.Capture()!;
            editor.Adornments.MarginWidth = 36;
            await Task.Delay(100);
            Equal(null, editor.TextLayout.GetCaretBounds(replacement, 0), "margin invalidates capture");
            var margin = editor.TextLayout.Capture()!;
            Equal(true, margin.TextViewport.Left >= 36, "text viewport excludes margin");
        });

        yield return new("layout returns separate bidirectional selection fragments", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc אבג def");
            await Task.Delay(100);
            var layout = editor.TextLayout.Capture()!;
            var rectangles = editor.TextLayout.GetRangeBounds(layout, new(1, 5));
            Equal(true, rectangles.Count >= 2, $"bidi fragments: {string.Join("; ", rectangles)}");
            var source = editor.Document.CurrentSnapshot;
            editor.Document = new RichTextDocument();
            await Task.Delay(100);
            var empty = editor.TextLayout.Capture()!;
            Equal(1, empty.Lines.Count, "empty caret line");
            Equal(new RichTextRange(0, 0), empty.Lines[0].SourceRanges.Single());
            Equal(true, editor.TextLayout.GetCaretBounds(empty, 0) is not null);
        });

#if ANDROID || IOS || MACCATALYST
        yield return new("composition defers native decorations and widget layout and rejects stale painting", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("ab");
            editor.SelectedRange = new(1, 0);
            editor.Focus();
            using var layer = editor.Decorations.CreateLayer();
            layer.TrySet(editor.Document.Revision, [new(new(0, 1), new() { ForegroundColor = Colors.Blue })]);
            await Task.Delay(100);
            var before = CharacterFormattingTests.Capture(editor, 0);
#if ANDROID
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            using var info = new global::Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var first = new Java.Lang.String("仮名");
            connection.SetComposingText(first, 1);
#else
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
#endif
            var revision = editor.Document.Revision;
            var version = editor.Decorations.Version;
            Equal(true, layer.TrySet(revision, [new(new(0, 1), new() { ForegroundColor = Colors.Red })]));
            Equal(true, editor.Decorations.Version > version, "managed presentation is published during composition");
            using var item = editor.Adornments.Add(editor.Document.Length, new Label { Text = "label", WidthRequest = 40, HeightRequest = 20 },
                new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(100);
            Equal(before, CharacterFormattingTests.Capture(editor, 0), "native paint waits for composition");
            Equal(editor.Document.Text, NativeText(editor), "native reservations wait for composition");
#if ANDROID
            using var second = new Java.Lang.String("仮名文");
            connection.SetComposingText(second, 1);
            connection.FinishComposingText();
#else
            native.SetMarkedText("仮名文", new Foundation.NSRange(3, 0));
            native.UnmarkText();
#endif
            await Task.Delay(150);
            Equal(false, editor.Composition.IsActive);
            Equal("a仮名文b", editor.Document.Text);
            Equal(before, CharacterFormattingTests.Capture(editor, 0), "stale pending paint is discarded");
            Equal(editor.Document.Text + "\uFFFC", NativeText(editor));
            Equal(true, layer.TrySet(editor.Document.Revision, [new(new(0, 1), new() { ForegroundColor = Colors.Red })]));
            Equal(false, Equals(before, CharacterFormattingTests.Capture(editor, 0)), "fresh paint applies after composition");
            item.Dispose();
            await Task.Delay(100);
        });

        yield return new("composition is observable before source changes and ends without a text delta", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("ab");
            editor.SelectedRange = new(1, 0);
            editor.Focus();
            await Task.Delay(100);
            var sawActiveTextChange = false;
            var transitions = new List<RichTextCompositionState>();
            void OnText(object? sender, RichTextTextChangedEventArgs args) => sawActiveTextChange |= editor.Composition.IsActive;
            void OnComposition(object? sender, RichTextCompositionChangedEventArgs args) => transitions.Add(args.Current);
            editor.TextChanged += OnText;
            editor.CompositionChanged += OnComposition;
            try
            {
#if ANDROID
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
                using var info = new global::Android.Views.InputMethods.EditorInfo();
                using var connection = native.OnCreateInputConnection(info)!;
                using var text = new Java.Lang.String("仮名");
                connection.SetComposingText(text, 1);
#else
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
                native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
#endif
                Equal(true, sawActiveTextChange, "text observers see active composition");
                Equal(true, editor.Composition.IsActive);
                Equal(new RichTextRange(1, 2), editor.Composition.Range);
                var snapshot = editor.Document.CurrentSnapshot;
                var selection = editor.SelectionState;
                Equal(false, editor.TryApplyEdits(snapshot.Revision, (RichTextEdit[])[new(new(0, 0), "wrong")]));
                Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));
                Equal(selection, editor.SelectionState);
#if ANDROID
                connection.FinishComposingText();
#else
                native.UnmarkText();
#endif
                Equal(false, editor.Composition.IsActive);
                Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot), "ending composition needs no source delta");
                Equal("a仮名b", editor.Document.Text);
                Equal(true, transitions.First().IsActive);
                Equal(false, transitions.Last().IsActive);
            }
            finally
            {
                editor.TextChanged -= OnText;
                editor.CompositionChanged -= OnComposition;
            }
        });
#endif
    }
}
