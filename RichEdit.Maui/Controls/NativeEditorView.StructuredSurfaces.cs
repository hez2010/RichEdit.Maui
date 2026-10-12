using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private readonly List<WeakReference<NativeEditorView>> _structuredSurfaces = [];
    internal event EventHandler? StructuredSurfacesChanged;
    private bool _focusingStructuredSelection;
    private int _structuredSurfaceUpdateDepth;
    // A newly inserted cell may not have a native control yet. Late selection
    // callbacks from old controls must not replace its requested caret. The
    // target surface may report normally; edits, explicit selections and pointer
    // gestures release the request without retaining a native view.
    private (DocumentRevision Revision, long SelectionGeneration, RichTextPoint Point)? _requestedStructuredFocus;
    internal bool IsUpdatingStructuredSurfaces
    {
        get
        {
            var owner = StructuredInputCoordinator ?? this;
            return owner._structuredSurfaceUpdateDepth != 0 || owner._requestedStructuredFocus is { } pending &&
                owner.StructuredSession?.Document.Revision == pending.Revision &&
                owner.StructuredSession.SelectionGeneration == pending.SelectionGeneration &&
                Document.StructuredProjection?.ContainsParagraph(pending.Point.Paragraph) != true;
        }
    }
    private void RequestStructuredFocus()
    {
        RememberStructuredFocusRequest();
        FocusStructuredSelection();
    }
    private void RememberStructuredFocusRequest()
    {
        if (StructuredSession is { } session && _structuredSurfaces.Count != 0 && StructuredPrimaryText is { } text)
            _requestedStructuredFocus = (session.Document.Revision, session.SelectionGeneration, text.Active);
    }
    internal IDisposable BeginStructuredSurfaceUpdate()
    {
        _structuredSurfaceUpdateDepth++;
        return new StructuredSurfaceUpdate(this);
    }
    private sealed partial class StructuredSurfaceUpdate(NativeEditorView owner) : IDisposable
    {
        private NativeEditorView? _owner = owner;
        public void Dispose()
        {
            if (_owner is not { } view) return;
            _owner = null;
            view._structuredSurfaceUpdateDepth--;
        }
    }
    internal Point StructuredInputOffset { get; set; }
    internal Func<Rect>? StructuredInputBoundsProvider { get; set; }
    internal Point CurrentStructuredInputOffset => StructuredInputBoundsProvider?.Invoke().Location ?? StructuredInputOffset;

    internal IEnumerable<NativeEditorView> StructuredSurfaces()
    {
        _structuredSurfaces.RemoveAll(reference => !reference.TryGetTarget(out var surface) || !ReferenceEquals(surface.StructuredInputCoordinator, this) ||
            !surface.HasCurrentStructuredCoordinator);
        return _structuredSurfaces.Select(reference => reference.TryGetTarget(out var surface) ? surface : null).OfType<NativeEditorView>();
    }
    private void RegisterStructuredSurface(NativeEditorView surface)
    {
        if (StructuredSurfaces().Contains(surface)) return;
        _structuredSurfaces.Add(new(surface));
        surface.Focused += OnStructuredSurfaceFocused;
        surface.Unfocused += OnStructuredSurfaceUnfocused;
        surface.TextLayout.Changed += OnStructuredSurfaceLayoutChanged;
        StructuredSurfacesChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UnregisterStructuredSurface(NativeEditorView surface)
    {
        _structuredSurfaces.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, surface));
        surface.Focused -= OnStructuredSurfaceFocused;
        surface.Unfocused -= OnStructuredSurfaceUnfocused;
        surface.TextLayout.Changed -= OnStructuredSurfaceLayoutChanged;
        StructuredSurfacesChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnStructuredSurfaceFocused(object? sender, FocusEventArgs args)
    {
        if (sender is not NativeEditorView surface || !surface.HasCurrentStructuredCoordinator) return;
        if (Composition.IsActive && _structuredInputSurface?.TryGetTarget(out var active) == true && !ReferenceEquals(active, surface) &&
            !TryCompleteNativeComposition()) return;
        RememberStructuredInput(surface);
        if (!_focusingStructuredSelection && !IsUpdatingStructuredSurfaces && StructuredSession?.Composition is null &&
            StructuredSession?.Selection is not (RichCellSelection or RichBlockSelection or RichBoundarySelection))
        {
            if (StructuredSession?.Selection is not RichTextSelectionSet set || set.Primary.Id != surface.Selections.Primary.Id)
                surface.SynchronizeStructuredSelection();
        }
        RefreshStructuredSurfaceCarets();
    }
    private void OnStructuredSurfaceUnfocused(object? sender, FocusEventArgs args) => RefreshStructuredSurfaceCarets();
    private void OnStructuredSurfaceLayoutChanged(object? sender, EventArgs args) => TextLayout.Invalidate();
    private void RefreshStructuredSurfaceCarets()
    {
        foreach (var surface in StructuredSurfaces()) (surface.Handler as RichEditorHandler)?.RefreshSelectionPresentation();
    }
    internal bool HasFocusedStructuredSurface => StructuredSurfaces().Any(surface => surface.IsFocused);
    internal bool DrawScopedPrimaryAsSecondary => HasCurrentStructuredCoordinator && StructuredSession!.Selection is RichTextSelectionSet set &&
        set.Primary.Id != Selections.Primary.Id && set.Any(item => item.Id == Selections.Primary.Id);

    internal NativeEditorView? FindStructuredSurface(RichSelection selection)
    {
        var surfaces = StructuredSurfaces().Where(surface => surface.Document.StructuredProjection is not null).ToArray();
        if (selection is RichBoundarySelection boundary)
        {
            var boundarySurface = surfaces.FirstOrDefault(surface => surface.Document.StructuredScope!.BoundaryOffset(surface.Document.StructuredProjection!, boundary) is not null);
            if (boundarySurface is not null) return boundarySurface;
        }
        var point = selection switch
        {
            RichEdit.Core.RichTextSelection text => text.Active,
            RichTextSelectionSet set => set.Primary.Selection.Active,
            _ => StructuredSession!.Document.CurrentSnapshot.GetSelectedText(selection).FirstOrDefault().Start,
        };
        return surfaces.FirstOrDefault(surface => surface.Document.StructuredProjection!.ContainsParagraph(point.Paragraph));
    }
    internal bool FocusStructuredSelection()
    {
        RememberStructuredFocusRequest();
        if (StructuredSession is null || FindStructuredSurface(StructuredSession.Selection) is not { } surface) return false;
#if ANDROID
        if (surface.Handler is RichEditorHandler handler)
        {
            var native = handler.PlatformView;
            // MAUI queues Android focus work. Keep native traversal and the
            // shared selection update in that same main-loop operation.
            native.Post(() =>
            {
                if (ReferenceEquals(surface.Handler, handler) && ReferenceEquals(FindStructuredSurface(StructuredSession!.Selection), surface))
                    Focus(native.RequestFocus);
            });
            return true;
        }
#endif
        return Focus(surface.Focus);

        bool Focus(Func<bool> request)
        {
            using var selectionUpdate = BeginStructuredSurfaceUpdate();
            _focusingStructuredSelection = true;
            try
            {
                RememberStructuredInput(surface);
                var focused = request();
                if (focused) surface.OnStructuredSelectionChanged(this, EventArgs.Empty);
                return focused;
            }
            finally { _focusingStructuredSelection = false; }
        }
    }
    internal void UnfocusStructuredSurface()
    {
        foreach (var surface in StructuredSurfaces().Where(surface => surface.IsFocused)) surface.Unfocus();
    }

    private bool MoveStructuredSelections(EditorKey direction, bool extend, bool byWord)
    {
        if (Composition.IsActive || StructuredSession?.Selection is not (RichEdit.Core.RichTextSelection or RichTextSelectionSet)) return false;
        var rootProjection = Document.StructuredProjection!;
        var moved = new List<TextSelection>();
        foreach (var selection in Selections)
        {
            if (rootProjection.GetPoint(selection.Anchor) is not { } anchor || rootProjection.GetPoint(selection.Active) is not { } active) return false;
            var surface = FindStructuredSurface(new RichEdit.Core.RichTextSelection(anchor, active));
            if (surface?.Handler is not RichEditorHandler handler || surface.Document.StructuredProjection is not { } scoped) return false;
            var surfaceOffset = surface.CurrentStructuredInputOffset;
            var local = selection with { Anchor = scoped.ContainsParagraph(anchor.Paragraph) ? scoped.GetOffset(anchor) : scoped.GetOffset(active),
                Active = scoped.GetOffset(active), PreferredVisualX = selection.PreferredVisualX - surfaceOffset.X };
            if (handler.NavigateSelection(local, direction, extend, byWord) is not { } result || scoped.GetPoint(result.Anchor) is not { } nextAnchor ||
                scoped.GetPoint(result.Active) is not { } nextActive) return false;
            if ((extend || local.IsEmpty) && result.Active == local.Active && direction is EditorKey.Left or EditorKey.Right &&
                (local.Active == 0 || local.Active == scoped.Text.Length))
            {
                var forward = scoped.Text.Length == 0 ? direction == EditorKey.Right : local.Active == scoped.Text.Length;
                var index = 0;
                while (index < rootProjection.Paragraphs.Length && rootProjection.Paragraphs[index].Paragraph != active.Paragraph) index++;
                index += forward ? 1 : -1;
                if (index >= 0 && index < rootProjection.Paragraphs.Length && !scoped.ContainsParagraph(rootProjection.Paragraphs[index].Paragraph))
                {
                    var paragraph = rootProjection.Paragraphs[index];
                    nextActive = nextAnchor = new RichTextPoint(active.Story, paragraph.Paragraph, forward ? 0 : paragraph.Range.Length);
                    result = result with { PreferredVisualX = null };
                }
            }
            if (direction is EditorKey.Up or EditorKey.Down && handler.GetTextCaretBounds(local.Active, (RichTextCaretAffinity)local.Affinity) is { } previousCaret &&
                handler.GetTextCaretBounds(result.Active, (RichTextCaretAffinity)result.Affinity) is { } movedCaret && Math.Abs(previousCaret.Center.Y - movedCaret.Center.Y) < 1)
            {
                var x = selection.PreferredVisualX ?? previousCaret.X + surfaceOffset.X;
                var y = previousCaret.Center.Y + surfaceOffset.Y;
                var forward = direction == EditorKey.Down;
                var candidates = StructuredSurfaces().Where(view => !ReferenceEquals(view, surface))
                    .Select(view => (View: view, Handler: view.Handler as RichEditorHandler, Projection: view.Document.StructuredProjection))
                    .Where(item => item.Handler is not null && item.Projection is not null)
                    .Select(item => (item.View, Handler: item.Handler!, Projection: item.Projection!,
                        Caret: item.Handler!.GetTextCaretBounds(forward ? 0 : item.Projection!.Text.Length, forward ? RichTextCaretAffinity.Downstream : RichTextCaretAffinity.Upstream), Offset: item.View.CurrentStructuredInputOffset))
                    .Where(item => item.Caret is { } caret && (forward ? caret.Center.Y + item.Offset.Y > y + previousCaret.Height / 2 :
                        caret.Center.Y + item.Offset.Y < y - previousCaret.Height / 2))
                    .OrderBy(item => Math.Abs(item.Caret!.Value.Center.Y + item.Offset.Y - y))
                    .ThenBy(item => item.View.StructuredInputBoundsProvider?.Invoke() is { } bounds ? Math.Max(bounds.Left - x, Math.Max(0, x - bounds.Right)) :
                        Math.Abs(item.Caret!.Value.X + item.Offset.X - x));
                foreach (var candidate in candidates)
                {
                    if (candidate.Handler.HitTestText(new(x - candidate.Offset.X, candidate.Caret!.Value.Center.Y)) is not { } hit ||
                        candidate.Projection.GetPoint(hit.Position, (CaretAffinity)hit.Affinity) is not { } point) continue;
                    nextActive = nextAnchor = point;
                    result = result with { PreferredVisualX = x - surfaceOffset.X };
                    break;
                }
            }
            if (extend) nextAnchor = anchor;
            result = result with { PreferredVisualX = result.PreferredVisualX + surfaceOffset.X };
            moved.Add(result with { Anchor = rootProjection.GetOffset(nextAnchor), Active = rootProjection.GetOffset(nextActive) });
        }
        Selections = new(moved, Selections.PrimaryIndex);
        RequestStructuredFocus();
        return true;
    }

    private void ObserveCoordinatedPointer(RichTextPointerEventArgs args, bool pressed)
    {
        var coordinator = StructuredInputCoordinator!;
        if (pressed) coordinator._requestedStructuredFocus = null;
        if (pressed && args.Modifiers == EditorKeyModifiers.None && (args.Buttons & RichTextPointerButtons.Primary) != 0 &&
            (!coordinator.Composition.IsActive || coordinator.TryCompleteNativeComposition()) && Document.StructuredProjection is { } projection)
        {
            if (projection.Paragraphs.IsEmpty) StructuredSession!.Selection = Document.StructuredScope!.StartSelection(projection);
            else if (Handler is RichEditorHandler handler && handler.HitTestText(args.Point) is { } hit && projection.GetPoint(hit.Position) is { } point)
                StructuredSession!.Selection = new RichEdit.Core.RichTextSelection(point, point);
        }
        var offset = CurrentStructuredInputOffset;
        var mapped = new RichTextPointerEventArgs(new(args.Point.X + offset.X, args.Point.Y + offset.Y),
            args.DeviceKind, args.Buttons, args.Modifiers);
        coordinator.ObservePointer(mapped, pressed);
    }
}
