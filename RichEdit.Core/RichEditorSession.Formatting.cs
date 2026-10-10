namespace RichEdit.Core;

public sealed partial class RichEditorSession
{
    /// <summary>Applies character overrides to selected content, or to view-owned typing state at collapsed carets.</summary>
    public RichEditResult ApplyCharacterFormat(RichCharacterStyle format)
    {
        VerifyIdle();
        ArgumentNullException.ThrowIfNull(format);
        RichStyleSheet.Validate(format);
        if (IsReadOnly || Composition is not null) return Edit(_ => { });
        var ranges = Document.CurrentSnapshot.GetSelectedText(Selection);
        if (ranges.Length == 0 || ranges.All(range => range.Length == 0 && !range.IncludesParagraphEnd))
        {
            TypingCharacterFormat = TypingCharacterFormat.Overlay(format);
            return Edit(_ => { });
        }
        return Edit(edit => edit.ApplyCharacterFormat(Selection, format));
    }

    /// <summary>Toggles bold according to the resolved values across the typed selection.</summary>
    public RichEditResult ToggleBold() => ApplyCharacterFormat(new() { Bold = !SelectedFormats().All(format => format.Bold) });
    /// <summary>Toggles italic according to the resolved values across the typed selection.</summary>
    public RichEditResult ToggleItalic() => ApplyCharacterFormat(new() { Italic = !SelectedFormats().All(format => format.Italic) });
    /// <summary>Toggles single underlining according to the resolved values across the typed selection.</summary>
    public RichEditResult ToggleUnderline() => ApplyCharacterFormat(new()
    { Underline = SelectedFormats().All(format => format.Underline != RichUnderline.None) ? RichUnderline.None : RichUnderline.Single });

    private IEnumerable<ResolvedCharacterStyle> SelectedFormats()
    {
        var snapshot = Document.CurrentSnapshot;
        var ranges = snapshot.GetSelectedText(Selection);
        var formats = snapshot.GetSelectedCharacterFormats(Selection);
        return ranges.Length == 0 || ranges.All(range => range.Length == 0 && !range.IncludesParagraphEnd)
            ? formats.Select(format => TypingCharacterFormat.Apply(format)) : formats;
    }
}
