using System.Globalization;
using TextEdit.Core;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfProjectionWriter
{
    private readonly Dictionary<RichNodeId, SortedDictionary<int, List<Func<Task>>>> _anchors = [];
    private readonly HashSet<(RichNodeId, int)> _writtenAnchors = [];

    private void CollectSemanticResources()
    {
        Collect(snapshot.DefaultCharacter.Apply(new()));
        Collect(snapshot.DefaultParagraph.Apply(new()));
        foreach (var style in snapshot.Styles.Definitions.Values)
        {
            Collect(style.Character.Apply(new()));
            Collect(style.Paragraph.Apply(new()));
        }
        var projection = snapshot.ProjectText();
        foreach (var bookmark in snapshot.Bookmarks.Values.OrderBy(value => value.Id.Value))
        {
            if (bookmark.Range is not { } range) continue;
            var reverse = projection.GetOffset(range.Anchor) > projection.GetOffset(range.Active);
            Add(reverse ? range.Active : range.Anchor, () => Marker("bkmkstart", bookmark.Name, range, reverse, bookmark.Id));
            Add(reverse ? range.Anchor : range.Active, () => Marker("bkmkend", bookmark.Name));
        }
        foreach (var comment in snapshot.Comments.Values.OrderBy(value => value.Id.Value))
        {
            if (comment.Range is not { } range) continue;
            var reverse = projection.GetOffset(range.Anchor) > projection.GetOffset(range.Active);
            var name = "c" + comment.Id.Value.ToString(CultureInfo.InvariantCulture);
            Add(reverse ? range.Active : range.Anchor, () => Marker("atrfstart", name, range, reverse));
            Add(reverse ? range.Anchor : range.Active, async () =>
            {
                await Marker("atrfend", name);
                await Comment(comment, name);
            });
        }
        void Add(RichTextPoint point, Func<Task> write)
        {
            if (!_anchors.TryGetValue(point.Paragraph, out var positions)) _anchors[point.Paragraph] = positions = [];
            if (!positions.TryGetValue(point.Offset, out var events)) positions[point.Offset] = events = [];
            events.Add(write);
        }
    }

    private async Task SemanticHeader()
    {
        await S(@"{\*\remodel1}");
        if (includeIdentities)
        {
            await S(@"{\*\reids ");
            foreach (var node in RtfIdentityMap.Preorder(snapshot))
            {
                var identity = contextScaffolding?.Contains(node.Id) == true ? 0 : node.Id.Value;
                await S(identity.ToString(CultureInfo.InvariantCulture) + "/" + RtfIdentityMap.Kind(node).ToString(CultureInfo.InvariantCulture) + " ");
            }
            await S("}");
        }
        await S(@"{\*\defchp ");
        await Character(snapshot.DefaultCharacter.Apply(new()));
        await Masks("rec", RtfStyleState.Masks(snapshot.DefaultCharacter));
        await S(@"}{\*\defpap ");
        await ParagraphProperties(snapshot.DefaultParagraph.Apply(new()));
        await Masks("rep", RtfStyleState.Masks(snapshot.DefaultParagraph), snapshot.DefaultParagraph.Background.ApplyValue());
        await S("}");
        if (snapshot.Styles.Definitions.Count != 0)
        {
            await S(@"{\stylesheet ");
            foreach (var style in snapshot.Styles.Definitions.Values.OrderBy(value => value.Id.Value))
            {
                await S(style.Kind == RichStyleKind.Character ? @"{\*" : "{");
                await C(style.Kind == RichStyleKind.Character ? "cs" : "s", style.Id.Value);
                if (style.Kind == RichStyleKind.Character) await C("additive");
                if (style.BasedOn is { } parent) await C("sbasedon", parent.Value);
                if (style.NextStyle is { } next) await C("snext", next.Value);
                var character = style.Kind == RichStyleKind.Paragraph ? snapshot.Styles.ResolveCharacter(style.Id) : snapshot.Styles.ResolveCharacter(characterStyle: style.Id);
                await Character(character);
                await Masks("rec", RtfStyleState.Masks(style.Character));
                if (style.Kind == RichStyleKind.Paragraph)
                {
                    await ParagraphProperties(snapshot.Styles.ResolveParagraph(style.Id));
                    await Masks("rep", RtfStyleState.Masks(style.Paragraph), style.Paragraph.Background.ApplyValue());
                }
                await Name(style.Name);
                await S(";}");
            }
            await S("}");
        }
        var used = snapshot.Nodes.Values.OfType<RichImage>().Select(image => image.Asset).ToHashSet();
        foreach (var asset in snapshot.Assets.Values.Where(asset => !used.Contains(asset.Id)))
        {
            // An ignorable resource destination uses ordinary RTF picture syntax.
            // It retains an owned, currently unused resource without adding body text.
            await S(@"{\*\reasset ");
            await Image(new(default, null, asset.Id, new(1), new(1)));
            await S("}");
        }
    }

    private async Task Masks(string destination, (int Inherit, int Initial, int Null) masks, RichColor? background = null)
    {
        await S(@"{\*");
        await C(destination);
        await C("reprops", destination == "rep" ? RtfStyleState.ParagraphPropertyCount : RtfStyleState.CharacterPropertyCount);
        await C("reinherit", masks.Inherit);
        await C("reinitial", masks.Initial);
        await C("renull", masks.Null);
        if (background is { } color) await C("rebgcolor", Index(color));
        await S("}");
    }

    private async Task ParagraphProperties(ResolvedParagraphStyle value)
    {
        await C(value.Alignment switch { RichAlignment.Center => "qc", RichAlignment.Right => "qr", RichAlignment.Justify => "qj", RichAlignment.Distributed => "qd", _ => "ql" });
        if (value.Direction != RichDirection.Automatic) await C(value.Direction == RichDirection.RightToLeft ? "rtlpar" : "ltrpar");
        await C("li", value.LeadingIndent.Twips);
        await C("ri", value.TrailingIndent.Twips);
        await C("fi", value.FirstLineIndent.Twips);
        await C("sb", value.SpaceBefore.Twips);
        await C("sa", value.SpaceAfter.Twips);
        await C("sl", value.LineSpacing.RtfValue);
        await C("slmult", value.LineSpacing.Kind == RichLineSpacingKind.Multiple ? 1 : 0);
        await C("hyphpar", value.Hyphenation ? 1 : 0);
        foreach (var tab in value.TabStops)
        {
            await C(tab.Alignment switch { RichTabAlignment.Center => "tqc", RichTabAlignment.Right => "tqr", RichTabAlignment.Decimal => "tqdec", _ => "tql" });
            if (tab.Leader != RichTabLeader.None)
                await C(tab.Leader switch { RichTabLeader.Dots => "tldot", RichTabLeader.Hyphens => "tlhyph", RichTabLeader.Underline => "tlul", RichTabLeader.ThickLine => "tlth", _ => "tleq" });
            await C("tx", tab.Position.Twips);
        }
        await C("keep", value.KeepTogether ? 1 : 0);
        await C("keepn", value.KeepWithNext ? 1 : 0);
        await C(value.WidowControl ? "widctlpar" : "nowidctlpar");
        if (value.HeadingLevel is { } level) await C("outlinelevel", level - 1);
        if (value.Shading is { } shading)
        {
            await C("shading", shading.Coverage);
            await C("cfpat", Index(shading.Foreground));
            await C("cbpat", Index(shading.Background));
        }
        else if (value.Background is { } background) { await C("shading", 10000); await C("cfpat", _colors[background]); }
        if (value.Border is { } border)
            foreach (var (side, control) in new[] { (RichBorderSides.Left, "brdrl"), (RichBorderSides.Top, "brdrt"), (RichBorderSides.Right, "brdrr"), (RichBorderSides.Bottom, "brdrb") })
                if ((border.Sides & side) != 0)
                {
                    await C(control);
                    await C(border.Style switch { RichBorderStyle.Double => "brdrdb", RichBorderStyle.Dotted => "brdrdot", RichBorderStyle.Dashed => "brdrdash", _ => "brdrs" });
                    await C("brdrw", border.Width.Twips);
                    await C("brdrcf", Index(border.Color));
                }
    }

    private async Task Name(string name)
    {
        var pieces = name.Split(';');
        for (var index = 0; index < pieces.Length; index++)
        {
            if (index != 0) await S(@"\u59?");
            await T(pieces[index]);
        }
    }

    private async Task AnnotatedText(RichNodeId paragraph, int offset, TextSnapshot text)
    {
        var end = checked(offset + text.Length);
        var positions = _anchors.GetValueOrDefault(paragraph);
        var start = offset;
        if (positions is not null)
            foreach (var position in positions.Keys.Where(position => position >= offset && position < end))
            {
                foreach (var chunk in text.GetChunks(new(start - offset, position - start)))
                    await _output.TextAsync(chunk, _cancellation).ConfigureAwait(false);
                await Anchors(paragraph, position);
                start = position;
            }
        foreach (var chunk in text.GetChunks(new(start - offset, end - start)))
            await _output.TextAsync(chunk, _cancellation).ConfigureAwait(false);
    }

    private async Task Anchors(RichNodeId paragraph, int offset)
    {
        if (!_anchors.TryGetValue(paragraph, out var positions) || !positions.TryGetValue(offset, out var events) || !_writtenAnchors.Add((paragraph, offset))) return;
        foreach (var write in events) await write();
    }

    private async Task Marker(string destination, string name, RichTextSelection? range = null, bool reverse = false, RichAnnotationId? identity = null)
    {
        await S(@"{\*"); await C(destination);
        if (includeIdentities && identity is { } id)
        { await S(@"{\*\reanchorid "); await S(id.Value.ToString(CultureInfo.InvariantCulture)); await S("}"); }
        if (range is not null)
        {
            await C("rereverse", reverse ? 1 : 0);
            await C("reanchor", (int)range.Anchor.Affinity);
            await C("reactive", (int)range.Active.Affinity);
        }
        await T(name); await S("}");
    }

    private async Task Comment(RichComment comment, string name)
    {
        await S(@"{\*\annotation{\*\atnid "); await T(name);
        await S(@"}{\*\atnauthor "); await T(comment.Author); await S("}");
        if (comment.Created is { } created)
        {
            var date = created.UtcDateTime;
            var packed = date.Minute | date.Hour << 6 | date.Day << 11 | date.Month << 16 | (date.Year - 1900) << 20;
            await C("atndate", packed);
        }
        // RTF DTTM has no zone. This destination fixes the library's UTC convention.
        await S(@"{\*\reutc}");
        var lines = comment.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        // Preserve authored CR and LF individually, rather than normalizing annotations.
        var start = 0;
        for (var i = 0; i < comment.Text.Length; i++)
        {
            var ch = comment.Text[i];
            if (ch is not ('\r' or '\n')) continue;
            await _output.TextAsync(comment.Text.AsMemory(start, i - start), _cancellation);
            await C("u", ch);
            await S("?");
            start = i + 1;
        }
        await _output.TextAsync(comment.Text.AsMemory(start), _cancellation);
        await S("}");
    }

    private async Task OrphanAnnotations()
    {
        foreach (var bookmark in snapshot.Bookmarks.Values.Where(value => value.IsOrphaned))
        { await S(@"{\*\reorphan "); await Marker("bkmkstart", bookmark.Name, identity: bookmark.Id); await S("}"); }
        foreach (var comment in snapshot.Comments.Values.Where(value => value.IsOrphaned))
        { await S(@"{\*\reorphan "); await Comment(comment, "c" + comment.Id.Value.ToString(CultureInfo.InvariantCulture)); await S("}"); }
    }
}

