---
title: Layout, hit testing, and composition
description: Query visible native text geometry, observe pointer activity, and coordinate extensions with IME composition.
---

# Layout, hit testing, and composition

`TextLayout` reports visible text geometry for popup placement, selection overlays, and hit testing. Queries leave selection and scrolling unchanged.

## Capture visible geometry

```csharp
var layout = editor.TextLayout.Capture();
if (layout is not null)
{
    var caret = editor.TextLayout.GetCaretBounds(layout, editor.SelectionState.Active);
    var rectangles = editor.TextLayout.GetRangeBounds(layout, editor.SelectedRange);

    foreach (var line in layout.Lines)
        System.Diagnostics.Debug.WriteLine($"{line.Bounds}, baseline {line.Baseline}");
}
```

`Capture()` returns null until native layout is available. A capture contains visible line fragments and source ranges, viewport bounds, the document revision, and a separate layout version. Capture and query it on the UI thread.

Coordinates use device-independent units relative to the editor, including for CodeEditor. For a popup in a surrounding view, use `CreateRelativeTo(ancestor)` and make both the capture and queries through that returned object.

## Geometry and layout changes

Wrapped and bidirectional text can produce several rectangles for one source range. Hidden and offscreen text can have no visible geometry. At ambiguous caret boundaries, choose `RichTextCaretAffinity.Upstream` or `Downstream`.

A stale or foreign capture returns no result. Scrolling, resizing, appearance, folds, adornments, and source edits can invalidate layout. Recapture on `TextLayout.Changed`, even if the text revision is unchanged.

## Observe pointers

```csharp
editor.PointerMoved += (_, args) =>
{
    var layout = editor.TextLayout.Capture();
    if (layout is null)
        return;

    var hit = editor.TextLayout.HitTest(layout, args.Point);
    if (hit is not null)
        System.Diagnostics.Debug.WriteLine($"{hit.Kind} at UTF-16 offset {hit.Position}");
};
```

`PointerMoved`, `PointerPressed`, and `PointerExited` expose point, device kind, buttons, and modifiers. Hit testing identifies text, the nearest position in whitespace, or an adornment. Pointer observations cannot suppress native selection or focus behavior.

## IME composition

`Composition` exposes `IsActive` and a nullable marked `Range`. `CompositionChanged` reports transitions after source and selection have been synchronized. Composition can be active with a null range when the native editor cannot report accurate bounds.

| Operation during composition | Behavior |
| --- | --- |
| Custom hardware shortcuts | Native composition takes precedence |
| `TryApplyEdits` | Returns false |
| Folding mutations | Throw |
| Decoration publication | Painting is deferred and rechecked for staleness |

Defer completion acceptance and format-on-type until composition ends, then analyze the committed text.
