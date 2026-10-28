---
title: Source folding
description: View-local source folds with canonical content preserved.
---

# Source folding

`CodeEditor.Folding` hides explicit source ranges in one view. It does not remove text from the source document, persistence, clipboard, or shared history.

## Collapse and expand ranges

The application discovers foldable regions and supplies nonempty ranges within the current source:

```csharp
using CodeEdit.Maui;
using TextEdit.Core;

var code = new CodeEditor { Document = new CodeDocument("header\nbody\nfooter") };
code.Folding.Collapse(new TextSpan(6, 5)); // Hide the first newline and body text.
```

`SetCollapsedRanges` replaces the set. `CollapsedRanges` exposes individual entries, and `GetCollapsedRange` returns the effective hidden union at an offset. Nested and intersecting folds retain independent state. `Expand(range)` removes an exact entry; `ExpandAll()` removes all entries.

Change folding on the editor's UI thread after completing native composition. Folding changes during active composition are rejected. Subscribe to `Folding.Changed` when application fold indicators need refreshing.

## Edits and navigation

Fold anchors move through source edits. Boundary insertions remain outside a fold; replacing its entire content removes it. Undo does not recreate discarded transient folds. Document replacement clears the set.

Explicitly expand folds containing a destination before selecting or revealing hidden text. Navigation and reveal commands do not expand folds automatically. Layout queries return source positions, accounting for collapsed folds and visible widgets. Geometry queries do not move selection.
