---
title: Comments and bookmarks
description: Author, locate, update, and reattach persistent rich-document annotations.
---

# Comments and bookmarks

Comments and bookmarks are document-owned annotations in `RichEdit.Core`. They participate in undo, tracked revisions, and the library's RTF persistence. The library stores annotations and maps their anchors; the application supplies a comment sidebar, bookmark list, and navigation UI. Use [decorations](decorations.md) and [widgets](adornments.md) to show annotations beside text.

## Add an annotation

Both APIs take a `RichEdit.Core.RichTextSelection`. Resolve the current selection to the intended text range when your UI also supports object, block, cell, or multiple selections. This example annotates a known paragraph:

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var editor = new RichEditor { Document = RichDocument.FromPlainText("Review this wording.") };
var snapshot = editor.Document.CurrentSnapshot;
var paragraph = snapshot.Get<RichStory>(snapshot.Body).Blocks[0];
var start = new RichTextPoint(snapshot.Body, paragraph, 0);
var range = new RichEdit.Core.RichTextSelection(start, start with { Offset = 6 });
RichAnnotationId commentId = default;

editor.Edit(edit =>
{
    edit.AddBookmark("introduction", range);
    commentId = edit.AddComment(range, "Clarify the opening.", author: "Reviewer");
}, new RichEditOptions(Description: "Annotate introduction"));
```

Bookmark names contain 1–40 letters, digits, or underscores, begin with a letter or underscore, and are unique without regard to case within the document. Keep returned annotation IDs for later commands, and validate that they still exist in the current snapshot. IDs belong to this document; importing RTF regenerates document-local identities.

## Read and navigate

`snapshot.Comments` and `snapshot.Bookmarks` are immutable dictionaries keyed by `RichAnnotationId`. Comments expose `Text`, `Author`, `Created`, `Range`, and `IsOrphaned`; bookmarks expose `Name`, `Range`, and `IsOrphaned`. Refresh your list after `ContentChanged`, and rebind it after `DocumentChanged`.

```csharp
var current = editor.Document.CurrentSnapshot;
if (current.Comments.TryGetValue(commentId, out var comment) &&
    comment.Range is { } anchoredRange)
{
    editor.Selection = anchoredRange;
    editor.TextLayout.ScrollIntoView(anchoredRange.Active);
    editor.Focus();
}
```

Setting a selection and scrolling are separate operations. Do not navigate using an old snapshot's range after intervening edits; look up the annotation again by ID.

## Update, remove, and reattach

Use `SetCommentText(id, text)` to edit a comment, `SetAnnotationRange(id, range)` to move either kind of annotation, and `RemoveAnnotation(id)` to delete it. Call these methods inside a transaction so toolbar actions participate in shared history:

```csharp
editor.Edit(edit => edit.SetCommentText(commentId, "Opening reviewed."),
    new RichEditOptions(Description: "Update comment"));
```

When the anchored content is deleted, an annotation can remain with `Range = null` and `IsOrphaned = true`. Display that state in your annotation list and let the user remove it or attach it to a new range. An orphaned comment's text remains available. Undo restores the annotation and anchor state associated with the undone edit.

Annotations are distinct from temporary [source trackers](tracking.md). A disposable tracker is useful for a hover or snippet session, but it does not create a saved bookmark. For rich transient anchors, use `RichAddressMap` or the automatic mapping provided by rich presentation producers.
