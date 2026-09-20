using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

#if ANDROID
using Android.Runtime;
#endif

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
#if WINDOWS
    [WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.UIElement))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicMethods, typeof(RichEditorHandler))]
#endif
    private static IEnumerable<Case> ProjectionCases()
    {
#if ANDROID || IOS || MACCATALYST
        yield return new("projection custom keyboards retain capitalization and explicit overrides", async editor =>
        {
            editor.ClearValue(RichEditor.IsSpellCheckEnabledProperty);
            editor.ClearValue(RichEditor.IsTextPredictionEnabledProperty);
            editor.Keyboard = Keyboard.Create(KeyboardFlags.None);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
            var capitals = global::Android.Text.InputTypes.TextFlagCapSentences | global::Android.Text.InputTypes.TextFlagCapWords | global::Android.Text.InputTypes.TextFlagCapCharacters;
            Equal(0, (int)(native.InputType & capitals));
            Equal(true, (native.InputType & global::Android.Text.InputTypes.TextFlagNoSuggestions) != 0);
            editor.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);
            Equal(true, (native.InputType & global::Android.Text.InputTypes.TextFlagCapWords) != 0);
#else
            Equal(UIKit.UITextAutocapitalizationType.None, native.AutocapitalizationType);
            Equal(UIKit.UITextSpellCheckingType.No, native.SpellCheckingType);
            editor.Keyboard = Keyboard.Create(KeyboardFlags.CapitalizeWord);
            Equal(UIKit.UITextAutocapitalizationType.Words, native.AutocapitalizationType);
#endif
            editor.IsSpellCheckEnabled = true;
            editor.IsTextPredictionEnabled = true;
#if ANDROID
            Equal(0, (int)(native.InputType & global::Android.Text.InputTypes.TextFlagNoSuggestions));
            Equal(true, (native.InputType & global::Android.Text.InputTypes.TextFlagAutoCorrect) != 0);
#else
            Equal(UIKit.UITextSpellCheckingType.Yes, native.SpellCheckingType);
            Equal(UIKit.UITextAutocorrectionType.Yes, native.AutocorrectionType);
#endif
            await Task.Delay(30);
        });
#endif

#if ANDROID
        yield return new("projection Android text queries clamp large lengths without overflow", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("A😀 bc\ntail");
            using var item = editor.Adornments.Add(4, new Label { Text = "hint", WidthRequest = 30, HeightRequest = 20 },
                new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            using var info = new global::Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            var snapshot = editor.Document.CurrentSnapshot;
            connection.SetSelection(4, 4);
            Equal("bc\ntail", ReadAfter(int.MaxValue));
            Equal("b", ReadAfter(1));
            Equal("", ReadAfter(0));
            Equal(new RichTextRange(4, 0), editor.SelectedRange);
            connection.SetSelection(6, 4);
            Equal("\ntail", ReadAfter(int.MaxValue), "text follows the normalized selection end");
            Equal(new RichTextRange(4, 2), editor.SelectedRange);
            connection.SetSelection(editor.Document.Length, editor.Document.Length);
            Equal("", ReadAfter(int.MaxValue));
            Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));

            string ReadAfter(int length)
            {
                using var text = connection.GetTextAfterCursorFormatted(length, global::Android.Views.InputMethods.GetTextFlags.None);
                return text?.ToString() ?? "";
            }
        });

        yield return new("projection Android IME replacements and relative carets skip reservations", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcd");
            using var first = editor.Adornments.Add(0, new Label { Text = "first", WidthRequest = 30, HeightRequest = 20 }, new() { Placement = RichTextAdornmentPlacement.Inline });
            using var second = editor.Adornments.Add(2, new Label { Text = "second", WidthRequest = 30, HeightRequest = 20 }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            using var info = new global::Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                using var replacement = new Java.Lang.String("X");
                Equal(true, connection.ReplaceText(2, 1, replacement, 1, null));
                Equal("aXcd", editor.Document.Text);
                editor.Undo();
                await Task.Delay(150);
                Equal("abcd", editor.Document.Text);
                Equal(true, connection.ReplaceText(int.MaxValue, int.MaxValue, replacement, 1, null));
                Equal("abcdX", editor.Document.Text, "replacement endpoints clamp in source coordinates");
                editor.Undo();
                await Task.Delay(150);
            }

            connection.SetSelection(0, 0);
            using var insertion = new Java.Lang.String("!");
            Equal(true, connection.CommitText(insertion, 4));
            Equal("!abcd", editor.Document.Text);
            Equal(new RichTextRange(4, 0), editor.SelectedRange);
            await Task.Delay(150);
            using var composition = new Java.Lang.String("?");
            Equal(true, connection.SetComposingText(composition, -2));
            Equal("!abc?d", editor.Document.Text);
            Equal(new RichTextRange(2, 0), editor.SelectedRange);
            Equal(true, editor.Composition.IsActive);
            connection.FinishComposingText();
        });

        yield return new("projection Android baseline offsets raise text and images survive shifts", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a");
            editor.Document.Edit(edit => edit.SetCharacterFormat(new(0, 1), new() { BaselineOffset = 4 }));
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            using var paint = new global::Android.Text.TextPaint { TextSize = native.TextSize };
            var spans = native.EditableText!.GetSpans(0, 1, Java.Lang.Class.FromType(typeof(global::Android.Text.Style.MetricAffectingSpan)))!;
            foreach (var span in spans.OfType<global::Android.Text.Style.MetricAffectingSpan>())
                span.UpdateMeasureState(paint);
            Equal(true, paint.BaselineShift < 0, "positive authored baseline raises native text");

            editor.Document.Edit(edit => edit.InsertImage(1, Image()));
            await Task.Delay(50);
            var imageType = Java.Lang.Class.FromType(typeof(global::Android.Text.Style.ImageSpan));
            var image = native.EditableText!.GetSpans(1, 2, imageType)!.Single();
            editor.Document.Edit(edit => edit.InsertText(0, "prefix"));
            var moved = native.EditableText!.GetSpans(7, 8, imageType)!.Single();
            Equal(image.Handle, moved.Handle, "shifting text retains the decoded native image span");
        });
