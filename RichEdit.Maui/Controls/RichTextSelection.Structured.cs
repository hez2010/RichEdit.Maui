using RichEdit.Core;

namespace RichEdit.Maui;

public sealed partial class RichTextSelection
{
    private void UpdateStructuredCharacterFormat(Func<RichTextCharacterFormat, RichTextCharacterFormat> update)
    {
        var session = _editor.StructuredSession!;
        var snapshot = session.Document.CurrentSnapshot;
        var ranges = snapshot.GetSelectedText(session.Selection);
        if (ranges.Length == 0 || ranges.All(range => range.Length == 0 && !range.IncludesParagraphEnd))
        {
            var before = RichTextDocumentSnapshot.ToNative(session.ResolveTypingCharacterFormat());
            var after = update(before) ?? throw new InvalidOperationException("A format update cannot return null.");
            session.ApplyCharacterFormat(RichTextDocument.CharacterOverrides(before, after));
            return;
        }
        session.Edit(edit =>
        {
            foreach (var range in ranges)
            {
                var paragraph = snapshot.Get<RichParagraph>(range.Start.Paragraph);
                var position = 0;
                foreach (var id in paragraph.Inlines)
                {
                    var inline = snapshot.Get<RichInline>(id);
                    var end = position + RichDocumentSnapshot.InlineLength(inline);
                    var first = Math.Max(position, range.Start.Offset);
                    var last = Math.Min(end, range.Start.Offset + range.Length);
                    if (first < last)
                    {
                        var before = RichTextDocumentSnapshot.ToNative(snapshot.ResolveCharacter(id));
                        var after = update(before) ?? throw new InvalidOperationException("A format update cannot return null.");
                        edit.SetCharacterFormat(range.Start with { Offset = first }, last - first,
                            inline.Format.Overlay(RichTextDocument.CharacterOverrides(before, after)));
                    }
                    position = end;
                }
                if (range.IncludesParagraphEnd)
                {
                    var before = RichTextDocumentSnapshot.ToNative(snapshot.Styles.ResolveCharacter(paragraph.Style, null, new(), snapshot.DefaultCharacter, paragraph.Character));
                    var after = update(before) ?? throw new InvalidOperationException("A format update cannot return null.");
                    edit.SetParagraphCharacterFormat(paragraph.Id, paragraph.Character.Overlay(RichTextDocument.CharacterOverrides(before, after)));
                }
            }
        });
    }

    private void UpdateStructuredParagraphFormat(Func<RichTextParagraphFormat, RichTextParagraphFormat> update)
    {
        var session = _editor.StructuredSession!;
        var snapshot = session.Document.CurrentSnapshot;
        session.Edit(edit =>
        {
            foreach (var id in snapshot.GetSelectedText(session.Selection).Select(range => range.Start.Paragraph).Distinct())
            {
                var before = RichTextDocumentSnapshot.ToNative(snapshot.ResolveParagraph(id));
                var after = update(before) ?? throw new InvalidOperationException("A format update cannot return null.");
                edit.SetParagraphFormat(id, snapshot.Get<RichParagraph>(id).Format.Overlay(RichTextDocument.ParagraphOverrides(before, after)));
            }
        });
    }
}
