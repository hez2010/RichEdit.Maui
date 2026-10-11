---
title: Tables, links, fields, images, and fragments
description: Structured content and resource ownership.
---

# Tables, links, fields, images, and fragments

Tables, lists, and inline objects belong to the same document and history as surrounding paragraphs. Cells hold blocks, including nested tables.

## Tables

```csharp
RichNodeId table = default;
editor.Edit(edit =>
{
    table = edit.InsertTable(edit.Body, 1, 2,
        new RichLength[] { new(2400), new(2400) });
});
editor.Selection = new RichCellSelection(table, 0, 0, 1, 2);
```

`InsertRows`, `DeleteRows`, `InsertColumns`, `DeleteColumns`, `MergeCells`, `SplitCell`, and cell-content operations validate the resulting grid. `GetTableGrid` resolves spanning cells. `SetCellFormat` authors independent edge borders, padding, shading, and vertical alignment. Merged-cell selections expand to whole cells.

`MoveToAdjacentCell` navigates row order; forward navigation can append a row as one undo unit. Android uses native cell editors coordinated by one session. Up/Down and Shift-arrow navigation use the cells' native geometry and rich anchors.

## Inline content

Use `SetHyperlink` for a text range, `InsertField` for an instruction/result pair, and `SetFieldResult` to update a retained field result. A field is an atomic authored object; the application supplies field evaluation. `LinkInvoked` and `ObjectInvoked` support application handling.

Add an image resource with `AddImageAsset`, then reference it with `InsertImage`. The object has explicit dimensions and alternative text. Supported retained image kinds are PNG, JPEG, DIB, EMF, and WMF; native display support varies. Resource identity and bytes remain document-owned.

## Fragments

`RichDocumentFragment.FromSelection(snapshot, selection)` freezes selected rich content and its resources. `ReplaceSelection(fragment)` inserts a fragment with identity/resource remapping. A fragment is serialized through `RtfCodec` using its `Content` snapshot. There is no separate fragment file format.

Application widgets are transient view objects and do not enter the rich schema, clipboard RTF, or undo history. Use [presentation widgets](adornments.md) for completion actions, hints, or review controls.
