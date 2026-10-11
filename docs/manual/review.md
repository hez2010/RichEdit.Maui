---
title: Tracked revisions and review
description: Retained change groups, resolution dependencies, and native review views.
---

# Tracked revisions and review

Tracked revisions are authored document state, independent of undo history. Each record retains inserted/deleted content and before/after formatting, resources, structure, and annotation relationships for its transaction.

```csharp
var group = new RichRevisionInfo("Reviewer");
editor.Session.RevisionTracking = group;
editor.ReplaceSelection("Updated wording");
editor.Session.RevisionTracking = null;
```

Reuse a group for one logical change. Revision metadata contains author, group ID, and a bounded UTC timestamp. A composition commit creates a revision; provisional updates do not.

Use `snapshot.GetRevisionResolution(groupId, accept)` to inspect the complete affected closure before presenting a review command. Acceptance includes prerequisites; rejection includes dependents. Whole groups resolve atomically. Untracked edits overlapping unresolved retained state are rejected so later rejection cannot silently overwrite unrelated work.

```csharp
editor.Edit(edit => edit.AcceptRevision(group.Group));
// Or: editor.Edit(edit => edit.RejectRevision(group.Group));
```

Resolution must not share a transaction with unrelated authored edits. It is undoable and does not depend on retaining the original edit in undo history.

## Native review views

`RichReviewView` accepts a canonical `Source` document and `RichReviewMode.Original` or `Final`. It renders a derived read-only snapshot through the native rich editor without creating authoring history. Changes to Source refresh the projection. `Projection.ToSource` and `ToReview` map typed points; deleted-only locations can have no counterpart.

`RichDocumentSnapshot.CreateReviewProjection` provides the same frozen projection for application workflows and supports cancellation. Original reverses pending groups; Final shows their current authored result. There is no inline redline/markup mode.

RTF saved by this library preserves pending revision groups. Other RTF readers may show only the current body and omit revision information. Word tracked-change interchange is not supported.
