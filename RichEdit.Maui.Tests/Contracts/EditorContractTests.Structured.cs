using RichEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> StructuredBindingCases()
    {
#if WINDOWS || IOS || MACCATALYST
        yield return new("structured native table editing retains rows and cell selections", async editor =>
        {
            var previous = editor.Document;
            RichNodeId table = default, beta = default;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1440), new(1440)]);
                var snapshot = edit.Snapshot;
                var grid = snapshot.GetTableGrid(table);
                var labels = new[] { "alpha", "beta", "gamma", "delta" };
                for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 2; column++)
                    {
                        var paragraph = snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0];
                        edit.ReplaceText(new(edit.Body, paragraph, 0), 0, labels[row * 2 + column]);
                        if (row == 0 && column == 1) beta = paragraph;
                    }
                edit.DeleteBlock(new(2));
            }));
            try
            {
                editor.BindStructuredDocument(model);
                editor.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(model.CurrentSnapshot.Body, beta, 0), new(model.CurrentSnapshot.Body, beta, 4));
                NativeMultiInsert(editor, "BETA");
                Equal("alpha\tBETA\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(2, model.CurrentSnapshot.GetTableGrid(table).Rows);
                editor.Undo();
                Equal("alpha\tbeta\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
                var before = model.CurrentSnapshot;
                editor.StructuredSession.Selection = new RichCellSelection(table, 0, 0, 1, 2);
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal(true, editor.SendKeyDown(EditorKey.Delete, EditorKeyModifiers.None));
                Equal("\t\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(2, model.CurrentSnapshot.GetTableGrid(table).Columns);
                editor.Undo();
                Equal(true, editor.StructuredSession.Selection is RichCellSelection);
                editor.StructuredSession.Selection = new RichBoundarySelection(model.CurrentSnapshot.Body, table, RichBlockEdge.After);
                NativeMultiInsert(editor, "tail");
                Equal("alpha\tbeta\ngamma\tdelta\ntail", model.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal(1, model.CurrentSnapshot.Get<RichStory>(model.CurrentSnapshot.Body).Blocks.Count);
#if IOS || MACCATALYST
                editor.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(new(model.CurrentSnapshot.Body, beta, 2), new(model.CurrentSnapshot.Body, beta, 2));
                editor.Focus();
                await Task.Delay(100);
                var committed = model.CurrentSnapshot;
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
                native.SetMarkedText("仮名", new Foundation.NSRange(1, 0));
                Equal(true, ReferenceEquals(committed, model.CurrentSnapshot));
                Equal("alpha\tbe仮名ta\ngamma\tdelta", editor.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                Equal(3, ((RichEdit.Core.RichTextSelection)editor.StructuredSession.Composition.Selection).Active.Offset);
                native.UnmarkText();
                Equal("alpha\tbe仮名ta\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal("alpha\tbeta\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
#endif
                await Task.Delay(30);
            }
            finally { editor.DiscardComposition(); editor.Document = previous; }
        });
#endif
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
