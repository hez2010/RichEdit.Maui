using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Vertical placement of a revealed source caret within the native text viewport.</summary>
public enum CodeRevealAlignment
{
    /// <summary>Scroll only as far as needed to show the caret.</summary>
    Nearest,
    /// <summary>Place the caret at the top, subject to the native scroll limits.</summary>
    Start,
    /// <summary>Center the caret, subject to the native scroll limits.</summary>
    Center,
    /// <summary>Place the caret at the bottom, subject to the native scroll limits.</summary>
    End,
}

/// <summary>A value-only observation of the first visible source caret and its offset from the text viewport.</summary>
public sealed record CodeViewportSnapshot(DocumentRevision Revision, int Position, CaretAffinity Affinity,
    Point Offset, Rect TextViewport);

/// <summary>A native viewport observation and the application token of the request causing it.</summary>
public sealed class CodeViewportChangedEventArgs(CodeViewportSnapshot viewport, object? causeToken) : EventArgs
{
    /// <summary>Gets the current source-based viewport observation.</summary>
    public CodeViewportSnapshot Viewport { get; } = viewport;
    /// <summary>Gets the caller's token during a reveal or restoration; null for other changes.</summary>
    public object? CauseToken { get; } = causeToken;
}

/// <summary>A disposable source anchor that follows edits, independent of any native view.</summary>
public sealed partial class CodeScrollAnchor : IDisposable
{
    private readonly TextTrackedPosition _position;
    internal CodeDocument Document { get; }
    internal CodeScrollAnchor(CodeDocument document, CodeViewportSnapshot viewport)
    {
        Document = document;
        _position = document.Tracking.TrackPosition(viewport.Position, deletion: TrackingDeletionBehavior.Preserve);
        Affinity = viewport.Affinity;
        Offset = viewport.Offset;
    }
    /// <summary>Gets the current UTF-16 boundary, or null after disposal or document reset.</summary>
    public int? Position => _position.Position;
    /// <summary>Gets the captured wrap/bidi edge.</summary>
    public CaretAffinity Affinity { get; }
    /// <summary>Gets the caret's position relative to the text viewport, in DIPs.</summary>
    public Point Offset { get; }
    /// <summary>Releases the tracked source position on the document's owning thread.</summary>
    public void Dispose() => _position.Dispose();
}

public sealed partial class CodeEditor
{
    private CodeViewportSnapshot? _lastViewport;
    private long _scrollRequestGeneration;
    private object? _scrollCause;

    /// <summary>Occurs when native scrolling, layout, or edits change the source-based viewport.</summary>
    public event EventHandler<CodeViewportChangedEventArgs>? ViewportChanged;

    /// <summary>Captures the first visible source caret without moving selection or focus.</summary>
    public CodeViewportSnapshot? CaptureViewport()
    {
        VerifyAccess();
        if (TextLayout.Capture() is not { } layout || layout.TextViewport is not { Width: > 0, Height: > 0 } viewport)
            return null;
        var row = layout.Lines.FirstOrDefault(line => line.SourceRanges.Count != 0 && line.Bounds.Bottom > viewport.Top);
        if (row is null) return null;
        var hit = TextLayout.HitTest(layout, new(viewport.Left + 0.01,
            Math.Clamp(row.Bounds.Center.Y, viewport.Top + 0.01, viewport.Bottom - 0.01)));
        if (hit is null || TextView.Handler is not RichEditorHandler handler ||
            handler.GetTextCaretBounds(hit.Position, hit.Affinity) is not { } caret) return null;
        return new(Document.Revision, hit.Position, (CaretAffinity)hit.Affinity,
            new(caret.X + layout.Origin.X - viewport.X, caret.Y + layout.Origin.Y - viewport.Y), viewport);
    }

    /// <summary>Captures a live source anchor for restoration in this or another view of the same document.</summary>
    public CodeScrollAnchor? CaptureScrollAnchor() => CaptureViewport() is { } viewport ? new(Document, viewport) : null;

