---
title: Range folding
description: Hide explicit source ranges without changing text, history, or source coordinates.
---

# Range folding

`Folding` hides explicit source ranges in either editor. Text, selection offsets, clipboard content, and history still refer to the full document.

## Collapse a source range

```csharp
editor.Document = CodeDocument.FromPlainText("Heading\nHidden body\nNext heading\n");
var snapshot = editor.Document.CurrentSnapshot;
int start = snapshot.GetLineRange(2).Start;
int end = snapshot.GetLineRange(3).Start;
var body = new RichTextRange(start, end - start);

editor.Folding.Collapse(body);
```

Only the supplied range is hidden, leaving the first line visible here. Fold discovery and indicators are supplied by your code; an [adornment](adornments.md) can provide an expand button or ellipsis.

`SetCollapsedRanges` replaces the full set; `Collapse` adds a range; `Expand` removes a particular entry; `ExpandAll` clears the set. Subscribe to `Changed` to refresh fold controls.

## Expand before navigation

Nested and overlapping entries retain independent state. `GetCollapsedRange(offset)` returns their hidden union at an offset, which need not equal a stored entry. Pass actual entries from `CollapsedRanges` to `Expand`:

```csharp
static void Reveal(CodeEditor editor, int offset)
{
    foreach (var range in editor.Folding.CollapsedRanges
        .Where(range => range.Start <= offset && offset < range.End))
    {
        editor.Folding.Expand(range);
    }

    editor.SelectionState = new RichTextSelectionState(offset, offset);
    editor.ScrollIntoView(new RichTextRange(offset, 0));
}
```

Changing selection or scrolling does not expand folds. The helper expands every fold containing the destination before moving the caret.

## Editing folded text

Collapsed ranges track source changes. Insertions at their boundaries remain outside the fold. Replacing an entire folded range removes that fold, and undo does not recreate a discarded fold. Replacing the editor's document clears all folding state.

CodeEditor line numbers continue to identify original source lines, so folded views can show gaps in numbering. Geometry queries omit hidden source, and adornments anchored inside folds are hidden.

Folding changes must run on the UI thread and throw during active IME composition.
