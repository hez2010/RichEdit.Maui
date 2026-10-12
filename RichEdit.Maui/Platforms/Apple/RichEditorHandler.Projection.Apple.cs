#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private void ApplyReservationChanges(RichTextChangeSet changes, TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat)
    {
        var snapshot = DisplayPresentationSnapshot;
        var storage = PlatformView.TextStorage;
        var applying = _applyingDocument;
        _applyingDocument = true;
        _projectionGeneration++;
        try
        {
            storage.BeginEditing();
            try
            {
                foreach (var change in changes.Changes.OfType<RichTextTextChange>())
                    storage.Replace(new(change.OldRange.Start, change.OldRange.Length), change.InsertedText);

                var affected = changes.GetAffectedRange(snapshot.Length);
                ApplyCharacterFormatsIncrementally(snapshot, affected);
                if (VirtualView.SourceSession is not null && !affected.IsEmpty)
                {
                    using var attributes = CreateParagraphAttributes(snapshot.DefaultParagraphFormat);
                    storage.AddAttributes(attributes, new(affected.Start, affected.Length));
                }
                foreach (var paragraph in VirtualView.SourceSession is null ? snapshot.Paragraphs : [])
                {
                    var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
                    if (paragraph.Start > affected.End || end < affected.Start || paragraph.Start == end)
                        continue;

                    using var attributes = CreateParagraphAttributes(paragraph.Format);
                    storage.AddAttributes(attributes, new(paragraph.Start, end - paragraph.Start));
                }
                if (!affected.IsEmpty)
                {
                    var range = new NSRange(affected.Start, affected.Length);
                    storage.RemoveAttribute(UIStringAttributeKey.Link, range);
                    storage.RemoveAttribute(UIStringAttributeKey.Attachment, range);
                    storage.RemoveAttribute(ImageMetadataKey, range);
                    foreach (var link in snapshot.Links.Where(link => link.End > affected.Start && link.Start < affected.End))
                    {
                        var start = Math.Max(link.Start, affected.Start);
                        var end = Math.Min(link.End, affected.End);
                        using var target = new NSString(link.Target);
                        storage.AddAttribute(UIStringAttributeKey.Link, target, new(start, end - start));
                    }

                    foreach (var image in snapshot.Images.Where(image => image.Position >= affected.Start && image.Position < affected.End))
                        ApplyImage(storage, image);
                }
            }
            finally
            {
                storage.EndEditing();
            }

            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            PlatformView.UpdatePlaceholderVisibility();
        }
        finally
        {
            _applyingDocument = applying;
        }
    }

    private static readonly NSString DisplayReservationKey = new("RichEdit.Maui.DisplayReservation");

    private sealed class DisplayReservationMetadata(RichTextDisplayMarker marker) : NSObject
    {
        internal RichTextDisplayMarker Marker { get; } = marker;
    }

    private partial void WriteDisplayReservationMetadata()
    {
        var text = PlatformView.TextStorage;
        var markers = NativeProjection.Markers.ToDictionary(static item => item.Position, static item => item.Marker);
        var retained = new HashSet<int>();
        var removed = new List<NSRange>();
        text.EnumerateAttribute(DisplayReservationKey, new(0, text.Length), NSAttributedStringEnumeration.None,
            (NSObject value, NSRange range, ref bool stop) =>
            {
                if (value is not DisplayReservationMetadata metadata)
                    return;
                if (range.Length == 1 && markers.TryGetValue((int)range.Location, out var marker) && metadata.Marker == marker)
                    retained.Add((int)range.Location);
                else removed.Add(range);
            });
        // NSTextStorage moves these attributes with native edits. Keep matching
        // metadata instead of rewriting every reservation after each keystroke.
        if (removed.Count == 0 && retained.Count == markers.Count)
            return;

        var applying = _applyingDocument;
        _applyingDocument = true;
        text.BeginEditing();
        try
        {
            foreach (var range in removed)
                text.RemoveAttribute(DisplayReservationKey, range);
            foreach (var (position, marker) in markers)
                if (!retained.Contains(position))
                    text.AddAttribute(DisplayReservationKey, new DisplayReservationMetadata(marker), new(position, 1));
        }
        finally
        {
            text.EndEditing();
            _applyingDocument = applying;
        }
    }

    private partial RichTextDisplayProjection ReadDisplayReservationMetadata(string text)
    {
        var spans = new List<(RichTextDisplayMarker, int, int)>();
        PlatformView.TextStorage.EnumerateAttribute(DisplayReservationKey, new(0, text.Length), NSAttributedStringEnumeration.None,
            (NSObject value, NSRange range, ref bool stop) =>
            {
                if (value is DisplayReservationMetadata metadata)
                    spans.Add((metadata.Marker, (int)range.Location, (int)range.Length));
            });
        return RichTextDisplayProjection.FromNative(spans, text);
    }

    private partial Point? GetDisplayReservationBaseline(int position)
    {
        var layout = PlatformView.LayoutManager;
        NativeGeometryQueryCount += 4;
        var glyphs = layout.GetGlyphRange(new NSRange(position, 1));
        var rect = layout.GetBoundingRect(glyphs, PlatformView.TextContainer);
        var line = layout.GetLineFragmentRect((nuint)glyphs.Location, out var lineGlyphs);
        var baseline = line.Y + layout.GetLocationForGlyph((nuint)lineGlyphs.Location).Y;
        var inset = PlatformView.TextContainerInset;
        var offset = PlatformView.ContentOffset;
        return new(rect.X + inset.Left - offset.X, baseline + inset.Top - offset.Y);
    }
}
#endif
