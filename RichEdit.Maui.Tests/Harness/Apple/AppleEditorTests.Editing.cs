#if (DEBUG || EDITOR_TESTS) && (IOS || MACCATALYST)
using Foundation;
using RichEdit.Core;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace RichEdit.Maui.Tests;

internal static partial class AppleEditorTests
{
    private static async Task VerifyReferenceTableSelection(NativeEditorView editor)
    {
        using var input = typeof(AppleEditorTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var previous = editor.Document;
        try
        {
            var document = new RichDocument(imported.Snapshot);
            editor.BindStructuredDocument(document);
            editor.Focus();
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            var before = native.TextStorage.Value ?? throw new InvalidOperationException("Native reference text is missing.");
            var table = document.CurrentSnapshot.Nodes.Values.OfType<RichTable>().First();
            foreach (var (anchorRow, anchorColumn) in new[] { (1, 0), (1, 4), (5, 0), (5, 4) })
            {
                var anchor = before.IndexOf($"{anchorRow}.{anchorColumn + 1} item", StringComparison.Ordinal) + 1;
                var rowStep = anchorRow == 1 ? 1 : -1;
                var columnStep = anchorColumn == 0 ? 1 : -1;
                foreach (var (rowDistance, columnDistance) in new[] { (0, 1), (1, 1), (2, 2), (4, 4), (1, 2), (1, 1), (0, 1), (0, 0) })
                {
                    var row = anchorRow + rowStep * rowDistance;
                    var column = anchorColumn + columnStep * columnDistance;
                    var active = before.IndexOf($"{row}.{column + 1} item", StringComparison.Ordinal) + 2;
                    var range = new NSRange(Math.Min(anchor, active), Math.Abs(active - anchor));
                    native.SelectedRange = range;
                    await Task.Delay(30);
                    EditorContractTests.Equal(range, native.SelectedRange, "native drag endpoints retained");
                    EditorContractTests.Equal(before, native.TextStorage.Value, "selection does not rewrite native text");
                    EditorContractTests.Equal(false, document.CanUndo, "selection creates no edit");
                    if (rowDistance == 0 && columnDistance == 0)
                        EditorContractTests.Equal(true, editor.StructuredSession!.Selection is RichEdit.Core.RichTextSelection, "range inside one cell remains text");
                    else
                        EditorContractTests.Equal<RichSelection>(new RichCellSelection(table.Id, Math.Min(row, anchorRow), Math.Min(column, anchorColumn), rowDistance + 1, columnDistance + 1),
                            editor.StructuredSession!.Selection, "native cell rectangle");
                }
            }
        }
        finally { editor.Document = previous; }
    }

    private static async Task VerifyReferenceBulletDeletion(NativeEditorView editor)
    {
        using var input = typeof(AppleEditorTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var previous = editor.Document;
        try
        {
            var document = new RichDocument(imported.Snapshot);
            editor.BindStructuredDocument(document);
            var snapshot = document.CurrentSnapshot;
            var list = snapshot.Nodes.Values.OfType<RichList>().First(item => !item.Numbered);
            var paragraph = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
            var start = snapshot.ProjectText().GetOffset(new(snapshot.Body, paragraph, 0));
            var length = snapshot.GetParagraphLength(paragraph);
            editor.SelectedRange = new(start + length, 0);
            editor.ScrollIntoView(editor.SelectedRange);
            await Task.Delay(150);
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
            for (var index = 0; index < length; index++) native.DeleteBackward();
            await Task.Delay(80);
            EditorContractTests.Equal(0, document.CurrentSnapshot.GetParagraphLength(paragraph), "item text was deleted");
            native.DeleteBackward();
            await Task.Delay(80);
            EditorContractTests.Equal(snapshot.Body, document.CurrentSnapshot.Get<RichParagraph>(paragraph).Parent!.Value, "empty first item exits the list");
            var beforeJoin = document.CurrentSnapshot.ProjectText().Text.ToString();
            var joinAt = editor.SelectedRange.Start;
            native.DeleteBackward();
            await Task.Delay(80);
            EditorContractTests.Equal(beforeJoin.Remove(joinAt - 1, 1), document.CurrentSnapshot.ProjectText().Text.ToString(), "Backspace removes the remaining empty paragraph");
            for (var index = 0; index < length + 2 && document.CanUndo; index++) editor.Undo();
            EditorContractTests.Equal(snapshot.ProjectText().Text.ToString(), document.CurrentSnapshot.ProjectText().Text.ToString(), "undo restores the original reference");
            EditorContractTests.Equal(list.Items.Count, document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count, "undo restores the list");
            editor.SelectedRange = new(start, length);
            await Task.Delay(60);
            native.DeleteBackward();
            await Task.Delay(80);
            EditorContractTests.Equal(snapshot.ProjectText().Text.ToString().Remove(start - 1, length + 1), document.CurrentSnapshot.ProjectText().Text.ToString(), "native smart deletion can remove the selected item's leading paragraph break");
            EditorContractTests.Equal(list.Items.Count - 1, document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count, "selected item deletion retains the following list");
            editor.Undo();
            EditorContractTests.Equal(snapshot.ProjectText().Text.ToString(), document.CurrentSnapshot.ProjectText().Text.ToString(), "undo restores the selected item");
        }
        finally { editor.Document = previous; }
    }

    private static async Task VerifyNumberedReferenceEnter(NativeEditorView editor, bool withInputRule = false)
    {
        using var input = typeof(AppleEditorTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var previous = editor.Document;
        using var rule = withInputRule ? editor.InputRules.Register(static _ => { }) : null;
        try
        {
            var document = new RichDocument(imported.Snapshot);
            editor.BindStructuredDocument(document);
            var snapshot = document.CurrentSnapshot;
            var list = snapshot.Nodes.Values.OfType<RichList>().First(item => item.Numbered);
            var paragraph = snapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
            var point = new RichTextPoint(snapshot.Body, paragraph, snapshot.GetParagraphLength(paragraph));
            editor.SelectedRange = new(snapshot.ProjectText().GetOffset(point), 0);
            editor.ScrollIntoView(editor.SelectedRange);
            await Task.Delay(150);
            var handler = (RichEditorHandler)editor.Handler!;
            var native = handler.PlatformView;
            using var caret = native.GetDisplayPosition(native.BeginningOfDocument, (nint)native.SelectedRange.Location)!;
            PositionReferenceCaret(native, caret);
            await Task.Delay(100);
            var offset = native.ContentOffset.Y;
            native.InsertText("\n");
            await Task.Delay(80);
            EditorContractTests.Equal(true, Math.Abs(native.ContentOffset.Y - offset) < 2, $"numbered Enter keeps the viewport stable: before={offset}, after={native.ContentOffset.Y}, content={native.ContentSize.Height}, viewport={native.Bounds.Height}");
            var continued = document.CurrentSnapshot.Get<RichList>(list.Id);
            EditorContractTests.Equal(list.Items.Count + 1, continued.Items.Count, "Return at item end creates another numbered item");
            EditorContractTests.Equal(list.NumberStyle, continued.NumberStyle, "numbering style retained");
            native.InsertText("new numbered item");
            await Task.Delay(80);
            var nextParagraph = document.CurrentSnapshot.Get<RichListItem>(continued.Items[1]).Blocks[0];
            EditorContractTests.Equal("new numbered item", document.CurrentSnapshot.ProjectText(nextParagraph).Text.ToString(), "typing stays in the new item");
            handler.UpdateNativeListMarkers();
            EditorContractTests.Equal(true, native.Subviews.OfType<NativeListMarkerLabel>().Any(label => label.Text == "II."), "next Roman number is visible");
            editor.Undo();
            editor.Undo();
            EditorContractTests.Equal(snapshot.ProjectText().Text.ToString(), document.CurrentSnapshot.ProjectText().Text.ToString(), "undo restores the reference text");
            EditorContractTests.Equal(list.Items.Count, document.CurrentSnapshot.Get<RichList>(list.Id).Items.Count, "undo restores list membership");
        }
        finally { editor.Document = previous; }
    }

    private static async Task VerifyReferenceEditing(NativeEditorView editor)
    {
        using var input = typeof(AppleEditorTests).Assembly.GetManifestResourceStream("ReferenceDocument")!;
        var imported = await RichEdit.Core.Rtf.RtfCodec.ReadAsync(input);
        var previous = editor.Document;
        try
        {
            editor.BindStructuredDocument(new RichDocument(imported.Snapshot));
            var handler = (RichEditorHandler)editor.Handler!;
            var native = handler.PlatformView;
            var position = editor.Document.Text.IndexOf("The text you are reading is aligned left", StringComparison.Ordinal) + 12;
            editor.SelectedRange = new(position, 0);
            editor.ScrollIntoView(editor.SelectedRange);
            await Task.Delay(200);
            var before = editor.Document.Text;
            foreach (var character in "abc") native.InsertText(character.ToString());
            await Task.Delay(100);
            EditorContractTests.Equal(before.Insert(position, "abc"), editor.Document.Text, "native typing retained");
            native.SetMarkedText("か", new(1, 0));
            await Task.Delay(30);
            native.SetMarkedText("かな", new(2, 0));
            await Task.Delay(30);
            native.SetMarkedText("仮名", new(2, 0));
            native.UnmarkText();
            await Task.Delay(100);
            EditorContractTests.Equal(before.Insert(position, "abc仮名"), editor.Document.Text, "IME candidate committed once");
            editor.Undo();
            await Task.Delay(60);
            EditorContractTests.Equal(before.Insert(position, "abc"), editor.Document.Text, "IME undo is one edit");
            native.SetMarkedText("かな", new(2, 0));
            native.InsertText("仮名");
            native.UnmarkText();
            await Task.Delay(100);
            EditorContractTests.Equal(before.Insert(position, "abc仮名"), editor.Document.Text, "IME replacement commit retained");

            var model = editor.StructuredSession!.Document;
            var list = model.CurrentSnapshot.Nodes.Values.OfType<RichList>().First(item => !item.Numbered);
            var paragraph = model.CurrentSnapshot.Get<RichListItem>(list.Items[0]).Blocks[0];
            var projected = model.CurrentSnapshot.ProjectText();
            var point = new RichTextPoint(model.CurrentSnapshot.Body, paragraph, 0);
            editor.SelectedRange = new(projected.GetOffset(point), 0);
            editor.ScrollIntoView(editor.SelectedRange);
            await Task.Delay(150);
            handler.UpdateNativeListMarkers();
            var marker = native.Subviews.OfType<NativeListMarkerLabel>().First(label => label.Text == "•");
            var frame = marker.Frame;
            native.InsertText("XYZ");
            await Task.Delay(80);
            handler.UpdateNativeListMarkers();
            marker = native.Subviews.OfType<NativeListMarkerLabel>().First(label => label.Text == "•");
            EditorContractTests.Equal(frame.X, marker.Frame.X, "typing does not move the bullet");
            using var caretPosition = native.GetDisplayPosition(native.BeginningOfDocument, (nint)native.SelectedRange.Location)!;
            PositionReferenceCaret(native, caretPosition);
            await Task.Delay(100);
            var beforeEnter = native.ContentOffset.Y;
            native.InsertText("\n");
            await Task.Delay(80);
            EditorContractTests.Equal(true, Math.Abs(native.ContentOffset.Y - beforeEnter) < 2, $"list Enter keeps the visible caret in place: before={beforeEnter}, after={native.ContentOffset.Y}, content={native.ContentSize.Height}, viewport={native.Bounds.Height}");
            native.InsertText("new item");
            await Task.Delay(80);
            var updated = model.CurrentSnapshot.Get<RichList>(list.Id);
            EditorContractTests.Equal(list.Items.Count + 1, updated.Items.Count, "Enter creates another list item");
            EditorContractTests.Equal(list.NumberStyle, updated.NumberStyle, "Enter retains the list style");
            handler.UpdateNativeListMarkers();
            EditorContractTests.Equal(true, native.Subviews.OfType<NativeListMarkerLabel>().Count(label => label.Text == "•") >= 2, "both bullets remain visible");
            editor.BindStructuredDocument(new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "one");
                edit.WrapInList(edit.Body, 0, 1);
            })));
            editor.SelectedRange = new(3, 0);
            await Task.Delay(60);
            native.InsertText("\n");
            await Task.Delay(80);
            handler.UpdateNativeListMarkers();
            EditorContractTests.Equal(2, native.Subviews.OfType<NativeListMarkerLabel>().Count(label => label.Text == "•"), "empty final item has a bullet before typing");
            native.InsertText("two");
            await Task.Delay(80);
            handler.UpdateNativeListMarkers();
            EditorContractTests.Equal(2, native.Subviews.OfType<NativeListMarkerLabel>().Count(label => label.Text == "•"), "typing retains the final bullet");
        }
        finally { editor.Document = previous; }
    }

    private static void PositionReferenceCaret(RichTextView native, UITextPosition caret)
    {
        // Start within the native scroll range. A short desktop document cannot
        // place its last paragraphs 100 points from the top without overscrolling.
        double minimum = -native.AdjustedContentInset.Top;
        double maximum = Math.Max(minimum, native.ContentSize.Height - native.Bounds.Height + native.AdjustedContentInset.Bottom);
        native.SetContentOffset(new(0, Math.Clamp(native.GetDisplayCaret(caret).Y - 100, minimum, maximum)), false);
    }
}
#endif
