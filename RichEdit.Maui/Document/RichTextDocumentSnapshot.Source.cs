using System.Collections.Immutable;

namespace RichEdit.Maui;

public sealed partial class RichTextDocumentSnapshot
{
    // A native render description, never a second authored source document. Its
    // text root is the canonical source root; default paragraph entries are only
    // materialized if a platform renderer actually enumerates them.
    internal static RichTextDocumentSnapshot CreateSourceProjection(TextSnapshot text, RichTextCharacterFormat? defaultCharacterFormat = null) =>
        new(text, defaultCharacterFormat);

    internal static RichTextDocumentSnapshot CreateSourceDisplay(TextSnapshot text, IEnumerable<RichTextRun> runs, IEnumerable<RichTextImage> reservations) =>
        new(text, runs, reservations);

    private RichTextDocumentSnapshot(TextSnapshot text, IEnumerable<RichTextRun> runs, IEnumerable<RichTextImage> reservations) : this(text)
    {
        // Inputs are validated authored runs and private reservation descriptions
        // built by the projection. No imported/user object bypasses validation.
        _runs = NormalizeRuns(text.Length, runs, DefaultCharacterFormat);
        _images = reservations.OrderBy(image => image.Position).ToImmutableArray();
    }

    private RichTextDocumentSnapshot(TextSnapshot text, RichTextCharacterFormat? defaultCharacterFormat = null)
    {
        _text = text;
        Version = text.Revision.IsValid ? text.Revision.Version : 0;
        Revision = text.Revision;
        DefaultCharacterFormat = defaultCharacterFormat ?? RichTextCharacterFormat.Default;
        DefaultParagraphFormat = RichTextParagraphFormat.Default;
        _runs = text.Length == 0 ? [] : [new(0, text.Length, DefaultCharacterFormat)];
        _plainParagraphs = new(() => text.GetLines()
            .Select(line => new RichTextParagraph(line.Span.Start, DefaultParagraphFormat)).ToImmutableArray());
        _links = [];
        _fields = [];
        _images = [];
        _lists = ImmutableDictionary<RichTextListId, RichTextListDefinition>.Empty;
        _listPictures = ImmutableDictionary<string, RichTextListPicture>.Empty;
        _metadata = ImmutableDictionary<string, string>.Empty;
    }
}
