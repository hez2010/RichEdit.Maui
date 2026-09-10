using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>Owns explicitly collapsed source ranges for one editor.</summary>
/// <remarks>
/// Folding changes presentation only. It does not change source, selection, saved state, or undo history.
/// Ranges track text edits; text inserted at either boundary stays outside the fold. Replacing or deleting
/// an entire folded range removes it. Replacing the document clears all folds. Navigation never expands folds.
/// Mutations require the editor's UI thread and cannot run during IME composition.
/// </remarks>
public sealed class RichTextFolding
{
    private readonly RichEditor _editor;
    private ImmutableArray<RichTextRange> _ranges = [];
    private ImmutableArray<RichTextRange> _effectiveRanges = [];
    private bool _changed;
#if WINDOWS
    private readonly RichTextDecorationLayer _layer;
    private static readonly RichTextDecorationStyle FoldStyle = new() { Hidden = true };
#endif

    internal RichTextFolding(RichEditor editor)
    {
        _editor = editor;
#if WINDOWS
        _layer = editor.Decorations.CreateLayer();
#endif
    }

    /// <summary>Gets the independently collapsed ranges, ordered by start and then decreasing length.</summary>
    /// <remarks>Overlapping and nested ranges retain their own state; their union is hidden.</remarks>
    public IReadOnlyList<RichTextRange> CollapsedRanges => _ranges;

    /// <summary>Occurs after explicit changes, edit remapping, or document replacement changes the fold state.</summary>
    public event EventHandler? Changed;

    /// <summary>Replaces all collapsed ranges. The application owns fold discovery and expansion policy.</summary>
    /// <param name="ranges">Nonempty UTF-16 source ranges. Duplicates are ignored.</param>
    public void SetCollapsedRanges(IEnumerable<RichTextRange> ranges)
    {
        _editor.VerifyAccess();
        ArgumentNullException.ThrowIfNull(ranges);
        var items = Normalize(ranges);
        foreach (var range in items)
        {
            range.Validate(_editor.Document.Length, nameof(ranges));
            if (range.IsEmpty) throw new ArgumentException("Collapsed ranges must be nonempty.", nameof(ranges));
        }
        if (_ranges.AsSpan().SequenceEqual(items.AsSpan())) return;
        if ((_editor.Handler as IRichEditorHandler)?.IsComposing == true)
            throw new InvalidOperationException("Folding cannot be changed during text composition.");
        SetRangesCore(items);
#if WINDOWS
        _layer.TrySet(_editor.Document.Revision, CreateDecorations());
#endif
        (_editor.Handler as IRichEditorHandler)?.ApplyFolding();
        NotifyChanged();
    }

    /// <summary>Collapses a source range without changing selection or other folds.</summary>
    /// <param name="range">The nonempty UTF-16 source range.</param>
    public void Collapse(RichTextRange range) => SetCollapsedRanges(_ranges.Add(range));

    /// <summary>Removes the exact range from the collapsed set, preserving independently collapsed nested ranges.</summary>
    /// <param name="range">A range from <see cref="CollapsedRanges"/>.</param>
    public void Expand(RichTextRange range) => SetCollapsedRanges(_ranges.Remove(range));

    /// <summary>Expands all ranges.</summary>
    public void ExpandAll() => SetCollapsedRanges([]);

    /// <summary>Gets the union of collapsed ranges containing an offset, or null if that character is visible.</summary>
    /// <param name="offset">A UTF-16 source offset; the end of the document is allowed.</param>
    /// <returns>The containing collapsed interval. Its end is exclusive.</returns>
    public RichTextRange? GetCollapsedRange(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _editor.Document.Length);
        return FindRange(offset);
    }

    internal ImmutableArray<RichTextRange> EffectiveRanges => _effectiveRanges;

    internal RichTextRange? FindRange(int offset)
    {
        var low = 0;
        var high = _effectiveRanges.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var range = _effectiveRanges[middle];
            if (offset < range.Start) high = middle - 1;
            else if (offset >= range.End) low = middle + 1;
            else return range;
        }
        return null;
    }

    internal bool MapThrough(RichTextChangeSet changes)
    {
        if (_ranges.IsEmpty || !changes.IsTextChanged) return false;
        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
            SetRangesCore([]);
        else
        {
            var items = _ranges;
            foreach (var change in changes.Changes.OfType<RichTextTextChange>())
                items = [.. items.Select(range => Map(range, change)).Where(static range => !range.IsEmpty)];
            SetRangesCore(Normalize(items));
        }
#if WINDOWS
        // Folding uses outward boundary affinity, unlike character decorations.
        _layer.Items = [.. CreateDecorations()];
        _editor.Decorations.Invalidate();
#endif
        return _changed;
    }

    internal void Reset()
    {
        if (!_ranges.IsEmpty) SetRangesCore([]);
    }

    internal void NotifyChanged()
    {
        if (!_changed) return;
        _changed = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetRangesCore(ImmutableArray<RichTextRange> ranges)
    {
        if (_ranges.AsSpan().SequenceEqual(ranges.AsSpan())) return;
        _ranges = ranges;
        var effective = new List<RichTextRange>();
        foreach (var range in ranges)
        {
            if (effective.Count > 0 && range.Start <= effective[^1].End)
            {
                var previous = effective[^1];
                effective[^1] = new(previous.Start, Math.Max(previous.End, range.End) - previous.Start);
            }
            else effective.Add(range);
        }
        _effectiveRanges = [.. effective];
        _changed = true;
    }

#if WINDOWS
    private IEnumerable<RichTextDecoration> CreateDecorations() =>
        _effectiveRanges.Select(static range => new RichTextDecoration(range, FoldStyle));
#endif

    private static ImmutableArray<RichTextRange> Normalize(IEnumerable<RichTextRange> ranges) =>
        [.. ranges.Distinct().OrderBy(static range => range.Start).ThenByDescending(static range => range.Length)];

    private static RichTextRange Map(RichTextRange range, RichTextTextChange change)
    {
        var old = change.OldRange;
        if (old.Start <= range.Start && old.End >= range.End) return default;
        return RichTextPositionMap.Map(range, change, RichTextTrackingAffinity.AfterInsertion,
            RichTextTrackingAffinity.BeforeInsertion, RichTextTrackingDeletionBehavior.Preserve)!.Value;
    }
}
