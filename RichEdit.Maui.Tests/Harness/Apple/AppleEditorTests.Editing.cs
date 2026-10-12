#if (DEBUG || EDITOR_TESTS) && (IOS || MACCATALYST)
using Foundation;
using RichEdit.Core;
using RichEdit.Maui.Platforms.Apple;
using UIKit;

namespace RichEdit.Maui.Tests;

internal static partial class AppleEditorTests
{
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
            native.SetContentOffset(new(0, Math.Max(0, native.GetDisplayCaret(caretPosition).Y - 100)), false);
            await Task.Delay(100);
            var beforeEnter = native.ContentOffset.Y;
            native.InsertText("\n");
            await Task.Delay(80);
            EditorContractTests.Equal(true, Math.Abs(native.ContentOffset.Y - beforeEnter) < 2, "list Enter keeps the visible caret in place");
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
}
#endif
