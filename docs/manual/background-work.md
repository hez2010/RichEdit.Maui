---
title: Background work and checked edits
description: Revision checks, producer generations, and cancellation.
---

# Background work and checked edits

Capture an immutable snapshot and source revision before starting background analysis. Return to the owner dispatcher to publish. Cancellation saves work; revision checks provide correctness even when cancellation arrives too late.

## Apply checked edits

```csharp
using TextEdit.Core;

var snapshot = code.Document.CurrentSnapshot;
var selectionGeneration = code.SelectionGeneration;
// Compute replacements from snapshot on a worker.
var edits = new TextReplacement[] { new(new TextSpan(0, 0), "// header\n") };
bool accepted = code.TryApplyEdits(snapshot.Revision, edits,
    expectedSelectionGeneration: selectionGeneration);
```

Every replacement in this batch addresses the original captured snapshot. Reject overlapping ranges before publication. Capture selection generation for completion and similar actions whose validity also depends on the current caret.

For rich content, `RichEditor.Edit` accepts `expectedRevision` and optional `expectedSelectionGeneration`; its callback uses typed rich addresses. Inspect `RichEditResult.Rejection` instead of retrying a stale operation at unrelated coordinates.

## Publish presentation results

A revision alone cannot order two computations made with different settings against unchanged text. Decoration layers and widget sources therefore expose `BeginUpdate` and `TryPublish`. The request captures both document revision and producer generation. A later request, clear, disposal, or document replacement invalidates older publication.

Keep worker results value-only. Do not create MAUI views, access native geometry, or mutate a live document on a worker. Widget factories execute on the UI dispatcher only when content is realized.
