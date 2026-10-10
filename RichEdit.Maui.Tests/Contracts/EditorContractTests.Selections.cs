using TextEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
#if !WINDOWS
    private static Task WithClipboardAccess(Func<Task> action) => action();
#endif

    private static IEnumerable<Case> MultipleSelectionCases()
    {
        yield return new("selections native navigation preserves graphemes and moves all carets", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("A😀e\u0301 Z😀e\u0301");
            editor.Selections = new((TextSelection[])[new(1, 1), new(7, 7)]);
            await Task.Delay(100);
            var revision = editor.Document.Revision;
            Equal(true, editor.MoveSelections(EditorKey.Right));
            Equal("3,9", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, editor.MoveSelections(EditorKey.Right, extend: true));
            Equal("3:5,9:11", string.Join(",", editor.Selections.Select(item => $"{item.Anchor}:{item.Active}")));
            Equal(true, editor.MoveSelections(EditorKey.Left));
            Equal("3,9", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(revision, editor.Document.Revision);
            Equal(false, editor.CanUndo);
            await Verify(editor);
        });

        yield return new("selections adjacent native lines retain preferred horizontal positions", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("aaaa\nx\naaaa");
            Equal(editor.Document.Text, NativeText(editor), "initial native navigation text");
            editor.SelectedRange = new(3, 0);
            Equal(editor.Document.Text, NativeText(editor), "after assigning the initial navigation caret");
            await Task.Delay(100);
            Equal(editor.Document.Text, NativeText(editor), "after the initial navigation caret settles");
            Equal(true, editor.AddCaretBelow());
            Equal(editor.Document.Text, NativeText(editor), "after adding the second caret");
            Equal(6, editor.Selections.Primary.Active);
            var preferred = editor.Selections.Primary.PreferredVisualX!.Value;
            var navigationLayout = editor.TextLayout.Capture()!;
            var targetLine = navigationLayout.Lines.Last();
            var expected = editor.TextLayout.HitTest(navigationLayout, new(preferred, targetLine.Bounds.Y + targetLine.Bounds.Height / 2))!.Position;
            Equal(true, editor.AddCaretBelow());
            Equal(editor.Document.Text, NativeText(editor), "after adding the third caret");
            Equal(expected, editor.Selections.Primary.Active, "vertical movement follows native geometry rather than character columns");
            Equal(preferred, editor.Selections.Primary.PreferredVisualX!.Value);
            Equal(3, editor.Selections.Count);
            Equal(true, editor.MoveSelections(EditorKey.Home));
            Equal("0,5,7", string.Join(",", editor.Selections.Select(item => item.Active)), $"native=[{NativeText(editor)}], source=[{editor.Document.Text}]");
            Equal(true, editor.MoveSelections(EditorKey.End));
            Equal("4,6,11", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(false, editor.CanUndo);
            await Verify(editor);
        });

        yield return new("selections horizontal navigation follows native RTL order", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("אבגד אבגד");
            editor.Selections = new((TextSelection[])[new(1, 1), new(6, 6)]);
            await Task.Delay(100);
            Equal(true, editor.MoveSelections(EditorKey.Left));
#if WINDOWS
            // TOM follows Windows' native logical movement convention for this run.
            Equal("0,5", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, editor.MoveSelections(EditorKey.Right, extend: true));
            Equal("0:1,5:6", string.Join(",", editor.Selections.Select(item => $"{item.Anchor}:{item.Active}")));
            Equal(true, editor.MoveSelections(EditorKey.Left));
            Equal("0,5", string.Join(",", editor.Selections.Select(item => item.Active)));
#else
            Equal("2,7", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, editor.MoveSelections(EditorKey.Right, extend: true));
            Equal("2:1,7:6", string.Join(",", editor.Selections.Select(item => $"{item.Anchor}:{item.Active}")));
            Equal(true, editor.MoveSelections(EditorKey.Left));
            Equal("2,7", string.Join(",", editor.Selections.Select(item => item.Active)));
#endif
            await Verify(editor);
        });

        yield return new("selections native line ends include authored trailing spaces", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a  \nb  ");
            editor.Selections = new((TextSelection[])[new(0, 0), new(4, 4)]);
            await Task.Delay(100);
            Equal(true, editor.MoveSelections(EditorKey.End));
            Equal("3,7", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, editor.MoveSelections(EditorKey.Home));
            Equal("0,4", string.Join(",", editor.Selections.Select(item => item.Active)));
            await Verify(editor);
        });

        yield return new("selections navigation skips private inline reservations", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("ab\ncd");
            using var hint = editor.Adornments.Add(1, new Label { Text = "hint", WidthRequest = 30, HeightRequest = 20 },
                new() { Placement = RichTextAdornmentPlacement.Inline });
            editor.Selections = new((TextSelection[])[new(0, 0), new(3, 3)]);
            await Task.Delay(150);
            Equal(true, editor.MoveSelections(EditorKey.Right));
            Equal("1,4", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, editor.MoveSelections(EditorKey.Right));
            Equal("2,5", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal("ab\ncd", editor.Document.Text);
            Equal(false, editor.CanUndo);
            hint.Dispose();
            await Task.Delay(100);
        });

