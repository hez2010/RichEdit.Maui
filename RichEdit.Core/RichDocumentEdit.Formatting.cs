namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    /// <summary>Overlays specified properties on all selected characters, preserving other direct overrides and style references.</summary>
    /// <remarks>Inherit leaves a property unchanged. Use UpdateCharacterFormat to restore explicit inheritance.</remarks>
    public void ApplyCharacterFormat(RichSelection selection, RichCharacterStyle format)
    {
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        UpdateCharacterFormat(selection, current => current.Overlay(format));
    }

    /// <summary>Transforms direct overrides in the typed selection. Selected paragraph ends include their character defaults.</summary>
    /// <remarks>Collapsed text carets do not author formatting. Their typing state belongs to a session.</remarks>
    public void UpdateCharacterFormat(RichSelection selection, Func<RichCharacterStyle, RichCharacterStyle> update)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(update);
        foreach (var range in Candidate(validate: false).GetSelectedText(selection))
        {
            ChangeInlines(range.Start, range.Length, inline => inline with { Format = Change(inline.Format) });
            if (range.IncludesParagraphEnd)
            {
                var paragraph = Get<RichParagraph>(range.Start.Paragraph);
                _nodes[paragraph.Id] = paragraph with { Character = Change(paragraph.Character) };
            }
        }
        RichCharacterStyle Change(RichCharacterStyle previous)
        {
            var next = update(previous) ?? throw new InvalidOperationException("A format update cannot return null.");
            RichStyleSheet.Validate(next);
            return next;
        }
    }

    /// <summary>Overlays specified properties on paragraphs touched by a typed selection.</summary>
    public void ApplyParagraphFormat(RichSelection selection, RichParagraphStyle format)
    {
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        UpdateParagraphFormat(selection, current => current.Overlay(format));
    }

    /// <summary>Transforms direct paragraph overrides once per selected paragraph.</summary>
    public void UpdateParagraphFormat(RichSelection selection, Func<RichParagraphStyle, RichParagraphStyle> update)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(update);
        foreach (var paragraph in Candidate(validate: false).GetSelectedText(selection).Select(range => range.Start.Paragraph).Distinct())
            SetParagraphFormat(paragraph, update(Get<RichParagraph>(paragraph).Format) ?? throw new InvalidOperationException("A format update cannot return null."));
    }
}
