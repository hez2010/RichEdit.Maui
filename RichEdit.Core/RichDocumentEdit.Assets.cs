using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Copies encoded image bytes into a document-owned resource.</summary>
    public RichAssetId AddImageAsset(RichImageKind kind, ReadOnlyMemory<byte> data)
    {
        VerifyOpen();
        if (!Enum.IsDefined(kind) || data.IsEmpty) throw new ArgumentException("Supply a supported image representation and nonempty payload.");
        var id = new RichAssetId(Allocate().Value);
        _assets.Add(id, new(id, kind, ImmutableArray.Create(data.Span)));
        return id;
    }

    /// <summary>Removes an image resource. A transaction cannot commit while images still reference it.</summary>
    public void RemoveImageAsset(RichAssetId asset)
    {
        VerifyOpen();
        if (!_assets.Remove(asset)) throw new ArgumentException("The image resource does not exist.", nameof(asset));
    }

    /// <summary>Inserts an authored image as one inline atom, sharing the parent document's history.</summary>
    public RichNodeId InsertImage(RichTextPoint point, RichAssetId asset, RichLength width, RichLength height, string? alternativeText = null)
    {
        VerifyOpen();
        ValidatePoint(point);
        if (!_assets.ContainsKey(asset) || width.Twips <= 0 || height.Twips <= 0)
            throw new ArgumentException("Supply an existing image resource and positive dimensions.");
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var inherited = InlineAt(paragraph, point.Offset);
        var (left, right) = Partition(paragraph, point.Offset, paragraph.Id);
        var id = Allocate();
        _nodes.Add(id, new RichImage(id, paragraph.Id, asset, width, height)
        { Style = inherited?.Style, Format = inherited?.Format ?? new(), AlternativeText = alternativeText });
        _nodes[paragraph.Id] = paragraph with { Inlines = left.Add(id).AddRange(right) };
        _maps.Add(new RichAddressMap.Text(paragraph.Id, point.Offset, 0, 1));
        return id;
    }
}
