---
title: Decorations and syntax colors
description: Transient appearance with explicit ownership and freshness checks.
---

# Decorations and syntax colors

Decorations change view appearance without changing authored content, RTF, saved state, or history. Higher-priority layers override lower-priority layers. Equal priorities compose in creation order; only specified properties override preceding appearance.

Source layers use UTF-16 `TextSpan` values:

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

Native readback separates display overrides from authored formatting. Metric properties such as weight and italic can require native layout; colors ordinarily affect paint. Publication during composition follows the native adapter's deferred presentation boundary. Geometry-dependent diagnostic drawings should use native visible range bounds.
