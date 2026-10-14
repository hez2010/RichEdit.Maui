using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using RichEdit.Core;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private RichEditBox? _structuredScratch;

    private partial void ApplyStructuredInlineEditCore(TextSpan replaced, string text, RichTextDocumentSnapshot display, TextSpan formatted, TextSelectionState selection)
    {
        var applying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            PlatformView.Document.GetRange(replaced.Start, replaced.End).SetText(TextSetOptions.None, text);
            ApplyCharacterFormatsIncrementally(display, formatted);
            _nativeTextSnapshot = null;
            SetNativeSelectionCore(selection);
            PlatformView.Document.ClearUndoRedoHistory();
        }
        finally { _applyingDocument = applying; }
    }

    private partial StructuredNativeFormats CaptureStructuredFormatsCore(StructuredRichNativeMap map)
        => CaptureStructuredFormats(PlatformView.Document, map);

    private StructuredNativeFormats CaptureStructuredFormats(RichEditTextDocument document, StructuredRichNativeMap map)
    {
        var runs = new List<RichTextRun>();
        var paragraphs = new Dictionary<RichNodeId, RichTextParagraphFormat>();
        var range = document.GetRange(0, 0);
        foreach (var paragraph in map.Paragraphs)
        {
            range.SetRange(paragraph.Start, paragraph.Start);
            paragraphs.Add(paragraph.Id, ReadParagraphFormat(range.ParagraphFormat, null));
            for (var offset = paragraph.Start; offset < paragraph.End;)
            {
                range.SetRange(offset, offset + 1);
                var format = ReadCharacterFormat(range.CharacterFormat);
                range.Expand(TextRangeUnit.CharacterFormat);
                var end = Math.Clamp(range.EndPosition, offset + 1, paragraph.End);
                runs.Add(new(offset, end - offset, format));
                offset = end;
            }
        }
        return new(runs, paragraphs);
    }

    private partial StructuredRichNativeMap CreateStructuredNativeDocumentCore(RichDocumentSnapshot snapshot, bool apply)
    {
        var presentation = StructuredRichPresentation.Create(VirtualView, snapshot);
        var sourceProjection = VirtualView.ProjectStructuredInput(snapshot);
        snapshot = presentation?.Render ?? snapshot;
        // The writer uses an owned MemoryStream, so preparation has no pending
        // asynchronous I/O. All native work remains on the document's UI thread.
        var defaults = PlatformView.Document.GetDefaultCharacterFormat();
        var color = ResolveTextColor();
        var selectionColors = StructuredSelectionColors();
        var prepared = StructuredRichNativeMap.PrepareAsync(snapshot, fallbackFont: VirtualView.FontFamily ?? defaults.Name,
            fallbackSize: RichFontSize.FromPoints(VirtualView.FontSize ?? defaults.Size),
            fallbackColor: color is null ? null : new((byte)Math.Round(color.Red * 255), (byte)Math.Round(color.Green * 255), (byte)Math.Round(color.Blue * 255)),
            selectedCells: (VirtualView.StructuredSession!.Composition?.Selection ?? VirtualView.StructuredSession.Selection) as RichCellSelection,
            selectionBackground: selectionColors.Background, selectionForeground: selectionColors.Foreground,
            nativePrivateParagraphs: presentation?.PrivateParagraphs).GetAwaiter().GetResult();
        var scratch = (_structuredScratch ??= new RichEditBox()).Document;
        scratch.SetText(TextSetOptions.FormatRtf | TextSetOptions.ApplyRtfDocumentDefaults, prepared.Rtf);
        var text = ReadNativeStoryText(scratch);
        var resolved = prepared.Resolve(text);
        scratch.BatchDisplayUpdates();
        try
        {
            foreach (var removal in resolved.Removals.Reverse()) scratch.GetRange(removal.Start, removal.End).Delete(TextRangeUnit.Character, 0);
            ApplyStructuredCellInsets(scratch, resolved.Map);
        }
        finally { scratch.ApplyDisplayUpdates(); }
        scratch.ClearUndoRedoHistory();
        if (ReadNativeStoryText(scratch) != resolved.Map.NativeText) throw new InvalidOperationException("Native rich marker removal changed document coordinates.");
        var result = resolved.Map.WithAuthoredDisplay(RichTextDisplayProjection.NativeDescription(resolved.Map)
            .With(runs: CaptureStructuredFormats(scratch, resolved.Map).Characters));
        if (presentation is not null) result = result.WithPresentation(presentation, sourceProjection);
        if (apply)
        {
            var applying = _applyingDocument;
            var readOnly = PlatformView.IsReadOnly;
            _applyingDocument = true;
            try
            {
                PlatformView.IsReadOnly = false;
                var changedTables = ChangedCellSelectionTables();
                var previous = _structuredNativeMap is { } baseline && HasStructuredNativeBaseline(baseline) ? baseline : null;
                if (previous is not null && changedTables is null && previous.CanApplyFormatting(result))
                {
                    ApplyCharacterFormatsIncrementally(RichTextDisplayProjection.NativeDescription(result), new(0, result.NativeText.Length), RichTextDisplayProjection.NativeDescription(previous));
                    foreach (var paragraph in result.Paragraphs)
                        if (previous.Snapshot.ResolveParagraph(paragraph.Id) != result.Snapshot.ResolveParagraph(paragraph.Id))
                        {
                            var target = PlatformView.Document.GetRange(paragraph.Start, paragraph.Start).ParagraphFormat;
                            var source = scratch.GetRange(paragraph.Start, paragraph.Start).ParagraphFormat;
                            target.SetClone(source);
                            ApplyParagraphTabs(target, ReadParagraphFormat(source, null));
                        }
                }
                else if (previous?.FindBlockReplacement(result, changedTables) is { } replacement)
                {
                    PlatformView.Document.GetRange(replacement.Before.Start, replacement.Before.End).FormattedText =
                        scratch.GetRange(replacement.After.Start, replacement.After.End);
                }
                else
                {
                    var incoming = scratch.GetRange(0, Math.Max(0, resolved.Map.NativeText.Length - 1));
                    var target = PlatformView.Document.GetRange(0, 0);
                    target.SetRange(0, Math.Max(0, target.StoryLength - 1));
                    target.FormattedText = incoming;
                }
                if (ReadNativeStoryText(PlatformView.Document) != resolved.Map.NativeText)
                {
                    // TOM can merge or remove table boundaries during formatted
                    // range replacement. Rebuild from the already validated RTF
                    // before publishing the new address map.
                    PlatformView.Document.SetText(TextSetOptions.FormatRtf | TextSetOptions.ApplyRtfDocumentDefaults, prepared.Rtf);
                    foreach (var removal in resolved.Removals.Reverse())
                        PlatformView.Document.GetRange(removal.Start, removal.End).Delete(TextRangeUnit.Character, 0);
                    ApplyStructuredCellInsets(PlatformView.Document, resolved.Map);
                }
                // The mandatory final paragraph mark is excluded from the text
                // transfer. Its paragraph attributes still belong to the document.
                var terminal = Math.Max(0, resolved.Map.NativeText.Length - 1);
                PlatformView.Document.GetRange(terminal, terminal).ParagraphFormat.SetClone(
                    scratch.GetRange(terminal, terminal).ParagraphFormat);
                _hasNativeLinks = false; // The structured map addresses raw TOM positions, including field gaps.
                _nativeTextSnapshot = null;
                PlatformView.Document.ClearUndoRedoHistory();
                var actual = ReadNativeStoryText(PlatformView.Document);
                if (actual != resolved.Map.NativeText)
                    throw new InvalidOperationException($"The native rich document changed coordinates during transfer (expected {resolved.Map.NativeText.Length}, actual {actual.Length}, common prefix {actual.AsSpan().CommonPrefixLength(resolved.Map.NativeText)}).");
            }
            finally { PlatformView.IsReadOnly = readOnly; _applyingDocument = applying; }
        }
        return result;
    }

    private partial void ApplyStructuredDecorationFormatsCore(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after)
    {
        var applying = _applyingDocument;
        var readOnly = PlatformView.IsReadOnly;
        _applyingDocument = true;
        try
        {
            PlatformView.IsReadOnly = false;
            ApplyCharacterFormatsIncrementally(after, new(0, after.Length), before);
            PlatformView.Document.ClearUndoRedoHistory();
        }
        finally { PlatformView.IsReadOnly = readOnly; _applyingDocument = applying; }
    }

    private partial void ApplyStructuredReservationsCore(IReadOnlyList<RichTextImage> images)
    {
        var applying = _applyingDocument;
        var readOnly = PlatformView.IsReadOnly;
        _applyingDocument = true;
        try
        {
            PlatformView.IsReadOnly = false;
            foreach (var image in images) ApplyImageIncrementally(image);
            PlatformView.Document.ClearUndoRedoHistory();
        }
        finally { PlatformView.IsReadOnly = readOnly; _applyingDocument = applying; }
    }

    private static void ApplyStructuredCellInsets(RichEditTextDocument native, StructuredRichNativeMap map)
    {
        // RichEdit's row margin is uniform and its RTF importer ignores clpad*.
        // Use native paragraph metrics for cell text insets. They are captured
        // in the presentation baseline, so native readback cannot author them.
        var snapshot = map.Snapshot;
        foreach (var cell in snapshot.Nodes.Values.OfType<RichTableCell>())
        {
            if (cell.Format.Padding == default) continue;
            var paragraphs = snapshot.EnumerateParagraphs(cell.Id).ToArray();
            foreach (var id in paragraphs)
            {
                var node = snapshot.Get<RichNode>(id);
                while (node.Parent is { } parent && snapshot.Get<RichNode>(parent) is not RichTableCell) node = snapshot.Get<RichNode>(parent);
                if (node.Parent != cell.Id) continue;
                var position = map.ToNative(new(snapshot.Body, id, 0));
                var format = native.GetRange(position, position).ParagraphFormat;
                var padding = cell.Format.Padding;
                format.SetIndents(format.FirstLineIndent, format.LeftIndent + (float)padding.Left.Points, format.RightIndent + (float)padding.Right.Points);
                if (id == paragraphs[0]) format.SpaceBefore += (float)padding.Top.Points;
                if (id == paragraphs[^1]) format.SpaceAfter += (float)padding.Bottom.Points;
            }
        }
    }
}
