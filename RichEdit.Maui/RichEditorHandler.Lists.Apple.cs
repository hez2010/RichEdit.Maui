#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView
    {
        /// <inheritdoc />
        public override void Draw(CGRect rect)
        {
            base.Draw(rect);
            ProjectionHandler?.DrawListMarkers(rect);
        }
    }
}

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        // UIKit can drop custom attributed-string keys when inserting an item,
        // but retains its NSTextList. Keep the projection data with that owner.
        private sealed class NativeListMetadata(RichTextListFormat format, double firstLineIndent, double textIndent) : NSObject
        {
            internal RichTextListFormat Format { get; } = format;
            internal double FirstLineIndent { get; } = firstLineIndent;
            internal double TextIndent { get; } = textIndent;
        }

        [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_setAssociatedObject")]
        private static partial void SetAssociatedListMetadata(nint list, nint key, nint metadata, nuint policy);

        [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getAssociatedObject")]
        private static partial nint GetAssociatedListMetadata(nint list, nint key);

        private static NativeListMetadata? GetNativeListMetadata(NSParagraphStyle style)
        {
            if ((!OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsMacCatalystVersionAtLeast(16)) ||
                style.TextLists is not { Length: > 0 } lists) return null;
            return Runtime.GetNSObject<NativeListMetadata>(GetAssociatedListMetadata(lists[^1].Handle, ParagraphMetadataKey.Handle));
        }

        private static double GetNativeFirstLineIndent(RichTextParagraphFormat format)
        {
            if (format.NativeList is null) return format.LeadingIndent + format.FirstLineIndent;
            var tab = format.TabStops.FirstOrDefault(tab => tab.Alignment == RichTextTabAlignment.Left)?.Position ?? 0;
            return tab > 0 ? tab : format.LeadingIndent;
        }

        internal void DrawListMarkers(CGRect rect)
        {
            var snapshot = DisplayPresentationSnapshot;
            if (snapshot.Lists.Count == 0) return;
            var layout = PlatformView.LayoutManager;
            var inset = PlatformView.TextContainerInset;
            var padding = PlatformView.TextContainer.LineFragmentPadding;
            var visibleGlyphs = layout.GetGlyphRangeForBoundingRectWithoutAdditionalLayout(
                new CGRect(rect.X - inset.Left, rect.Y - inset.Top, rect.Width, rect.Height), PlatformView.TextContainer);
            var visibleCharacters = layout.GetCharacterRange(visibleGlyphs);
            var counters = new Dictionary<(int Id, int Level), int>();
            foreach (var paragraph in snapshot.Paragraphs)
            {
                if (paragraph.Start > visibleCharacters.Location + visibleCharacters.Length) break;
                if (paragraph.Format.NativeList is not { } list) continue;
                var key = (list.Id, list.Level);
                if (list.Restart || !counters.TryGetValue(key, out var number)) number = list.StartAt;
                counters[key] = number == int.MaxValue ? number : number + 1;
                if (paragraph.Start < visibleCharacters.Location || FindDisplayFoldRange(paragraph.Start) is not null ||
                    paragraph.Start > PlatformView.TextStorage.Length) continue;

                using var position = PlatformView.GetDisplayPosition(PlatformView.BeginningOfDocument, paragraph.Start);
                if (position is null) continue;
                var caret = PlatformView.GetDisplayCaret(position);
                if (caret.Bottom < rect.Top) continue;
                if (caret.Top > rect.Bottom) break;
                var attributes = new UIStringAttributes(paragraph.Start < PlatformView.TextStorage.Length
                    ? PlatformView.TextStorage.GetAttributes(paragraph.Start, out _)
                    : PlatformView.TypingAttributes2);
                var font = attributes.Font ?? _defaultFont;
                nfloat baseline;
                if (paragraph.Start < PlatformView.TextStorage.Length)
                {
                    var glyph = layout.GetGlyphIndex((nuint)paragraph.Start);
                    var line = layout.GetLineFragmentUsedRect(glyph, out _);
                    // A newline glyph's origin includes the descender. An empty
                    // item's marker still uses the font's normal text baseline.
                    var empty = paragraph.Start == snapshot.Length || snapshot.Text[paragraph.Start] == '\n';
                    baseline = inset.Top + line.Y + (empty ? font.Ascender : layout.GetLocationForGlyph(glyph).Y);
                }
                else
                {
                    baseline = inset.Top + layout.ExtraLineFragmentUsedRect.Y + font.Ascender;
                }
                using var marker = new NSString(RichTextListFormatter.FormatMarker(list, number));
                var markerAttributes = new UIStringAttributes
                {
                    Font = font,
                    ForegroundColor = attributes.ForegroundColor ?? _defaultTextColor,
                };
                var indent = (nfloat)(paragraph.Format.LeadingIndent + paragraph.Format.FirstLineIndent);
                var rtl = PlatformView.GetBaseWritingDirection(position, UITextStorageDirection.Forward) == NSWritingDirection.RightToLeft;
                var x = rtl
                    ? inset.Left + PlatformView.TextContainer.Size.Width - padding - indent - marker.GetSizeUsingAttributes(markerAttributes).Width
                    : inset.Left + padding + indent;
                marker.DrawString(new CGPoint(x, baseline - font.Ascender), markerAttributes);
            }
        }
    }
}
#endif
