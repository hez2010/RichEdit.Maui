using RichEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> StructuredBindingCases()
    {
        yield return new("structured clipboard retains table cells through RTF copy paste and cut", async editor =>
        {
            var previous = editor.Document;
            RichNodeId table = default;
            var source = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1440), new(1440)]);
                var paragraph = edit.Snapshot.Get<RichTableCell>(edit.Snapshot.GetTableGrid(table)[0, 0]).Blocks[0];
                edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "copy 日本語");
                edit.ApplyCharacterFormat(new RichCellSelection(table, 0, 0, 2, 2), new() { Bold = true });
                edit.DeleteBlock(new(2));
            }));
            try
            {
                editor.BindStructuredDocument(source);
                var cells = new RichCellSelection(table, 0, 0, 2, 2);
                editor.StructuredSession!.Selection = cells;
                await editor.CopyAsync();
                var destination = new RichDocument();
                editor.BindStructuredDocument(destination);
                await editor.PasteAsync();
                var copiedTable = destination.CurrentSnapshot.Nodes.Values.OfType<RichTable>().Single();
                Equal(2, destination.CurrentSnapshot.GetTableGrid(copiedTable.Id).Rows);
                Equal(1, destination.CurrentSnapshot.Get<RichStory>(destination.CurrentSnapshot.Body).Blocks.Count);
                var copied = destination.CurrentSnapshot.Nodes.Values.OfType<RichText>().Single();
                Equal("copy 日本語", copied.Content.ToString());
                Equal(true, destination.CurrentSnapshot.ResolveCharacter(copied.Id).Bold);
                editor.Undo();
                Equal(false, destination.CanUndo);
                editor.BindStructuredDocument(source);
                editor.StructuredSession!.Selection = cells;
                await editor.CutAsync();
                Equal("\t\n\t", source.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal(cells, editor.StructuredSession.Selection);
            }
            finally { editor.Document = previous; }
        });
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
                editor.StructuredSession.Selection = new RichCellSelection(table, 0, 0, 2, 1);
                editor.Selection.ToggleBold();
                Equal(true, editor.Selection.CharacterFormat.Bold);
                Equal(true, editor.StructuredSession.Selection is RichCellSelection);
                foreach (var run in model.CurrentSnapshot.Nodes.Values.OfType<RichText>())
                    Equal(run.Content.ToString() is "alpha" or "gamma", model.CurrentSnapshot.ResolveCharacter(run.Id).Bold);
                editor.Undo();
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
                editor.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(new(model.CurrentSnapshot.Body, beta, 1), new(model.CurrentSnapshot.Body, beta, 3));
                var formattingNative = ((RichEditorHandler)editor.Handler!).PlatformView;
                formattingNative.TextStorage.AddAttribute(UIKit.UIStringAttributeKey.Font,
                    UIKit.UIFont.ItalicSystemFontOfSize(formattingNative.Font?.PointSize ?? 18)!, formattingNative.SelectedRange);
                await Task.Delay(30);
                var italic = model.CurrentSnapshot.Nodes.Values.OfType<RichText>().Where(run => model.CurrentSnapshot.ResolveCharacter(run.Id).Italic).ToArray();
                Equal(1, italic.Length);
                Equal("et", italic[0].Content.ToString());
                editor.Undo();
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
                var cells = new RichCellSelection(table, 0, 0, 1, 2);
                editor.StructuredSession.Selection = cells;
                committed = model.CurrentSnapshot;
                native.SetMarkedText("仮名", new Foundation.NSRange(1, 0));
                Equal(true, ReferenceEquals(committed, model.CurrentSnapshot));
                Equal("仮名\t\ngamma\tdelta", editor.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                Equal(1, ((RichEdit.Core.RichTextSelection)editor.StructuredSession.Composition.Selection).Active.Offset);
                native.UnmarkText();
                Equal("仮名\t\ngamma\tdelta", model.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal(cells, editor.StructuredSession.Selection);
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