#if ANDROID || IOS || MACCATALYST
        yield return new("composition multiple selections commit once and keep other views unchanged while marked", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("aa bb");
            editor.Selections = new((TextSelection[])[new(2, 2), new(5, 5)], 1);
            using var peer = new SharedEditor(editor);
            editor.Focus();
            await Task.Delay(100);
            var snapshot = editor.Document.CurrentSnapshot;
            var events = 0;
            void Changed(object? sender, RichTextTextChangedEventArgs args) => events++;
            editor.TextChanged += Changed;
            try
            {
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
                using var info = new Android.Views.InputMethods.EditorInfo();
                using var connection = native.OnCreateInputConnection(info)!;
                using var first = new Java.Lang.String("か");
                using var second = new Java.Lang.String("仮名");
                connection.SetComposingText(first, 1);
                connection.SetComposingText(second, 1);
#else
                native.SetMarkedText("か", new Foundation.NSRange(1, 0));
                native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
#endif
                Equal(true, ReferenceEquals(snapshot, editor.Document.CurrentSnapshot));
                Equal("aa bb", NativeText(peer.Editor));
                Equal("aa bb仮名", editor.CompositionSnapshot!.Text);
                await Task.Delay(50);
                Equal(null, editor.TextLayout.Capture());
                var inputLayout = editor.TextLayout.CaptureInput()!;
                Equal(true, inputLayout.IsProvisional);
                Equal(7, inputLayout.SourceLength);
                Equal(true, editor.TextLayout.GetCaretBounds(inputLayout, editor.CompositionSelection!.Value.Active) is not null);
                Equal(0, events);
                Equal(false, editor.Document.IsModified);
                Equal(false, peer.Editor.TryApplyEdits(snapshot.Revision, (RichTextEdit[])[new(new(0, 0), "wrong")]));
                var saveRejected = false;
                try { editor.Document.CreateSavePoint(); }
                catch (InvalidOperationException) { saveRejected = true; }
                Equal(true, saveRejected);
#if ANDROID
                connection.FinishComposingText();
#else
                native.UnmarkText();
#endif
                await Verify(editor);
                Equal("aa仮名 bb仮名", editor.Document.Text);
                Equal(editor.Document.Text, NativeText(peer.Editor));
                Equal(1, events);
                Equal(false, editor.Composition.IsActive);
                Equal(null, editor.CompositionSnapshot);
                Equal(null, editor.TextLayout.GetCaretBounds(inputLayout, 0));
                peer.Editor.Undo();
                await Verify(editor);
                Equal("aa bb", editor.Document.Text);
                Equal(false, editor.CanUndo);
                Equal(2, peer.Editor.Selections.Count);
            }
            finally { editor.TextChanged -= Changed; }
        });

        yield return new("composition cancellation creates no document version or undo entry", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc");
            editor.Selections = new((TextSelection[])[new(0, 0), new(3, 3)]);
            var before = editor.Document.CurrentSnapshot;
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var marked = new Java.Lang.String("仮");
            using var empty = new Java.Lang.String("");
            connection.SetComposingText(marked, 1);
            connection.SetComposingText(empty, 1);
            connection.FinishComposingText();
#else
            native.SetMarkedText("仮", new Foundation.NSRange(1, 0));
            native.SetMarkedText("", new Foundation.NSRange(0, 0));
            native.UnmarkText();
#endif
            await Verify(editor);
            Equal(true, ReferenceEquals(before, editor.Document.CurrentSnapshot));
            Equal(false, editor.CanUndo);
            Equal(2, editor.Selections.Count);
            Equal("abc", NativeText(editor));
        });

        yield return new("composition can complete through another view before capturing a save point", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a");
            editor.SelectedRange = new(1, 0);
            using var peer = new SharedEditor(editor);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var marked = new Java.Lang.String("仮名");
            connection.SetComposingText(marked, 1);
#else
            native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
#endif
            Equal("a", editor.Document.Text);
            Equal(true, peer.Editor.TryCompleteComposition());
            await Verify(editor);
            Equal("a仮名", editor.Document.Text);
            var saved = editor.Document.CreateSavePoint();
            editor.Document.MarkSaved(saved);
            Equal(false, editor.Document.IsModified);
        });
