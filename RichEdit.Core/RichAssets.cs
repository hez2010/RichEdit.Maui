using System.Collections.Immutable;

namespace RichEdit.Core;

/// <summary>A document-owned image resource identity.</summary>
public readonly record struct RichAssetId(long Value)
{
    /// <summary>Gets whether the identity is valid.</summary>
    public bool IsValid => Value > 0;
}

/// <summary>An image payload representation supported by RTF picture controls.</summary>
public enum RichImageKind
{
    /// <summary>PNG image bytes.</summary>
    Png,
    /// <summary>JPEG image bytes.</summary>
    Jpeg,
    /// <summary>Windows device-independent bitmap bytes.</summary>
    Dib,
    /// <summary>Enhanced Windows metafile bytes.</summary>
    Emf,
    /// <summary>Windows metafile bytes.</summary>
    Wmf,
}

/// <summary>Immutable owned image bytes. Rendering support can differ from RTF payload preservation.</summary>
public sealed record RichImageAsset(RichAssetId Id, RichImageKind Kind, ImmutableArray<byte> Data);

/// <summary>An authored inline image referencing document-owned bytes.</summary>
public sealed record RichImage(RichNodeId Id, RichNodeId? Parent, RichAssetId Asset, RichLength Width, RichLength Height) : RichInline(Id, Parent)
{
    /// <summary>Gets alternative text for accessibility and text-only export contexts.</summary>
    public string? AlternativeText { get; init; }
}
