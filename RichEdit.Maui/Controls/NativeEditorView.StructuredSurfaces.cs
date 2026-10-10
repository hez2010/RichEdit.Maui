using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    private readonly List<WeakReference<NativeEditorView>> _structuredSurfaces = [];
    private bool _focusingStructuredSelection;
    private int _structuredSurfaceUpdateDepth;
    // A newly inserted cell may not have a native control yet. Late selection
    // callbacks from old controls must not replace its requested caret. The
    // target surface may report normally; edits, explicit selections and pointer
    // gestures release the request without retaining a native view.
    private (DocumentRevision Revision, RichTextPoint Point)? _requestedStructuredFocus;
    internal bool IsUpdatingStructuredSurfaces
    {
        get
        {
            var owner = StructuredInputCoordinator ?? this;
            return owner._structuredSurfaceUpdateDepth != 0 || owner._requestedStructuredFocus is { } pending &&
                owner.StructuredSession?.Document.Revision == pending.Revision &&
                Document.StructuredProjection?.ContainsParagraph(pending.Point.Paragraph) != true;
        }
    }
    private void RequestStructuredFocus()
    {
        if (StructuredSession is { } session && _structuredSurfaces.Count != 0 && StructuredPrimaryText is { } text && text.Anchor == text.Active)
            _requestedStructuredFocus = (session.Document.Revision, text.Active);
        FocusStructuredSelection();
    }
    internal IDisposable BeginStructuredSurfaceUpdate()
    {
        _structuredSurfaceUpdateDepth++;
        return new StructuredSurfaceUpdate(this);
    }
    private sealed class StructuredSurfaceUpdate(NativeEditorView owner) : IDisposable
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
    }
    private void UnregisterStructuredSurface(NativeEditorView surface)
    {
        _structuredSurfaces.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, surface));
        surface.Focused -= OnStructuredSurfaceFocused;
        surface.Unfocused -= OnStructuredSurfaceUnfocused;
        surface.TextLayout.Changed -= OnStructuredSurfaceLayoutChanged;
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
        if (StructuredSession is null || FindStructuredSurface(StructuredSession.Selection) is not { } surface) return false;
        _focusingStructuredSelection = true;
        try { RememberStructuredInput(surface); return surface.Focus(); }
        finally { _focusingStructuredSelection = false; }
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
            if (surface?.Handler is not RichEditorHandler handler || surface.Document.StructuredProjection is not { } scoped ||
                !scoped.ContainsParagraph(anchor.Paragraph)) return false;
            var local = selection with { Anchor = scoped.GetOffset(anchor), Active = scoped.GetOffset(active) };
            if (handler.NavigateSelection(local, direction, extend, byWord) is not { } result || scoped.GetPoint(result.Anchor) is not { } nextAnchor ||
                scoped.GetPoint(result.Active) is not { } nextActive) return false;
            moved.Add(result with { Anchor = rootProjection.GetOffset(nextAnchor), Active = rootProjection.GetOffset(nextActive) });
        }
        Selections = new(moved, Selections.PrimaryIndex);
        FocusStructuredSelection();
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
