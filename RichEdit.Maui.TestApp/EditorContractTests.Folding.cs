using Microsoft.Maui.Graphics;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using WinRT;
#endif

namespace RichEdit.Maui.TestApp;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> FoldingCases()
    {
        yield return new("folding removes line space and preserves source and selection", async editor =>
        {
            const string source = "head\none\ntwo\ntail";
            editor.Document = RichTextDocument.FromPlainText(source);
            editor.SelectionState = new(source.Length, source.Length);
            await Verify(editor);
            var before = FoldingCaretRect(editor, 13);
            var line = FoldingCaretRect(editor, 0);
            var snapshot = editor.Document.CurrentSnapshot;
            var selection = editor.SelectionState;
            editor.Folding.Collapse(new(5, 8));
            await Task.Delay(100);
            var after = FoldingCaretRect(editor, 13);
            Equal(true, after.Y < before.Y - line.Height, $"folded line space: before={before}, after={after}, line={line}");
            Equal(source, NativeText(editor));
            Equal(source, editor.Document.Text);
            Equal(snapshot.Version, editor.Document.Version);
            Equal(false, editor.Document.IsModified);
            Equal(false, editor.CanUndo);
            Equal(selection, editor.SelectionState);
            editor.Folding.ExpandAll();
            await Task.Delay(100);
            Equal(true, Math.Abs(before.Y - FoldingCaretRect(editor, 13).Y) < 1, "expanded line space");
            Equal(snapshot.Version, editor.Document.Version);
        });

        yield return new("folding nested ranges retain independent state", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\none\ntwo\ntail");
            await Verify(editor);
            var before = FoldingCaretRect(editor, 13).Y;
            var outer = new RichTextRange(5, 8);
            var inner = new RichTextRange(9, 4);
            editor.Folding.SetCollapsedRanges((RichTextRange[])[inner, outer]);
            await Task.Delay(80);
            var both = FoldingCaretRect(editor, 13).Y;
            editor.Folding.Expand(outer);
            await Task.Delay(80);
            var child = FoldingCaretRect(editor, 13).Y;
            Equal(true, both < child && child < before, $"nested layout: both={both}, child={child}, expanded={before}");
            Equal(inner, editor.Folding.CollapsedRanges.Single());
            editor.SelectedRange = new(10, 0);
            editor.ScrollIntoView(new(10, 0));
            Equal(inner, editor.Folding.CollapsedRanges.Single());
            editor.Folding.ExpandAll();
        });

        yield return new("folding inline Unicode ranges retain native offsets", async editor =>
        {
            const string source = "before [日本語😀] after";
            var start = source.IndexOf('[');
            var end = source.IndexOf(']') + 1;
            editor.Document = RichTextDocument.FromPlainText(source);
            await Verify(editor);
            var before = FoldingCaretRect(editor, end);
            editor.Folding.Collapse(new(start, end - start));
            await Task.Delay(80);
            var after = FoldingCaretRect(editor, end);
            Equal(true, after.X < before.X - 10, $"inline layout: before={before}, after={after}");
            Equal(source, NativeText(editor));
            editor.SelectedRange = new(source.Length, 0);
            NativeReplace(editor, "!");
            await Verify(editor);
            Equal(source + "!", editor.Document.Text);
            editor.Folding.ExpandAll();
            await Verify(editor);
        });

        yield return new("folding follows native edits without consuming hidden source", async editor =>
        {
            const string source = "head\none\ntwo\ntail";
            editor.Document = RichTextDocument.FromPlainText(source);
            editor.Folding.Collapse(new(5, 8));
            editor.SelectedRange = new(0, 0);
            NativeReplace(editor, "prefix\n");
            await Verify(editor);
            Equal("prefix\n" + source, editor.Document.Text);
            Equal(new RichTextRange(12, 8), editor.Folding.CollapsedRanges.Single());
            editor.SelectedRange = new(editor.Document.Length, 0);
            NativeReplace(editor, "!");
            await Verify(editor);
            Equal("prefix\n" + source + "!", editor.Document.Text);
            Equal(new RichTextRange(12, 8), editor.Folding.CollapsedRanges.Single());
            editor.Undo();
            await Verify(editor);
            Equal("prefix\n" + source, editor.Document.Text);
            Equal(false, editor.Document.GetCharacterFormat(new(12, 8)).RepresentativeFormat.Hidden);
            editor.Folding.ExpandAll();
            await Verify(editor);
        });

        yield return new("folding retains formatting and resets on document replacement", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("head\none\ntwo\ntail");
            editor.Folding.Collapse(new(5, 8));
            editor.SelectedRange = new(5, 3);
            editor.Selection.CharacterFormat.Bold = true;
            using var colors = editor.Decorations.CreateLayer();
            colors.TrySet(editor.Document.Revision, (RichTextDecoration[])[new(new(0, editor.Document.Length), new() { ForegroundColor = Colors.Blue })]);
            await Verify(editor);
            Equal(false, editor.Document.GetCharacterFormat(new(5, 3)).RepresentativeFormat.Hidden);
            editor.Folding.ExpandAll();
            Equal(true, editor.Document.GetCharacterFormat(new(5, 3)).RepresentativeFormat.Bold);
            editor.Folding.Collapse(new(5, 8));
            editor.Document = RichTextDocument.FromPlainText("new\nfull\ndocument");
            await Verify(editor);
            Equal(0, editor.Folding.CollapsedRanges.Count);
            Equal("new\nfull\ndocument", NativeText(editor));
            Equal(true, FoldingCaretRect(editor, 9).Y > FoldingCaretRect(editor, 0).Y, "replacement layout");
        });

        yield return new("folding deleting all folded content preserves subsequent native input", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("one\ntwo\nthree");
            editor.Folding.Collapse(new(0, editor.Document.Length));
            NativeSelect(editor, new(0, editor.Document.Length));
            await Drain(); // WinUI delivers native selection changes asynchronously.
            Equal(new RichTextRange(0, editor.Document.Length), editor.SelectedRange, "native selection before deletion");
            NativeReplace(editor, string.Empty);
            await Verify(editor);
            Equal(string.Empty, editor.Document.Text);
            Equal(0, editor.Folding.CollapsedRanges.Count);
            NativeReplace(editor, "new\nsource");
            await Verify(editor);
            Equal("new\nsource", editor.Document.Text);
            Equal(true, FoldingCaretRect(editor, 4).Y > FoldingCaretRect(editor, 0).Y, "input after all folds were deleted");

            // Source selection APIs also edit a completely collapsed document.
            editor.Folding.Collapse(new(0, editor.Document.Length));
            editor.SelectedRange = new(0, editor.Document.Length);
            editor.Selection.ReplaceText("explicit replacement");
            await Verify(editor);
            Equal("explicit replacement", editor.Document.Text);
            Equal(0, editor.Folding.CollapsedRanges.Count);
        });
    }

#if WINDOWS
    [DynamicWindowsRuntimeCast(typeof(RichEditBox))]
#endif
    private static Rect FoldingCaretRect(RichEditor editor, int offset)
    {
#if WINDOWS
        var native = (Microsoft.UI.Xaml.Controls.RichEditBox)editor.Handler!.PlatformView!;
        native.Document.GetRange(offset, offset).GetRect(Microsoft.UI.Text.PointOptions.ClientCoordinates | Microsoft.UI.Text.PointOptions.AllowOffClient, out var rect, out _);
        return new(rect.X, rect.Y, rect.Width, rect.Height);
#elif ANDROID
        var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
        var layout = native.Layout!;
        var line = layout.GetLineForOffset(offset);
        return new(layout.GetPrimaryHorizontal(offset), layout.GetLineTop(line), 1, layout.GetLineBottom(line) - layout.GetLineTop(line));
#else
        var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
        using var position = native.GetPosition(native.BeginningOfDocument, offset)!;
        var rect = native.GetCaretRectForPosition(position);
        return new(rect.X, rect.Y, rect.Width, rect.Height);
#endif
    }
}