#endif

#if WINDOWS
        yield return new("composition Windows boundary keeps provisional UIA ranges valid through commit", editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("aa bb cc");
            editor.Selections = new((TextSelection[])[new(2, 2), new(5, 5), new(8, 8)], 1);
            var handler = (RichEditorHandler)editor.Handler!;
            var native = handler.PlatformView;
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(native);
            var provider = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
            var word = provider.DocumentRange.Clone();
            word.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 3);
            word.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, -3);
            var composing = typeof(RichEditorHandler).GetField("_isComposing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var observe = typeof(RichEditorHandler).GetMethod("UpdateCompositionState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                composing.SetValue(handler, true);
                observe.Invoke(handler, null);
                NativeReplace(editor, "仮");
                native.Document.Selection.SetRange(6, 6);
                Equal("aa bb cc", editor.Document.Text);
                Equal("aa bb仮 cc", provider.DocumentRange.GetText(-1));
                Equal("bb", word.GetText(-1));
                var transient = provider.DocumentRange.Clone();
                transient.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 7);
                Equal("cc", transient.GetText(-1));
                Equal(false, editor.TryCompleteComposition());
                // WinUI reconciles its final native text/selection before ending composition.
                typeof(RichEditorHandler).GetMethod("ReconcileCompositionSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(handler, null);
                composing.SetValue(handler, false);
                observe.Invoke(handler, null);
                Equal("aa仮 bb仮 cc仮", editor.Document.Text);
                Equal("bb", word.GetText(-1));
                Equal("cc", transient.GetText(-1));
                editor.Document.Edit(edit => edit.InsertText(0, "!"));
                Equal("bb", word.GetText(-1));
                Equal("cc", transient.GetText(-1));
            }
            finally
            {
                composing.SetValue(handler, false);
                editor.DiscardComposition();
            }
            return Task.CompletedTask;
        });

        yield return new("selections UI Automation exposes and edits the selection collection", editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abcdefghij");
            editor.Selections = new((TextSelection[])[new(0, 2), new(5, 7)]);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(native);
            var provider = (Microsoft.UI.Xaml.Automation.Provider.ITextProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Text);
            Equal(Microsoft.UI.Xaml.Automation.SupportedTextSelection.Multiple, provider.SupportedTextSelection);
            Equal("ab,fg", string.Join(",", provider.GetSelection().Select(range => range.GetText(-1))));
            var additional = provider.DocumentRange.Clone();
            additional.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.Start, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, 3);
            additional.MoveEndpointByUnit(Microsoft.UI.Xaml.Automation.Text.TextPatternRangeEndpoint.End, Microsoft.UI.Xaml.Automation.Text.TextUnit.Character, -6);
            additional.AddToSelection();
            Equal(3, editor.Selections.Count);
            additional.RemoveFromSelection();
            Equal(2, editor.Selections.Count);
            provider.GetSelection()[0].Select();
            Equal(1, editor.Selections.Count);
            Equal(false, editor.Document.IsModified);
            return Task.CompletedTask;
        });
