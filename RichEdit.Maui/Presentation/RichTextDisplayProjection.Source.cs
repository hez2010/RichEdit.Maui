namespace RichEdit.Maui;

internal sealed partial class RichTextDisplayProjection
{
    private LfTextProjection? _lineEndings;

    internal RichTextDisplayProjection WithLineEndings(LfTextProjection lineEndings) =>
        new(Reservations.Select(item => item with { SourcePosition = lineEndings.ToSource(item.SourcePosition) }))
        { _lineEndings = lineEndings };

    private RichTextDisplayProjection WithoutLineEndings() => _lineEndings is null ? this :
        new(Reservations.Select(item => item with { SourcePosition = _lineEndings.ToDisplay(item.SourcePosition) }));
}
