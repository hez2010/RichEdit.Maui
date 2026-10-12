#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using RichEdit.Core;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple
{
    internal sealed class NativeListMarkerLabel : UILabel
    {
        internal NativeListMarkerLabel()
        {
            UserInteractionEnabled = false;
            IsAccessibilityElement = false;
            Lines = 1;
        }
    }
}

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        private static readonly NSString StructuredListMarkerKey = new("RichEdit.Maui.ListMarker");
        private sealed class StructuredListMarker(string text, double indent) : NSObject
        {
            internal string Text { get; } = text;
            internal double Indent { get; } = indent;
        }
        private readonly Dictionary<int, Platforms.Apple.NativeListMarkerLabel> _listMarkerViews = [];
        private bool _structuredHasListMarkers;

        partial void IncludeNativeAdornmentLayout(ref bool required) =>
            required |= _listMarkerViews.Count != 0 || DisplayPresentationSnapshot.Lists.Count != 0;

        private static bool ApplyStructuredParagraphPresentation(NSMutableAttributedString content, StructuredRichNativeMap map,
            IReadOnlySet<RichNodeId>? privateParagraphs)
        {
            var markers = new Dictionary<RichNodeId, string>();
            foreach (var list in map.Snapshot.Nodes.Values.OfType<RichList>())
                for (var index = 0; index < list.Items.Count; index++)
                {
                    var item = map.Snapshot.Get<RichListItem>(list.Items[index]);
                    var paragraph = item.Blocks.Select(map.Snapshot.Get<RichBlock>).OfType<RichParagraph>()
                        .FirstOrDefault(paragraph => privateParagraphs?.Contains(paragraph.Id) != true);
                    if (paragraph is null) continue;
                    var number = (int)Math.Min(int.MaxValue, (long)list.StartNumber + index);
                    markers.Add(paragraph.Id, list.Numbered
                        ? RichTextListFormatter.FormatNumber(number, list.NumberStyle switch
                        {
                            RichListNumberingStyle.UpperRoman => RichListNumberStyle.UpperRoman,
                            RichListNumberingStyle.LowerRoman => RichListNumberStyle.LowerRoman,
                            RichListNumberingStyle.UpperLetter => RichListNumberStyle.UpperLetter,
                            RichListNumberingStyle.LowerLetter => RichListNumberStyle.LowerLetter,
                            _ => RichListNumberStyle.Arabic,
                        }) + "." : "•");
                }

            foreach (var paragraph in map.Paragraphs)
            {
                if (paragraph.Start >= content.Length) continue;
                var inTable = false;
                for (var node = map.Snapshot.Get<RichNode>(paragraph.Id); node.Parent is { } parent; node = map.Snapshot.Get<RichNode>(parent))
                    if (node is RichTableCell) { inTable = true; break; }
                var hasMarker = markers.TryGetValue(paragraph.Id, out var marker);
                if (inTable && !hasMarker) continue;
                using var attributes = content.GetAttributes(paragraph.Start, out _);
                if (attributes?[UIStringAttributeKey.ParagraphStyle] is not NSParagraphStyle imported) continue;
                using var style = (NSMutableParagraphStyle)imported.MutableCopy();
                // RTF import encodes a page's right edge as a positive tail indent.
                // Continuous editing uses the authored indent relative to the view.
                if (!inTable) style.TailIndent = (nfloat)(-map.Snapshot.ResolveParagraph(paragraph.Id).TrailingIndent.Points);
                if (hasMarker)
                {
                    content.AddAttribute(StructuredListMarkerKey, new StructuredListMarker(marker!, style.HeadIndent - style.FirstLineHeadIndent), new(paragraph.Start, 1));
                    style.FirstLineHeadIndent = style.HeadIndent;
                }
                // A mutable copy retains the RTF importer's native table blocks.
                content.AddAttribute(UIStringAttributeKey.ParagraphStyle, style,
                    new(paragraph.Start, Math.Min((int)content.Length, paragraph.End + 1) - paragraph.Start));
            }
            return markers.Count != 0;
        }

        internal void UpdateNativeListMarkers()
        {
            if (!_adornmentsConnected || _applyingDocument) return;
            var snapshot = DisplayPresentationSnapshot;
            if (_structuredNativeMap is null ? snapshot.Lists.Count == 0 : !_structuredHasListMarkers)
            {
                ClearNativeListMarkers();
                return;
            }
            var layout = PlatformView.LayoutManager;
            var inset = PlatformView.TextContainerInset;
            var padding = PlatformView.TextContainer.LineFragmentPadding;
            var viewport = PlatformView.Bounds;
            var glyphs = layout.GetGlyphRangeForBoundingRectWithoutAdditionalLayout(
                new(viewport.X - inset.Left, viewport.Y - inset.Top, viewport.Width, viewport.Height), PlatformView.TextContainer);
            var visible = layout.GetCharacterRange(glyphs);
            var markers = new List<(int Position, string Text, double Indent, bool Relative)>();
            if (_structuredNativeMap is not null)
            {
                PlatformView.TextStorage.EnumerateAttribute(StructuredListMarkerKey, visible, NSAttributedStringEnumeration.None,
                    (NSObject value, NSRange range, ref bool stop) =>
                    {
                        if (value is StructuredListMarker marker) markers.Add(((int)range.Location, marker.Text, marker.Indent, true));
                    });
            }
            else
            {
                var counters = new Dictionary<(int Id, int Level), int>();
                foreach (var paragraph in snapshot.Paragraphs)
                {
                    if (paragraph.Start > visible.Location + visible.Length) break;
                    if (paragraph.Format.NativeList is not { } list) continue;
                    var key = (list.Id, list.Level);
                    if (list.Restart || !counters.TryGetValue(key, out var number)) number = list.StartAt;
                    counters[key] = number == int.MaxValue ? number : number + 1;
                    if (paragraph.Start >= visible.Location)
                        markers.Add((paragraph.Start, RichTextListFormatter.FormatMarker(list, number), paragraph.Format.LeadingIndent + paragraph.Format.FirstLineIndent, false));
                }
            }

            var retained = new HashSet<int>();
            foreach (var marker in markers)
            {
                if (marker.Position > PlatformView.TextStorage.Length || FindDisplayFoldRange(marker.Position) is not null) continue;
                using var position = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, marker.Position);
                if (position is null) continue;
                var caret = PlatformView.GetDisplayCaret(position);
                if (caret.Bottom < viewport.Top || caret.Top > viewport.Bottom) continue;
                var attributes = new UIStringAttributes(marker.Position < PlatformView.TextStorage.Length
                    ? PlatformView.TextStorage.GetAttributes(marker.Position, out _) : PlatformView.TypingAttributes2);
                var font = attributes.Font ?? _defaultFont;
                nfloat baseline;
                if (marker.Position < PlatformView.TextStorage.Length)
                {
                    var glyph = layout.GetGlyphIndex((nuint)marker.Position);
                    var line = layout.GetLineFragmentUsedRect(glyph, out _);
                    var empty = PlatformView.TextStorage.Value![marker.Position] == '\n';
                    baseline = inset.Top + line.Y + (empty ? font.Ascender : layout.GetLocationForGlyph(glyph).Y);
                }
                else baseline = inset.Top + layout.ExtraLineFragmentUsedRect.Y + font.Ascender;

                if (!_listMarkerViews.TryGetValue(marker.Position, out var label))
                {
                    label = new();
                    _listMarkerViews.Add(marker.Position, label);
                    PlatformView.AddSubview(label);
                }
                if (label.Text != marker.Text) label.Text = marker.Text;
                if (label.Font is null || !label.Font.IsEqual(font)) label.Font = font;
                var color = attributes.ForegroundColor ?? _defaultTextColor;
                if (label.TextColor is null || !label.TextColor.IsEqual(color)) label.TextColor = color;
                var size = label.SizeThatFits(new(nfloat.MaxValue, nfloat.MaxValue));
                var rtl = PlatformView.GetBaseWritingDirection(position, UITextStorageDirection.Forward) == NSWritingDirection.RightToLeft;
                var x = rtl ? inset.Left + PlatformView.TextContainer.Size.Width - padding - (nfloat)marker.Indent - size.Width
                    : inset.Left + padding + (nfloat)marker.Indent;
                if (marker.Relative)
                    x = (nfloat)(rtl ? Math.Max(caret.X + (nfloat)marker.Indent - size.Width, caret.X + 4)
                        : Math.Min(caret.X - (nfloat)marker.Indent, caret.X - size.Width - 4));
                var frame = new CGRect(x, baseline - font.Ascender, size.Width, size.Height);
                if (label.Frame != frame) label.Frame = frame;
                retained.Add(marker.Position);
            }
            foreach (var key in _listMarkerViews.Keys.Where(key => !retained.Contains(key)).ToArray())
            {
                _listMarkerViews[key].RemoveFromSuperview();
                _listMarkerViews[key].Dispose();
                _listMarkerViews.Remove(key);
            }
        }

        private void ClearNativeListMarkers()
        {
            foreach (var label in _listMarkerViews.Values) { label.RemoveFromSuperview(); label.Dispose(); }
            _listMarkerViews.Clear();
        }
    }
}
#endif
