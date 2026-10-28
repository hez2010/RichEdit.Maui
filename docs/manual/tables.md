---
title: Tables and cell editing
description: Create tables, populate cells, select merged regions, and edit grid structure.
---

# Tables and cell editing

Tables are authored blocks in `RichEdit.Core`. A `RichTable` owns column widths and rows; rows reference `RichTableCell` nodes, and cells contain blocks. Cell content, structure, formatting, and surrounding text all share the parent document's undo history. Nested tables use the same model.

## Insert and populate a table

This example appends a two-column table to the body and writes into its first cell in one transaction:

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var editor = new RichEditor { Document = RichDocument.FromPlainText("Items") };
RichNodeId table = default;

var result = editor.Edit(edit =>
{
    int index = edit.Snapshot.Get<RichStory>(edit.Body).Blocks.Count;
    table = edit.InsertTable(edit.Body, index, rows: 2,
        columns: new[] { RichLength.FromPoints(160), RichLength.FromPoints(80) });

    var snapshot = edit.Snapshot;
    var cellId = snapshot.GetTableGrid(table)[0, 0];
    var paragraph = snapshot.Get<RichTableCell>(cellId).Blocks[0];
    var point = new RichTextPoint(edit.Body, paragraph, 0);
    edit.ReplaceText(point, 0, "Description");
    edit.SetCharacterFormat(point, 11, new RichCharacterStyle { Bold = true });
    edit.SetSelection(new RichEdit.Core.RichTextSelection(point, point));
}, new RichEditOptions(Description: "Insert item table"));
```

New cells contain an empty paragraph. In an existing cell, inspect its blocks rather than assuming its first block is a paragraph. The point's `Story` is the containing story, not the cell ID. `edit.Snapshot` exposes the candidate after preceding builder operations, so newly created cells can be addressed before commit. Check the edit result before using IDs captured from the callback.

## Select cells and merged regions

`RichCellSelection(table, row, column, rowCount, columnCount)` uses zero-based coordinates and counts, not end indices. Select the first row of the example table with:

```csharp
editor.Selection = new RichCellSelection(table, 0, 0, 1, 2);
```

Use `snapshot.GetTableGrid(table)` to resolve cells by logical row and column. Its indexer returns the same cell ID at every position covered by a merged cell. `GetExtent(cellId)` gives that cell's full rectangle, and `Expand(selection)` includes every merged cell intersecting a selection. Reacquire the grid from the current snapshot after structural edits.

Merge a rectangle, then split the resulting cell in a later command:

```csharp
RichNodeId mergedCell = default;
editor.Edit(edit =>
    mergedCell = edit.MergeCells(new RichCellSelection(table, 0, 0, 1, 2)));

// A subsequent command, while mergedCell still exists:
editor.Edit(edit => edit.SplitCell(mergedCell));
```

Merging combines the selected cells' blocks into the surviving cell. Splitting keeps that content in the original cell and creates empty cells for the remaining positions. It does not reconstruct the original distribution of content; Undo does.

## Edit rows, columns, and widths

All structural methods below belong to `RichDocumentEdit` and are called inside `editor.Edit`:

| Operation | Method |
| --- | --- |
| Insert rows at a zero-based boundary | `InsertRows(table, index, count)` |
| Remove rows | `DeleteRows(table, index, count)` |
| Insert columns with explicit widths | `InsertColumns(table, index, widths)` |
| Remove columns | `DeleteColumns(table, index, count)` |
| Resize the existing grid | `SetColumnWidths(table, widths)` |
| Empty cell content while retaining the grid | `ClearCells(selection)` |
| Remove the table block | `DeleteBlock(table)` |

The builder validates merged spans and complete grid coverage before publication. `SetColumnWidths` requires one positive width per existing column; it preserves cell identities, spans, and content. The total width must fit the RTF cell-boundary range.

```csharp
editor.Edit(edit => edit.SetColumnWidths(table,
    new[] { RichLength.FromPoints(180), RichLength.FromPoints(100) }),
    new RichEditOptions(Description: "Resize table columns"));
```

Replacing text in a cell selection clears its selected content and preserves the grid. Native Delete or Backspace on a selection covering every cell removes the table. Use `ClearCells` when a clear-table command must retain the grid.

## Format cells

`SetCellFormat` applies a complete cell-format record. To preserve distinct existing formats, iterate the selected unique cell IDs and use each cell's `Format with { ... }` value.

```csharp
editor.Edit(edit => edit.SetCellFormat(
    new RichCellSelection(table, 0, 0, 1, 2),
    new RichCellFormat
    {
        Padding = RichCellPadding.Uniform(RichLength.FromPoints(6)),
        VerticalAlignment = RichCellVerticalAlignment.Center,
        Shading = new RichShading(10000, foreground: new RichColor(230, 238, 250)),
        Borders = RichCellBorders.Uniform(new RichBorderLine(
            RichBorderStyle.Single, RichLength.FromPoints(0.5),
            new RichColor(100, 120, 150))),
    }));
```

`RichCellBorders` also supports independent left, top, right, and bottom edges. To format text inside cells, pass the cell selection to `edit.ApplyCharacterFormat` or `edit.ApplyParagraphFormat`.

## Navigation and native layout

`editor.MoveToAdjacentCell(forward: true, appendRow: true)` moves in row order and can append a row when moving beyond the final cell. Pass `appendRow: false` for navigation that must not grow the table. The appended row is undoable. Native Up/Down and Shift-arrow navigation use text geometry and rich addresses.

On Android, coordinated native cell editors participate in the same session. Use `editor.TextLayout` for editor-relative geometry and typed points for cell content. Block widgets inside cells measure against the available cell width, accounting for spans, padding, and paragraph indents. Large tables can reduce native layout responsiveness.
