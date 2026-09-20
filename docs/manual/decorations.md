---
title: Decorations and syntax colors
description: Apply temporary highlights and diagnostic marks through independent revision-checked presentation layers.
---

# Decorations and syntax colors

Decorations add temporary colors and underlines for search matches, syntax highlighting, or diagnostics. They leave document content, saved state, and undo history unchanged.

## Create a layer

```csharp
var highlights = editor.Decorations.CreateLayer();
var snapshot = editor.Document.CurrentSnapshot;
int start = snapshot.Text.IndexOf("TODO", StringComparison.Ordinal);

if (start >= 0)
{
    RichTextDecoration[] matches =
    [
        new RichTextDecoration(new RichTextRange(start, 4),
            new RichTextDecorationStyle { BackgroundColor = Colors.Gold }),
    ];
    highlights.TrySet(snapshot.Revision, matches);
}
```

`TrySet` replaces all entries in the <xref:RichEdit.Maui.RichTextDecorationLayer>. `Clear()` empties it; `Dispose()` removes the layer.

## Combine layers

Create one layer for syntax colors, another for diagnostics, and another for search. Layers compose in creation order. A later layer overrides only the properties it supplies, so a search background can coexist with syntax foreground colors and a diagnostic underline.

<xref:RichEdit.Maui.RichTextDecorationStyle> supports `ForegroundColor`, `BackgroundColor`, `Underline`, and `UnderlineColor`. A null property leaves the underlying appearance unchanged. Native underline rendering follows the same platform limitations as authored underlines.

Within one layer, ranges must be nonempty, in bounds, ordered, and non-overlapping. Overlap between layers is supported.

## Updates and stale results

`TrySet` copies the collection and returns `false` if the supplied revision is no longer current. Invalid ranges throw. For background analysis, pass the revision captured with the source snapshot.

Decoration ranges track edits, but syntax colors or search matches may still need recalculation. Replacing the document clears layer contents while keeping the layers reusable.

During native IME composition, painting is deferred. A deferred publication is discarded if its revision becomes stale before painting, so a successful `TrySet` does not guarantee immediate display during composition.

## Theme changes

A layer uses the colors you supply. When the theme changes, republish the existing classifications with new styles; no new source analysis is needed.
