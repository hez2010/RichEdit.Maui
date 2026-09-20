---
title: Atomic edits and selection
description: Make transactional document changes, edit the active selection, and choose the right coordinate system for a batch.
---

# Atomic edits and selection

`Document.Edit` groups content changes into one transaction. `Selection` applies editing commands to the current selection, and `TryApplyEdits` accepts replacements calculated from a captured revision.

## Edit atomically

```csharp
var document = RichTextDocument.FromPlainText("Hello, world!");
document.Edit(edit =>
{
    edit.ReplaceText(new RichTextRange(0, 5), "Welcome");
    edit.UpdateCharacterFormat(new RichTextRange(0, 7),
        format => format with { FontWeight = 700 });
    edit.SetLink(new RichTextRange(0, 7), "https://example.com");
}, new RichTextEditOptions(undoDescription: "Update greeting"));
```

Ranges refer to the result of preceding operations. Here, replacing `Hello` with `Welcome` changes the range to format from five characters to seven.

A changed transaction increments `Version`, raises one `Changed` event, and creates one undo unit by default. If the callback throws, its changes are discarded. A transaction that leaves the content unchanged does not increment the version or record history.

The callback is synchronous, and the edit builder is valid only within it. Nested transactions are not supported. For work that requires `await`, calculate the result first and apply it with a [revision check](background-work.md).

## Choose edit operations

| Task | Rich document edit methods |
| --- | --- |
| Insert, replace, or delete text | `InsertText`, `ReplaceText`, `DeleteText` |
| Insert rich content | `ReplaceFragment` |
| Change character formatting | `SetCharacterFormat`, `UpdateCharacterFormat`, `SetCharacterFormats` |
| Change paragraph formatting | `SetParagraphFormat`, `UpdateParagraphFormat` |
| Reset formatting to document defaults | `ClearCharacterFormat`, `ClearParagraphFormat` |
| Set document defaults | `SetDefaultCharacterFormat`, `SetDefaultParagraphFormat` |
| Manage lists | `CreateList`, `ApplyList`, `UpdateList`, `ChangeListLevel`, `RestartList`, `RemoveList` |
| Add rich content | `SetLink`, `InsertField`, `InsertImage` |

<xref:CodeEdit.Maui.CodeDocumentEdit> exposes only text insertion, replacement, and deletion. Both document types accept <xref:RichEdit.Maui.RichTextEditOptions> for history behavior, an undo description, and an optional application tag on the change set.

`Document.Edit` bypasses the control's `IsReadOnly` and `MaxLength` settings. Selection editing methods respect `IsReadOnly`, but do not enforce `MaxLength`. `TryApplyEdits` checks both settings and rejects edits during composition.

## Work with the selection

```csharp
editor.Document = RichTextDocument.FromPlainText("Hello, world!");
editor.SelectedRange = new RichTextRange(0, 5);
editor.Selection.ReplaceText("Welcome");
// The selection is now a caret after "Welcome".

editor.SelectionState = new RichTextSelectionState(anchor: 7, active: 0);
// The same word is selected backwards, with the caret at its beginning.
```

`SelectedRange` stores the span from its lower to upper offset. `SelectionState` also preserves direction, with `Active` at the caret end. Assigning a range selects forwards. Replacing selected text uses the typing format and moves the caret after the insertion.

For a custom action with an explicit result selection, use `Selection.Edit`:

```csharp
var range = editor.SelectedRange;
string replacement = "replacement";
editor.Selection.Edit(
    edit => edit.ReplaceText(range, replacement),
    new RichTextSelectionState(range.Start, range.Start + replacement.Length),
    new RichTextEditOptions(undoDescription: "Replace and select"));
```

`selectionAfter` uses coordinates after the edit. The document records directional selection with history, so undo and redo can restore it.

## Batch coordinates

Every replacement in a `TryApplyEdits` batch uses the original snapshot's coordinates. Unlike operations inside `Document.Edit`, later ranges need no adjustment for earlier replacements.

This suits replace-all, rename, or completion results. Use an [undo group](history-and-saving.md#group-separate-operations) when separately committed operations should share a single Undo action.
