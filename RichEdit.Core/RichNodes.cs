using System.Collections.Immutable;
using TextEdit.Core;

namespace RichEdit.Core;

/// <summary>An immutable structural node. Child references are identities in the owning snapshot.</summary>
public abstract record RichNode(RichNodeId Id, RichNodeId? Parent);

/// <summary>An independently addressable document text flow.</summary>
public sealed record RichStory(RichNodeId Id, ImmutableList<RichNodeId> Blocks) : RichNode(Id, null);

/// <summary>A block in a story, list item, or table cell.</summary>
public abstract record RichBlock(RichNodeId Id, RichNodeId? Parent) : RichNode(Id, Parent);

/// <summary>A paragraph with semantic style references and inline content.</summary>
public sealed record RichParagraph(RichNodeId Id, RichNodeId? Parent, ImmutableList<RichNodeId> Inlines) : RichBlock(Id, Parent)
{
    /// <summary>Gets the reusable paragraph style, or null for document defaults.</summary>
    public RichStyleId? Style { get; init; }
    /// <summary>Gets direct paragraph overrides.</summary>
    public RichParagraphStyle Format { get; init; } = new();
    /// <summary>Gets direct character defaults for this paragraph.</summary>
    public RichCharacterStyle Character { get; init; } = new();
}

/// <summary>A standard RTF list counter format.</summary>
public enum RichListNumberingStyle
{
    /// <summary>Decimal digits.</summary>
    Decimal,
    /// <summary>Uppercase Roman numerals.</summary>
    UpperRoman,
    /// <summary>Lowercase Roman numerals.</summary>
    LowerRoman,
    /// <summary>Uppercase alphabetic counters.</summary>
    UpperLetter,
    /// <summary>Lowercase alphabetic counters.</summary>
    LowerLetter,
}

/// <summary>A list containing items whose content consists of blocks.</summary>
public sealed record RichList(RichNodeId Id, RichNodeId? Parent, ImmutableList<RichNodeId> Items) : RichBlock(Id, Parent)
{
    /// <summary>Gets whether this list is numbered; false selects a bullet marker.</summary>
    public bool Numbered { get; init; }
    /// <summary>Gets the first displayed number.</summary>
    public int StartNumber { get; init; } = 1;
    /// <summary>Gets the number format. Bullet lists use the neutral Decimal value.</summary>
    public RichListNumberingStyle NumberStyle { get; init; }
}

/// <summary>One list item containing paragraphs and nested blocks.</summary>
public sealed record RichListItem(RichNodeId Id, RichNodeId? Parent, ImmutableList<RichNodeId> Blocks) : RichNode(Id, Parent);

/// <summary>A table with an explicit column grid and row identities.</summary>
public sealed record RichTable(RichNodeId Id, RichNodeId? Parent, ImmutableArray<RichLength> Columns,
    ImmutableList<RichNodeId> Rows) : RichBlock(Id, Parent);

/// <summary>A table row containing cells that start on this row; spanning cells can originate on an earlier row.</summary>
public sealed record RichTableRow(RichNodeId Id, RichNodeId? Parent, ImmutableList<RichNodeId> Cells) : RichNode(Id, Parent);

/// <summary>A table cell anchored at a zero-based column and spanning one or more rows and columns.</summary>
public sealed record RichTableCell(RichNodeId Id, RichNodeId? Parent, int Column, int ColumnSpan, int RowSpan,
    ImmutableList<RichNodeId> Blocks) : RichNode(Id, Parent);

/// <summary>An authored inline within one paragraph.</summary>
public abstract record RichInline(RichNodeId Id, RichNodeId? Parent) : RichNode(Id, Parent)
{
    /// <summary>Gets a reusable character style, or null to inherit the paragraph's character style.</summary>
    public RichStyleId? Style { get; init; }
    /// <summary>Gets direct character overrides.</summary>
    public RichCharacterStyle Format { get; init; } = new();
    /// <summary>Gets an authored hyperlink. Activation is an application decision.</summary>
    public RichHyperlink? Link { get; init; }
}

/// <summary>A hyperlink stored using an RTF HYPERLINK field.</summary>
public sealed record RichHyperlink(string Target, string? ToolTip = null);

/// <summary>An authored field atom with an instruction and a retained single-line display result.</summary>
/// <remarks>The library does not execute instructions or fetch external resources. A host updates the result explicitly.</remarks>
public sealed record RichField(RichNodeId Id, RichNodeId? Parent, string Instruction, string Result) : RichInline(Id, Parent);

/// <summary>A persistent text run containing neither paragraph terminators nor soft-break characters.</summary>
public sealed record RichText(RichNodeId Id, RichNodeId? Parent, TextSnapshot Content) : RichInline(Id, Parent);

/// <summary>An authored soft line break within a paragraph.</summary>
public sealed record RichSoftBreak(RichNodeId Id, RichNodeId? Parent) : RichInline(Id, Parent);

/// <summary>A text point in one story and paragraph, using a zero-based paragraph-local UTF-16 offset.</summary>
public readonly record struct RichTextPoint(RichNodeId Story, RichNodeId Paragraph, int Offset, CaretAffinity Affinity = CaretAffinity.Downstream);

/// <summary>A typed rich selection; structural selections are distinct from text offsets.</summary>
public abstract record RichSelection;

/// <summary>A directional pair of rich text points within one story.</summary>
public sealed record RichTextSelection(RichTextPoint Anchor, RichTextPoint Active) : RichSelection;

/// <summary>A selected authored inline object.</summary>
public sealed record RichObjectSelection(RichNodeId Object) : RichSelection;

/// <summary>A table rectangle in zero-based grid coordinates. Commands expand it to whole merged cells.</summary>
public sealed record RichCellSelection(RichNodeId Table, int Row, int Column, int RowCount, int ColumnCount) : RichSelection;

/// <summary>A contiguous inclusive range of complete blocks in one story, table cell, or list item.</summary>
/// <remarks>Whole-document selection uses the body's first and last blocks. Removing blocks is distinct from clearing table cells.</remarks>
public sealed record RichBlockSelection(RichNodeId Parent, RichNodeId First, RichNodeId Last) : RichSelection;

/// <summary>A side of a structural block in its parent text container.</summary>
public enum RichBlockEdge
{
    /// <summary>Immediately before the block.</summary>
    Before,
    /// <summary>Immediately after the block.</summary>
    After,
}

/// <summary>A caret at a structural block boundary, such as the native final paragraph after a table.</summary>
/// <remarks>The boundary does not manufacture authored text. Inserting a paragraph is an explicit transaction.</remarks>
public sealed record RichBoundarySelection(RichNodeId Story, RichNodeId Block, RichBlockEdge Edge) : RichSelection;
