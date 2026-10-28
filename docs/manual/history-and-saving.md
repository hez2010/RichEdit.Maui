---
title: Undo, redo, and saving
description: Shared history, retention limits, and snapshot-bound save acknowledgement.
---

# Undo, redo, and saving

The document owns one global undo sequence. A view's Undo or Redo restores the transaction's saved selection in that view; other views map their independent selections. Native undo commands route to document history. Detaching a native handler does not clear history.

Every authored change, including formatting, styles, comments, and revision resolution, participates in history. Decorations and widgets do not.

`HistoryLimit` and `HistoryByteLimit` bound retained entries and estimated memory. `RetainedHistoryByteEstimate` is an estimate, not process memory. Application-held snapshots and pending tracked revisions can retain content independently of history. Automatic typing coalescing is session-owned and ends at selection, input-mode, other-view, save, and composition boundaries.

## Group commands and manage history

Prefer one `Edit` callback when a change must validate and publish atomically. Use an undo group when existing commands commit separately but should be undone together:

```csharp
using (editor.Document.BeginUndoGroup("Emphasize selection"))
{
    editor.ApplyCharacterFormat(new RichCharacterStyle { Bold = true });
    editor.Edit(edit => edit.ApplyParagraphFormat(editor.Selection,
        new RichParagraphStyle { SpaceAfter = RichLength.FromPoints(8) }));
}
```

Dispose the group on the owning thread; nested groups close in reverse order. Do not keep it open across unrelated user interaction or asynchronous work. If a later operation fails, earlier committed operations remain; an undo group does not provide transaction rollback.

`RichDocument.ClearHistory()` and `CodeDocument.ClearUndoHistory()` discard undo/redo entries. They do not accept tracked revisions or save the file. Use `UndoDescription` and `RedoDescription` for application menu labels, and acknowledge actual writes with `MarkSaved(capture)` to update dirty state.

## Save a frozen state

Complete native composition before capturing a save. `TryCompleteComposition()` returns false if the current input cannot be resolved yet.

```csharp
using RichEdit.Core.Rtf;

if (!editor.TryCompleteComposition())
    return;

var document = editor.Document;
var capture = document.CaptureSave();
var diagnostics = RtfCodec.Assess(capture.Snapshot);
if (!diagnostics.IsEmpty)
    throw new RtfFidelityException(diagnostics);

await RtfCodec.WriteAsync(capture.Snapshot, outputStream);
// Flush and successfully replace the application's destination first.
document.MarkSaved(capture);
```

The capture pairs one immutable snapshot with its content-state token. Acknowledging it leaves subsequent edits modified and allows undo back to the saved state to become clean. Do not mark a file saved after a canceled write, failed replacement, or export with unacknowledged loss.

The application owns destination selection, temporary files, atomic replacement, and error handling. Streams remain caller-owned. Large codec work can run on a worker using the frozen snapshot, followed by save acknowledgement on the owner dispatcher.

Source documents use `CaptureSave` and `MarkSaved(capture)` with ordinary text output. Encoding, BOM, and invalid-byte policy belong to the application's file session; source snapshots preserve UTF-16 content and line endings.

Serialize writes to the same destination so an older save cannot finish after a newer one and replace it.
