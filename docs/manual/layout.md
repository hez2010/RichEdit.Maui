---
title: Layout, hit testing, and composition
description: Native geometry and snapshot-bound coordinate mapping.
---

# Layout, hit testing, and composition

The platform text engine is the geometry authority. Layout captures describe visible native lines and carry document/layout identity. Stale or foreign captures return no result; geometry queries never move the caret.

For source text:

```csharp
var layout = code.TextLayout.Capture();
if (layout is not null)
{
    var bounds = code.TextLayout.GetCaretBounds(layout, code.SelectionState.Active);
    var hit = code.TextLayout.HitTest(layout, new Point(30, 40));
}
```

`Capture` describes committed geometry and can return null during composition or before native layout. `CaptureInput` includes provisional input and marks the result `IsProvisional`. Do not interpret provisional coordinates as a committed edit range.

For rich text, `editor.TextLayout` uses `RichTextPoint`, typed selections, and a `RichEditorLayoutSnapshot`. Its `TextProjection` binds visible line ranges to the exact rich snapshot. Android captures combine visible native text flows and cells in editor coordinates.

`Changed` signals document, presentation, or viewport invalidation. Capture again after scrolling, resizing, changing fonts, or changing widget reservations. `GetSelectionBounds` returns separate rectangles; it does not assume a bidi selection is one rectangle.

`ScrollIntoView` reveals a source range or rich point using native scrolling. Selection and scroll positions are independent between views. Native table hosts preserve a visible rich anchor across structural relayout where a current anchor is available.

Android keeps an explicit reveal pending while native widget reservations finish relayout, so native caret scrolling cannot overwrite the requested target. Pointer input, selection changes, composition, document changes, or completed layout release that request.

`PointerPressed`, `PointerMoved`, and `PointerExited` expose observations for application UI. Keep adornment hit testing and document hit testing distinct. All returned rectangles are device-independent control coordinates, not physical pixels.