    /// <summary>Reveals a source position using native scrolling without changing selection or focus.</summary>
    /// <returns>True after scrolling settles, including clamping at a document edge; false if superseded or unavailable.</returns>
    public Task<bool> RevealAsync(int position, CodeRevealAlignment alignment = CodeRevealAlignment.Nearest,
        object? causeToken = null, CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        new TextSpan(position, 0).Validate(Document.Length, nameof(position));
        if (!Enum.IsDefined(alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        return ScrollToSourceAsync(position, CaretAffinity.Downstream, null, alignment, causeToken, cancellationToken);
    }

    /// <summary>Restores a tracked source anchor using this view's native geometry and scroll limits.</summary>
    /// <returns>False for a disposed/foreign anchor, unavailable layout, or a superseding scroll request.</returns>
    public Task<bool> RestoreScrollAnchorAsync(CodeScrollAnchor anchor, object? causeToken = null,
        CancellationToken cancellationToken = default)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(anchor);
        return ReferenceEquals(anchor.Document, Document) && anchor.Position is { } position
            ? ScrollToSourceAsync(position, anchor.Affinity, anchor.Offset, CodeRevealAlignment.Start, causeToken, cancellationToken)
            : Task.FromResult(false);
    }

    private async Task<bool> ScrollToSourceAsync(int position, CaretAffinity affinity, Point? offset,
        CodeRevealAlignment alignment, object? causeToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Composition.IsActive || TextView.Handler is not RichEditorHandler handler || !handler.HasTextLayout) return false;
        var document = Document;
        using var target = document.Tracking.TrackPosition(position, deletion: TrackingDeletionBehavior.Preserve);
        var generation = ++_scrollRequestGeneration;
        _scrollCause = causeToken;
        try
        {
            CodeViewportSnapshot? previous = null;
            var stable = 0;
            // Native layout and widget measurement may finish after a scroll call.
            // A bounded settling period avoids retaining a view indefinitely if a
            // backend never supplies usable geometry. Callers can cancel earlier.
            var deadline = Environment.TickCount64 + 2000;
            while (Environment.TickCount64 < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (generation != _scrollRequestGeneration || !ReferenceEquals(document, Document) ||
                    !ReferenceEquals(TextView.Handler, handler) || Composition.IsActive || target.Position is not { } current)
                    return false;
                if (TextLayout.Capture() is { } layout &&
                    handler.GetTextCaretBounds(current, (RichTextCaretAffinity)affinity) is { } native)
                {
                    var caret = new Rect(native.X + layout.Origin.X, native.Y + layout.Origin.Y, native.Width, native.Height);
                    var viewport = layout.TextViewport;
                    var y = offset?.Y ?? alignment switch
                    {
                        CodeRevealAlignment.Start => 0,
                        CodeRevealAlignment.Center => (viewport.Height - caret.Height) / 2,
                        CodeRevealAlignment.End => viewport.Height - caret.Height,
                        _ => Math.Clamp(caret.Top - viewport.Top, 0, Math.Max(0, viewport.Height - caret.Height)),
                    };
                    var x = offset?.X ?? Math.Clamp(caret.Left - viewport.Left, 0, Math.Max(0, viewport.Width - caret.Width));
                    if (!handler.ScrollViewportBy(new(caret.Left - viewport.Left - x, caret.Top - viewport.Top - y))) return false;
                    var observed = CaptureViewport();
                    stable = observed is not null && observed == previous ? stable + 1 : 0;
                    previous = observed;
                    PublishViewport();
                    if (stable >= 2) return true;
                }
                await Task.Delay(16, cancellationToken);
            }
            return false;
        }
        finally
        {
            if (generation == _scrollRequestGeneration) _scrollCause = null;
        }
    }

    private void CancelScrollRequest()
    {
        _scrollRequestGeneration++;
        _scrollCause = null;
    }

    private void PublishViewport()
    {
        if (ViewportChanged is null) return;
        if (CaptureViewport() is not { } viewport || viewport == _lastViewport) return;
        _lastViewport = viewport;
        ViewportChanged?.Invoke(this, new(viewport, _scrollCause));
    }
}
