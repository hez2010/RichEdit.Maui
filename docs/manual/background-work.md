---
title: Background work and checked edits
description: Analyze immutable snapshots, reject stale results, and safely apply edits calculated by language or search tools.
---

# Background work and checked edits

Capture a snapshot on the UI thread and analyze it in the background. Back on the UI thread, pass its `Revision` when applying the result. This checks both the document identity and version.

## Apply a batch from one snapshot

This example changes two occurrences using their original positions:

```csharp
editor.Document = CodeDocument.FromPlainText("var color = color;");
var snapshot = editor.Document.CurrentSnapshot;

RichTextEdit[] edits =
[
    new(new RichTextRange(4, 5), "backgroundColor"),
    new(new RichTextRange(12, 5), "backgroundColor"),
];

bool applied = editor.TryApplyEdits(snapshot.Revision, edits,
    options: new RichTextEditOptions(undoDescription: "Rename color"));
```

Both ranges refer to `snapshot.Text`; the second range does not move to account for the first replacement. Checked edits validate the whole batch, reject overlaps, retain caller order for insertions at the same offset, and normally create one undo unit. Replacement line endings are normalized to LF.

An explicit `selectionAfter` refers to the resulting normalized text. Without it, the current directional selection is mapped through the replacements.

## Rejected edits

`TryApplyEdits` returns `false` for a stale or foreign revision, read-only state, active composition, or a length increase beyond `MaxLength`. Invalid ranges, options, and overlaps throw.

A stale result needs to be recomputed. Substituting the current revision would bypass the check while leaving the edit coordinates tied to old text.

RichEditor exposes the same method for text replacements in rich documents.

## Overlapping requests

Two requests can use the same source revision but different search terms. A separate request counter prevents the older search from replacing newer results:

```csharp
using CodeEdit.Maui;
using Microsoft.Maui.Graphics;
using RichEdit.Maui;

public sealed class SearchHighlighter : IDisposable
{
    private readonly CodeEditor _editor;
    private readonly RichTextDecorationLayer _layer;
    private int _generation;
    private bool _disposed;

    public SearchHighlighter(CodeEditor editor)
    {
        _editor = editor;
        _layer = editor.Decorations.CreateLayer();
    }

    // Construct, call, and dispose on the editor's UI thread.
    public async Task<bool> HighlightAsync(string term, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(term);
        int generation = ++_generation;
        var snapshot = _editor.Document.CurrentSnapshot;

        var ranges = await Task.Run(() =>
        {
            var found = new List<RichTextDecoration>();
            if (term.Length == 0)
                return found;

            var style = new RichTextDecorationStyle { BackgroundColor = Colors.Gold };
            int offset = 0;
            while ((offset = snapshot.Text.IndexOf(term, offset, StringComparison.Ordinal)) >= 0)
            {
                token.ThrowIfCancellationRequested();
                found.Add(new(new RichTextRange(offset, term.Length), style));
                offset += term.Length;
            }
            return found;
        }, token);

        token.ThrowIfCancellationRequested();
        return !_disposed && generation == _generation &&
            _layer.TrySet(snapshot.Revision, ranges);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _generation++;
        _layer.Dispose();
    }
}
```

Create one helper per search UI and call `HighlightAsync` when the text or search term changes. It finds non-overlapping literal matches and returns `false` if another request or edit has overtaken it. Dispose it when closing the search UI; a late result then leaves the removed layer alone.
