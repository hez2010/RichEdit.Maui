namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    // Source and Presentation both use display coordinates. Commit them with their
    // map so native input never observes offsets from a different projection.
    private sealed record DisplayState(RichTextDisplayProjection Projection, RichTextDocumentSnapshot Source, RichTextDocumentSnapshot Presentation);
    private DisplayState? _displayState;
    private RichTextDocumentSnapshot? _projectionSource;
    private RichTextDocumentSnapshot? _paintedSnapshot;
    private double _displayWidth = 1;
    private double _displayViewportWidth = -1;
    private long _displayAdornmentVersion = -1;
    private long _displayDecorationVersion = -1;
    private bool _updatingDisplayProjection;
    private bool _displayPublicationPending;
    private readonly HashSet<RichTextAdornment> _publishedAdornments = [];
    internal RichTextDisplayProjection NativeProjection => _displayState?.Projection ?? RichTextDisplayProjection.Empty;

    private RichTextDocumentSnapshot DisplaySourceSnapshot => NativeProjection.IsEmpty ? VirtualView.Document.CurrentSnapshot : _displayState!.Source;
    private RichTextDocumentSnapshot DisplayPresentationSnapshot => NativeProjection.IsEmpty ? VirtualView.PresentationSnapshot : _displayState!.Presentation;
    private int DisplayInputLimit => VirtualView.MaxLength < 0 ? -1 : (int)Math.Min(int.MaxValue,
        (long)VirtualView.MaxLength + NativeProjection.Reservations.Sum(static item => item.Text.Length));
    private IEnumerable<RichTextRange> DisplayFoldRanges => VirtualView.Folding.EffectiveRanges.Select(range => NativeProjection.ToDisplay(range));
    private RichTextRange? FindDisplayFoldRange(int position) => VirtualView.Folding.FindRange(NativeProjection.ToSource(position)) is { } range ? NativeProjection.ToDisplay(range) : null;

    private bool PrepareDisplayProjection()
    {
        var viewportWidth = GetAdornmentViewport().Width;
        var widthChanged = viewportWidth != _displayViewportWidth;
        if (!_displayPublicationPending && !widthChanged && _displayAdornmentVersion == VirtualView.Adornments.Version &&
            _displayDecorationVersion == VirtualView.Decorations.Version && ReferenceEquals(_projectionSource, VirtualView.Document.CurrentSnapshot)) return false;
        _displayViewportWidth = viewportWidth;
        _displayAdornmentVersion = VirtualView.Adornments.Version;
        _displayDecorationVersion = VirtualView.Decorations.Version;
        if (!VirtualView.Adornments.Any(static item => item.ReservesSpace))
        {
            _displayPublicationPending = false;
            _publishedAdornments.Clear();
            _projectionSource = VirtualView.Document.CurrentSnapshot;
            _displayState = new(RichTextDisplayProjection.Empty, _projectionSource, VirtualView.PresentationSnapshot);
            return true;
        }
        if (widthChanged || NativeProjection.IsEmpty) _displayWidth = Math.Max(1, (CaptureTextLayoutCore()?.TextViewport ?? GetAdornmentViewport()).Width - 1);
        var measured = false;
        foreach (var item in VirtualView.Adornments)
        {
            if (!item.ReservesSpace) continue;
            if (item.IsRealized && (item.MeasureDirty || widthChanged && item.Options.Placement != RichTextAdornmentPlacement.Inline))
            {
                var size = ((IView)item.View).Measure(item.Options.Placement == RichTextAdornmentPlacement.Inline ? double.PositiveInfinity : _displayWidth, double.PositiveInfinity);
                if (item.Options.Baseline > size.Height) throw new ArgumentException("The inline baseline cannot exceed the measured content height.");
                measured |= item.MeasuredSize != size;
                item.MeasuredSize = size;
                item.MeasureDirty = false;
            }
            else if (item.MeasuredSize == Size.Zero || !item.IsRealized && item.MeasureDirty)
            {
                var size = new Size(item.View.WidthRequest >= 0 ? item.View.WidthRequest : item.MeasuredSize.Width > 0 ? item.MeasuredSize.Width : Math.Min(100, _displayWidth),
                    item.View.HeightRequest >= 0 ? item.View.HeightRequest : item.MeasuredSize.Height > 0 ? item.MeasuredSize.Height : Math.Max(20, (VirtualView.FontSize ?? 14) * 1.4));
                measured |= item.MeasuredSize != size;
                item.MeasuredSize = size;
                item.MeasureDirty = false;
            }
        }
        // Publish new native objects in bounded batches. Already displayed objects stay
        // present while the next batch is prepared, including across intervening input.
        _publishedAdornments.RemoveWhere(static item => !item.IsValid || !item.ReservesSpace);
        var added = 0;
        _displayPublicationPending = false;
        foreach (var item in VirtualView.Adornments.OrderByDescending(static item => item.IsRealized))
        {
            if (!item.ReservesSpace || _publishedAdornments.Contains(item)) continue;
            if (added++ < AdornmentBatchSize) _publishedAdornments.Add(item);
            else _displayPublicationPending = true;
        }
        var projection = RichTextDisplayProjection.Create(VirtualView, _displayWidth, _publishedAdornments);
        var reuseSource = ReferenceEquals(_projectionSource, VirtualView.Document.CurrentSnapshot) &&
            NativeProjection.Reservations.AsSpan().SequenceEqual(projection.Reservations.AsSpan()) && !widthChanged && !measured;
        var input = VirtualView.Document.CurrentSnapshot;
        var source = reuseSource ? _displayState!.Source : projection.Project(input, _displayWidth);
        var presentation = projection.Project(VirtualView.PresentationSnapshot, _displayWidth);
        _displayState = new(projection, source, presentation);
        _projectionSource = input;
        return true;
    }

    private void ApplyCurrentDocument(int selectionStart, int selectionLength)
    {
        PrepareDisplayProjection();
        ApplyDisplayDocumentCore(DisplayPresentationSnapshot, selectionStart, selectionLength);
        CompleteDisplayProjection();
    }

    private partial void ApplyDisplayDocumentCore(RichTextDocumentSnapshot document, int selectionStart, int selectionLength);

    private RichTextChangeSet PrepareDisplayChanges(RichTextChangeSet changes)
    {
        var before = _paintedSnapshot ?? VirtualView.Decorations.Project(changes.BeforeSnapshot ?? VirtualView.Document.CurrentSnapshot);
        var hadReservations = !NativeProjection.IsEmpty;
        PrepareDisplayProjection();
        if (!hadReservations && NativeProjection.IsEmpty) return changes;
        return new(changes.VersionBefore, changes.VersionAfter, changes.Origin,
            [.. CreateDisplayDelta(before, DisplayPresentationSnapshot)], changes.Tag,
            changes.SourceToken, before, DisplayPresentationSnapshot, changes.UndoBehavior, changes.UndoDescription);
    }

    private void ApplyIncrementalChangesCore(RichTextChangeSet changes, RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat)
    {
        changes = PrepareDisplayChanges(changes);
        ApplyDisplayChangesCore(changes, selection, typingCharacterFormat, typingParagraphFormat);
        CompleteDisplayProjection();
    }

    private partial void ApplyDisplayChangesCore(RichTextChangeSet changes, RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat);

    private void CompleteDisplayProjection()
    {
        _paintedSnapshot = DisplayPresentationSnapshot;
        WriteDisplayReservationMetadata();
        UpdateDisplayInputLimit();
        if (_displayPublicationPending) QueueAdornmentLayout();
    }

    private void RefreshDisplayProjection(RichTextNativeLayout? previousLayout)
    {
        if (!_adornmentsConnected || _updatingDisplayProjection || _applyingDocument || ((IRichEditorHandler)this).IsComposing) return;
        if (NativeProjection.IsEmpty && !VirtualView.Adornments.Any(static item => item.ReservesSpace)) return;
        _updatingDisplayProjection = true;
        try
        {
            var anchor = previousLayout?.Lines.SelectMany(static line => line.SourceRanges).FirstOrDefault().Start ?? 0;
            var anchorBounds = anchor > 0 ? GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(anchor), RichTextCaretAffinity.Downstream) : null;
            var before = _paintedSnapshot ?? VirtualView.PresentationSnapshot;
            if (!PrepareDisplayProjection()) return;
            var changes = CreateDisplayDelta(before, DisplayPresentationSnapshot);
            if (changes.Count > 0)
            {
                if (anchorBounds is { } anchored) PreserveAdornmentScrollAnchor(anchor, anchored.Y);
                ApplyDisplayChangesCore(new(0, 1, RichTextChangeOrigin.Programmatic, [.. changes], null,
                    beforeSnapshot: before, afterSnapshot: DisplayPresentationSnapshot), VirtualView.SelectedRange,
                    VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
                ApplyFoldingCore();
                if (anchorBounds is { } prior && GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(anchor), RichTextCaretAffinity.Downstream) is { } current &&
                    Math.Abs(current.Y - prior.Y) > 0.5) OffsetAdornmentScroll(current.Y - prior.Y);
            }
            CompleteDisplayProjection();
        }
        finally { _updatingDisplayProjection = false; }
    }

    private static IReadOnlyList<RichTextChange> CreateDisplayDelta(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after)
    {
        var changes = RichTextDocument.CreateDelta(before, after);
        if (changes.OfType<RichTextTextChange>().FirstOrDefault() is not { } text) return changes;
        // Native replacements already move surviving objects. A positional shift is not
        // an image replacement: repainting all later objects made each publication batch
        // rewrite the entire tail of a densely annotated document.
        var shift = text.NewRange.Length - text.OldRange.Length;
        var prior = before.Images.Where(image => image.Position < text.OldRange.Start || image.Position >= text.OldRange.End)
            .Select(image => image.Position < text.OldRange.Start ? image : image with { Position = image.Position + shift });
        var current = after.Images.Where(image => image.Position < text.NewRange.Start || image.Position >= text.NewRange.End);
        return prior.SequenceEqual(current) ? changes.Where(static change => change.Kind != RichTextChangeKind.Image).ToArray() : changes;
    }

    private void UpdateDocumentFromDisplay(RichTextDocumentSnapshot snapshot, int selectionStart, int selectionLength,
        object sourceToken, RichTextChangeOrigin origin = RichTextChangeOrigin.User, bool mergeWithPrevious = false,
        long? projectedVersion = null, RichTextSelectionState? selectionState = null, RichTextDocumentSnapshot? presentation = null)
    {
        var nativePresentation = presentation ?? PreserveNativePresentation(snapshot);
        var map = ReadDisplayReservationMetadata(snapshot.Text);
        var selection = map.ToSource(selectionState ?? RichTextSelectionState.FromRange(new(selectionStart, selectionLength)));
        _displayState = new(map, snapshot, snapshot);
        _paintedSnapshot = nativePresentation;
        var source = map.Unproject(snapshot);
        // Metadata has no native representation. An application can change it while IME
        // painting is deferred, so a native text callback must retain the current value.
        if (!ReferenceEquals(source.Metadata, VirtualView.Document.CurrentSnapshot.Metadata))
            source = source.With(metadata: VirtualView.Document.CurrentSnapshot.Metadata);
        VirtualView.UpdateDocumentFromPlatform(source, selection.Range.Start, selection.Range.Length,
            sourceToken, origin, mergeWithPrevious, projectedVersion, selection);
        // The source commit has moved/inactivated document-owned anchors. Reconcile any removed
        // reservation after native editing finishes, never in the middle of an IME callback.
        _projectionSource = VirtualView.Document.CurrentSnapshot;
        _displayState = _displayState with { Source = map.Project(_projectionSource, _displayWidth) };
        QueueAdornmentLayout();
    }

    private RichTextDocumentSnapshot PreserveNativePresentation(RichTextDocumentSnapshot observed)
    {
        if (NativeProjection.IsEmpty || _paintedSnapshot is not { } painted || painted.Text == observed.Text) return observed;
        // Incremental native readers reuse authored runs outside the edit. Those runs are
        // not a report that native decorations disappeared: retain the last painted state.
        var hint = NativeProjection.ToDisplay(VirtualView.SelectedRange);
        int start, insertedLength;
        if (RichTextDocumentSnapshot.TryGetReplacement(painted.Text, observed.Text, hint, out var inserted))
        {
            start = hint.Start;
            insertedLength = inserted.Length;
        }
        else
        {
            start = painted.Text.AsSpan().CommonPrefixLength(observed.Text);
            var suffix = 0;
            while (suffix < Math.Min(painted.Length, observed.Length) - start && painted.Text[^(suffix + 1)] == observed.Text[^(suffix + 1)]) suffix++;
            insertedLength = observed.Length - start - suffix;
        }
        var end = start + insertedLength;
        var removedEnd = start + painted.Length - observed.Length + insertedLength;
        var shift = end - removedEnd;
        var runs = new List<RichTextRun>();
        foreach (var run in painted.Runs)
        {
            if (run.End <= start) runs.Add(run);
            else if (run.Start >= removedEnd) runs.Add(shift == 0 ? run : run with { Start = run.Start + shift });
            else
            {
                if (run.Start < start) runs.Add(new(run.Start, start - run.Start, run.Format));
                if (run.End > removedEnd) runs.Add(new(end, run.End - removedEnd, run.Format));
            }
        }
        foreach (var run in observed.Runs)
        {
            var first = Math.Max(start, run.Start);
            var last = Math.Min(end, run.End);
            if (first < last) runs.Add(new(first, last - first, run.Format));
        }
        return observed.With(runs: runs);
    }

    private void UpdateSelectionFromDisplay(RichTextSelectionState selection) => VirtualView.UpdateSelectionFromPlatform(NativeProjection.ToSource(selection));
    private void UpdateSelectionFromDisplay(int start, int length) => UpdateSelectionFromDisplay(RichTextSelectionState.FromRange(new(start, length)));
    internal void PrepareNativeSourceKey(EditorKey key, EditorKeyModifiers modifiers = EditorKeyModifiers.None)
    {
        if (NativeProjection.IsEmpty || VirtualView.Composition.IsActive ||
            key is not (EditorKey.Backspace or EditorKey.Delete or EditorKey.Left or EditorKey.Right)) return;
        var selection = VirtualView.SelectionState;
        var arrow = key is EditorKey.Left or EditorKey.Right;
        if (!selection.Range.IsEmpty && (!arrow || (modifiers & EditorKeyModifiers.Shift) == 0)) return;
        var before = NativeProjection.ToDisplay(selection.Active, false);
        var after = NativeProjection.ToDisplay(selection.Active);
        if (before == after) return;
        var forward = key is EditorKey.Delete or EditorKey.Right;
        if (arrow && GetTextCaretBoundsCore(before, RichTextCaretAffinity.Upstream) is { } left &&
            GetTextCaretBoundsCore(after, RichTextCaretAffinity.Downstream) is { } right && Math.Abs(left.Y - right.Y) < 1 && left.X > right.X) forward = !forward;
        var position = forward ? after : before;
        SetNativeSelectionCore(new(selection.Range.IsEmpty ? position : NativeProjection.ToDisplay(selection).Anchor, position));
    }
    internal bool IsDisplayReservationLine(int position)
    {
        if (NativeProjection.IsEmpty) return false;
        var text = DisplaySourceSnapshot.Text;
        position = Math.Clamp(position, 0, text.Length);
        var start = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
        var end = text.IndexOf('\n', position);
        if (end < 0) end = text.Length;
        return NativeProjection.ToSource(start) == NativeProjection.ToSource(end) &&
            (NativeProjection.ContainsDisplayCharacter(start) || end > start && NativeProjection.ContainsDisplayCharacter(end - 1));
    }
    private RichTextSelectionState InferDisplaySelectionState(RichTextRange range)
    {
        var previous = NativeProjection.ToDisplay(VirtualView.SelectionState);
        if (previous.Range == range) return previous;
        if (range.End == previous.Anchor) return new(range.End, range.Start);
        if (range.Start == previous.Anchor) return new(range.Start, range.End);
        return RichTextSelectionState.FromRange(range);
    }

    private partial void WriteDisplayReservationMetadata();
    partial void UpdateDisplayInputLimit();
    private partial RichTextDisplayProjection ReadDisplayReservationMetadata(string text);
    private Rect? GetDisplayReservationBounds(RichTextDisplayReservation reservation)
    {
        if (reservation.ImagePosition < 0 || GetDisplayReservationBaseline(reservation.ImagePosition) is not { } baseline) return null;
        var item = reservation.Adornment;
        var size = item.MeasuredSize;
        var width = item.Options.Placement == RichTextAdornmentPlacement.Inline ? Math.Min(size.Width, _displayWidth) : _displayWidth;
        return new(baseline.X, baseline.Y - (item.Options.Baseline ?? size.Height), width, size.Height);
    }
    private partial Point? GetDisplayReservationBaseline(int position);
    partial void PreserveAdornmentScrollAnchor(int position, double top);
}
