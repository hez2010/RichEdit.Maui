---
title: Tracked positions and ranges
description: Keep temporary source locations aligned with document edits.
---

# Tracked positions and ranges

`CodeDocument.Tracking` creates temporary source anchors for search results, snippets, navigation history, and hover UI. `TrackPosition` and `TrackRange` return disposable handles that move through edits. They are document-bound application state and are not saved with the text.

## Track a position

Positions use zero-based UTF-16 offsets. Insertion affinity chooses which side of text inserted at that position retains the anchor:

```csharp
using TextEdit.Core;
using CodeEdit.Maui;

var document = new CodeDocument("first\nsecond");
using var caret = document.Tracking.TrackPosition(
    3, PositionAffinity.AfterInsertion);
document.Edit(edit => edit.InsertText(0, "prefix "));
int? current = caret.Position; // 10
```

`AfterInsertion` places the anchor after inserted text; `BeforeInsertion` keeps it before the insertion. Read `Position` when using the anchor rather than retaining the original offset.

## Track a range

The default range affinities exclude insertions at both boundaries. To include text inserted at either boundary, use `BeforeInsertion` for the start and `AfterInsertion` for the end:

```csharp
using var tracked = document.Tracking.TrackRange(new TextSpan(0, 6),
    startAffinity: PositionAffinity.BeforeInsertion,
    endAffinity: PositionAffinity.AfterInsertion,
    deletion: TrackingDeletionBehavior.Preserve);
TextSpan? currentRange = tracked.Range;
```

`TrackingDeletionBehavior.Invalidate` is the default: replacing or deleting a tracked position, or any source covered by a tracked range, invalidates the handle. `Preserve` keeps a mapped boundary or range when that source is replaced. An invalidated or disposed handle has `IsValid = false` and returns null from `Position` or `Range`.

## Lifetime and document replacement

Keep handles alive while their feature is active and dispose them on the document's owner thread when it ends. Invalidated handles are not resurrected by undo.

Replacing a control's document does not move application-held trackers to the replacement document. Dispose old trackers and create new ones for the new file. A source offset from one document must not be applied to another document's selection.
