using TextEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
#if !WINDOWS
    private static Task WithClipboardAccess(Func<Task> action) => action();
#endif

    private static IEnumerable<Case> MultipleSelectionCases()
    {
#if WINDOWS
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
            Equal(new RichTextRange(2, 6), untouched.Range!.Value);
            editor.Undo();
            await Verify(editor);
            Equal(new RichTextRange(4, 6), untouched.Range!.Value);
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

    private static void NativeMultiInsert(RichEditor editor, string text)
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
