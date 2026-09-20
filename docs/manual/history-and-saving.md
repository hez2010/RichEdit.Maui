---
title: Undo, redo, and saving
description: Group edits, report modified state, and save a captured document without losing track of later changes.
---

# Undo, redo, and saving

The document records native input and programmatic edits in the same undo history, including rich content and directional selection.

## Connect history to the UI

Bind buttons to `editor.Commands.Undo` and `editor.Commands.Redo`. Use `Document.UndoDescription` and `RedoDescription` for labels, and `Document.IsModified` for the unsaved-change indicator.

`ClearUndoHistory()` removes undo and redo entries without changing `IsModified`.

## Choose an undo policy

| `RichTextUndoBehavior` | Effect |
| --- | --- |
| `CreateUnit` | Default: one undo unit for a changed transaction |
| `MergeWithPrevious` | Merge with the preceding compatible unit |
| `ClearHistory` | Commit the change and clear undo and redo history |
| `PreserveHistory` | Commit character, paragraph, or default formatting while keeping existing history |

`PreserveHistory` accepts only character, paragraph, and default formatting. These changes still affect document state and RTF, and undo or redo may restore earlier formatting. Use [decorations](decorations.md) for temporary syntax colors and diagnostics.

## Group separate operations

Use a single `Document.Edit` when all changes should commit atomically. If several operations already commit independently, combine their history with an undo group:

```csharp
using (editor.Document.BeginUndoGroup("Insert section"))
{
    editor.Selection.ReplaceText("Heading\n");
    editor.Selection.ReplaceText("Body text\n");
}
```

Each operation still changes the document and raises its own notifications. A later failure does not roll back earlier commits. Groups can nest and must close in reverse order. Undo and redo are unavailable while any group is open; history-clearing operations are also disallowed inside a group.

## Save while editing

Capture the document, snapshot, and save point together on the UI thread. Serialize the snapshot, then mark that save point after the write succeeds:

```csharp
// Call from the editor's UI thread and await the returned task.
static async Task SaveRtfAsync(RichEditor editor, string path)
{
    var document = editor.Document;
    var snapshot = document.CurrentSnapshot;
    var savePoint = document.CreateSavePoint();

    string rtf = await Task.Run(() => snapshot.RtfText);
    await File.WriteAllTextAsync(path, rtf);
    document.MarkSaved(savePoint);
}
```

This method starts and resumes on the UI thread. `Task.Run` moves RTF serialization to a worker; `MarkSaved` still runs on the document's owning thread.

The captured document and save point identify what was written. If the user edits while saving, those later edits remain modified. If they open another file, the new document is unaffected.

Calling the parameterless `MarkSaved()` after the write would mark the current state, including any unsaved edits. Serialize overlapping saves to the same path so an older write cannot overwrite a newer one.

## Save source code

Use the same pattern with the code snapshot's text:

```csharp
static async Task SaveSourceAsync(CodeEditor editor, string path)
{
    var document = editor.Document;
    var snapshot = document.CurrentSnapshot;
    var savePoint = document.CreateSavePoint();

    await File.WriteAllTextAsync(path, snapshot.Text);
    document.MarkSaved(savePoint);
}
```

Source text uses LF. Convert line endings when writing if the file needs another convention.

Undoing back to the saved state clears `IsModified`, even though `Version` continues to increase.
