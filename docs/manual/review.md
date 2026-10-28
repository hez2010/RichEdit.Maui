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

Resolution must not share a transaction with unrelated authored edits. It is undoable and does not depend on retaining the original edit in undo history.

## Build a revision list

`snapshot.Revisions.Values` contains retained transactions. Group them by `revision.Info.Group` for review UI; one logical group can contain several transactions. `Info` supplies `Author` and `Created`, while the revision exposes changed nodes, styles, assets, comments, and bookmarks. Clear or replace `Session.RevisionTracking` when an authoring group ends; it is view/session state, so configure it on each editing session that should track changes.

## Accept or reject a group

Before a user confirms an accept/reject command, compute the affected group set from a captured snapshot:

```csharp
var snapshot = editor.Document.CurrentSnapshot;
var affected = snapshot.GetRevisionResolution(group.Group, accept: true);
var affectedGroups = affected.Select(revision => revision.Info.Group).Distinct().ToArray();

// Present affectedGroups in application review UI, then apply to that revision:
var result = editor.Edit(edit => edit.AcceptRevision(group.Group),
    expectedRevision: snapshot.Revision);
```

Use `accept: false` with `RejectRevision` for rejection. If the document changes while confirmation is open, refresh the resolution instead of applying an obsolete decision. Check `result.Rejection` for a rejected operation. A checked edit's revision guard includes document identity, so it also protects against document replacement.

## Native review views

`RichReviewView` accepts a canonical `Source` document and `RichReviewMode.Original` or `Final`. It renders a derived read-only snapshot through the native rich editor without creating authoring history. Changes to Source refresh the projection. `Projection.ToSource` and `ToReview` map typed points; deleted-only locations can have no counterpart.

`RichDocumentSnapshot.CreateReviewProjection` provides the same frozen projection for application workflows and supports cancellation. Original reverses pending groups; Final shows their current authored result. There is no inline redline/markup mode.

```csharp
using RichEdit.Core;
using RichEdit.Maui;

var original = new RichReviewView
{
    Source = editor.Document,
    Mode = RichReviewMode.Original,
};
var final = new RichReviewView
{
    Source = editor.Document,
    Mode = RichReviewMode.Final,
};
```

Place each view in a bounded layout area. Review views support independent selection, `SelectAll`, `CopyAsync`, `Focus`, fallback fonts/colors, and `TextLayout`. Subscribe to `ProjectionChanged` when your UI needs to rebuild mappings after source changes. Read `Projection` again instead of retaining points from an old projection. Replacing the editable control's document does not automatically replace a review view's `Source`; bind or assign both to the new document.

RTF saved by this library preserves pending revision groups. Other RTF readers may show only the current body and omit revision information. Word tracked-change interchange is not supported.
