---
title: Anchored views
description: Place interactive MAUI views beside or within text without adding source characters.
---

# Anchored views

`Adornments` anchors MAUI views to source positions for line actions, inline hints, fold controls, or result panels. The views retain normal input and accessibility behavior without adding document characters or undo entries.

## Add a margin action

```csharp
editor.Adornments.MarginWidth = 32;
var button = new Button
{
    Text = "+",
    WidthRequest = 24,
    HeightRequest = 24,
    Padding = 0,
};
SemanticProperties.SetDescription(button, "Add a note on this line");

var adornment = editor.Adornments.Add(
    editor.SelectionState.Active,
    button,
    new RichTextAdornmentOptions
    {
        Placement = RichTextAdornmentPlacement.LeftMargin,
    });
```

The view must be unparented. Use the returned handle's `Options` to change its placement or `Dispose()` to remove it.

## Placement

| Placement | Layout behavior |
| --- | --- |
| `Overlay` | Draw over text at the anchor; reserve no text-flow space |
| `LeftMargin` | Draw in the width reserved by `MarginWidth` |
| `Inline` | Reserve the view's measured width and height in text flow |
| `AboveLine` | Reserve a row above the anchor's logical line |
| `BelowLine` | Reserve a row below the anchor's logical line |

Overlay and margin placements accept an `Offset`. Inline `Baseline` is measured from the top of the view; a null baseline aligns its bottom. Inline content wider than the viewport occupies its own row.

```csharp
var hint = editor.Adornments.Add(
    editor.SelectionState.Active,
    new Label { Text = "inferred: string", FontSize = 12, TextColor = Colors.Gray },
    new RichTextAdornmentOptions { Placement = RichTextAdornmentPlacement.Inline });
```

## Results from background work

When an anchor comes from background analysis, call `TryAdd` with that snapshot's revision:

```csharp
var snapshot = editor.Document.CurrentSnapshot;
var label = new Label { Text = "Result" };
bool added = editor.Adornments.TryAdd(
    snapshot.Revision,
    0,
    label,
    out var result,
    new RichTextAdornmentOptions { Placement = RichTextAdornmentPlacement.BelowLine });
```

Create the view and call `TryAdd` on the UI thread. A stale revision returns `false`; invalid positions or parented views throw.

## Anchor behavior

Anchors follow text edits with `AfterInsertion` affinity by default. Set `Affinity = BeforeInsertion` to keep a view before text inserted at its position. Deletion or replacement containing an anchor removes its view.

Views are clipped to the viewport and hidden when their anchors are folded. Document replacement clears the collection. Undo does not recreate a removed view, so regenerate derived adornments when needed.

`Adornments.Clear()` removes all views from the collection.
