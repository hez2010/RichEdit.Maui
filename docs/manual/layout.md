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

Source editors also provide aligned, cancellable scrolling:

```csharp
await code.RevealAsync(position, CodeRevealAlignment.Center,
    causeToken: navigationToken, cancellationToken: cancellationToken);
using var anchor = code.CaptureScrollAnchor();
// The anchor follows edits in this document.
if (anchor is not null)
    await otherPane.RestoreScrollAnchorAsync(anchor, causeToken: navigationToken);
```

`CodeScrollAnchor` stores a tracked source boundary, wrap/bidi affinity, and the caret's offset from the text viewport in DIPs. It retains no native view. Dispose it on the document's owning thread. Restoration requires the same document identity; a disposed anchor or an unavailable view returns `false`. Native scroll limits clamp placement at document edges. A superseding navigation request, pointer press, hardware key, focus loss, composition, document replacement, or handler change ends pending navigation. Cancellation throws `OperationCanceledException`. Requests return `false` if native layout does not settle within two seconds.

`CaptureViewport` returns a value-only observation with a revision, position, affinity, viewport rectangle, and relative offset. Its position is bound to that revision; use a live anchor when it must follow edits. `ViewportChanged` reports changed source-based observations after scrolling or layout. During an explicit navigation request its `CauseToken` is the caller's object. Linked panes should forward the token and ignore events carrying their own synchronization token, avoiding pixel-offset feedback loops. Changes unrelated to a navigation request carry `null`.

Android keeps an explicit reveal pending while native widget reservations finish relayout, so native caret scrolling cannot overwrite the requested target. Pointer input, selection changes, composition, document changes, or completed layout release that request.

`PointerPressed`, `PointerMoved`, and `PointerExited` expose observations for application UI. Keep adornment hit testing and document hit testing distinct. All returned rectangles are device-independent control coordinates, not physical pixels.
