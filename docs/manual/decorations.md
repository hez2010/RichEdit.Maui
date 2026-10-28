---
title: Decorations and syntax colors
description: Transient appearance with explicit ownership and freshness checks.
---

# Decorations and syntax colors

Decorations change view appearance without changing authored content, RTF, saved state, or history. Higher-priority layers override lower-priority layers. Equal priorities compose in creation order; only specified properties override preceding appearance.

## Source decorations

Source layers use UTF-16 `TextSpan` values. Each range must fit the snapshot used for the publication request:

```csharp
using TextEdit.Core;
using RichEdit.Maui;

using var layer = code.Decorations.CreateLayer(priority: 10);
var request = layer.BeginUpdate();
layer.TryPublish(request, new[]
{
    new RichTextDecoration(new TextSpan(0, 5),
        new RichTextDecorationStyle { ForegroundColor = Colors.Blue }),
});
```

Keep the layer alive for the feature's lifetime. Source ranges within a layer must be nonempty, ordered, and nonoverlapping. `Clear` removes appearance; `Dispose` releases ownership.

## Rich decorations

Rich layers use typed addresses:

```csharp
using var layer = editor.Presentation.CreateDecorationLayer(priority: 10);
var request = layer.BeginUpdate();
layer.TryPublish(request, new[]
{
    new RichEditorDecoration(new RichEdit.Core.RichTextSelection(startPoint, endPoint),
        new RichTextDecorationStyle { BackgroundColor = Colors.Yellow }),
});
```

Rich ranges are validated and routed to the native surface containing each paragraph, including table cells. Structural address maps move their endpoints. Removing an anchored paragraph drops its transient presentation; undo does not resurrect dropped items. Replacing the document clears producers.

Decorations do not change authored formatting. Weight and italic can change text layout. Presentation updates may be deferred until composition finishes. Use `TextLayout` range bounds to position diagnostic labels and other native views.

## Compose independent features

Use separate layers for syntax, search matches, and diagnostics so each feature can refresh or clear its own output. `RichTextDecorationStyle` supports `FontWeight`, `Italic`, `ForegroundColor`, `BackgroundColor`, `Underline`, and `UnderlineColor`. Null leaves the preceding appearance unchanged. Decoration underline values use `RichTextUnderlineStyle`; authored rich formatting uses `RichUnderline`.

For example, a low-priority syntax layer can set foreground colors, a search layer can set only the background, and a higher-priority diagnostic layer can set underline and underline color. Overlapping ranges belong in separate layers or must be combined into nonoverlapping runs within one source layer.

Capture `BeginUpdate()` before starting background classification, alongside the immutable source snapshot. Publish the complete new layer on the owner dispatcher with that request. Rejected output should be discarded and recomputed from a current snapshot; calling `BeginUpdate()` after computation would lose the generation check that orders competing requests. Rebuild colors when the application theme changes, even if source text is unchanged.
