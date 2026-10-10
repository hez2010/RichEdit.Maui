using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Inserts self-contained fragment blocks into a story, list item, or cell, remapping all document-owned identities.</summary>
    /// <remarks>Fragment defaults become a private imported base style, so destination defaults cannot change the copied appearance.</remarks>
    public ImmutableArray<RichNodeId> InsertFragment(RichNodeId parent, int index, RichDocumentFragment fragment)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(fragment);
        var destination = Blocks(Get<RichNode>(parent));
        if (index < 0 || index > destination.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var content = fragment.Content;
        var nodes = content.Nodes.Keys.Where(id => id != content.Body).ToDictionary(id => id, _ => Allocate());
        var assets = content.Assets.Keys.ToDictionary(id => id, _ => new RichAssetId(Allocate().Value));
        var definitions = _styles.Definitions.Values.ToList();
        var usedStyles = _styles.Definitions.Keys.Select(id => id.Value).ToHashSet();
        var nextStyle = 1;
        RichStyleId AllocateStyle()
        {
            while (usedStyles.Contains(nextStyle) && nextStyle <= short.MaxValue) nextStyle++;
            if (nextStyle > short.MaxValue) throw new InvalidOperationException("The fragment exceeds the RTF stylesheet capacity.");
            usedStyles.Add(nextStyle);
            return new(nextStyle++);
        }
        var defaults = AllocateStyle();
        definitions.Add(new(defaults, "Imported defaults", RichStyleKind.Paragraph)
        {
            Character = Rtf.RtfStyleState.Restore(content.DefaultCharacter.Apply(new()), 0, 0, 0),
            Paragraph = Rtf.RtfStyleState.Restore(content.DefaultParagraph.Apply(new()), 0, 0, 0),
        });
        var styles = content.Styles.Definitions.Keys.ToDictionary(id => id, _ => AllocateStyle());
        foreach (var style in content.Styles.Definitions.Values)
            definitions.Add(style with
            {
                Id = styles[style.Id],
                BasedOn = style.BasedOn is { } based ? styles[based] : style.Kind == RichStyleKind.Paragraph ? defaults : null,
                NextStyle = style.NextStyle is { } next ? styles[next] : null,
            });
        _styles = new(definitions);
        foreach (var asset in content.Assets.Values) _assets.Add(assets[asset.Id], asset with { Id = assets[asset.Id] });
        var bookmarkNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = _bookmarks.Values.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var bookmark in content.Bookmarks.Values)
        {
            var name = bookmark.Name;
            for (var suffix = 2; !usedNames.Add(name); suffix++)
            {
                var number = "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                name = bookmark.Name[..Math.Min(bookmark.Name.Length, 40 - number.Length)] + number;
            }
            bookmarkNames.Add(bookmark.Name, name);
        }
        foreach (var node in content.Nodes.Values)
        {
            if (node is RichStory) continue;
            RichNode copy = node switch
            {
                RichParagraph p => p with { Inlines = Map(p.Inlines), Style = p.Style is { } style ? styles[style] : defaults },
                RichList list => list with { Items = Map(list.Items) },
                RichListItem item => item with { Blocks = Map(item.Blocks) },
                RichTable table => table with { Rows = Map(table.Rows) },
                RichTableRow row => row with { Cells = Map(row.Cells) },
                RichTableCell cell => cell with { Blocks = Map(cell.Blocks) },
                RichImage image => image with { Asset = assets[image.Asset] },
                RichField field => field with { Instruction = Rtf.RtfFieldSyntax.RemapBookmarkReferences(field.Instruction, bookmarkNames) },
                _ => node,
            };
            copy = copy with { Id = nodes[node.Id], Parent = node.Parent == content.Body ? parent : nodes[node.Parent!.Value] };
            if (copy is RichInline inline)
            {
                var link = inline.Link;
                if (link is not null && link.Target.StartsWith('#') && bookmarkNames.TryGetValue(link.Target[1..], out var name)) link = link with { Target = "#" + name };
                copy = inline with { Style = inline.Style is { } style ? styles[style] : null, Link = link };
            }
            _nodes.Add(copy.Id, copy);
        }
        foreach (var bookmark in content.Bookmarks.Values)
        {
            var id = new RichAnnotationId(Allocate().Value);
            _bookmarks.Add(id, bookmark with { Id = id, Name = bookmarkNames[bookmark.Name], Range = MapRange(bookmark.Range) });
        }
        foreach (var comment in content.Comments.Values)
        {
            var id = new RichAnnotationId(Allocate().Value);
            _comments.Add(id, comment with { Id = id, Range = MapRange(comment.Range) });
        }
        var blocks = content.Get<RichStory>(content.Body).Blocks.Select(id => nodes[id]).ToImmutableArray();
        SetBlocks(parent, destination.InsertRange(index, blocks));
        return blocks;

        ImmutableList<RichNodeId> Map(ImmutableList<RichNodeId> values) => values.Select(id => nodes[id]).ToImmutableList();
        RichTextSelection? MapRange(RichTextSelection? range) => range is null ? null : new(
            range.Anchor with { Story = _body, Paragraph = nodes[range.Anchor.Paragraph] }, range.Active with { Story = _body, Paragraph = nodes[range.Active.Paragraph] });
    }
}
