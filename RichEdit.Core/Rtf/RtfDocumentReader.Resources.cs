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
        internal int ScaleX = 100, ScaleY = 100;
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
    private sealed class Field
    {
        internal string Instruction = "";
        internal readonly StringBuilder Result = new();
        internal bool Atom, IsLink;
        internal StringBuilder? OuterCapture;
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
        if (picture.Name is { Length: > 0 } name && _assetNames.TryGetValue(name, out var existing) &&
            _assets[existing].Kind == kind && _assets[existing].Data.AsSpan().SequenceEqual(picture.Bytes.ToArray()))
        {
            asset = existing;
        }
        else
        {
            asset = new(Allocate().Value);
            _assets.Add(asset, new(asset, kind, picture.Bytes.ToImmutableArray()));
            if (picture.Name is { Length: > 0 } key && !_assetNames.TryAdd(key, asset) && (_semantic || _wireNodes is not null))
                throw Error("A retained picture identity is reused for different bytes");
        }
        if (child.ResourceOnly) return;
        if (_state.Destination == "fldrslt" && _state.Capture is { } result)
        {
            Report("field-result-object", "A picture inside a field result was retained as an unused resource with a text fallback.");
            result.Append(picture.Alt ?? "\uFFFC");
            return;
        }
        var width = picture.Width > 0 ? picture.Width : picture.PixelWidth > 0 ? checked(picture.PixelWidth * 15) : 1440;
        var height = picture.Height > 0 ? picture.Height : picture.PixelHeight > 0 ? checked(picture.PixelHeight * 15) : 1440;
        if (picture.Width <= 0 || picture.Height <= 0) Report("picture-size", "Missing picture dimensions were inferred at 96 DPI, with a one-inch fallback.");
        width = Scale(width, picture.ScaleX);
        height = Scale(height, picture.ScaleY);
        var paragraph = EnsureParagraph();
        paragraph.Inlines.Add(new RichImage(Allocate(), paragraph.Id, asset, new(width), new(height))
        { Format = child.Character, Style = child.CharacterStyle, Link = child.Link, AlternativeText = picture.Alt });
        paragraph.Length++;

        int Scale(int value, int percent)
        {
            if (percent < 0) Report("picture-transform", "Mirrored picture scaling was imported with positive dimensions.");
            var scaled = ((long)value * Math.Abs((long)percent) + 50) / 100;
            if (scaled > int.MaxValue) throw Error("Picture dimensions exceed the supported range");
            if (scaled == 0)
            {
                Report("picture-size", "A zero-sized picture was retained at the minimum size of one twip.");
                scaled = 1;
            }
            return (int)scaled;
        }
    }

    private void StartAnchor(State child, Dictionary<string, Anchor> anchors)
    {
        var name = child.Capture?.ToString() ?? "";
        if (anchors.ContainsKey(name)) throw Error("A persistent anchor name is duplicated");
        if (child.Destination == "bkmkstart")
        {
            try { RichDocumentSnapshot.ValidateBookmarkName(name); }
            catch (ArgumentException) { Report("bookmark-name", "A bookmark with an unsupported name was omitted."); return; }
            if (child.Orphan)
            {
                var id = new RichAnnotationId(Allocate().Value);
                _bookmarks.Add(new(id, name, null));
                if (child.WireAnchor is { } wire) Identities.Bookmarks.Add(id, wire);
                return;
            }
        }
        ChargeResource();
        anchors.Add(name, new(child.Orphan ? null : Point(), child.Reverse, child.AnchorAffinity, child.ActiveAffinity, child.WireAnchor));
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
        if (bookmark)
        {
            var id = new RichAnnotationId(Allocate().Value);
            _bookmarks.Add(new(id, name, range));
            if (anchor.WireId is { } wire) Identities.Bookmarks.Add(id, wire);
        }
        else _commentRanges[name] = range;
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
        var id = new RichAnnotationId(Allocate().Value);
        _comments.Add(new(id, child.Orphan ? null : range, annotation.Text.ToString(), annotation.Author, created));
        if (_wireNodes is not null)
        {
            if (!annotation.Name.StartsWith('c')) throw Error("A retained comment requires its identity");
            Identities.Comments.Add(id, RtfIdentityMap.Number(annotation.Name.AsSpan(1)));
        }
    }

    private void FinishField(State child)
    {
        var field = child.Field!;
        if (field.IsLink) return;
        if (field.OuterCapture is { } outer)
        {
            Report("nested-field", "A field inside another field or annotation was flattened to its result text.");
            outer.Append(field.Result);
            return;
        }
        if (string.IsNullOrWhiteSpace(field.Instruction))
        { Report("field-instruction", "A field without an instruction was imported as its result text."); AddText(field.Result.ToString()); return; }
        var instruction = field.Instruction;
        var result = field.Result.ToString();
        if (instruction.AsSpan().ContainsAny('\r', '\n', '\0'))
        { Report("field-instruction", "Line breaks in a field instruction were replaced with spaces."); instruction = instruction.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' '); }
        if (result.AsSpan().ContainsAny('\r', '\n', '\u2028'))
        { Report("field-result", "A multi-line field result was imported as a single-line fallback."); result = result.Replace('\r', ' ').Replace('\n', ' ').Replace('\u2028', ' '); }
        var paragraph = EnsureParagraph();
        paragraph.Inlines.Add(new RichField(Allocate(), paragraph.Id, instruction, result) { Format = child.Character, Style = child.CharacterStyle, Link = child.Link });
        paragraph.Length++;
    }
}
