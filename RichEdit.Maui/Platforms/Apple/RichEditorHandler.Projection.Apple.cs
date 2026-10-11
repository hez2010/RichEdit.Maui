#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private (DocumentRevision Revision, int Position, double Top)? _adornmentScrollAnchor;
    private (DocumentRevision Revision, CoreGraphics.CGPoint Offset)? _viewportAfterLayout;
    private bool _restoringViewport;
    private bool _explicitViewportReveal;

    private void OnNativeScrollOffsetChanged()
    {
        if (!_adornmentsConnected || _applyingDocument || _restoringViewport ||
            _adornmentScrollAnchor is null && _viewportAfterLayout is null) return;
        if (PlatformView.Dragging || PlatformView.Decelerating || PlatformView.Tracking)
        { CancelViewportRestoration(); return; }
        if (_viewportAfterLayout is not { } saved || saved.Revision != VirtualView.Document.Revision) return;
        _restoringViewport = true;
        try { OffsetAdornmentScroll(saved.Offset.Y - PlatformView.ContentOffset.Y); }
        finally { _restoringViewport = false; }
    }

    partial void PreserveAdornmentScrollAnchor(int position, double top)
    {
        if (_explicitViewportReveal) return;
        _viewportAfterLayout = null;
        _adornmentScrollAnchor = (VirtualView.Document.Revision, position, top);
    }

    internal void CancelViewportRestoration()
    {
        _explicitViewportReveal = false;
        _adornmentScrollAnchor = null;
        _viewportAfterLayout = null;
    }

    internal void OnExplicitNativeScroll()
    {
        if (!_applyingDocument && !_restoringViewport && !_updatingDisplayProjection)
            CancelViewportRestoration();
    }

    internal void OnNativeLayoutCompleted()
    {
        if (!_adornmentsConnected || ((IElementHandler)this).PlatformView is null || _applyingDocument || _restoringViewport)
            return;
        if (PlatformView.Dragging || PlatformView.Decelerating || PlatformView.Tracking)
        {
            CancelViewportRestoration();
            return;
        }

        var anchor = _adornmentScrollAnchor;
        var viewport = _viewportAfterLayout;
        if (anchor?.Revision != VirtualView.Document.Revision && viewport?.Revision != VirtualView.Document.Revision)
        { CancelViewportRestoration(); return; }
        _restoringViewport = true;
        try
        {
            if (anchor is { } source && source.Revision == VirtualView.Document.Revision &&
                GetTextCaretBoundsCore(NativeProjection.ToDisplayCaret(source.Position), RichTextCaretAffinity.Downstream) is { } bounds)
            {
                var inset = PlatformView.AdjustedContentInset;
                var top = _explicitViewportReveal ? Math.Clamp(source.Top, inset.Top,
                    Math.Max(inset.Top, PlatformView.Bounds.Height - inset.Bottom - bounds.Height)) : source.Top;
                OffsetAdornmentScroll(bounds.Y - top);
            }
            else if (viewport is { } saved && saved.Revision == VirtualView.Document.Revision)
                OffsetAdornmentScroll(saved.Offset.Y - PlatformView.ContentOffset.Y);
        }
        finally { _restoringViewport = false; }

        // Keep the requested viewport through later UIKit layout/selection
        // adjustments. A new revision, user navigation, focus, or explicit reveal
        // releases it; no timer needs to guess when native layout has settled.
    }

    private void ApplyReservationChanges(RichTextChangeSet changes, TextSpan selection,
        RichTextCharacterFormat typingCharacterFormat, RichTextParagraphFormat typingParagraphFormat)
    {
        var snapshot = DisplayPresentationSnapshot;
        var storage = PlatformView.TextStorage;
        var viewport = PlatformView.ContentOffset;
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
            PlatformView.LayoutIfNeeded();
            PlatformView.SetContentOffset(viewport, false);
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
