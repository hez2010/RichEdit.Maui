using RichEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> StructuredBindingCases()
    {
#if ANDROID || IOS || MACCATALYST
        yield return new("structured binding native composition keeps the rich root provisional", async editor =>
        {
            var previous = editor.Document;
            var model = RichDocument.FromPlainText("aa bb");
            try
            {
                editor.BindStructuredDocument(model);
                editor.Selections = new((TextSelection[])[new(2, 2), new(5, 5)], 1);
                using var peer = new SharedEditor(editor);
                peer.Editor.BindStructuredDocument(model);
                editor.Focus();
                await Task.Delay(100);
                var before = model.CurrentSnapshot;
                var events = 0;
                model.Changed += (_, _) => events++;
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
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal("aa bb仮名", editor.StructuredSession!.Composition!.Snapshot.ProjectText().Text.ToString());
                Equal("aa bb", NativeText(peer.Editor));
                Equal(0, events);
                var rejected = false;
                try { model.CaptureSave(); } catch (InvalidOperationException) { rejected = true; }
                Equal(true, rejected);
                Equal(TextEditRejection.CompositionActive, model.TryEdit(model.Revision, edit => edit.InsertParagraph(edit.Body, 0, "blocked")).Rejection);
#if ANDROID
                connection.FinishComposingText();
#else
                native.UnmarkText();
#endif
                await Verify(editor);
                Equal("aa仮名 bb仮名", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(1, events);
                Equal(editor.Document.Text, NativeText(peer.Editor));
                peer.Editor.Undo();
                await Verify(editor);
                Equal("aa bb", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(2, peer.Editor.Selections.Count);
            }
            finally { editor.DiscardComposition(); editor.Document = previous; }
        });
#endif
        yield return new("structured binding native carets share rich history and preserve styles", async editor =>
        {
            var previous = editor.Document;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                edit.SetStyles(new((RichStyleDefinition[])[new(new(1), "Strong", RichStyleKind.Paragraph) { Character = new() { Bold = true } }]));
                edit.SetParagraphStyle(new(2), new(1));
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "aa bb");
            }));
            try
            {
                editor.BindStructuredDocument(model);
                editor.Selections = new((TextSelection[])[new(1, 1), new(4, 4)], 1);
                var states = new List<string>();
                model.Changed += (_, _) => states.Add(model.CurrentSnapshot.ProjectText().Text.ToString());
                NativeMultiInsert(editor, "X");
                await Verify(editor);
                Equal("aXa bXb", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(1, states.Count);
                Equal(true, model.CurrentSnapshot.Nodes.Values.OfType<RichText>().All(run => run.Format == new RichCharacterStyle()));
                editor.Undo();
                await Verify(editor);
                Equal("aa bb", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal("1,4", string.Join(',', editor.Selections.Select(item => item.Active)));
                editor.Redo();
                await Verify(editor);
                Equal("aXa bXb", model.CurrentSnapshot.ProjectText().Text.ToString());
                var capture = model.CaptureSave();
                using var stream = new MemoryStream();
                await RichEdit.Core.Rtf.RtfCodec.WriteAsync(capture.Snapshot, stream);
                model.MarkSaved(capture);
                Equal(false, editor.Document.IsModified);
            }
            finally { editor.Document = previous; }
        });
    }
}
