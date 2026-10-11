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
    private RichTextDocumentSnapshot? _inputPresentationSource;
    private RichTextDocumentSnapshot? _inputPresentation;
    private RichTextDisplayProjection? _inputPresentationMap;
    private readonly RichPresentationCostEstimate _nativeReservationCost = new();
    private int _reservationPublicationCount;

    internal RichTextDocumentSnapshot? GetCompositionPresentation()
    {
        if (_paintedSnapshot is null)
            return null;
        if (!ReferenceEquals(_inputPresentationSource, _paintedSnapshot) || !ReferenceEquals(_inputPresentationMap, NativeProjection))
        {
            _inputPresentationSource = _paintedSnapshot;
            _inputPresentationMap = NativeProjection;
            _inputPresentation = NativeProjection.Unproject(_paintedSnapshot);
        }
        return _inputPresentation;
    }

    internal RichTextDisplayProjection NativeProjection => _displayState?.Projection ?? RichTextDisplayProjection.Empty;

    private RichTextDocumentSnapshot DisplaySourceSnapshot => NativeProjection.IsEmpty ? VirtualView.InputSnapshot : _displayState!.Source;

    private RichTextDocumentSnapshot DisplayPresentationSnapshot => NativeProjection.IsEmpty ? VirtualView.PresentationSnapshot : _displayState!.Presentation;

    private int DisplayInputLimit => VirtualView.StructuredSession is not null || VirtualView.MaxLength < 0 ? -1 : (int)Math.Min(int.MaxValue,
        (long)VirtualView.MaxLength + NativeProjection.Reservations.Sum(static item => item.Text.Length));

    private IEnumerable<TextSpan> DisplayFoldRanges => VirtualView.Folding.EffectiveRanges.Select(range => NativeProjection.ToDisplay(range));

    private TextSpan? FindDisplayFoldRange(int position) => VirtualView.Folding.FindRange(NativeProjection.ToSource(position)) is { } range ? NativeProjection.ToDisplay(range) : null;

    private bool PrepareDisplayProjection()
    {
        if (UsesStructuredNativeDocument)
        {
            if (_structuredNativeMap is null) TryApplyStructuredNativeDocument();
            return false;
        }
        var viewportWidth = GetAdornmentViewport().Width;
        var widthChanged = viewportWidth != _displayViewportWidth;
        if (!_displayPublicationPending && !widthChanged && _displayAdornmentVersion == VirtualView.Adornments.Version &&
            _displayDecorationVersion == VirtualView.Decorations.Version && ReferenceEquals(_projectionSource, VirtualView.Document.CurrentSnapshot))
            return false;

        _displayViewportWidth = viewportWidth;
        _displayAdornmentVersion = VirtualView.Adornments.Version;
        _displayDecorationVersion = VirtualView.Decorations.Version;
        if (VirtualView.SourceSession is null && !VirtualView.Adornments.Any(static item => item.ReservesSpace))
        {
            _displayPublicationPending = false;
            _publishedAdornments.Clear();
            _projectionSource = VirtualView.Document.CurrentSnapshot;
            _displayState = new(RichTextDisplayProjection.Empty, _projectionSource, VirtualView.PresentationSnapshot);
            return true;
        }
        if (widthChanged || NativeProjection.IsEmpty)
            _displayWidth = Math.Max(1, (CaptureTextLayoutCore()?.TextViewport ?? GetAdornmentViewport()).Width - 1);

        var budget = _presentationBudget ?? new RichPresentationBudget();
        var measured = false;
        var measurementPending = false;
        foreach (var item in VirtualView.Adornments)
        {
            if (!item.ReservesSpace)
                continue;
            if (widthChanged && item.Options.Placement != RichTextAdornmentPlacement.Inline) item.MeasureDirty = true;
            if (item.IsRealized && (item.MeasureDirty || widthChanged && item.Options.Placement != RichTextAdornmentPlacement.Inline))
            {
                if (!budget.TryStart()) { measurementPending = true; continue; }
                var size = ((IView)item.View).Measure(item.Options.Placement == RichTextAdornmentPlacement.Inline ? double.PositiveInfinity : _displayWidth, double.PositiveInfinity);
                if (item.Options.Baseline > size.Height)
                    throw new ArgumentException("The inline baseline cannot exceed the measured content height.");

                measured |= item.MeasuredSize != size;
                item.MeasuredSize = size;
                item.MeasureDirty = false;
            }
            else if (item.MeasuredSize == Size.Zero || !item.IsRealized && item.MeasureDirty)
            {
                var view = item.CreatedView;
                var size = new Size(view?.WidthRequest >= 0 ? view.WidthRequest : item.MeasuredSize.Width > 0 ? item.MeasuredSize.Width : Math.Min(100, _displayWidth),
                    view?.HeightRequest >= 0 ? view.HeightRequest : item.MeasuredSize.Height > 0 ? item.MeasuredSize.Height : Math.Max(20, (VirtualView.FontSize ?? 14) * 1.4));
                measured |= item.MeasuredSize != size;
                item.MeasuredSize = size;
                item.MeasureDirty = false;
            }
        }
        // Use elapsed work and observed native publication cost when admitting
        // more reservations. Existing reservations remain present between pulses.
        _publishedAdornments.RemoveWhere(static item => !item.IsValid || !item.ReservesSpace);
        var added = 0;
        _displayPublicationPending = measurementPending;
        foreach (var item in VirtualView.Adornments.OrderByDescending(static item => item.IsRealized))
        {
            if (!item.ReservesSpace || _publishedAdornments.Contains(item))
                continue;
            if (budget.TryStart((added + 1) * _nativeReservationCost.MillisecondsPerItem))
            {
                _publishedAdornments.Add(item);
                added++;
            }
            else
                _displayPublicationPending = true;
        }
        if (added > 0)
        {
            _reservationPublicationCount = added;
        }

        var projection = RichTextDisplayProjection.Create(VirtualView, _displayWidth, _publishedAdornments);
        var reuseSource = ReferenceEquals(_projectionSource, VirtualView.Document.CurrentSnapshot) &&
            NativeProjection.Reservations.AsSpan().SequenceEqual(projection.Reservations.AsSpan()) && !widthChanged && !measured;
        var input = VirtualView.Document.CurrentSnapshot;
        var projectedText = reuseSource ? _displayState!.Source.TextBuffer : projection.ProjectText(input.TextBuffer, NativeProjection,
            _projectionSource?.TextBuffer, _displayState?.Source.TextBuffer);
        var source = reuseSource ? _displayState!.Source : projection.Project(input, _displayWidth, projectedText);
        var presentation = projection.Project(VirtualView.PresentationSnapshot, _displayWidth, projectedText);
        _displayState = new(projection, source, presentation);
        _projectionSource = input;
        return true;
    }

    private void ApplyCurrentDocument(int selectionStart, int selectionLength)
    {
        if (TryApplyStructuredNativeDocument()) return;
        PrepareDisplayProjection();
        var publicationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        ApplyDisplayDocumentCore(DisplayPresentationSnapshot, selectionStart, selectionLength);
        RecordNativePublicationCost(publicationStarted);
        CompleteDisplayProjection();
    }

    private partial void ApplyDisplayDocumentCore(RichTextDocumentSnapshot document, int selectionStart, int selectionLength);

    private RichTextChangeSet PrepareDisplayChanges(RichTextChangeSet changes)
    {
        var before = _paintedSnapshot ?? VirtualView.Decorations.Project(changes.BeforeSnapshot ?? VirtualView.Document.CurrentSnapshot);
        var hadReservations = !NativeProjection.IsEmpty;
        var hadWidgets = !NativeProjection.Reservations.IsEmpty;
        PrepareDisplayProjection();
        if (!hadWidgets && NativeProjection.Reservations.IsEmpty && VirtualView.Document.GetSourceDisplayChange(changes) is { } sourceChange)
            return sourceChange;
        if (!hadReservations && NativeProjection.IsEmpty)
            return RemoveImagePositionChanges(changes);

        return new(changes.VersionBefore, changes.VersionAfter, changes.Origin,
            [.. CreateDisplayDelta(before, DisplayPresentationSnapshot)], changes.Tag,
            changes.SourceToken, before, DisplayPresentationSnapshot, changes.UndoBehavior, changes.UndoDescription);
    }

    private void ApplyIncrementalChangesCore(RichTextChangeSet changes, TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat)
    {
        if (TryApplyStructuredNativeDocument()) return;
        changes = PrepareDisplayChanges(changes);
        var publicationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        ApplyDisplayChangesCore(changes, selection, typingCharacterFormat, typingParagraphFormat);
        RecordNativePublicationCost(publicationStarted);
        CompleteDisplayProjection();
    }

    private partial void ApplyDisplayChangesCore(RichTextChangeSet changes, TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat);

    private void CompleteDisplayProjection()
    {
        _paintedSnapshot = DisplayPresentationSnapshot;
        WriteDisplayReservationMetadata();
        UpdateDisplayInputLimit();
        _reservationPublicationCount = 0;
        if (_displayPublicationPending)
            QueueAdornmentLayout();
    }

    private void RecordNativePublicationCost(long started)
    {
        if (_reservationPublicationCount == 0) return;
        // Measure native application separately. Charging fixed managed
        // projection construction to every new reservation causes batches to
        // shrink toward one while repeating that same fixed cost each pulse.
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _nativeReservationCost.Observe(_reservationPublicationCount, elapsed);
    }

    private void RefreshDisplayProjection(RichTextNativeLayout? previousLayout)
    {
        if (!_adornmentsConnected || _updatingDisplayProjection || _applyingDocument || ((IRichEditorHandler)this).IsComposing)
            return;
        if (NativeProjection.IsEmpty && !VirtualView.Adornments.Any(static item => item.ReservesSpace))
            return;

        _updatingDisplayProjection = true;
        try
        {
            var anchor = previousLayout?.Lines.SelectMany(static line => line.SourceRanges).FirstOrDefault().Start ?? 0;
            var anchorBounds = anchor > 0 ? GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(anchor), RichTextCaretAffinity.Downstream) : null;
            var before = _paintedSnapshot ?? VirtualView.PresentationSnapshot;
            if (!PrepareDisplayProjection())
                return;

            var changes = CreateDisplayDelta(before, DisplayPresentationSnapshot);
            if (changes.Count > 0)
            {
                if (anchorBounds is { } anchored)
                    PreserveAdornmentScrollAnchor(anchor, anchored.Y);

                var publicationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                ApplyDisplayChangesCore(new(0, 1, RichTextChangeOrigin.Programmatic, [.. changes], null,
                    beforeSnapshot: before, afterSnapshot: DisplayPresentationSnapshot), VirtualView.SelectedRange,
                    VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
                ApplyFoldingCore();
                RecordNativePublicationCost(publicationStarted);
                if (anchorBounds is { } prior && GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(anchor), RichTextCaretAffinity.Downstream) is { } current &&
                    Math.Abs(current.Y - prior.Y) > 0.5)
                    OffsetAdornmentScroll(current.Y - prior.Y);
            }

            CompleteDisplayProjection();
        }
        finally
        {
            _updatingDisplayProjection = false;
        }
    }

    private static IReadOnlyList<RichTextChange> CreateDisplayDelta(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after)
    {
        var changes = RichTextDocument.CreateDelta(before, after);
        if (changes.OfType<RichTextTextChange>().FirstOrDefault() is not { } text)
            return changes;
        // Native replacements already move surviving objects. A positional shift is not
        // an image replacement: repainting all later objects made each publication batch
        // rewrite the entire tail of a densely annotated document.
        var shift = text.NewRange.Length - text.OldRange.Length;
        var prior = before.Images.Where(image => image.Position < text.OldRange.Start || image.Position >= text.OldRange.End)
            .Select(image => image.Position < text.OldRange.Start ? image : image with { Position = image.Position + shift });
        var current = after.Images.Where(image => image.Position < text.NewRange.Start || image.Position >= text.NewRange.End);
        return prior.SequenceEqual(current) ? changes.Where(static change => change.Kind != RichTextChangeKind.Image).ToArray() : changes;
    }

    internal static Dictionary<int, RichTextImage> GetSurvivingImages(RichTextChangeSet changes)
    {
        var result = new Dictionary<int, RichTextImage>();
        if (changes.BeforeSnapshot is not { } before || changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
            return result;

        var edits = changes.Changes.OfType<RichTextTextChange>().ToArray();
        foreach (var image in before.Images)
        {
            int? position = image.Position;
            foreach (var edit in edits)
            {
                if (position is not { } current)
                    break;

                position = RichTextPositionMap.Map(current, edit, PositionAffinity.AfterInsertion, TrackingDeletionBehavior.Invalidate);
            }
            if (position is { } retained)
                result.Add(retained, retained == image.Position ? image : image with { Position = retained });
        }

        return result;
    }

    private static RichTextChangeSet RemoveImagePositionChanges(RichTextChangeSet changes)
    {
        if (!changes.IsTextChanged || changes.AfterSnapshot is not { } after ||
            !changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Image))
            return changes;

        var surviving = GetSurvivingImages(changes);
        if (surviving.Count != after.Images.Length || !after.Images.All(image => surviving.GetValueOrDefault(image.Position) == image))
            return changes;

        return new(changes.VersionBefore, changes.VersionAfter, changes.Origin,
            [.. changes.Changes.Where(static change => change.Kind != RichTextChangeKind.Image)], changes.Tag,
            changes.SourceToken, changes.BeforeSnapshot, after, changes.UndoBehavior, changes.UndoDescription)
        { SelectionAfter = changes.SelectionAfter, SelectionOwnerToken = changes.SelectionOwnerToken, SelectionsAfter = changes.SelectionsAfter };
    }

    private void UpdateDocumentFromDisplay(RichTextDocumentSnapshot snapshot, int selectionStart, int selectionLength,
        object sourceToken, RichTextChangeOrigin origin = RichTextChangeOrigin.User, bool mergeWithPrevious = false,
        long? projectedVersion = null, TextSelectionState? selectionState = null, RichTextDocumentSnapshot? presentation = null)
    {
        var nativePresentation = presentation ?? PreserveNativePresentation(snapshot);
        var map = ReadDisplayReservationMetadata(snapshot.Text);
        var selection = map.ToSource(selectionState ?? TextSelectionState.FromRange(new(selectionStart, selectionLength)));
        var source = map.Unproject(snapshot);
        if (VirtualView.SourceSession is not null)
        {
            var restored = VirtualView.RestoreNativeSource(source, selection);
            source = restored.Snapshot;
            selection = restored.Selection;
            map = map.WithLineEndings(restored.Projection);
        }
        _displayState = new(map, snapshot, snapshot);
        _paintedSnapshot = nativePresentation;
        // Metadata has no native representation. An application can change it while IME
        // painting is deferred, so a native text callback must retain the current value.
        if (!ReferenceEquals(source.Metadata, VirtualView.Document.CurrentSnapshot.Metadata))
            source = source.With(metadata: VirtualView.Document.CurrentSnapshot.Metadata);

        VirtualView.UpdateDocumentFromPlatform(source, selection.Range.Start, selection.Range.Length,
            sourceToken, origin, mergeWithPrevious, projectedVersion, selection);
        // The source commit has moved/inactivated document-owned anchors. Reconcile any removed
        // reservation after native editing finishes, never in the middle of an IME callback.
        _projectionSource = VirtualView.InputSnapshot;
        var projected = NativeProjection.ProjectText(_projectionSource.TextBuffer, map, source.TextBuffer, snapshot.TextBuffer);
        _displayState = _displayState with { Source = NativeProjection.Project(_projectionSource, _displayWidth, projected) };
        QueueAdornmentLayout();
    }

    private RichTextDocumentSnapshot PreserveNativePresentation(RichTextDocumentSnapshot observed)
    {
        if (NativeProjection.IsEmpty || _paintedSnapshot is not { } painted || painted.Text == observed.Text)
            return observed;
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
            while (suffix < Math.Min(painted.Length, observed.Length) - start && painted.Text[^(suffix + 1)] == observed.Text[^(suffix + 1)])
                suffix++;

            insertedLength = observed.Length - start - suffix;
        }

        var end = start + insertedLength;
        var removedEnd = start + painted.Length - observed.Length + insertedLength;
        var shift = end - removedEnd;
        var runs = new List<RichTextRun>();
        foreach (var run in painted.Runs)
        {
            if (run.End <= start)
                runs.Add(run);
            else if (run.Start >= removedEnd)
                runs.Add(shift == 0 ? run : run with { Start = run.Start + shift });
            else
            {
                if (run.Start < start)
                    runs.Add(new(run.Start, start - run.Start, run.Format));
                if (run.End > removedEnd)
                    runs.Add(new(end, run.End - removedEnd, run.Format));
            }
        }

        foreach (var run in observed.Runs)
        {
            var first = Math.Max(start, run.Start);
            var last = Math.Min(end, run.End);
            if (first < last)
                runs.Add(new(first, last - first, run.Format));
        }

        return observed.With(runs: runs);
    }

    private void UpdateSelectionFromDisplay(TextSelectionState selection) => VirtualView.UpdateSelectionFromPlatform(NativeProjection.ToSource(selection));

    private void UpdateSelectionFromDisplay(int start, int length) => UpdateSelectionFromDisplay(TextSelectionState.FromRange(new(start, length)));

    internal void PrepareNativeSourceKey(EditorKey key, EditorKeyModifiers modifiers = EditorKeyModifiers.None)
    {
        if (NativeProjection.IsEmpty || VirtualView.Composition.IsActive ||
            key is not (EditorKey.Backspace or EditorKey.Delete or EditorKey.Left or EditorKey.Right))
            return;

        var selection = VirtualView.SelectionState;
        var arrow = key is EditorKey.Left or EditorKey.Right;
        if (!selection.Range.IsEmpty && (!arrow || (modifiers & EditorKeyModifiers.Shift) == 0))
            return;

        var before = NativeProjection.ToDisplay(selection.Active, false);
        var after = NativeProjection.ToDisplay(selection.Active);
        if (before == after)
            return;

        var forward = key is EditorKey.Delete or EditorKey.Right;
        if (arrow && GetTextCaretBoundsCore(before, RichTextCaretAffinity.Upstream) is { } left &&
            GetTextCaretBoundsCore(after, RichTextCaretAffinity.Downstream) is { } right && Math.Abs(left.Y - right.Y) < 1 && left.X > right.X)
            forward = !forward;

        var position = forward ? after : before;
        SetNativeSelectionCore(new(selection.Range.IsEmpty ? position : NativeProjection.ToDisplay(selection).Anchor, position));
    }

    internal bool IsDisplayReservationLine(int position)
    {
        if (NativeProjection.IsEmpty)
            return false;

        var text = DisplaySourceSnapshot.Text;
        position = Math.Clamp(position, 0, text.Length);
        var start = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
        var end = text.IndexOf('\n', position);
        if (end < 0)
            end = text.Length;

        return NativeProjection.ToSource(start) == NativeProjection.ToSource(end) &&
            (NativeProjection.ContainsDisplayCharacter(start) || end > start && NativeProjection.ContainsDisplayCharacter(end - 1));
    }

    private TextSelectionState InferDisplaySelectionState(TextSpan range)
    {
        var previous = NativeProjection.ToDisplay(VirtualView.SelectionState);
        if (previous.Range == range)
            return previous;
        if (range.End == previous.Anchor)
            return new(range.End, range.Start);
        if (range.Start == previous.Anchor)
            return new(range.Start, range.End);

        return TextSelectionState.FromRange(range);
    }

    private partial void WriteDisplayReservationMetadata();

    partial void UpdateDisplayInputLimit();

    private partial RichTextDisplayProjection ReadDisplayReservationMetadata(string text);

    private Rect? GetDisplayReservationBounds(RichTextDisplayReservation reservation)
    {
        if (reservation.ImagePosition < 0 || GetDisplayReservationBaseline(reservation.ImagePosition) is not { } baseline)
            return null;

        var item = reservation.Adornment;
        var size = item.MeasuredSize;
        var width = item.Options.Placement == RichTextAdornmentPlacement.Inline ? Math.Min(size.Width, _displayWidth) : _displayWidth;
        return new(baseline.X, baseline.Y - (item.Options.Baseline ?? size.Height), width, size.Height);
    }

    private partial Point? GetDisplayReservationBaseline(int position);

    partial void PreserveAdornmentScrollAnchor(int position, double top);
}
