---
title: Widgets and anchored views
description: Inline hints, block actions, and other transient interactive content.
---

# Widgets and anchored views

Widgets are view-owned presentation and never enter authored content, undo, or RTF. Use a data-backed source for many hints or actions; its factories run near the viewport and recycle views outside it.

## Source widgets

Source widgets use UTF-16 offsets in the current document. The offset in this example requires at least 12 UTF-16 units of source:

```csharp
using var source = code.Adornments.CreateSource(
    widget => new Label { Text = (string?)widget.Data },
    (widget, view) => { /* Return view to a bounded application pool. */ });

source.TryPublish(source.BeginUpdate(), new[]
{
    new RichTextWidget("hint-1", 12, new Size(60, 20), Data: "parameter:",
        Options: new() { Placement = RichTextAdornmentPlacement.Inline },
        AccessibleDescription: "Parameter name"),
});
```

## Rich widgets

Rich widgets use paragraph identities and a valid insertion point in the current snapshot:

```csharp
using var source = editor.Presentation.CreateWidgetSource(
    widget => new Label { Text = (string?)widget.Data });

source.TryPublish(source.BeginUpdate(), new[]
{
    new RichEditorWidget("comment-action", point, new Size(60, 24),
        Data: "Review", Options: new() { Placement = RichTextAdornmentPlacement.AboveLine }),
});
```

The rich anchor's affinity controls insertion behavior. Widgets follow structural moves and paragraph splits. Deleted anchors disappear. These producers clear on document replacement and reject stale revision/generation requests. Block placement refers to the containing rich paragraph, including paragraphs in table cells.

Block widgets in tables measure against the containing cell's width, including column spans and excluding cell padding and paragraph indents. They remeasure when columns, padding, or the available viewport change. Inline widgets retain their intrinsic width. An explicitly fixed widget width can overflow its available space.

## Placement and lifetime

| Placement | Behavior |
| --- | --- |
| Overlay | Overlaps text without reserving space |
| LeftMargin | Uses the native text surface's reserved margin |
| Inline | Reserves space at a native text position |
| AboveLine / BelowLine | Reserves a block row next to the anchor's logical line |

`code.Adornments.Add(position, view, options)` is a convenience for a small number of eagerly supplied interactive views. Set `code.Adornments.MarginWidth` or `editor.Presentation.MarginWidth` for margin content. The rich margin applies to each native text surface, including cells.

Stable IDs preserve unchanged live views and measurement. Increment `ContentVersion` when mutable application data changes in place. Estimates must be finite and nonnegative; an explicit inline baseline must fit the estimated height. Retain the source for the feature's lifetime and dispose it on the owner thread when the feature ends.

Keep factories and measurement fast so widget creation does not delay interaction. Factories run only when a usable viewport is available. Provide accessible descriptions for meaningful interactive content. Large sets of rich widgets can reduce responsiveness.
