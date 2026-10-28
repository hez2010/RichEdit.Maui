---
title: Tracking and persistent annotations
description: Disposable source anchors and document-owned rich comments and bookmarks.
---

# Tracking and persistent annotations

Use `CodeDocument.Tracking` for temporary source anchors. `TrackPosition` and `TrackRange` return disposable handles that move through edits. Insertion affinity chooses which side of an insertion retains the anchor. Deletion behavior determines whether removed content invalidates the handle or collapses its range.

```csharp
using TextEdit.Core;

using var caret = code.Document.Tracking.TrackPosition(
    3, PositionAffinity.AfterInsertion);
code.Document.Edit(edit => edit.InsertText(0, "prefix "));
int? current = caret.Position;
```

Tracking handles are view/application tools, not persistence. Invalidated handles are not resurrected by undo. Dispose them when a hover, search session, snippet, or other feature ends.

Rich document changes expose `RichAddressMap.Map(point)`. It preserves moved node identities, maps paragraph split/join offsets, and returns null for removed paragraphs. Rich presentation producers apply these maps automatically.

Persistent rich bookmarks and comments belong to the document:

```csharp
editor.Edit(edit =>
{
    edit.AddBookmark("intro", new RichEdit.Core.RichTextSelection(startPoint, endPoint));
    edit.AddComment(new RichEdit.Core.RichTextSelection(startPoint, endPoint),
        "Check this wording.", author: "Reviewer");
});
```

Annotations retain semantic anchors and can become orphaned when their content is removed. `SetAnnotationRange` reattaches them; `SetCommentText` and `RemoveAnnotation` are authored edits. Undo restores their content and anchor state. RTF persistence preserves supported annotation relationships independently of disposable source trackers.

See [Comments and bookmarks](annotations.md) for creating annotation UI, navigating to current anchors, and handling orphaned annotations.
