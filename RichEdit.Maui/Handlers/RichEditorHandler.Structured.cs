using RichEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private StructuredRichNativeMap? _structuredNativeMap;
    private RichCellSelection? _paintedCellSelection;
    private TextSelectionState? _structuredSelection;
    private sealed record StructuredNativeFormats(IReadOnlyList<RichTextRun> Characters, IReadOnlyDictionary<RichNodeId, RichTextParagraphFormat> Paragraphs);
    private StructuredNativeFormats? _structuredFormats;
    private bool UsesStructuredNativeDocument
    {
        get
        {
#if WINDOWS || IOS || MACCATALYST
            return VirtualView?.Document.StructuredRoot is null &&
                VirtualView?.StructuredSession?.Document.CurrentSnapshot.Nodes.Values.Any(node => node is RichTable or RichList or RichField) == true;
#elif ANDROID
            return VirtualView?.ScopedInputProjection is { } projection && projection.Objects.Any(item => projection.Snapshot.Get<RichInline>(item.Object) is RichField);
#else
            return false;
#endif
        }
    }

    private bool TryApplyStructuredNativeDocument()
    {
        if (!UsesStructuredNativeDocument)
        {
            if (_structuredNativeMap is not null) { _structuredNativeMap = null; _structuredFormats = null; _displayState = null; _projectionSource = null; }
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

    private void PrepareStructuredComposition()
    {
        if (_structuredNativeMap is null || !VirtualView.PrepareStructuredComposition()) return;
        var composition = VirtualView.StructuredSession!.Composition!;
        SetStructuredNativeMap(CreateStructuredNativeDocumentCore(composition.Snapshot, apply: true));
        var selection = (RichEdit.Core.RichTextSelection)composition.Selection;
        SetNativeSelectionCore(new(_structuredNativeMap!.ToNative(selection.Anchor), _structuredNativeMap.ToNative(selection.Active)));
        _paintedCellSelection = null;
    }

    internal void ApplyStructuredCompositionPreview(RichTextDocumentSnapshot snapshot, TextSelectionState selection)
    {
        _displayState = null;
        _projectionSource = null;
        _paintedSnapshot = snapshot;
        CompleteCompositionTracking();
        ApplyDisplayDocumentCore(snapshot, selection.Range.Start, selection.Range.Length);
        _paintedSnapshot = snapshot;
    }

    private void SetStructuredNativeMap(StructuredRichNativeMap map, bool captureFormats = true)
    {
        _structuredNativeMap = map;
        if (captureFormats) _structuredFormats = CaptureStructuredFormatsCore(map);
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
        if (session.Selection is RichCellSelection or RichBoundarySelection or RichBlockSelection or RichObjectSelection && _structuredSelection == native) return true;
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
            if (VirtualView.StructuredInputCoordinator is null) TryUpdateStructuredSelection(selection);
            if (!VirtualView.Composition.IsActive) ReadStructuredFormatting(map);
            return true;
        }
        var prefix = map.NativeText.AsSpan().CommonPrefixLength(text);
        var suffix = 0;
        while (suffix < Math.Min(map.NativeText.Length, text.Length) - prefix && map.NativeText[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        var nativeRange = new TextSelectionState(prefix, map.NativeText.Length - suffix);
        var selected = (session.Composition?.Selection ?? session.Selection) is RichCellSelection or RichBlockSelection
            ? session.Composition?.Selection ?? session.Selection : map.SelectionFromNative(nativeRange);
        var replacement = text.Substring(prefix, text.Length - prefix - suffix).Replace('\r', '\n').Replace('\v', '\u2028');
        if (selected is RichBoundarySelection or RichCellSelection or RichBlockSelection)
        {
            if (VirtualView.Composition.IsActive) return true;
            if (!VirtualView.ApplyStructuredNativeInput(selected, replacement)) TryApplyStructuredNativeDocument();
            return true;
        }
        var before = session.Composition?.Snapshot ?? session.Document.CurrentSnapshot;
        var builder = new RichDocumentEdit(before, session.Composition?.NextNodeId ?? session.Document.NextNodeId);
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
        var projection = VirtualView.ProjectStructuredInput(candidate);
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
    private partial StructuredNativeFormats CaptureStructuredFormatsCore(StructuredRichNativeMap map);

    private void ReadStructuredFormatting(StructuredRichNativeMap map)
    {
        if (_structuredFormats is not { } previous) return;
        var current = CaptureStructuredFormatsCore(map);
        var changes = new List<(RichTextPoint Start, int Length, RichCharacterStyle Format)>();
        var priorIndex = 0;
        foreach (var run in current.Characters)
        {
            while (priorIndex < previous.Characters.Count && previous.Characters[priorIndex].End <= run.Start) priorIndex++;
            for (var index = priorIndex; index < previous.Characters.Count && previous.Characters[index].Start < run.End; index++)
            {
                var prior = previous.Characters[index];
                if (prior.Format == run.Format) continue;
                var start = Math.Max(prior.Start, run.Start);
                var end = Math.Min(prior.End, run.End);
                var format = RichTextDocument.CharacterOverrides(prior.Format, run.Format);
                if (format == new RichCharacterStyle()) continue;
                // Native format runs may include table markers and field instructions.
                // Only authored inline spans can receive the changed properties.
                foreach (var paragraph in map.Paragraphs.Where(paragraph => paragraph.End > start && paragraph.Start < end))
                    foreach (var segment in paragraph.Segments.Where(segment => segment.NativeEnd > start && segment.NativeStart < end))
                    {
                        var first = segment.Atom ? segment.LocalStart : segment.LocalStart + Math.Max(start, segment.NativeStart) - segment.NativeStart;
                        var last = segment.Atom ? segment.LocalEnd : segment.LocalStart + Math.Min(end, segment.NativeEnd) - segment.NativeStart;
                        changes.Add((new(map.Snapshot.Body, paragraph.Id, first), last - first, format));
                    }
            }
        }
        var paragraphs = current.Paragraphs.Where(pair => previous.Paragraphs.TryGetValue(pair.Key, out var prior) && prior != pair.Value)
            .Select(pair => (Id: pair.Key, Format: RichTextDocument.ParagraphOverrides(previous.Paragraphs[pair.Key], pair.Value)))
            .Where(pair => pair.Format != new RichParagraphStyle()).ToArray();
        _structuredFormats = current;
        if (changes.Count == 0 && paragraphs.Length == 0) return;
        VirtualView.StructuredSession!.Edit(edit =>
        {
            foreach (var change in changes)
                edit.ApplyCharacterFormat(new RichEdit.Core.RichTextSelection(change.Start, change.Start with { Offset = change.Start.Offset + change.Length }), change.Format);
            foreach (var change in paragraphs)
                edit.SetParagraphFormat(change.Id, map.Snapshot.Get<RichParagraph>(change.Id).Format.Overlay(change.Format));
        }, new(Origin: TextChangeOrigin.User));
    }

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

}
