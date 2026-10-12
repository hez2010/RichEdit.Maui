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

```csharp
editor.Selection = new RichEdit.Core.RichTextSelection(point, point with { Offset = 7 });
editor.ReplaceSelection("Hello");
```

A `RichTextSelectionSet` supplies stable selection IDs and one primary selection. Typing, deletion, paste, and history use the whole set. The native control owns primary selection and touch handles; secondary carets use native geometry.

Cell selections expand to include whole merged cells. Replacing selected cell content preserves the grid. Delete rows, columns, or complete blocks with explicit structural operations. Text replacement cannot silently flatten table boundaries.

Deleting text across items in one list joins the remaining text and keeps the list's numbering style. Deleting the first item's leading paragraph break joins its text to the preceding paragraph. Backspace at the start of the first item removes its list membership; if the paragraph is empty, another Backspace removes the remaining line. Undo restores both text and list membership.

## Source selections and batches

`CodeEditor.Selections` is a `TextSelectionSet`; `SelectedRange` and `SelectionState` address its primary selection. `AddCaret`, `ClearSecondarySelections`, and `ReplaceSelections` provide common commands. Selection sets normalize duplicate carets and intersecting ranges while preserving primary identity.

`CodeDocument.Edit` uses sequential coordinates. `CodeEditor.TryApplyEdits` accepts nonoverlapping replacements in the captured snapshot's original coordinates and validates `DocumentRevision`. Same-position insertions retain caller order. An optional selection generation rejects a completion after the user moves the caret without changing text.

Low-level ranges are exact UTF-16 spans. User navigation and deletion use native geometry and grapheme-aware boundaries; callers of raw replacement APIs must choose their intended range.