#endif

        yield return new("selections clipboard distributes cached RTF fragments and cuts atomically", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("one two");
            editor.Selections = new((TextSelection[])[new(0, 3), new(4, 7)]);
            await WithClipboardAccess(editor.CopyAsync);
            editor.Document = RichTextDocument.FromPlainText("A B");
            editor.Selections = new((TextSelection[])[new(0, 1), new(2, 3)]);
            await WithClipboardAccess(editor.PasteAsync);
            await Verify(editor);
            Equal("one two", editor.Document.Text);
            editor.Undo();
            Equal("A B", editor.Document.Text);
            editor.Redo();
            editor.Selections = new((TextSelection[])[new(0, 3), new(4, 7)]);
            await WithClipboardAccess(editor.CutAsync);
            await Verify(editor);
            Equal(" ", editor.Document.Text);
            editor.Undo();
            Equal("one two", editor.Document.Text);
            Equal(2, editor.Selections.Count);
            editor.Selections = new((TextSelection[])[new(0, 0), new(4, 7)]);
            Equal(true, editor.Commands.Copy.CanExecute(null));
            Equal(true, editor.Commands.Cut.CanExecute(null));
        });

        yield return new("selections native deletion respects each grapheme and keeps one history unit", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("😀 a😀");
            editor.Selections = new((TextSelection[])[new(2, 2), new(6, 6)]);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if WINDOWS
            native.Document.GetRange(0, 2).SetText(Microsoft.UI.Text.TextSetOptions.None, "");
#elif ANDROID
            native.EditableText!.Delete(0, 2);
#else
            native.DeleteBackward();
#endif
            await Verify(editor);
            Equal(" a", editor.Document.Text);
            Equal("0,2", string.Join(",", editor.Selections.Select(item => item.Active)));
            editor.Undo();
            await Verify(editor);
            Equal("😀 a😀", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });

        yield return new("selections native typing edits every caret in one transaction", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("aa bb cc");
            editor.Selections = new((TextSelection[])[new(2, 2), new(5, 5), new(8, 8)], 1);
            var before = editor.Selections;
            var count = 0;
            void Changed(object? sender, RichTextTextChangedEventArgs args)
            {
                count++;
                Equal("aax bbx ccx", editor.Document.Text, "one complete public edit");
                Equal(editor.Document.Text, NativeText(editor), "native state before notification");
            }
            editor.TextChanged += Changed;
            try { NativeMultiInsert(editor, "x"); }
            finally { editor.TextChanged -= Changed; }
            await Verify(editor);
            Equal(1, count);
            Equal("aax bbx ccx", editor.Document.Text);
            Equal("3,7,11", string.Join(",", editor.Selections.Select(item => item.Active)));
            editor.Undo();
            await Verify(editor);
            Equal("aa bb cc", editor.Document.Text);
            Equal(true, before.SequenceEqual(editor.Selections));
            Equal(false, editor.CanUndo);
            editor.Redo();
            await Verify(editor);
            Equal("aax bbx ccx", editor.Document.Text);
        });

        yield return new("selections native repeated text retains the insertion position", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("aaa bbb");
            editor.Selections = new((TextSelection[])[new(0, 0), new(4, 4)]);
            NativeMultiInsert(editor, "a");
            await Verify(editor);
            Equal("aaaa abbb", editor.Document.Text);
            Equal("1,6", string.Join(",", editor.Selections.Select(item => item.Active)));
        });

        yield return new("selections native replacement preserves distinct ranges and tracking", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("one middle two");
            using var untouched = editor.Document.Tracking.TrackRange(new(4, 6));
            editor.Selections = new((TextSelection[])[new(3, 0), new(11, 14)]);
            NativeMultiInsert(editor, "X");
            await Verify(editor);
            Equal("X middle X", editor.Document.Text);
            Equal(new TextSpan(2, 6), untouched.Range!.Value);
            editor.Undo();
            await Verify(editor);
            Equal(new TextSpan(4, 6), untouched.Range!.Value);
        });

        yield return new("selections input limit rejects the complete native batch", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("abc");
            editor.MaxLength = 4;
            editor.Selections = new((TextSelection[])[new(0, 0), new(3, 3)]);
            NativeMultiInsert(editor, "x");
            await Verify(editor);
            Equal("abc", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });

        yield return new("selections secondary overlays use visible native caret geometry", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("first\nsecond\nthird");
            editor.Selections = new((TextSelection[])[new(1, 1), new(8, 8), new(15, 15)]);
            await Task.Delay(100);
            var handler = (RichEditorHandler)editor.Handler!;
            Equal(2, SecondaryCarets(handler).Count);
            Equal(true, SecondaryCarets(handler).All(rect => rect.Height > 0));
            var revision = editor.Document.Revision;
            editor.Selections = new((TextSelection[])[editor.Selections.Primary]);
            await Task.Delay(50);
            Equal(0, SecondaryCarets(handler).Count);
            Equal(revision, editor.Document.Revision);
        });
    }

    private static IReadOnlyList<Rect> SecondaryCarets(RichEditorHandler handler) =>
        (IReadOnlyList<Rect>)typeof(RichEditorHandler).GetProperty("SecondaryCaretBounds",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(handler)!;

    private static void NativeMultiInsert(NativeEditorView editor, string text)
    {
#if ANDROID
        // Commit through the actual input connection. Assigning a native caret after
        // Editable.Replace is a separate selection command, not part of native typing.
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        using var info = new Android.Views.InputMethods.EditorInfo();
        using var connection = native.OnCreateInputConnection(info) ?? throw new InvalidOperationException("No native input connection.");
        using var value = new Java.Lang.String(text);
        Equal(true, connection.CommitText(value, 1));
#else
        NativeReplace(editor, text);
#endif
    }
}
