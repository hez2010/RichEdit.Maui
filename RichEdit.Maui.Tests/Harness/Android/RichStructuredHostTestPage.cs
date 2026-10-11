#if (DEBUG || EDITOR_TESTS) && ANDROID
using RichEdit.Core;
using NativeGrid = Android.Widget.GridLayout;

namespace RichEdit.Maui.Tests;

internal sealed class RichStructuredHostTestPage : ContentPage
{
    private readonly RichEditor _editor = new() { FontSize = 16 };
    private RichNodeId _table;
    private bool _started;
    internal RichStructuredHostTestPage()
    {
        Content = _editor;
        SafeAreaEdges = new(SafeAreaRegions.Container);
        _editor.Loaded += async (_, _) => { if (!_started) { _started = true; await RunAsync(); } };
    }
    private async Task RunAsync()
    {
        var report = Path.Combine(EditorTestOutput.Directory, "rich-structure-tests.txt");
        var results = new List<string>();
        async Task Test(string name, Func<Task> action)
        {
            results.Add("RUN " + name); File.WriteAllLines(report, results);
            try { Reset(); await Ready(); await action(); results[^1] = "PASS " + name; }
            catch (Exception error) { results[^1] = $"FAIL {name}: {error}"; }
            File.WriteAllLines(report, results);
        }
        await Test("column resize preserves cells and reflows native block widgets", async () =>
        {
            var cell = Cell(0, 0);
            var paragraph = _editor.Document.CurrentSnapshot.Get<RichTableCell>(cell).Blocks[0];
            var point = new RichTextPoint(_editor.Document.CurrentSnapshot.Body, paragraph, 0);
            _editor.Selection = new RichEdit.Core.RichTextSelection(point, point);
            Label? label = null;
            using var widgets = _editor.Presentation.CreateWidgetSource(_ => label = new Label
            { Text = "A cell annotation wraps and follows column resizing.", FontSize = 16 });
            Equal(true, widgets.TryPublish(widgets.BeginUpdate(), [new("cell-note", point, new(70, 30),
                Options: new() { Placement = RichTextAdornmentPlacement.AboveLine })]));
            await WaitFor(() => label is { Bounds.Width: > 0, Bounds.Height: > 30 });
            var width = label!.Bounds.Width;
            var height = label.Bounds.Height;
            var columns = _editor.Document.CurrentSnapshot.Get<RichTable>(_table).Columns;
            _editor.Edit(edit => edit.SetColumnWidths(_table, columns.SetItem(0, new(columns[0].Twips * 2))));
            await WaitFor(() => label.Bounds.Width > width && label.Bounds.Height < height);
            Equal(cell, Cell(0, 0));
            Equal(new RichEdit.Core.RichTextSelection(point, point), _editor.Selection);
            _editor.Undo();
            await WaitFor(() => Math.Abs(label.Bounds.Width - width) < 1 && label.Bounds.Height >= height - 1);
            Equal(0, _editor.Document.CurrentSnapshot.Assets.Count);

            async Task WaitFor(Func<bool> ready)
            {
                var deadline = Environment.TickCount64 + 3000;
                while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(20);
                Equal(true, ready());
            }
        });
        await Test("typed presentation routes to native cells and follows their structural identities", async () =>
        {
            var cell = Cell(0, 1);
            var paragraph = _editor.Document.CurrentSnapshot.Get<RichTableCell>(cell).Blocks[0];
            var point = new RichTextPoint(_editor.Document.CurrentSnapshot.Body, paragraph, 1);
            using var decorations = _editor.Presentation.CreateDecorationLayer();
            using var widgets = _editor.Presentation.CreateWidgetSource(_ => new Label { Text = "hint" });
            var before = _editor.Document.CurrentSnapshot;
            Equal(true, decorations.TryPublish(decorations.BeginUpdate(), [new(new(point with { Offset = 0 }, point), new() { Italic = true })]));
            Equal(true, widgets.TryPublish(widgets.BeginUpdate(), [new("cell-hint", point, new(30, 20))]));
            await Task.Delay(100);
            Equal(true, ReferenceEquals(before, _editor.Document.CurrentSnapshot));
            Equal(1, Surface(cell).Adornments.Count);
            Equal(0, Surface(Cell(0, 0)).Adornments.Count);
            Equal(true, Surface(cell).Decorations.Project(Surface(cell).Document.CurrentSnapshot).GetCharacterFormat(0).Italic);
            _editor.Edit(edit => edit.ReplaceText(point, 0, "X"));
            await Task.Delay(100);
            Equal(2, Surface(cell).Adornments.Single().Position);
            _editor.Edit(edit => edit.InsertRows(_table, 0, 1));
            await Ready();
            Equal(1, Surface(cell).Adornments.Count);
            Equal(2, Surface(cell).Adornments.Single().Position);
            Equal(true, Surface(cell).Decorations.Project(Surface(cell).Document.CurrentSnapshot).GetCharacterFormat(0).Italic);
        });
        await Test("native grid cells edit the shared structured document", async () =>
        {
            var grid = NativeViews<NativeGrid>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Single();
            Equal(2, grid.RowCount); Equal(2, grid.ColumnCount); Equal(4, grid.ChildCount);
            using var tableInfo = grid.CreateAccessibilityNodeInfo()!;
            using var collection = tableInfo.GetCollectionInfo()!;
            Equal(2, collection.RowCount); Equal(2, collection.ColumnCount);
            var right = Cell(0, 1);
            var view = Surface(right);
            var paragraph = _editor.Document.CurrentSnapshot.Get<RichTableCell>(right).Blocks[0];
            _editor.Selection = new RichEdit.Core.RichTextSelection(new(_editor.Document.CurrentSnapshot.Body, paragraph, 1), new(_editor.Document.CurrentSnapshot.Body, paragraph, 1));
            _editor.Focus(); await Task.Delay(50);
            var native = ((RichEditorHandler)view.Handler!).PlatformView;
            using var cellInfo = native.CreateAccessibilityNodeInfo()!;
            using var item = cellInfo.GetCollectionItemInfo()!;
            Equal(0, item.RowIndex); Equal(1, item.ColumnIndex); Equal(1, item.ColumnSpan);
            var capture = _editor.TextLayout.Capture() ?? throw new InvalidOperationException("Missing composite layout.");
            var point = new RichTextPoint(_editor.Document.CurrentSnapshot.Body, paragraph, 1);
            var caret = _editor.TextLayout.GetCaretBounds(capture, point) ?? throw new InvalidOperationException("Missing cell caret geometry.");
            var hit = _editor.TextLayout.HitTest(capture, new(caret.Left + 0.1, caret.Center.Y));
            Equal(point, hit?.Point ?? throw new InvalidOperationException("Missing cell hit."));
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var text = new Java.Lang.String("X");
            connection.CommitText(text, 1);
            Equal(true, _editor.TextLayout.GetCaretBounds(capture, point) is null);
            Equal("bXb", _editor.Document.CurrentSnapshot.ProjectText(right).Text.ToString());
            Equal("aa", Surface(Cell(0, 0)).Document.Text);
            _editor.Undo(); Equal("bb", view.Document.Text);
        });
        await Test("cell selection survives focus and native merge undo", async () =>
        {
            var cells = new RichCellSelection(_table, 0, 0, 1, 2);
            _editor.Selection = cells; _editor.Focus(); await Task.Delay(80);
            Equal(cells, _editor.Selection, "after focus");
            var selectedGrid = NativeViews<NativeGrid>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Single();
            var selectedBackground = (Android.Graphics.Drawables.GradientDrawable)selectedGrid.GetChildAt(0)!.Background!;
            Equal(((RichEditorHandler)Surface(Cell(0, 0)).Handler!).PlatformView.HighlightColor,
                selectedBackground.Color!.DefaultColor, "cell selection follows native highlight color");
            _editor.Edit(edit => edit.MergeCells(cells)); await Task.Delay(150);
            var grid = NativeViews<NativeGrid>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Single();
            Equal(3, grid.ChildCount);
            Equal("aa\nbb", Surface(Cell(0, 0)).Document.Text);
            using var mergedInfo = ((RichEditorHandler)Surface(Cell(0, 0)).Handler!).PlatformView.CreateAccessibilityNodeInfo()!;
            using var mergedItem = mergedInfo.GetCollectionItemInfo()!;
            Equal(2, mergedItem.ColumnSpan); Equal(true, mergedItem.IsSelected);
            _editor.Undo(); await Task.Delay(150);
            grid = NativeViews<NativeGrid>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Single();
            Equal(4, grid.ChildCount); Equal(cells, _editor.Selection, "after undo");
        });
        await Test("authored cell fill padding borders and vertical alignment use native containers", async () =>
        {
            var cell = Cell(0, 0);
            var surface = Surface(cell);
            var format = new RichCellFormat
            {
                Padding = new(new(120), new(160), new(200), new(240)), VerticalAlignment = RichCellVerticalAlignment.Bottom,
                Shading = new(10000, new(80, 120, 160)),
                Borders = new(new(RichBorderStyle.Dashed, new(40), new(200, 40, 20)), new(RichBorderStyle.Double, new(20)), null,
                    new(RichBorderStyle.Dotted, new(20))),
            };
            _editor.Edit(edit => edit.SetCellFormat(new(_table, 0, 0, 1, 1), format));
            await Task.Delay(100);
            Equal(true, ReferenceEquals(surface, Surface(cell)), "cell format keeps native editing surface");
            var grid = NativeViews<NativeGrid>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Single();
            var frame = (Android.Widget.FrameLayout)grid.GetChildAt(0)!;
            var density = frame.Resources!.DisplayMetrics!.Density;
            Equal((int)Math.Ceiling(6 * density), frame.PaddingLeft);
            Equal((int)Math.Ceiling(12 * density), frame.PaddingBottom);
            Equal(Android.Views.GravityFlags.Bottom, ((Android.Widget.FrameLayout.LayoutParams)frame.GetChildAt(0)!.LayoutParameters!).Gravity);
            Equal(new Android.Graphics.Color(80, 120, 160).ToArgb(), ((Android.Graphics.Drawables.GradientDrawable)frame.Background!).Color!.DefaultColor);
            using var bitmap = Android.Graphics.Bitmap.CreateBitmap(100, 100, Android.Graphics.Bitmap.Config.Argb8888!)!;
            using var canvas = new Android.Graphics.Canvas(bitmap);
            using var drawable = new RichCellDrawable(1);
            drawable.SetBounds(0, 0, 100, 100);
            drawable.Configure(format, null, Android.Graphics.Color.Black.ToArgb(), Android.Graphics.Color.Gray.ToArgb());
            drawable.Draw(canvas);
            Equal(new Android.Graphics.Color(80, 120, 160).ToArgb(), bitmap.GetPixel(50, 50));
            Equal(Android.Graphics.Color.Black.ToArgb(), bitmap.GetPixel(50, 0));
            _editor.Selection = new RichCellSelection(_table, 0, 0, 1, 1);
            Equal(((RichEditorHandler)surface.Handler!).PlatformView.HighlightColor, ((Android.Graphics.Drawables.GradientDrawable)frame.Background!).Color!.DefaultColor);
            _editor.Undo(); await Task.Delay(80);
            Equal(new RichCellFormat(), _editor.Document.CurrentSnapshot.Get<RichTableCell>(cell).Format);
            Equal(0, frame.PaddingLeft);
        });
        await Test("retained fields use native wrapped text and atomic rich addresses", async () =>
        {
            var cell = Cell(0, 1);
            var paragraph = _editor.Document.CurrentSnapshot.Get<RichTableCell>(cell).Blocks[0];
            RichNodeId field = default;
            _editor.Edit(edit => field = edit.InsertField(new(edit.Body, paragraph, 2), "DATE", "October 11"));
            await Task.Delay(80);
            var surface = Surface(cell);
            var native = ((RichEditorHandler)surface.Handler!).PlatformView;
            Equal("bbOctober 11", native.Text);
            Equal("bb\uFFFC", _editor.Document.CurrentSnapshot.ProjectText(cell).Text.ToString());
            native.SetSelection(5);
            Equal(new RichObjectSelection(field), _editor.Selection);
            _editor.Selection = new RichEdit.Core.RichTextSelection(new(_editor.Document.CurrentSnapshot.Body, paragraph, 3), new(_editor.Document.CurrentSnapshot.Body, paragraph, 3));
            Equal(12, native.SelectionStart, $"field selection before focus; local {surface.SelectionState}; mapped {((RichEditorHandler)surface.Handler!).NativeProjection.ToDisplay(3)}");
            _editor.Focus(); await Task.Delay(30);
            Equal(12, native.SelectionStart, $"field selection after focus; local {surface.SelectionState}; core {_editor.Selection}");
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var value = new Java.Lang.String("!");
            connection.CommitText(value, 1);
            Equal("bb\uFFFC!", _editor.Document.CurrentSnapshot.ProjectText(cell).Text.ToString());
            Equal("October 11", _editor.Document.CurrentSnapshot.Get<RichField>(field).Result);
            _editor.Undo();
            _editor.Selection = new RichObjectSelection(field);
            surface.Selection.ReplaceText("");
            Equal("bb", _editor.Document.CurrentSnapshot.ProjectText(cell).Text.ToString());
            _editor.Undo(); Equal("bbOctober 11", native.Text);
            var retained = "A retained field result that wraps across several native lines.";
            _editor.Edit(edit => edit.SetFieldResult(field, retained));
            await Task.Delay(80);
            Equal("bb" + retained, native.Text);
            Equal(true, native.Layout!.LineCount > 1);
            using var accessibility = native.CreateAccessibilityNodeInfo()!;
            var spoken = (Android.Text.ISpanned)accessibility.TextFormatted!;
            var descriptions = spoken.GetSpans(2, 3, Java.Lang.Class.FromType(typeof(Android.Text.Style.TtsSpan)))!;
            Equal(1, descriptions.Length);
            using var italic = new Android.Text.Style.StyleSpan(Android.Graphics.TypefaceStyle.Italic);
            native.EditableText!.SetSpan(italic, 0, 2, Android.Text.SpanTypes.ExclusiveExclusive);
            await Task.Delay(60);
            var firstRun = _editor.Document.CurrentSnapshot.Get<RichParagraph>(paragraph).Inlines[0];
            Equal(true, _editor.Document.CurrentSnapshot.ResolveCharacter(firstRun).Italic);
            Equal(new RichObjectSelection(field), _editor.Selection);
            _editor.Undo();
            Equal(false, _editor.Document.CurrentSnapshot.ResolveCharacter(firstRun).Italic);
        });
        await Test("native cells share multiple carets and one IME history unit", async () =>
        {
            var snapshot = _editor.Document.CurrentSnapshot;
            RichTextSelectionItem Caret(RichNodeId cell)
            {
                var paragraph = snapshot.Get<RichTableCell>(cell).Blocks[0];
                return new(new(new(snapshot.Body, paragraph, 2), new(snapshot.Body, paragraph, 2)));
            }
            var set = new RichTextSelectionSet(snapshot, (RichTextSelectionItem[])[Caret(Cell(0, 0)), Caret(Cell(0, 1))], 1);
            _editor.Selection = set; _editor.Focus(); await Task.Delay(60);
            var native = ((RichEditorHandler)Surface(Cell(0, 1)).Handler!).PlatformView;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var marked = new Java.Lang.String("仮名");
            connection.SetComposingText(marked, 1);
            Equal(true, ReferenceEquals(snapshot, _editor.Document.CurrentSnapshot));
            Equal(true, _editor.TryCompleteComposition());
            Equal("aa仮名", Surface(Cell(0, 0)).Document.Text);
            Equal("bb仮名", Surface(Cell(0, 1)).Document.Text);
            _editor.Undo(); Equal(set, _editor.Selection);
            Equal(false, _editor.CanUndo);
        });
        await Test("list Enter and soft keyboard Backspace preserve native focus across item surfaces", async () =>
        {
            var cell = Cell(0, 0);
            RichNodeId list = default;
            var paragraph = _editor.Document.CurrentSnapshot.Get<RichTableCell>(cell).Blocks[0];
            _editor.Edit(edit => list = edit.WrapInList(cell, 0, 1, numbered: true, startNumber: 3, numberStyle: RichListNumberingStyle.UpperRoman));
            await Task.Delay(120);
            Equal(true, NativeViews<Android.Widget.TextView>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Any(view => view.Text == "III."));
            var point = new RichTextPoint(_editor.Document.CurrentSnapshot.Body, paragraph, 1);
            _editor.Selection = new RichEdit.Core.RichTextSelection(point, point);
            _editor.Focus(); await Task.Delay(40);
            var first = _editor.NativeView.FindStructuredSurface(_editor.Selection)!;
            var native = ((RichEditorHandler)first.Handler!).PlatformView;
            using (var info = new Android.Views.InputMethods.EditorInfo())
            using (var connection = native.OnCreateInputConnection(info)!)
            using (var text = new Java.Lang.String("\n")) connection.CommitText(text, 1);
            await Task.Delay(160);
            Equal(2, _editor.Document.CurrentSnapshot.Get<RichList>(list).Items.Count);
            Equal(true, NativeViews<Android.Widget.TextView>((Android.Views.View)_editor.Content!.Handler!.PlatformView!).Any(view => view.Text == "IV."));
            Equal("a\na", _editor.Document.CurrentSnapshot.ProjectText(cell).Text.ToString());
            var second = _editor.NativeView.FindStructuredSurface(_editor.Selection)!;
            Equal(false, ReferenceEquals(first, second));
            native = ((RichEditorHandler)second.Handler!).PlatformView;
            Equal(true, native.IsFocused);
            var selectionBeforeBackspace = $"{_editor.Selection}; native {native.SelectionStart}:{native.SelectionEnd}; surface {second.SelectionState}; " +
                $"composition {_editor.Composition is not null}/{second.Composition.IsActive}; handler {native.ProjectionHandler is not null}; " +
                string.Join("|", _editor.Document.CurrentSnapshot.Get<RichList>(list).Items.Select(id => $"{id.Value}:[{string.Join(',', _editor.Document.CurrentSnapshot.Get<RichListItem>(id).Blocks.Select(block => block.Value))}]"));
            using (var info = new Android.Views.InputMethods.EditorInfo())
            using (var connection = native.OnCreateInputConnection(info)!) connection.DeleteSurroundingText(1, 0);
            await Task.Delay(160);
            Equal(1, _editor.Document.CurrentSnapshot.Get<RichList>(list).Items.Count, $"before {selectionBeforeBackspace}; after {_editor.Selection}");
            Equal("aa", _editor.Document.CurrentSnapshot.ProjectText(cell).Text.ToString());
            Equal(true, ((RichEditorHandler)_editor.NativeView.FindStructuredSurface(_editor.Selection)!.Handler!).PlatformView.IsFocused);
            _editor.Undo(); await Task.Delay(100);
            Equal(2, _editor.Document.CurrentSnapshot.Get<RichList>(list).Items.Count);
        });
        await Test("Tab moves native cell focus and appends one shared row", async () =>
        {
            var snapshot = _editor.Document.CurrentSnapshot;
            var first = snapshot.Get<RichTableCell>(Cell(0, 0)).Blocks[0];
            var point = new RichTextPoint(snapshot.Body, first, 1);
            _editor.Selection = new RichEdit.Core.RichTextSelection(point, point);
            _editor.Focus(); await Task.Delay(40);
            Equal(true, _editor.MoveToAdjacentCell(), "first Tab");
            await Task.Delay(50);
            var selected = _editor.Selection is RichTextSelectionSet set ? set.Primary.Selection : (RichEdit.Core.RichTextSelection)_editor.Selection;
            Equal(snapshot.Get<RichTableCell>(Cell(0, 1)).Blocks[0], selected.Active.Paragraph);
            Equal(true, ((RichEditorHandler)Surface(Cell(0, 1)).Handler!).PlatformView.IsFocused, "right cell focus");
            Equal(false, _editor.CanUndo);
            Equal(true, Surface(Cell(0, 1)).SendKeyDown(EditorKey.Tab, EditorKeyModifiers.Shift), "Shift Tab");
            await Task.Delay(30);
            Equal(true, ((RichEditorHandler)Surface(Cell(0, 0)).Handler!).PlatformView.IsFocused, "left cell focus");
            _editor.MoveToAdjacentCell(); _editor.MoveToAdjacentCell(); _editor.MoveToAdjacentCell();
            Equal(true, _editor.MoveToAdjacentCell(), "append row");
            await Task.Delay(150);
            Equal(3, _editor.Document.CurrentSnapshot.GetTableGrid(_table).Rows);
            Equal(true, ((RichEditorHandler)Surface(Cell(2, 0)).Handler!).PlatformView.IsFocused, $"new row focus: {_editor.Selection}");
            _editor.Undo(); await Task.Delay(100);
            Equal(2, _editor.Document.CurrentSnapshot.GetTableGrid(_table).Rows);
            Equal(false, _editor.CanUndo);
        });
        await Test("table scrolling invalidates geometry and structural edits retain the viewport anchor", async () =>
        {
            RichNodeId table = default;
            _editor.Document = new RichDocument(RichDocumentSnapshot.Build(edit =>
            {
                table = edit.InsertTable(edit.Body, 1, 36, new RichLength[] { new(6000), new(6000) });
                var grid = edit.Snapshot.GetTableGrid(table);
                for (var row = 0; row < grid.Rows; row++)
                    for (var column = 0; column < grid.Columns; column++)
                        edit.ReplaceText(new(edit.Body, edit.Snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0], 0), 0, $"Row {row} cell {column}");
            }));
            await Task.Delay(220);
            var root = (Android.Widget.ScrollView)_editor.Content!.Handler!.PlatformView!;
            var horizontal = NativeViews<Android.Widget.HorizontalScrollView>(root).First();
            var changed = 0;
            void OnChanged(object? sender, EventArgs args) => changed++;
            _editor.TextLayout.Changed += OnChanged;
            try
            {
                horizontal.ScrollTo(90, 0);
                await Task.Delay(50);
                Equal(true, changed > 0, "horizontal viewport notification");
                var density = root.Resources!.DisplayMetrics!.Density;
                root.ScrollTo(0, (int)(500 * density));
                await Task.Delay(70);
                var capture = _editor.TextLayout.Capture()!;
                var line = capture.Lines.First(line => line.SourceRanges.Count > 0 && line.Bounds.Top >= capture.Viewport.Top);
                // A horizontal viewport can clip the logical start of a line.
                // Anchor a visible glyph instead of assuming its first source
                // address has visible caret geometry.
                var hit = _editor.TextLayout.HitTest(capture, new(Math.Max(line.Bounds.Left, capture.Viewport.Left) + 4, line.Bounds.Center.Y))
                    ?? throw new InvalidOperationException($"No visible table hit in {line.Bounds}; viewport {capture.Viewport}.");
                var anchor = hit.Point;
                var before = _editor.TextLayout.GetCaretBounds(capture, anchor)
                    ?? throw new InvalidOperationException($"No visible caret for {anchor}; line {line.Bounds}; viewport {capture.Viewport}.");
                _editor.Edit(edit => edit.InsertRows(table, 0));
                await Task.Delay(180);
                var afterCapture = _editor.TextLayout.Capture()!;
                var after = _editor.TextLayout.GetCaretBounds(afterCapture, anchor);
                Equal(true, after is not null && Math.Abs(after.Value.Top - before.Top) < 3,
                    $"anchor before {before}; after {after}; scroll {root.ScrollY}");
            }
            finally { _editor.TextLayout.Changed -= OnChanged; }
        });
        await Test("horizontal arrows move all carets across native cell boundaries", async () =>
        {
            var snapshot = _editor.Document.CurrentSnapshot;
            RichTextPoint PointAt(int row, int column, int offset) => new(snapshot.Body,
                snapshot.Get<RichTableCell>(Cell(row, column)).Blocks[0], offset);
            var first = PointAt(0, 0, 2);
            _editor.Selection = new RichEdit.Core.RichTextSelection(first, first);
            _editor.Focus(); await Task.Delay(30);
            Equal(true, Surface(Cell(0, 0)).SendKeyDown(EditorKey.Right, EditorKeyModifiers.None));
            await Task.Delay(40);
            var selected = (RichTextSelectionSet)_editor.Selection;
            Equal(PointAt(0, 1, 0), selected.Primary.Selection.Active);
            Equal(true, ((RichEditorHandler)Surface(Cell(0, 1)).Handler!).PlatformView.IsFocused);
            Equal(true, _editor.MoveSelections(EditorKey.Left));
            Equal(first, ((RichTextSelectionSet)_editor.Selection).Primary.Selection.Active);
            var other = PointAt(1, 0, 2);
            _editor.Selection = new RichTextSelectionSet(snapshot, new[]
            {
                new RichTextSelectionItem(new(first, first)), new RichTextSelectionItem(new(other, other)),
            });
            Equal(true, _editor.MoveSelections(EditorKey.Right));
            selected = (RichTextSelectionSet)_editor.Selection;
            Equal(2, selected.Count);
            Equal(PointAt(0, 1, 0), selected[0].Selection.Active);
            Equal(PointAt(1, 1, 0), selected[1].Selection.Active);
            Equal(false, _editor.CanUndo);
        });
        await Test("vertical arrows and Shift retain rich anchors across cell surfaces", async () =>
        {
            var snapshot = _editor.Document.CurrentSnapshot;
            RichTextPoint PointAt(int row, int column, int offset) => new(snapshot.Body,
                snapshot.Get<RichTableCell>(Cell(row, column)).Blocks[0], offset);
            var first = PointAt(0, 1, 1);
            _editor.Selection = new RichEdit.Core.RichTextSelection(first, first);
            _editor.Focus(); await Task.Delay(40);
            Equal(true, _editor.MoveSelections(EditorKey.Down, extend: true));
            var selection = ((RichTextSelectionSet)_editor.Selection).Primary.Selection;
            Equal(first, selection.Anchor);
            Equal(PointAt(1, 1, 1), selection.Active);
            Equal(true, _editor.MoveSelections(EditorKey.Up, extend: true));
            selection = ((RichTextSelectionSet)_editor.Selection).Primary.Selection;
            Equal(first, selection.Anchor); Equal(first, selection.Active);
            first = PointAt(0, 0, 2);
            _editor.Selection = new RichEdit.Core.RichTextSelection(first, first);
            Equal(true, _editor.MoveSelections(EditorKey.Right, extend: true));
            Equal(PointAt(0, 1, 0), ((RichTextSelectionSet)_editor.Selection).Primary.Selection.Active);
            Equal(true, _editor.MoveSelections(EditorKey.Right, extend: true));
            selection = ((RichTextSelectionSet)_editor.Selection).Primary.Selection;
            Equal(first, selection.Anchor); Equal(PointAt(0, 1, 1), selection.Active);
            Equal(false, _editor.CanUndo);
        });
        results.Add($"COMPLETE {results.Count} tests, {results.Count(result => result.StartsWith("FAIL", StringComparison.Ordinal))} failures");
        File.WriteAllLines(report, results);
    }
    private void Reset()
    {
        _editor.Document = new RichDocument(RichDocumentSnapshot.Build(edit =>
        {
            edit.ReplaceText(new(edit.Body, new(2), 0), 0, "before");
            _table = edit.InsertTable(edit.Body, 1, 2, (RichLength[])[new(2400), new(2400)]);
            var snapshot = edit.Snapshot;
            var grid = snapshot.GetTableGrid(_table);
            var labels = new[] { "aa", "bb", "cc", "dd" };
            for (var row = 0; row < 2; row++)
                for (var column = 0; column < 2; column++)
                    edit.ReplaceText(new(edit.Body, snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0], 0), 0, labels[row * 2 + column]);
            edit.InsertParagraph(edit.Body, 2, "after");
        }));
    }
    private RichNodeId Cell(int row, int column) => _editor.Document.CurrentSnapshot.GetTableGrid(_table)[row, column];
    private NativeEditorView Surface(RichNodeId cell)
    {
        var snapshot = _editor.Document.CurrentSnapshot;
        var paragraph = snapshot.Get<RichTableCell>(cell).Blocks[0];
        return _editor.NativeView.FindStructuredSurface(new RichEdit.Core.RichTextSelection(new(snapshot.Body, paragraph, 0), new(snapshot.Body, paragraph, 0)))!;
    }
    private async Task Ready()
    {
        for (var count = 0; count < 60; count++)
        {
            if (Surface(Cell(0, 0))?.Handler?.PlatformView is Android.Views.View { Width: > 0 }) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Native table layout did not become available.");
    }
    private static IEnumerable<T> NativeViews<T>(Android.Views.View root) where T : Android.Views.View
    {
        if (root is T found) yield return found;
        if (root is Android.Views.ViewGroup group)
            for (var index = 0; index < group.ChildCount; index++)
                foreach (var child in NativeViews<T>(group.GetChildAt(index)!)) yield return child;
    }
    private static void Equal<T>(T expected, T actual, string? context = null)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{context}: expected {expected}; actual {actual}."); }
}
#endif
