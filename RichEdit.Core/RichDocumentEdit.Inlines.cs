namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Sets the direct character defaults of an entire paragraph.</summary>
    public void SetParagraphCharacterFormat(RichNodeId paragraph, RichCharacterStyle format)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        _nodes[paragraph] = Get<RichParagraph>(paragraph) with { Character = format };
    }

    /// <summary>Assigns a character style to a paragraph-local range. Null restores inheritance.</summary>
    public void SetCharacterStyle(RichTextPoint start, int length, RichStyleId? style) => ChangeInlines(start, length, inline => inline with { Style = style });

    /// <summary>Sets or removes an authored hyperlink on a paragraph-local range.</summary>
    public void SetHyperlink(RichTextPoint start, int length, RichHyperlink? link) => ChangeInlines(start, length, inline => inline with { Link = link });

    /// <summary>Inserts one field atom. Its instruction is retained as data and is never executed by the library.</summary>
    public RichNodeId InsertField(RichTextPoint point, string instruction, string result)
    {
        VerifyOpen();
        ValidatePoint(point);
        ValidateText(result);
        if (string.IsNullOrWhiteSpace(instruction) || instruction.AsSpan().ContainsAny('\r', '\n', '\0'))
            throw new ArgumentException("A field instruction must be nonempty and single-line.", nameof(instruction));
        var paragraph = Get<RichParagraph>(point.Paragraph);
        var inherited = InlineAt(paragraph, point.Offset);
        var (left, right) = Partition(paragraph, point.Offset, paragraph.Id);
        var id = Allocate();
        _nodes.Add(id, new RichField(id, paragraph.Id, instruction, result)
        { Style = inherited?.Style, Format = inherited?.Format ?? new(), Link = inherited?.Link });
        _nodes[paragraph.Id] = paragraph with { Inlines = left.Add(id).AddRange(right) };
        Record(new RichAddressMap.Text(paragraph.Id, point.Offset, 0, 1));
        return id;
    }

    /// <summary>Updates a field's retained display result without changing its identity or instruction.</summary>
    public void SetFieldResult(RichNodeId field, string result)
    {
        VerifyOpen();
        ValidateText(result);
        _nodes[field] = Get<RichField>(field) with { Result = result };
    }

    private void ChangeInlines(RichTextPoint start, int length, Func<RichInline, RichInline> change)
    {
        VerifyOpen();
        ValidatePoint(start);
        var paragraph = Get<RichParagraph>(start.Paragraph);
        new TextEdit.Core.TextSpan(start.Offset, length).Validate(Length(paragraph));
        if (length == 0) return;
        var (left, remainder) = Partition(paragraph, start.Offset, paragraph.Id);
        var (middle, right) = Partition(paragraph with { Inlines = remainder }, length, paragraph.Id);
        foreach (var id in middle) _nodes[id] = change(Get<RichInline>(id));
        _nodes[paragraph.Id] = paragraph with { Inlines = Coalesce(left.AddRange(middle).AddRange(right)) };
    }
}
