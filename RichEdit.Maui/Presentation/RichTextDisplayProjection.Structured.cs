namespace RichEdit.Maui;

internal sealed partial class RichTextDisplayProjection
{
    private StructuredRichNativeMap? _structured;
    internal static RichTextDisplayProjection FromStructured(StructuredRichNativeMap map) => new(map.Reservations) { _structured = map };
    internal bool IsStructured => _structured is not null;
    internal RichTextDocumentSnapshot? StructuredDisplay => _structured is { } map ? NativeDescription(map) : null;

    internal static RichTextDocumentSnapshot NativeDescription(StructuredRichNativeMap map)
    {
        if (map.AuthoredDisplay is { } authored) return authored;
        if (map.Display is { } display) return display;
        var text = TextSnapshot.FromText(map.NativeText.Replace('\r', '\n').Replace('\v', RichTextDocument.SoftLineBreakCharacter));
        return RichTextDocumentSnapshot.CreateSourceProjection(text.WithRevision(map.Snapshot.Revision));
    }
}
