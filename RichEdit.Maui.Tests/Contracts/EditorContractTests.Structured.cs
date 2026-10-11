using RichEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> StructuredBindingCases()
    {
        yield return new("structured shading retains independent colors through native input", async editor =>
        {
            var previous = editor.Document;
            var character = new RichCharacterStyle { Shading = new RichShading(3333, new(200, 30, 20), new(40, 50, 210)), Background = new RichColor(30, 200, 30) };
            var paragraphFormat = new RichParagraphStyle { Shading = new RichShading(5000, new(180, 20, 10), new(230, 240, 255)), Background = new RichColor(230, 200, 30) };
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "shaded");
                edit.SetParagraphCharacterFormat(new(2), character);
                edit.SetParagraphFormat(new(2), paragraphFormat);
                // Exercise the standard native RTF import path as well as spans.
                edit.InsertField(new(edit.Body, new(2), 6), "DATE", "today");
            }));
            try
            {
                editor.BindStructuredDocument(model);
                await Task.Delay(60);
                var point = new RichTextPoint(model.CurrentSnapshot.Body, new(2), 3);
                editor.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(point, point);
                NativeMultiInsert(editor, "!");
                await Task.Delay(30);
                Equal("sha!ded\uFFFC", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(paragraphFormat, model.CurrentSnapshot.Get<RichParagraph>(new(2)).Format);
                Equal(character, model.CurrentSnapshot.Get<RichParagraph>(new(2)).Character);
                editor.Undo();
                Equal("shaded\uFFFC", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(false, model.CanUndo);
            }
            finally { editor.Document = previous; }
        });
        yield return new("structured paragraph tabs use native geometry and persist with hyphenation", async editor =>
        {
            var previous = editor.Document;
            var model = new RichDocument(RichDocumentSnapshot.FromPlainText("\tvalue"));
            try
            {
                editor.BindStructuredDocument(model);
                var paragraph = model.CurrentSnapshot.Get<RichStory>(model.CurrentSnapshot.Body).Blocks[0];
                var handler = (RichEditorHandler)editor.Handler!;
                var stops = new RichTabStops(new[] { new RichTabStop(RichLength.FromPoints(72)) });
                model.Edit(edit => edit.SetParagraphFormat(paragraph, new() { TabStops = stops, Hyphenation = true }));
                await Task.Delay(80);
                var first = handler.GetTextCaretBounds(1, RichTextCaretAffinity.Downstream)!.Value.X;
                model.Edit(edit => edit.SetParagraphFormat(paragraph, new() { TabStops = new RichTabStops(new[] { new RichTabStop(RichLength.FromPoints(144)) }), Hyphenation = true }));
                await Task.Delay(80);
                var second = handler.GetTextCaretBounds(1, RichTextCaretAffinity.Downstream)!.Value.X;
                var detail = $"model {string.Join(',', model.CurrentSnapshot.ResolveParagraph(paragraph).TabStops.Select(tab => tab.Position.Points))}; " +
                    $"projection {string.Join(',', editor.Document.CurrentSnapshot.Paragraphs[0].Format.TabStops.Select(tab => tab.Position))}; typing {string.Join(',', editor.TypingParagraphFormat.TabStops.Select(tab => tab.Position))}";
#if WINDOWS
                var nativeTabs = handler.PlatformView.Document.GetRange(0, 0).ParagraphFormat;
                var positions = new List<float>();
                for (var index = 0; index < nativeTabs.TabCount; index++) { nativeTabs.GetTab(index, out var position, out _, out _); positions.Add(position); }
                detail += $"; native {string.Join(',', positions)}";
#endif
                Equal(true, second > first + 40, $"tab geometry {first} -> {second}; {detail}");
                editor.Undo();
                Equal(stops, model.CurrentSnapshot.ResolveParagraph(paragraph).TabStops);
                Equal(true, model.CurrentSnapshot.ResolveParagraph(paragraph).Hyphenation);
                editor.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(model.CurrentSnapshot.Body, paragraph, 6), new(model.CurrentSnapshot.Body, paragraph, 6));
                NativeMultiInsert(editor, "!");
                Equal(stops, model.CurrentSnapshot.ResolveParagraph(paragraph).TabStops);
                editor.Undo(); editor.Undo();
                Equal(false, model.CanUndo);
            }
            finally { editor.Document = previous; }
        });
        yield return new("structured line spacing reaches native layout and survives input and undo", async editor =>
        {
            var previous = editor.Document;
            var model = new RichDocument(RichDocumentSnapshot.FromPlainText("first\u2028second"));
            try
            {
                editor.BindStructuredDocument(model);
                var paragraph = model.CurrentSnapshot.Get<RichStory>(model.CurrentSnapshot.Body).Blocks[0];
                var handler = (RichEditorHandler)editor.Handler!;
                await Task.Delay(50);
                double Height() => handler.GetTextCaretBounds(6, RichTextCaretAffinity.Downstream)!.Value.Y -
                    handler.GetTextCaretBounds(0, RichTextCaretAffinity.Downstream)!.Value.Y;
                var automatic = Height();
                var spacing = RichLineSpacing.Exactly(RichLength.FromPoints(40));
                editor.StructuredSession!.Edit(edit => edit.SetParagraphFormat(paragraph, new() { LineSpacing = spacing, Alignment = RichAlignment.Distributed }));
                await Task.Delay(80);
                Equal(true, Height() > automatic + 10, $"native line height {automatic} -> {Height()}");
                var point = new RichTextPoint(model.CurrentSnapshot.Body, paragraph, 5);
                editor.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(point, point);
                NativeMultiInsert(editor, "!");
                Equal(spacing, model.CurrentSnapshot.ResolveParagraph(paragraph).LineSpacing);
                Equal(RichAlignment.Distributed, model.CurrentSnapshot.ResolveParagraph(paragraph).Alignment);
                editor.Undo(); editor.Undo();
                await Task.Delay(60);
                Equal(true, Math.Abs(Height() - automatic) < 2, "line height restores on undo");
                Equal(false, model.CanUndo);
            }
            finally { editor.Document = previous; }
        });
        yield return new("structured table Tab traverses merged cells and appends one undoable row", async editor =>
        {
            var previous = editor.Document;
            RichNodeId table = default;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(1200), new(1200)]);
                edit.MergeCells(new(table, 0, 0, 1, 2));
            }));
            try
            {
                editor.BindStructuredDocument(model);
                var grid = model.CurrentSnapshot.GetTableGrid(table);
                var point = new RichTextPoint(model.CurrentSnapshot.Body, model.CurrentSnapshot.Get<RichTableCell>(grid[0, 0]).Blocks[0], 0);
                editor.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(point, point);
                Equal(true, editor.SendKeyDown(EditorKey.Tab, EditorKeyModifiers.None));
                Equal(model.CurrentSnapshot.Get<RichTableCell>(grid[1, 0]).Blocks[0], ((RichEdit.Core.RichTextSelection)editor.StructuredSession.Selection).Active.Paragraph);
                Equal(false, model.CanUndo);
                Equal(true, editor.SendKeyDown(EditorKey.Tab, EditorKeyModifiers.Shift));
                Equal(point, ((RichEdit.Core.RichTextSelection)editor.StructuredSession.Selection).Active);
                editor.MoveToAdjacentCell(true, false); editor.MoveToAdjacentCell(true, false);
                Equal(false, editor.MoveToAdjacentCell(true, false));
                Equal(true, editor.SendKeyDown(EditorKey.Tab, EditorKeyModifiers.None));
                Equal(3, model.CurrentSnapshot.GetTableGrid(table).Rows);
                editor.Undo();
                Equal(2, model.CurrentSnapshot.GetTableGrid(table).Rows);
                Equal(false, model.CanUndo);
                editor.IsReadOnly = true;
                Equal(false, editor.MoveToAdjacentCell(true, true));
                Equal(2, model.CurrentSnapshot.GetTableGrid(table).Rows);
                await Task.Delay(20);
            }
            finally { editor.IsReadOnly = false; editor.Document = previous; }
        });
        yield return new("structured list native Enter splits items and empty Enter leaves the list", async editor =>
        {
            var previous = editor.Document;
            RichNodeId listId = default;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                edit.ReplaceText(new(edit.Body, new(2), 0), 0, "ab");
                listId = edit.WrapInList(edit.Body, 0, 1, numbered: true, startNumber: 4);
            }));
            try
            {
                editor.BindStructuredDocument(model);
                editor.StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(new(model.CurrentSnapshot.Body, new(2), 1), new(model.CurrentSnapshot.Body, new(2), 1));
                Equal(true, editor.SendKeyDown(EditorKey.Enter, EditorKeyModifiers.Shift));
                Equal("a\u2028b", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(1, model.CurrentSnapshot.Get<RichList>(listId).Items.Count);
                editor.Undo();
                NativeMultiInsert(editor, "\n");
                await Task.Delay(40);
                Equal(2, model.CurrentSnapshot.Get<RichList>(listId).Items.Count);
                Equal("a\nb", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(true, editor.SendKeyDown(EditorKey.Backspace, EditorKeyModifiers.None));
                Equal(1, model.CurrentSnapshot.Get<RichList>(listId).Items.Count);
                Equal("ab", model.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                NativeMultiInsert(editor, "\n");
                await Task.Delay(40);
                var snapshot = model.CurrentSnapshot;
                var list = snapshot.Get<RichList>(listId);
                Equal(3, list.Items.Count, $"{snapshot.ProjectText().Text}; selection {editor.StructuredSession.Selection}; " +
                    string.Join("|", list.Items.Select(id => $"{id.Value}:[{string.Join(',', snapshot.Get<RichListItem>(id).Blocks.Select(block => block.Value))}]")));
                var empty = snapshot.Get<RichListItem>(list.Items[1]).Blocks[0];
                editor.StructuredSession.Selection = new RichEdit.Core.RichTextSelection(new(snapshot.Body, empty, 0), new(snapshot.Body, empty, 0));
                NativeMultiInsert(editor, "\n");
                await Task.Delay(40);
                var body = model.CurrentSnapshot.Get<RichStory>(snapshot.Body);
                Equal(3, body.Blocks.Count);
                Equal(empty, body.Blocks[1]);
                Equal(6, model.CurrentSnapshot.Get<RichList>(body.Blocks[2]).StartNumber);
                Equal("a\n\nb", model.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal(3, model.CurrentSnapshot.Get<RichList>(listId).Items.Count);
                editor.Undo(); editor.Undo();
                Equal("ab", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(false, model.CanUndo);
            }
            finally { editor.Document = previous; }
        });
#if ANDROID || IOS || MACCATALYST
        yield return new("structured coordinated cell carets share one native composition and commit", async editor =>
        {
            var previous = editor.Document;
            RichNodeId left = default, right = default, leftParagraph = default, rightParagraph = default;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                var table = edit.InsertTable(edit.Body, 1, 1, (RichLength[])[new(1440), new(1440)]);
                var snapshot = edit.Snapshot;
                var grid = snapshot.GetTableGrid(table);
                left = grid[0, 0]; right = grid[0, 1];
                leftParagraph = snapshot.Get<RichTableCell>(left).Blocks[0];
                rightParagraph = snapshot.Get<RichTableCell>(right).Blocks[0];
                edit.ReplaceText(new(edit.Body, leftParagraph, 0), 0, "aa");
                edit.ReplaceText(new(edit.Body, rightParagraph, 0), 0, "bb");
            }));
            var coordinator = new NativeEditorView();
            coordinator.BindStructuredDocument(model);
            try
            {
                editor.BindStructuredDocument(model, right, coordinator);
                using var peer = new SharedEditor(editor);
                peer.Editor.BindStructuredDocument(model, left, coordinator);
                Equal(true, ReferenceEquals(editor.StructuredSession, coordinator.StructuredSession));
                Equal(true, ReferenceEquals(peer.Editor.StructuredSession, coordinator.StructuredSession));
                var set = new RichTextSelectionSet(model.CurrentSnapshot, (RichTextSelectionItem[])[
                    new(new(new(model.CurrentSnapshot.Body, leftParagraph, 2), new(model.CurrentSnapshot.Body, leftParagraph, 2))),
                    new(new(new(model.CurrentSnapshot.Body, rightParagraph, 2), new(model.CurrentSnapshot.Body, rightParagraph, 2)))], 1);
                coordinator.StructuredSession!.Selection = set;
                editor.Focus();
                await Task.Delay(100);
                var before = model.CurrentSnapshot;
                coordinator.MaxLength = before.ProjectText().Text.Length + 1;
                NativeMultiInsert(editor, "!");
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal("aa", NativeText(peer.Editor));
                Equal("bb", NativeText(editor));
                coordinator.MaxLength = -1;
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
                using var info = new Android.Views.InputMethods.EditorInfo();
                using var connection = native.OnCreateInputConnection(info)!;
                using var marked = new Java.Lang.String("仮名");
                connection.SetComposingText(marked, 1);
#else
                native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
#endif
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal(true, coordinator.Composition.IsActive);
                Equal("aa", NativeText(peer.Editor));
                Equal("bb仮名", coordinator.StructuredSession.Composition!.Snapshot.ProjectText(right).Text.ToString());
                Equal(true, coordinator.TryCompleteComposition());
                Equal("aa仮名", NativeText(peer.Editor));
                Equal("bb仮名", NativeText(editor));
                Equal(2, ((RichTextSelectionSet)coordinator.StructuredSession.Selection).Count);
                coordinator.Undo();
                Equal("aa", NativeText(peer.Editor));
                Equal("bb", NativeText(editor));
                Equal(set, coordinator.StructuredSession.Selection);
                Equal(false, model.CanUndo);
                coordinator.SelectAll();
                before = model.CurrentSnapshot;
#if ANDROID
                using var whole = new Java.Lang.String("whole\nbody");
                connection.SetComposingText(whole, 1);
#else
                native.SetMarkedText("whole\nbody", new Foundation.NSRange(10, 0));
#endif
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal("whole\nbody", coordinator.StructuredSession.Composition!.Snapshot.ProjectText().Text.ToString());
                Equal(true, coordinator.TryCompleteComposition());
                Equal("whole\nbody", model.CurrentSnapshot.ProjectText().Text.ToString());
                Equal(0, model.CurrentSnapshot.Nodes.Values.OfType<RichTable>().Count());
                coordinator.Undo();
                Equal(true, coordinator.StructuredSession.Selection is RichBlockSelection);
                Equal("aa", NativeText(peer.Editor));
                Equal("bb", NativeText(editor));
            }
            finally { editor.DiscardComposition(); editor.Document = previous; coordinator.Document = new RichTextDocument(); }
        });

        yield return new("structured scoped cell composition uses the shared rich root and history", async editor =>
        {
            var previous = editor.Document;
            RichNodeId left = default, right = default;
            var model = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                var table = edit.InsertTable(edit.Body, 1, 1, (RichLength[])[new(1440), new(1440)]);
                var snapshot = edit.Snapshot;
                var grid = snapshot.GetTableGrid(table);
                left = grid[0, 0]; right = grid[0, 1];
                edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(left).Blocks[0], 0), 0, "left");
                edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(right).Blocks[0], 0), 0, "right");
            }));
            try
            {
                editor.BindStructuredDocument(model, left);
                using var peer = new SharedEditor(editor);
                peer.Editor.BindStructuredDocument(model, right);
                Equal("left", NativeText(editor));
                Equal("right", NativeText(peer.Editor));
                editor.SelectedRange = new(4, 0);
                editor.Focus();
                await Task.Delay(100);
                var before = model.CurrentSnapshot;
                var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
                using var info = new Android.Views.InputMethods.EditorInfo();
                using var connection = native.OnCreateInputConnection(info)!;
                using var marked = new Java.Lang.String("仮\n名");
                connection.SetComposingText(marked, 1);
#else
                native.SetMarkedText("仮\n名", new Foundation.NSRange(3, 0));
#endif
                Equal(true, ReferenceEquals(before, model.CurrentSnapshot));
                Equal("left仮\n名", editor.StructuredSession!.Composition!.Snapshot.ProjectText(left).Text.ToString());
                Equal("right", NativeText(peer.Editor));
#if ANDROID
                connection.FinishComposingText();
#else
                native.UnmarkText();
#endif
                Equal("left仮\n名", model.CurrentSnapshot.ProjectText(left).Text.ToString());
                Equal("right", model.CurrentSnapshot.ProjectText(right).Text.ToString());
                peer.Editor.Undo();
                Equal("left", NativeText(editor));
                Equal("right", NativeText(peer.Editor));
                Equal(false, model.CanUndo);
            }
            finally { editor.DiscardComposition(); editor.Document = previous; }
        });
#endif
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
                editor.SelectAll();
                Equal(true, editor.StructuredSession.Selection is RichBlockSelection);
                await editor.CutAsync();
                Equal(0, source.CurrentSnapshot.Nodes.Values.OfType<RichTable>().Count());
                Equal("", source.CurrentSnapshot.ProjectText().Text.ToString());
                editor.Undo();
                Equal(true, editor.StructuredSession.Selection is RichBlockSelection);
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
