using RichEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private StructuredRichNativeMap? _structuredNativeMap;
    private RichCellSelection? _paintedCellSelection;
    private TextSelectionState? _structuredSelection;
    private bool UsesStructuredNativeDocument
    {
        get
        {
#if WINDOWS || IOS || MACCATALYST
            return VirtualView?.StructuredSession?.Document.CurrentSnapshot.Nodes.Values.Any(node => node is RichTable or RichList or RichField) == true;
#else
            return false;
#endif
        }
    }

    private bool TryApplyStructuredNativeDocument()
    {
        if (!UsesStructuredNativeDocument)
        {
            if (_structuredNativeMap is not null) { _structuredNativeMap = null; _displayState = null; _projectionSource = null; }
            return false;
        }
        var snapshot = VirtualView.StructuredSession!.Document.CurrentSnapshot;
        SetStructuredNativeMap(CreateStructuredNativeDocumentCore(snapshot, apply: true));
        _paintedCellSelection = VirtualView.StructuredSession.Selection as RichCellSelection;
        _structuredSelection = StructuredNativeSelection();
        SetNativeSelectionCore(_structuredSelection.Value);
        CompleteDisplayProjection();
        return true;
    }

    private void SetStructuredNativeMap(StructuredRichNativeMap map)
    {
        _structuredNativeMap = map;
        var projection = RichTextDisplayProjection.FromStructured(map);
        var display = RichTextDisplayProjection.NativeDescription(map);
        _displayState = new(projection, display, display);
        _projectionSource = VirtualView.Document.CurrentSnapshot;
        _displayPublicationPending = false;
        _displayAdornmentVersion = VirtualView.Adornments.Version;
        _displayDecorationVersion = VirtualView.Decorations.Version;
    }

    private TextSelectionState StructuredNativeSelection()
    {
        var map = _structuredNativeMap!;
        if (VirtualView.StructuredSession!.Selection is RichCellSelection)
        {
            var start = map.FromSource(VirtualView.SelectedRange.Start);
            return new(start, start);
        }
        if (VirtualView.StructuredSession!.Selection is RichBoundarySelection boundary)
        {
            if (map.BoundaryPosition(boundary) is { } position) return new(position, position);
            var body = map.Snapshot.Get<RichStory>(map.Snapshot.Body);
            if (boundary.Edge == RichBlockEdge.After && boundary.Block == body.Blocks[^1])
            {
#if WINDOWS
                var end = Math.Max(0, map.NativeText.Length - 1);
#else
                var end = map.NativeText.Length;
#endif
                return new(end, end);
            }
            if (boundary.Edge == RichBlockEdge.Before && boundary.Block == body.Blocks[0]) return new(0, 0);
        }
        return NativeProjection.ToDisplay(VirtualView.SelectionState);
    }

    private bool TryUpdateStructuredSelection(TextSelectionState native)
    {
        if (_structuredNativeMap is not { } map || VirtualView.StructuredSession is not { } session || VirtualView.Composition.IsActive) return false;
        if (native.Range.End > map.NativeText.Length) return false;
        if (session.Selection is RichCellSelection or RichBoundarySelection && _structuredSelection == native) return true;
        var selection = map.SelectionFromNative(native);
        // Preserve stable multi-caret identities when the primary text range is
        // unchanged. A native structural selection deliberately replaces the set.
        if (session.Selection is RichTextSelectionSet set && selection is RichEdit.Core.RichTextSelection text && set.Primary.Selection == text) return true;
        session.Selection = selection;
        return true;
    }

    private bool TryReadStructuredNativeChange(string text, TextSelectionState selection)
    {
        if (_structuredNativeMap is not { } map || VirtualView.StructuredSession is not { } session) return false;
        if (VirtualView.IsReadOnly || session.IsReadOnly) { TryApplyStructuredNativeDocument(); return true; }
        if (text == map.NativeText)
        {
            TryUpdateStructuredSelection(selection);
            return true;
        }
        var prefix = map.NativeText.AsSpan().CommonPrefixLength(text);
        var suffix = 0;
        while (suffix < Math.Min(map.NativeText.Length, text.Length) - prefix && map.NativeText[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        var nativeRange = new TextSelectionState(prefix, map.NativeText.Length - suffix);
        var selected = session.Selection is RichCellSelection cells ? cells : map.SelectionFromNative(nativeRange);
        var replacement = text.Substring(prefix, text.Length - prefix - suffix).Replace('\r', '\n').Replace('\v', '\u2028');
        if (selected is RichBoundarySelection or RichCellSelection)
        {
            if (VirtualView.Composition.IsActive) return true;
            var result = session.Edit(edit => edit.SetSelection(edit.ReplaceSelection(selected, replacement, session.TypingCharacterFormat)),
                new(Origin: TextChangeOrigin.User));
            if (!result.Accepted) TryApplyStructuredNativeDocument();
            return true;
        }
        var before = session.Composition?.Snapshot ?? session.Document.CurrentSnapshot;
        var builder = new RichDocumentEdit(before, Math.Max(session.Document.NextNodeId, before.Nodes.Keys.Max(id => id.Value)));
        RichDocumentSnapshot candidate;
        RichSelection after;
        try
        {
            after = builder.ReplaceSelection(selected, replacement, session.TypingCharacterFormat);
            candidate = builder.Finish().Snapshot;
        }
        catch (ArgumentException)
        {
            // A native edit cannot dissolve a table boundary or split an atom.
            // Keep the canonical root and recover its native projection.
            if (!VirtualView.Composition.IsActive) TryApplyStructuredNativeDocument();
            return true;
        }
        finally { builder.Close(); }
        var projection = candidate.ProjectText();
        var caret = ((RichEdit.Core.RichTextSelection)after).Active;
        var resulting = new TextSelectionState(projection.GetOffset(caret), projection.GetOffset(caret));
        var candidateMap = CreateStructuredNativeDocumentCore(candidate, apply: false);
        if (candidateMap.NativeText == text && candidateMap.SelectionFromNative(selection) is RichEdit.Core.RichTextSelection selectedText)
            resulting = new(projection.GetOffset(selectedText.Anchor), projection.GetOffset(selectedText.Active));
        var snapshot = RichTextDocumentSnapshot.CreateStructuredProjection(projection);
        VirtualView.UpdateDocumentFromPlatform(snapshot, resulting.Range.Start, resulting.Range.Length, _sourceToken, selectionState: resulting);
        if (VirtualView.Composition.IsActive && session.Composition is { } composition)
            SetStructuredNativeMap(CreateStructuredNativeDocumentCore(composition.Snapshot, apply: false));
        else if (_structuredNativeMap?.Snapshot.Revision != session.Document.Revision) TryApplyStructuredNativeDocument();
        return true;
    }

    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply);

    internal void RefreshStructuredSelection()
    {
        if (_structuredNativeMap is not null && _paintedCellSelection != VirtualView.StructuredSession?.Selection as RichCellSelection)
            TryApplyStructuredNativeDocument();
    }

    private (RichColor Background, RichColor Foreground) StructuredSelectionColors()
    {
        var color = GetSecondarySelectionColors().Selection;
        var background = new RichColor((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255));
        static double Linear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        var luminance = 0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);
        return (background, luminance > 0.179 ? new(0, 0, 0) : new(255, 255, 255));
    }

#if ANDROID
    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply) =>
        throw new NotSupportedException("Android table projections use native cell hosts.");
#endif
}
