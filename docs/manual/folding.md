---
title: Source folding
description: View-local source folds with canonical content preserved.
---

# Source folding

`CodeEditor.Folding` hides explicit source ranges in one view. It does not remove text from the source document, persistence, clipboard, or shared history.

```csharp
using TextEdit.Core;

code.Folding.Collapse(new TextSpan(10, 40));
code.Folding.ExpandAll();
```

`SetCollapsedRanges` replaces the set. `CollapsedRanges` exposes individual entries, and `GetCollapsedRange` returns the effective hidden union at an offset. Nested and intersecting folds retain independent state.

Fold anchors move through source edits. Boundary insertions remain outside a fold; replacing its entire content removes it. Undo does not recreate discarded transient folds. Document replacement clears the set.

Reveal or expand a target before navigating to content hidden by a fold. Layout queries return source positions, accounting for collapsed folds and visible widgets. Geometry queries do not move selection.

Structured rich editing uses explicit block and cell semantics; the source folding API is exposed by CodeEditor.
