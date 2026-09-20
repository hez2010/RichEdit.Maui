---
title: Tracked positions and ranges
description: Keep bookmarks and editable placeholders aligned with text changes, and choose boundary and deletion behavior.
---

# Tracked positions and ranges

`Document.Tracking` provides positions and ranges that follow text edits, undo, and redo. Both document types expose this service.

## Track a bookmark

```csharp
using var bookmark = editor.Document.Tracking.TrackPosition(editor.SelectionState.Active);

editor.Selection.ReplaceText("inserted text");
if (bookmark.Position is int position)
{
    editor.SelectionState = new RichTextSelectionState(position, position);
    editor.ScrollIntoView(new RichTextRange(position, 0));
}
```

The default position affinity is `AfterInsertion`: text inserted exactly at the anchor moves the anchor after the new text. Use `BeforeInsertion` to keep it before the new text.

Disposing a handle releases its tracking registration.

## Track an editable placeholder

```csharp
editor.Document = CodeDocument.FromPlainText("name");
using var placeholder = editor.Document.Tracking.TrackRange(
    new RichTextRange(0, 4),
    startAffinity: RichTextTrackingAffinity.BeforeInsertion,
    endAffinity: RichTextTrackingAffinity.AfterInsertion,
    deletion: RichTextTrackingDeletionBehavior.Preserve);

editor.SelectedRange = new RichTextRange(0, 4);
editor.Selection.ReplaceText("customerName");

if (placeholder.Range is RichTextRange range)
    editor.SelectedRange = range;
```

These affinities include insertions at both edges, and `Preserve` keeps the range through replacement, as needed for an editable snippet field.

## Choose boundary behavior

| Handle boundary | Default | Alternative |
| --- | --- | --- |
| Position | After inserted text | Before inserted text |
| Range start | After inserted text | Before inserted text to include it |
| Range end | Before inserted text | After inserted text to include it |
| Deleted source | Invalidate the handle | Preserve at the replacement boundary according to affinity |

The default range policy excludes boundary insertions and invalidates the range when any of its source is removed. An invalidated or disposed handle's `Position` or `Range` returns `null`. Undo does not revive an invalidated handle.

Handles remain attached to their original document when the editor changes documents. `DocumentChanged` is the place to release bookmarks or snippet state associated with the old document.
