---
title: Widgets and anchored views
description: Lazy native-flow reservations and transient interactive content.
---

# Widgets and anchored views

Widgets are view-owned presentation and never enter authored content, undo, or RTF. Use a data-backed source for many hints or actions; its factories run near the viewport and recycle views outside it.

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

Stable IDs preserve unchanged live views and measurement. Increment `ContentVersion` when mutable application data changes in place. Estimates must be finite and nonnegative; an explicit inline baseline must fit the estimated height.

Rich widgets use paragraph identities:

```csharp
using var source = editor.Presentation.CreateWidgetSource(
    widget => new Label { Text = (string?)widget.Data });

source.TryPublish(source.BeginUpdate(), new[]
{
    new RichEditorWidget("comment-action", point, new Size(60, 24),
        Data: "Review", Options: new() { Placement = RichTextAdornmentPlacement.AboveLine }),
});
```

The rich anchor's affinity controls insertion behavior. Structural moves, paragraph splits, and table rebuilds route widgets to their current native surface. Deleted anchors disappear. These producers clear on document replacement and reject stale revision/generation requests.

Native RTF table, list, and field surfaces reserve inline and block space with private native attachments. Block placement refers to the containing rich paragraph. The native projection maps these attachments and their block rows back to canonical rich addresses; they do not create authored image resources or paragraphs. Native reservation repair, typing, composition, and undo retain the canonical document.

| Placement | Behavior |
| --- | --- |
| Overlay | Overlaps text without reserving space |
| LeftMargin | Uses the source view's reserved margin |
| Inline | Reserves space at a native text position |
| AboveLine / BelowLine | Reserves a block row next to the anchor's logical line |

`code.Adornments.Add(position, view, options)` is a convenience for a small number of eagerly supplied interactive views. Set `code.Adornments.MarginWidth` or `editor.Presentation.MarginWidth` for margin content. The rich margin applies to each native text surface, including cells.

Reservation publication and measurement use elapsed-work scheduling. One expensive factory or indivisible native call can exceed the slice target. A zero-size or unavailable viewport does not cause all widget factories to run. Always provide accessible descriptions for meaningful interactive content.

Structured native RTF surfaces reconstruct their derived projection when reservations change. This can require broader native layout than a plain-source reservation update; large rich-widget sets do not have a publication-latency guarantee.
