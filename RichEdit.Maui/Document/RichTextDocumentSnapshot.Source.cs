using System.Collections.Immutable;

namespace RichEdit.Maui;

public sealed partial class RichTextDocumentSnapshot
{
    // A native render description, never a second authored source document. Its
    // text root is the canonical source root; default paragraph entries are only
    // materialized if a platform renderer actually enumerates them.
    internal static RichTextDocumentSnapshot CreateSourceProjection(TextSnapshot text) => new(text);

    private RichTextDocumentSnapshot(TextSnapshot text)
    {
        _text = text;
        Version = text.Revision.IsValid ? text.Revision.Version : 0;
        Revision = text.Revision;
        DefaultCharacterFormat = RichTextCharacterFormat.Default;
        DefaultParagraphFormat = RichTextParagraphFormat.Default;
        _runs = text.Length == 0 ? [] : [new(0, text.Length, DefaultCharacterFormat)];
        _plainParagraphs = new(() => Enumerable.Range(0, text.LineCount)
            .Select(index => new RichTextParagraph(text.GetLine(index).Span.Start, DefaultParagraphFormat)).ToImmutableArray());
        _links = [];
        _fields = [];
        _images = [];
        _lists = ImmutableDictionary<RichTextListId, RichTextListDefinition>.Empty;
        _listPictures = ImmutableDictionary<string, RichTextListPicture>.Empty;
        _metadata = ImmutableDictionary<string, string>.Empty;
    }
}
