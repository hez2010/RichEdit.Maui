---
title: Undo, redo, and saving
description: Shared history, retention limits, and snapshot-bound save acknowledgement.
---

# Undo, redo, and saving

The document owns one global undo sequence. A view's Undo or Redo restores the transaction's saved selection in that view; other views map their independent selections. Native undo commands route to document history. Detaching a native handler does not clear history.

Every authored change, including formatting, styles, comments, and revision resolution, participates in history. Decorations and widgets do not. `BeginUndoGroup` combines independently committed operations into one undo unit; nested groups close in reverse order. A group is not rollback for already committed operations.

`HistoryLimit` and `HistoryByteLimit` bound retained entries and estimated memory. `RetainedHistoryByteEstimate` is an estimate, not process memory. Application-held snapshots and pending tracked revisions can retain content independently of history. Automatic typing coalescing is session-owned and ends at selection, input-mode, other-view, save, and composition boundaries.

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
