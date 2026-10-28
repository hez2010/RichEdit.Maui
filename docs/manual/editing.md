---
title: Atomic edits and selection
description: Typed selections, checked transactions, and multiple carets.
---

# Atomic edits and selection

A transaction validates its candidate state before publication. A failed callback leaves the live document unchanged. Sequential builder operations address the result of preceding operations.

```csharp
using RichEdit.Core;

var snapshot = editor.Document.CurrentSnapshot;
var paragraph = snapshot.Get<RichStory>(snapshot.Body).Blocks[0];
var point = new RichTextPoint(snapshot.Body, paragraph, 0);

editor.Edit(edit =>
{
    edit.ReplaceText(point, 0, "Welcome ");
    edit.SetCharacterFormat(point, 7, new RichCharacterStyle { Bold = true });
}, new RichEditOptions(Description: "Insert greeting"));
```

This example starts with a paragraph as the first body block. General tools should inspect node types and use selection/structural commands.

## Rich selections

`RichSelection` distinguishes `RichTextSelection`, `RichTextSelectionSet`, `RichObjectSelection`, `RichCellSelection`, `RichBlockSelection`, and `RichBoundarySelection`. Text points contain story, paragraph, local UTF-16 offset, and affinity.

| Selection | Use |
| --- | --- |
| `RichTextSelection(anchor, active)` | A caret or directional text range in one story. |
| `RichTextSelectionSet` | Multiple text ranges with stable IDs and a primary range. |
| `RichObjectSelection(objectId)` | One authored inline object, such as an image or field. |
| `RichCellSelection` | A rectangular table region, expanded to whole merged cells. |
| `RichBlockSelection(parent, first, last)` | An inclusive range of complete blocks in one container. |
| `RichBoundarySelection(story, block, edge)` | A caret before or after a structural block without creating a paragraph. |

```csharp
editor.Selection = new RichEdit.Core.RichTextSelection(point, point with { Offset = 7 });
editor.ReplaceSelection("Hello");
```

A `RichTextSelectionSet` supplies stable selection IDs and one primary selection. Typing, deletion, paste, and history use the whole set. The native control owns primary selection and touch handles; secondary carets use native geometry.

Build a rich selection set against the current snapshot. This example places carets at both ends of a paragraph:

```csharp
editor.Document = RichDocument.FromPlainText("example");
var current = editor.Document.CurrentSnapshot;
var paragraphId = current.Get<RichStory>(current.Body).Blocks[0];
var start = new RichTextPoint(current.Body, paragraphId, 0);
var end = start with { Offset = 7 };
editor.Selection = new RichTextSelectionSet(current, new[]
{
    new RichTextSelectionItem(new RichEdit.Core.RichTextSelection(start, start)),
    new RichTextSelectionItem(new RichEdit.Core.RichTextSelection(end, end)),
}, primaryIndex: 0);
```

All points must be valid in `current`. To retain only the primary rich selection, assign `set.Primary.Selection` back to `editor.Selection`. `AddCaretAbove` and `AddCaretBelow` derive new positions from native visual geometry once layout is available.

Cell selections expand to include whole merged cells. Replacing selected cell content preserves the grid. Delete or Backspace removes the table when every cell is selected; with a partial cell selection, it clears the selected cells. Use `ClearCells` to empty a whole table while keeping its grid. Row, column, and block commands provide explicit structural operations. Text replacement does not flatten partially selected table boundaries.

Deleting text across list items or separate lists in the same text container joins the remaining prefix and suffix. Unselected text keeps its character formatting, and trailing list items retain their numbering. Fully selected lists and intervening blocks are removed. Deleting the first item's leading paragraph break joins its text to the preceding paragraph. Backspace at the start of the first item removes its list membership; if the paragraph is empty, another Backspace removes the remaining line. Undo restores both text and list membership.

## Source selections and batches

`CodeEditor.Selections` is a `TextSelectionSet`; `SelectedRange` and `SelectionState` address its primary selection. `AddCaret`, `ClearSecondarySelections`, and `ReplaceSelections` provide common commands. Selection sets normalize duplicate carets and intersecting ranges while preserving primary identity.

`CodeDocument.Edit` uses sequential coordinates. `CodeEditor.TryApplyEdits` accepts nonoverlapping replacements in the captured snapshot's original coordinates and validates `DocumentRevision`. Same-position insertions retain caller order. An optional selection generation rejects a completion after the user moves the caret without changing text.

Low-level ranges are exact UTF-16 spans. User navigation and deletion use native geometry and grapheme-aware boundaries; callers of raw replacement APIs must choose their intended range.

## Check edit outcomes

`RichEditor.Edit` and rich selection replacement return `RichEditResult`. `Accepted` is true for an accepted no-op as well as a change; `Change` is null when nothing was authored. `Rejection` describes policy or freshness rejection, such as a stale revision, read-only session, or active composition. Invalid arguments and invalid candidate structure can throw instead. Do not assume a returned result means an edit happened.

Use `expectedRevision` for commands computed from a snapshot and `expectedSelectionGeneration: editor.Session.SelectionGeneration` when their validity also depends on a captured selection. Capture those values before asynchronous work, not immediately before publication. Source commands use `code.SelectionGeneration`.
