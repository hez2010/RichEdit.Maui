namespace CodeEdit.Maui;

/// <summary>An immutable palette for source text, syntax tokens, and the line-number gutter.</summary>
public sealed record CodeEditorTheme
{
    /// <summary>Gets the light palette.</summary>
    public static CodeEditorTheme Light { get; } = new();
    /// <summary>Gets the dark palette.</summary>
    public static CodeEditorTheme Dark { get; } = new()
    {
        BackgroundColor = Color.FromArgb("#1E1E1E"), TextColor = Color.FromArgb("#D4D4D4"),
        GutterBackgroundColor = Color.FromArgb("#252526"), LineNumberColor = Color.FromArgb("#858585"),
        CurrentLineNumberColor = Color.FromArgb("#C6C6C6"), KeywordColor = Color.FromArgb("#569CD6"),
        StringColor = Color.FromArgb("#CE9178"), CommentColor = Color.FromArgb("#6A9955"),
        NumberColor = Color.FromArgb("#B5CEA8"), PreprocessorColor = Color.FromArgb("#C586C0"),
    };
    /// <summary>Gets the editor background.</summary>
    public Color BackgroundColor { get; init; } = Color.FromArgb("#FFFFFF");
    /// <summary>Gets the unclassified text color.</summary>
    public Color TextColor { get; init; } = Color.FromArgb("#202020");
    /// <summary>Gets the gutter background.</summary>
    public Color GutterBackgroundColor { get; init; } = Color.FromArgb("#F3F3F3");
    /// <summary>Gets the ordinary line-number color.</summary>
    public Color LineNumberColor { get; init; } = Color.FromArgb("#737373");
    /// <summary>Gets the line-number color at the current selection start.</summary>
    public Color CurrentLineNumberColor { get; init; } = Color.FromArgb("#202020");
    /// <summary>Gets the keyword color.</summary>
    public Color KeywordColor { get; init; } = Color.FromArgb("#0000FF");
    /// <summary>Gets the string and character-literal color.</summary>
    public Color StringColor { get; init; } = Color.FromArgb("#A31515");
    /// <summary>Gets the comment color.</summary>
    public Color CommentColor { get; init; } = Color.FromArgb("#008000");
    /// <summary>Gets the number color.</summary>
    public Color NumberColor { get; init; } = Color.FromArgb("#098658");
    /// <summary>Gets the preprocessor color.</summary>
    public Color PreprocessorColor { get; init; } = Color.FromArgb("#AF00DB");

    internal Color GetColor(CodeTokenKind kind) => kind switch
    {
        CodeTokenKind.Keyword => KeywordColor,
        CodeTokenKind.String => StringColor,
        CodeTokenKind.Comment => CommentColor,
        CodeTokenKind.Number => NumberColor,
        CodeTokenKind.Preprocessor => PreprocessorColor,
        _ => TextColor,
    };
}