#elif IOS || MACCATALYST
        yield return new("projection Apple native baselines are represented only once", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a");
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            var attributes = new UIKit.UIStringAttributes { Font = UIKit.UIFont.SystemFontOfSize(14), BaselineOffset = 3 };
            native.TextStorage.SetAttributes(attributes.Dictionary, new Foundation.NSRange(0, 1));
            await Task.Delay(100);
            Equal(3d, editor.Document.CurrentSnapshot.Runs[0].Format.BaselineOffset);
            Equal(RichTextScript.Normal, editor.Document.CurrentSnapshot.Runs[0].Format.Script);
            editor.Document.Edit(edit => edit.UpdateCharacterFormat(new(0, 1), format => format with { Italic = true }));
            await Task.Delay(100);
            var observed = new UIKit.UIStringAttributes(native.TextStorage.GetAttributes(0, out _));
            Equal(3f, observed.BaselineOffset!.Value);
        });

        yield return new("projection Apple deleting a multiline document retains its paragraph format", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a\nb");
            editor.Document.Edit(edit => edit.SetParagraphFormat(new(0, 3), new() { Alignment = RichTextAlignment.Center, LeadingIndent = 24 }));
            editor.SelectedRange = new(0, 3);
            NativeReplace(editor, "");
            await Task.Delay(100);
            Equal("", editor.Document.Text);
            Equal(RichTextAlignment.Center, editor.Document.CurrentSnapshot.Paragraphs[0].Format.Alignment);
            Equal(24d, editor.Document.CurrentSnapshot.Paragraphs[0].Format.LeadingIndent);
        });
