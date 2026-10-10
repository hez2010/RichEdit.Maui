using System.Collections.Immutable;
using System.Text;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private sealed class Picture
    {
        internal RichImageKind? Kind;
        internal readonly List<byte> Bytes = [];
        internal int Width, Height, PixelWidth, PixelHeight;
        internal int? Nibble;
        internal string? Name, Alt, Property;
    }
    private sealed class Annotation
    {
        internal readonly StringBuilder Text = new();
        internal string Name = "", Author = "";
        internal int? Date;
        internal bool Utc;
    }

    private void SetPictureKind(RichImageKind kind) { if (_state.Picture is { } picture) picture.Kind = kind; }
    private void AddPictureBytes(ReadOnlySpan<byte> bytes)
    {
        if (_state.Picture is not { } picture) throw Error("Picture data has no picture destination");
        if ((long)picture.Bytes.Count + bytes.Length > options.MaximumAssetBytes) throw Error("RTF picture size limit exceeded");
        foreach (var value in bytes) picture.Bytes.Add(value);
    }

    private void FinishPicture(State child)
    {
        var picture = child.Picture!;
        if (picture.Nibble is not null) throw Error("Incomplete picture hexadecimal byte");
        if (picture.Kind is not { } kind || picture.Bytes.Count == 0)
        { Report("picture", "An unsupported or empty picture was omitted."); return; }
        RichAssetId asset;
        if (picture.Name is { Length: > 0 } name && _assetNames.TryGetValue(name, out var existing))
        {
            var resource = _assets[existing];
            if (resource.Kind != kind || !resource.Data.AsSpan().SequenceEqual(picture.Bytes.ToArray())) throw Error("A picture identity is reused for different bytes");
            asset = existing;
        }
        else
        {
            asset = new(Allocate().Value);
            _assets.Add(asset, new(asset, kind, picture.Bytes.ToImmutableArray()));
            if (picture.Name is { Length: > 0 } key) _assetNames.Add(key, asset);
        }
        if (child.ResourceOnly) return;
        var width = picture.Width > 0 ? picture.Width : picture.PixelWidth > 0 ? checked(picture.PixelWidth * 15) : 1440;
        var height = picture.Height > 0 ? picture.Height : picture.PixelHeight > 0 ? checked(picture.PixelHeight * 15) : 1440;
        if (picture.Width <= 0 || picture.Height <= 0) Report("picture-size", "Missing picture dimensions were inferred at 96 DPI, with a one-inch fallback.");
        var paragraph = EnsureParagraph();
        paragraph.Inlines.Add(new RichImage(Allocate(), paragraph.Id, asset, new(width), new(height))
        { Format = child.Character, Style = child.CharacterStyle, AlternativeText = picture.Alt });
        paragraph.Length++;
    }

    private void StartAnchor(State child, Dictionary<string, Anchor> anchors)
    {
        var name = child.Capture?.ToString() ?? "";
        if (anchors.ContainsKey(name)) throw Error("A persistent anchor name is duplicated");
        if (child.Destination == "bkmkstart")
        {
            try { RichDocumentSnapshot.ValidateBookmarkName(name); }
            catch (ArgumentException) { Report("bookmark-name", "A bookmark with an unsupported name was omitted."); return; }
            if (child.Orphan) { _bookmarks.Add(new(new(Allocate().Value), name, null)); return; }
        }
        anchors.Add(name, new(child.Orphan ? null : Point(), child.Reverse, child.AnchorAffinity, child.ActiveAffinity));
    }

    private void EndAnchor(State child, bool bookmark)
    {
        var name = child.Capture?.ToString() ?? "";
        var starts = bookmark ? _bookmarkStarts : _commentStarts;
        if (!starts.Remove(name, out var anchor) || anchor.Point is not { } start)
        { Report("annotation-anchor", "An annotation endpoint has no matching start."); return; }
        var end = Point();
        var range = anchor.Reverse ? new RichTextSelection(end with { Affinity = anchor.AnchorAffinity }, start with { Affinity = anchor.ActiveAffinity }) :
            new RichTextSelection(start with { Affinity = anchor.AnchorAffinity }, end with { Affinity = anchor.ActiveAffinity });
        if (bookmark) _bookmarks.Add(new(new(Allocate().Value), name, range)); else _commentRanges[name] = range;
    }

    private void FinishAnnotation(State child)
    {
        var annotation = child.Annotation!;
        DateTimeOffset? created = null;
        if (annotation.Date is { } packed)
        {
            try { created = new(1900 + ((packed >> 20) & 511), (packed >> 16) & 15, (packed >> 11) & 31, (packed >> 6) & 31, packed & 63, 0, TimeSpan.Zero); }
            catch (ArgumentOutOfRangeException) { Report("annotation-date", "An invalid annotation timestamp was omitted."); }
            if (!annotation.Utc && created is not null) Report("annotation-timezone", "An RTF annotation timestamp without a timezone was interpreted as UTC.");
        }
        _commentRanges.Remove(annotation.Name, out var range);
        _comments.Add(new(new(Allocate().Value), child.Orphan ? null : range, annotation.Text.ToString(), annotation.Author, created));
    }
}