#else
        yield return new("projection Windows appearance reaches the projected tail", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcd");
            editor.Document.Edit(edit => edit.SetCharacterFormat(new(3, 1), new() { ForegroundColor = Colors.Red }));
            using var item = editor.Adornments.Add(0, new Label { Text = "hint", WidthRequest = 30, HeightRequest = 20 }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            editor.FontSize = 22;
            editor.TextColor = Colors.Blue;
            await Task.Delay(100);
            var format = ((RichEditorHandler)editor.Handler!).PlatformView.Document.GetRange(4, 5).CharacterFormat;
            Equal(22f, format.Size);
            Equal((byte)255, format.ForegroundColor.R);
            Equal((byte)0, format.ForegroundColor.B);
        });

        yield return new("projection Windows geometry excludes a long offscreen tail", async editor =>
        {
            // WinUI can return corrupt TOM geometry for a 50,000-character unwrapped
            // line even without our handler. This tail still exceeds the query budget.
            const int tailLength = 5000;
            const int queryBudget = 2000;
            var handler = (RichEditorHandler)editor.Handler!;
            var wrapping = handler.PlatformView.TextWrapping;
            var horizontalScrollBar = Microsoft.UI.Xaml.Controls.ScrollViewer.GetHorizontalScrollBarVisibility(handler.PlatformView);
            try
            {
                editor.IsSpellCheckEnabled = false;
                editor.IsTextPredictionEnabled = false;
                handler.PlatformView.TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap;
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(handler.PlatformView, Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto);
                editor.Document = RichTextDocument.FromPlainText("abc אבג " + new string('x', tailLength));
                editor.SelectedRange = new(0, 0);

                RichTextLayoutSnapshot? layout = null;
                RichTextLayoutSnapshot? lastCapture = null;
                Microsoft.UI.Xaml.Controls.ScrollViewer? scroller = null;
                Point? origin = null;
                Rect? nativePrefix = null;
                var prefixHit = 0;
                // Native scrolling can complete after UpdateLayout. Establish the viewport
                // before testing geometry, independently of the APIs under test.
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    await Task.Delay(25);
                    handler.PlatformView.UpdateLayout();
                    scroller ??= FindWindowsScroller(handler.PlatformView);
                    if (scroller is null || scroller.ViewportWidth <= 0 || scroller.ViewportHeight <= 0)
                        continue;
                    if (scroller.HorizontalOffset != 0 || scroller.VerticalOffset != 0)
                    {
                        scroller.ChangeView(0, 0, null, disableAnimation: true);
                        continue;
                    }
                    if (scroller.Content is not Microsoft.UI.Xaml.UIElement content)
                        continue;

                    var point = content.TransformToVisual(handler.PlatformView).TransformPoint(new(0, 0));
                    origin = new(point.X, point.Y);
                    handler.PlatformView.Document.GetRange(0, 1).GetRect(
                        Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var rect, out prefixHit);
                    nativePrefix = new(rect.X + point.X, rect.Y + point.Y, rect.Width, rect.Height);
                    lastCapture = editor.TextLayout.Capture();
                    if (lastCapture is not null && nativePrefix.Value.Width > 0 && nativePrefix.Value.Height > 0 &&
                        nativePrefix.Value.IntersectsWith(lastCapture.TextViewport))
                    {
                        layout = lastCapture;
                        break;
                    }
                }
                if (layout is null)
                    throw new InvalidOperationException($"Native long-line layout did not expose its first character. {Diagnostics()}");

                var snapshot = editor.Document.CurrentSnapshot;
                var selection = editor.SelectionState;
                Require(nativePrefix!.Value.Width < layout.TextViewport.Width && nativePrefix.Value.Height < layout.TextViewport.Height,
                    "Native first-character geometry exceeds the test viewport.");
                Require(layout.Lines.Count == 1 && layout.Lines[0].SourceRanges.Count == 1 &&
                    layout.Lines[0].SourceRanges[0] == new RichTextRange(0, editor.Document.Length),
                    "The capture does not contain the complete unwrapped source line.");
                Require(editor.TextLayout.GetCaretBounds(layout, 0) is not null, "Missing first-caret geometry.");
                Require(editor.TextLayout.GetRangeBounds(layout, new(0, 1)).Count > 0, "Missing first-character source geometry.");
                var before = GeometryQueries(handler);
                var bounds = editor.TextLayout.GetRangeBounds(layout, new(0, editor.Document.Length));
                Require(bounds.Any(rect => rect.IntersectsWith(nativePrefix!.Value)), "Full-range geometry lost the visible prefix.");
                var queries = GeometryQueries(handler) - before;
                Equal(true, queries < queryBudget, $"native geometry work is bounded by the visible part of the line: {queries} queries, budget {queryBudget}");
                Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot), "geometry preserves the document");
                Equal(selection, editor.SelectionState, "geometry preserves selection");
                Equal(0d, scroller!.HorizontalOffset, "geometry preserves horizontal scrolling");
                Equal(0d, scroller.VerticalOffset, "geometry preserves vertical scrolling");

                void Require(bool condition, string context)
                {
                    if (!condition)
                        throw new InvalidOperationException($"{context} {Diagnostics()}");
                }

                string Diagnostics()
                {
                    handler.PlatformView.Document.GetRange(0, 0).GetRect(
                        Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var caret, out var hit);
                    var lines = lastCapture is null ? "none" : string.Join("; ", lastCapture.Lines.Select(line =>
                        $"{line.Bounds}: [{string.Join(", ", line.SourceRanges)}]"));
                    var current = editor.TextLayout.Capture();
                    return $"Viewport: {lastCapture?.TextViewport}; scroll: ({scroller?.HorizontalOffset}, {scroller?.VerticalOffset}); " +
                        $"origin: {origin}; native prefix: {nativePrefix} (hit {prefixHit}); native caret: {caret} (hit {hit}); " +
                        $"font size (control/default/first): {handler.PlatformView.FontSize}/{handler.PlatformView.Document.GetDefaultCharacterFormat().Size}/{handler.PlatformView.Document.GetRange(0, 1).CharacterFormat.Size}; " +
                        $"captured/current layout version: {lastCapture?.LayoutVersion}/{current?.LayoutVersion}; " +
                        $"captured/current revision: {lastCapture?.DocumentRevision}/{editor.Document.Revision}; " +
                        $"source/native length: {editor.Document.Length}/{handler.PlatformView.Document.GetRange(0, 0).StoryLength}; lines: {lines}.";
                }
            }
            finally
            {
                handler.PlatformView.TextWrapping = wrapping;
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(handler.PlatformView, horizontalScrollBar);
            }
        });
#endif

        yield return new("projection inline baselines and resizing preserve source", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("ab");
            var source = editor.Document.CurrentSnapshot;
            using var first = editor.Adornments.Add(1, new Label { Text = "one", WidthRequest = 35, HeightRequest = 30 },
                new() { Placement = RichTextAdornmentPlacement.Inline, Baseline = 8 });
            using var second = editor.Adornments.Add(1, new Label { Text = "two", WidthRequest = 35, HeightRequest = 30 },
                new() { Placement = RichTextAdornmentPlacement.Inline, Baseline = 25 });
            await Task.Delay(150);
            for (var attempt = 0; attempt < 100 && (first.View.Bounds.X < 0 || second.View.Bounds.X < 0); attempt++)
                await Task.Delay(20);

            Equal(true, Math.Abs(first.View.Bounds.Y + 8 - second.View.Bounds.Y - 25) <= 2,
                $"shared source baseline: {first.View.Bounds}; {second.View.Bounds}");
            Equal(true, first.View.Bounds.Right <= second.View.Bounds.Left + 1, "same-anchor flow order");
            second.Options = second.Options with { Baseline = 8 };
            await Task.Delay(150);
            Equal(true, Math.Abs(first.View.Bounds.Y - second.View.Bounds.Y) <= 2, "changed baseline is reprojected");
            Equal(true, ReferenceEquals(source, editor.Document.CurrentSnapshot));
            first.Dispose();
            second.Dispose();
            await Task.Delay(150);
        });

        yield return new("projection keeps a visible source anchor when rows above it change", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText(string.Join("\n", Enumerable.Range(0, 250).Select(index => $"line {index}")));
            await Task.Delay(150);
            editor.ScrollIntoView(new(editor.Document.Text.IndexOf("line 150", StringComparison.Ordinal), 0));
            await Task.Delay(150);
            var layout = editor.TextLayout.Capture()!;
            var anchor = layout.Lines.Skip(1).First().SourceRanges.First().Start;
            var before = editor.TextLayout.GetCaretBounds(layout, anchor)!.Value;
            Equal(true, layout.Lines.First().SourceRanges.First().Start > 0, $"source is scrolled: first anchor {layout.Lines.First().SourceRanges.First().Start}");
            using var item = editor.Adornments.Add(0, new Label
                {
                    Text = "above",
                    HeightRequest = 40
                }, new() { Placement = RichTextAdornmentPlacement.AboveLine });
            await Task.Delay(150);
            layout = editor.TextLayout.Capture()!;
            var after = editor.TextLayout.GetCaretBounds(layout, anchor);
            Equal(true, after is { } bounds && Math.Abs(bounds.Y - before.Y) <= 2, $"source anchor: before {before}, after {after}");
            item.View.HeightRequest = 70;
            await Task.Delay(150);
            layout = editor.TextLayout.Capture()!;
            after = editor.TextLayout.GetCaretBounds(layout, anchor);
            Equal(true, after is { } resized && Math.Abs(resized.Y - before.Y) <= 2, "source anchor after size refinement");
            item.Dispose();
            await Task.Delay(150);
        });

        yield return new("projection authored objects, hyperlinks and clipboard stay logical", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("literal \uFFFC link\nimage: ");
            editor.SelectedRange = new(10, 4);
            editor.Selection.SetLink("https://example.com", "authored");
            editor.SelectedRange = new(editor.Document.Length, 0);
            editor.Selection.InsertImage(Image() with { AlternativeText = "authored pixel" });
            editor.Document.ClearUndoHistory();
            var original = editor.Document.CurrentSnapshot;
            Equal(original.Text, NativeText(editor), "authored native text before reservations");
            using var inline = editor.Adornments.Add(10, new Button
                {
                    Text = "hint",
                    WidthRequest = 50,
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            using var block = editor.Adornments.Add(0, new Label
                {
                    Text = "action",
                    HeightRequest = 30
                }, new() { Placement = RichTextAdornmentPlacement.AboveLine });
            await Task.Delay(150);
            Equal("\uFFFC\nliteral \uFFFC \uFFFClink\nimage: \uFFFC", NativeText(editor), "authored native text with reservations");
            editor.SelectedRange = new(0, editor.Document.Length);
            await editor.CopyAsync();
            Equal(original.Text, await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.GetTextAsync(), "clipboard source text");
            Equal(1, editor.Document.CurrentSnapshot.Images.Length);
            Equal(1, editor.Document.CurrentSnapshot.Links.Length);
            Equal(true, ReferenceEquals(original, editor.Document.CurrentSnapshot));
            editor.SelectedRange = new(0, 0);
            NativeReplace(editor, "!");
            await Task.Delay(150);
            Equal("!" + original.Text, editor.Document.Text);
            Equal("authored pixel", editor.Document.CurrentSnapshot.Images.Single().AlternativeText);
            Equal(new RichTextRange(11, 4), editor.Document.CurrentSnapshot.Links.Single().Range);
            editor.Undo();
            await Task.Delay(150);
            SameContent(original, editor.Document.CurrentSnapshot);
            inline.Dispose();
            block.Dispose();
            await Task.Delay(150);
        });

        yield return new("projection empty lines, same-anchor order, resize and folding", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("\nalpha beta\n");
            var original = editor.Document.CurrentSnapshot;
            using var above = editor.Adornments.Add(0, new Label
                {
                    Text = "first",
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.AboveLine });
            using var below = editor.Adornments.Add(editor.Document.Length, new Label
                {
                    Text = "last",
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.BelowLine });
            var first = new Button { Text = "1", WidthRequest = 40, HeightRequest = 25 };
            var second = new Button { Text = "2", WidthRequest = 60, HeightRequest = 25 };
            using var one = editor.Adornments.Add(6, first, new() { Placement = RichTextAdornmentPlacement.Inline, Baseline = 20 });
            using var two = editor.Adornments.Add(6, second, new() { Placement = RichTextAdornmentPlacement.Inline, Baseline = 20 });
            await Task.Delay(150);
            Equal(true, first.Bounds.Right <= second.Bounds.Left + 1, $"same-anchor order: {first.Bounds}; {second.Bounds}");
            var capture = editor.TextLayout.Capture()!;
            Equal(true, editor.TextLayout.GetCaretBounds(capture, 0) is not null);
            Equal(true, editor.TextLayout.GetCaretBounds(capture, editor.Document.Length) is not null);
            first.WidthRequest = 80;
            await Task.Delay(150);
            Equal(null, editor.TextLayout.GetCaretBounds(capture, 1));
            Equal(true, first.Width >= 79, $"refined width: {first.Bounds}");
            Equal(true, first.Bounds.Right <= second.Bounds.Left + 1);
            editor.Folding.Collapse(new(2, 7));
            await Task.Delay(150);
            Equal(true, first.Bounds.Right < 0, "folded inline view is not displayed");
            editor.Folding.ExpandAll();
            await Task.Delay(150);
            Equal(true, first.Bounds.Left >= 0, "expanding restores the reservation");
            Equal(true, ReferenceEquals(original, editor.Document.CurrentSnapshot));
            above.Dispose();
            below.Dispose();
            one.Dispose();
            two.Dispose();
            await Task.Delay(150);
            Equal(original.Text, NativeText(editor));
        });

        yield return new("projection native Backspace deletes source beside an inline widget", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc def");
            using var item = editor.Adornments.Add(3, new Button
                {
                    Text = "label",
                    WidthRequest = 55,
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            editor.SelectedRange = new(3, 0);
            var handler = (RichEditorHandler)editor.Handler!;
#if WINDOWS
            typeof(RichEditorHandler).GetMethod("PrepareNativeSourceKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(handler, [EditorKey.Backspace, EditorKeyModifiers.None]);
            handler.PlatformView.Document.Selection.Delete(Microsoft.UI.Text.TextRangeUnit.Character, -1);
#elif ANDROID
            using var key = new global::Android.Views.KeyEvent(global::Android.Views.KeyEventActions.Down, global::Android.Views.Keycode.Del);
            handler.PlatformView.OnKeyDown(global::Android.Views.Keycode.Del, key);
#else
            handler.PlatformView.DeleteBackward();
#endif
            await Task.Delay(150);
            Equal("ab def", editor.Document.Text);
            Equal(2, item.Position);
            Equal("ab\uFFFC def", NativeText(editor));
            item.Dispose();
            await Task.Delay(150);
        });

#if ANDROID
        yield return new("projection Android observes explicit native format changes across text edits", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcd");
            using var item = editor.Adornments.Add(2, new Label
                {
                    Text = "hint",
                    WidthRequest = 30,
                    HeightRequest = 20
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            var text = native.EditableText!;
            bool BoldAt(int position) => editor.Document.CurrentSnapshot.Runs.Single(run => run.Range.Start <= position && run.Range.End > position).Format.Bold;
            using var bold = new global::Android.Text.Style.StyleSpan(global::Android.Graphics.TypefaceStyle.Bold);
            text.SetSpan(bold, 0, 1, global::Android.Text.SpanTypes.ExclusiveExclusive);
            await Task.Delay(100);
            Equal(true, BoldAt(0), "native span addition is observed");
            text.SetSpan(bold, 3, 4, global::Android.Text.SpanTypes.ExclusiveExclusive);
            await Task.Delay(100);
            Equal(false, BoldAt(0), "explicit span movement clears its old source range");
            Equal(true, BoldAt(2), "explicit span movement decorates its new source range");
            editor.SelectedRange = new(0, 0);
            NativeReplace(editor, "x");
            await Task.Delay(100);
            Equal("xabcd", editor.Document.Text);
            Equal(true, BoldAt(3), "text editing carries the native format with its source");
            text.RemoveSpan(bold);
            await Task.Delay(100);
            Equal(false, BoldAt(3), "native span removal is observed");
        });

        yield return new("projection Android autofill exchanges logical source", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a\uFFFCb");
            using var item = editor.Adornments.Add(1, new Label
                {
                    Text = "hint",
                    WidthRequest = 30,
                    HeightRequest = 20
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            Equal(editor.Document.Text, native.AutofillValue!.TextValue);
            using var getter = native.Class.GetMethod("getAutofillValue")!;
            using var nativeValue = getter.Invoke(native)!.JavaCast<global::Android.Views.Autofill.AutofillValue>();
            Equal(editor.Document.Text, nativeValue.TextValue, "Java autofill dispatch uses logical source");
            using var text = new Java.Lang.String("replacement");
            using var value = global::Android.Views.Autofill.AutofillValue.ForText(text)!;
            editor.IsReadOnly = true;
            native.Autofill(value);
            Equal("a\uFFFCb", editor.Document.Text);
            editor.IsReadOnly = false;
            editor.MaxLength = 3;
            native.Autofill(value);
            Equal("a\uFFFCb", editor.Document.Text);
            editor.MaxLength = -1;
            native.Autofill(value);
            await Task.Delay(150);
            Equal("replacement", editor.Document.Text);
            Equal("replacement", native.AutofillValue!.TextValue);
            editor.Undo();
            Equal("a\uFFFCb", editor.Document.Text);
        });

        yield return new("projection Android IME and accessibility use source offsets", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("A😀 bc\ntail");
            using var item = editor.Adornments.Add(4, new Button
                {
                    Text = "label",
                    WidthRequest = 55,
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            editor.SelectedRange = new(4, 2);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            using var info = new global::Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            Equal(4, info.InitialSelStart);
            Equal(6, info.InitialSelEnd);
            using var request = new global::Android.Views.InputMethods.ExtractedTextRequest { Token = 17 };
            var extracted = connection.GetExtractedText(request, global::Android.Views.InputMethods.GetTextFlags.None)!;
            Equal(editor.Document.Text, extracted.Text!.ToString());
            Equal(4, extracted.SelectionStart);
            Equal("bc", connection.GetSelectedTextFormatted(global::Android.Views.InputMethods.GetTextFlags.None)!.ToString());
            connection.SetSelection(4, 4);
            Equal(new RichTextRange(4, 0), editor.SelectedRange);
            Equal("A😀 ", connection.GetTextBeforeCursorFormatted(20, global::Android.Views.InputMethods.GetTextFlags.None)!.ToString());
            Equal("bc\ntail", connection.GetTextAfterCursorFormatted(20, global::Android.Views.InputMethods.GetTextFlags.None)!.ToString());
            var sourceBeforeAccessibility = editor.Document.CurrentSnapshot;
            var nativeBeforeAccessibility = NativeText(editor);
            var selectionBeforeAccessibility = editor.SelectionState;
            using var node = native.CreateAccessibilityNodeInfo()!;
            Equal(editor.Document.Text, node.Text);
            Equal(true, ReferenceEquals(sourceBeforeAccessibility, editor.Document.CurrentSnapshot), "accessibility queries retain the source snapshot");
            Equal(nativeBeforeAccessibility, NativeText(editor), "accessibility queries retain native reservations");
            Equal(selectionBeforeAccessibility, editor.SelectionState, "accessibility queries retain selection");
            using var selection = new global::Android.OS.Bundle();
            selection.PutInt(global::Android.Views.Accessibility.AccessibilityNodeInfo.ActionArgumentSelectionStartInt, 1);
            selection.PutInt(global::Android.Views.Accessibility.AccessibilityNodeInfo.ActionArgumentSelectionEndInt, 3);
            Equal(true, native.PerformAccessibilityAction(global::Android.Views.Accessibility.Action.SetSelection, selection));
            Equal("😀", editor.Selection.Text);
            connection.SetSelection(3, 3);
            Equal(true, connection.DeleteSurroundingTextInCodePoints(1, 0));
            await Task.Delay(150);
            Equal("A bc\ntail", editor.Document.Text);
            editor.SelectedRange = new(2, 0);
            using var marked = new Java.Lang.String("仮名");
            connection.SetComposingText(marked, 1);
            Equal(true, editor.Composition.IsActive);
            Equal(new RichTextRange(2, 2), editor.Composition.Range);
            Equal("A 仮名bc\ntail", editor.Document.Text);
            connection.FinishComposingText();
            await Task.Delay(150);
            Equal(false, editor.Composition.IsActive);
            item.Dispose();
            await Task.Delay(150);
        });
#elif IOS || MACCATALYST
        yield return new("projection Apple accessibility reads source without private rows", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc\ntail");
            using var item = editor.Adornments.Add(0, new Button
                {
                    Text = "action",
                    HeightRequest = 30
                }, new() { Placement = RichTextAdornmentPlacement.AboveLine });
            using var inline = editor.Adornments.Add(2, new Label
                {
                    Text = "label",
                    WidthRequest = 40,
                    HeightRequest = 20
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            Equal(editor.Document.Text, native.AccessibilityValue);
            using var attributed = native.AccessibilityAttributedValue!;
            Equal(editor.Document.Text, attributed.Value);
            Equal(editor.Document.Text, ((UIKit.IUIAccessibilityReadingContent)native).GetAccessibilityPageContent());
            Equal("abc\n", ((UIKit.IUIAccessibilityReadingContent)native).GetAccessibilityContent(0));
            using var wholeText = native.GetTextRange(native.BeginningOfDocument, native.EndOfDocument);
            Equal(editor.Document.Text, native.TextInRange(wholeText), "UITextInput reads logical source");
            Equal((nint)editor.Document.Length, native.GetOffsetFromPosition(native.BeginningOfDocument, native.EndOfDocument), "UITextInput source offsets");
            using var selectionStart = native.GetPosition(native.BeginningOfDocument, 1);
            using var selectionEnd = native.GetPosition(native.BeginningOfDocument, 3);
            using var sourceSelection = native.GetTextRange(selectionStart, selectionEnd);
            Equal("bc", native.TextInRange(sourceSelection));
            using var forward = native.GetPosition(selectionStart, UIKit.UITextLayoutDirection.Right, 2);
            using var backward = native.GetPosition(selectionEnd, UIKit.UITextLayoutDirection.Left, 1);
            Equal((nint)3, native.GetOffsetFromPosition(native.BeginningOfDocument, forward), "directional movement skips reservations");
            Equal((nint)2, native.GetOffsetFromPosition(native.BeginningOfDocument, backward));
            using var character = native.GetCharacterRange(selectionEnd, UIKit.UITextLayoutDirection.Left);
            Equal("c", native.TextInRange(character), "character ranges exclude inline labels");
            native.SelectedTextRange = sourceSelection;
            await Task.Delay(50);
            Equal(new RichTextRange(1, 2), editor.SelectedRange, "UITextInput selection addresses source");
            editor.SelectedRange = new(0, 0);
            native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
            Equal(true, editor.Composition.IsActive);
            Equal(new RichTextRange(0, 2), editor.Composition.Range);
            Equal("仮名abc\ntail", editor.Document.Text);
            native.UnmarkText();
            await Task.Delay(150);
            Equal(editor.Document.Text, native.AccessibilityValue);
            item.Dispose();
            inline.Dispose();
            await Task.Delay(150);
        });
#endif
        yield return new("projection inline layout, native editing and history preserve source", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc def\ntail");
            await Task.Delay(100);
            editor.SelectedRange = new(3, 0);
            NativeReplace(editor, "!");
            await Task.Delay(100);
            var typingWithoutReservation = editor.Document.GetCharacterFormat(new(3, 1)).RepresentativeFormat;
            editor.Document = RichTextDocument.FromPlainText("abc def\ntail");
            var original = editor.Document.CurrentSnapshot;
            var view = new Button { Text = "label", WidthRequest = 70, HeightRequest = 28 };
            using var item = editor.Adornments.Add(3, view, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            Equal(true, ReferenceEquals(original, editor.Document.CurrentSnapshot), "adding a reservation preserves the snapshot");
            Equal("abc\uFFFC def\ntail", NativeText(editor), "native inline reservation");
            var layout = editor.TextLayout.Capture()!;
            var before = editor.TextLayout.GetCaretBounds(layout, 3, RichTextCaretAffinity.Upstream)!.Value;
            var after = editor.TextLayout.GetCaretBounds(layout, 3)!.Value;
            Equal(true, after.X - before.X >= 65, $"inline width before={before}, after={after}, view={view.Bounds}");
            Equal(true, view.Bounds.X >= 0 && view.Bounds.Y >= 0, "inline view is arranged");
            editor.SelectedRange = new(3, 0);
            NativeReplace(editor, "!");
            await Task.Delay(150);
            Equal("abc! def\ntail", editor.Document.Text, "native insertion maps to source");
            Equal(typingWithoutReservation, editor.Document.GetCharacterFormat(new(3, 1)).RepresentativeFormat, "reservation formatting must not change native typing");
            Equal(4, item.Position);
            Equal("abc!\uFFFC def\ntail", NativeText(editor), "insertion affinity");
            editor.Undo();
            await Task.Delay(150);
            SameContent(original, editor.Document.CurrentSnapshot);
            Equal(3, item.Position);
            editor.Redo();
            await Task.Delay(150);
            Equal("abc! def\ntail", editor.Document.Text);
            item.Dispose();
            await Task.Delay(150);
            Equal(editor.Document.Text, NativeText(editor), "removing a reservation restores the native source projection");
            Equal(null, view.Parent);
        });

        foreach (var placement in new[] { RichTextAdornmentPlacement.AboveLine, RichTextAdornmentPlacement.BelowLine })
            yield return new($"projection {placement} reserves a row without source or history", async editor =>
            {
                editor.Document = RichTextDocument.FromPlainText("abc\nnext");
                await Task.Delay(100);
                var original = editor.Document.CurrentSnapshot;
                var layout = editor.TextLayout.Capture()!;
                var prior = editor.TextLayout.GetCaretBounds(layout, 4)!.Value;
                using var item = editor.Adornments.Add(placement == RichTextAdornmentPlacement.AboveLine ? 4 : 1,
                    new Button { Text = "action", HeightRequest = 48 }, new() { Placement = placement });
                await Task.Delay(150);
                Equal(true, ReferenceEquals(original, editor.Document.CurrentSnapshot));
                Equal("abc\n\uFFFC\nnext", NativeText(editor), "native block reservation");
                layout = editor.TextLayout.Capture()!;
                var current = editor.TextLayout.GetCaretBounds(layout, 4)!.Value;
                Equal(true, current.Y >= prior.Y + 45, $"reserved row: before={prior}, after={current}");
                Equal(false, editor.CanUndo);
                editor.SelectedRange = new(4, 0);
                NativeReplace(editor, "!");
                await Task.Delay(150);
                Equal("abc\n!next", editor.Document.Text, "editing after a block row");
                editor.Undo();
                await Task.Delay(150);
                SameContent(original, editor.Document.CurrentSnapshot);
                item.Dispose();
                await Task.Delay(150);
                Equal(original.Text, NativeText(editor), "disposing a block row restores the native source projection");
            });

        yield return new("projection selection across reservations replaces only source", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcdef");
            using var first = editor.Adornments.Add(2, new Button
                {
                    Text = "one",
                    WidthRequest = 40,
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            using var second = editor.Adornments.Add(4, new Button
                {
                    Text = "two",
                    WidthRequest = 40,
                    HeightRequest = 25
                }, new() { Placement = RichTextAdornmentPlacement.Inline });
            await Task.Delay(150);
            Equal("ab\uFFFCcd\uFFFCef", NativeText(editor));
            editor.SelectedRange = new(1, 4);
            NativeReplace(editor, "X");
            await Task.Delay(150);
            Equal("aXf", editor.Document.Text);
            Equal("aXf", NativeText(editor));
            Equal(0, editor.Adornments.Count, "deleted anchors are removed");
            editor.Undo();
            await Task.Delay(150);
            Equal("abcdef", editor.Document.Text);
            Equal(0, editor.Adornments.Count, "undo does not resurrect application views");
        });
    }

#if WINDOWS
    [WinRT.DynamicWindowsRuntimeCast(typeof(Microsoft.UI.Xaml.Controls.ScrollViewer))]
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindWindowsScroller(Microsoft.UI.Xaml.DependencyObject view)
    {
        if (view is Microsoft.UI.Xaml.Controls.ScrollViewer scroller)
            return scroller;

        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(view); index++)
            if (FindWindowsScroller(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(view, index)) is { } child)
                return child;

        return null;
    }
#endif
}
