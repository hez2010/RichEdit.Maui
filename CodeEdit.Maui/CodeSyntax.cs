using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Identifies a lexical category for syntax coloring.</summary>
public enum CodeTokenKind
{
    /// <summary>A language keyword.</summary>
    Keyword,
    /// <summary>A string or character literal.</summary>
    String,
    /// <summary>A line or block comment.</summary>
    Comment,
    /// <summary>A numeric literal.</summary>
    Number,
    /// <summary>A preprocessor directive.</summary>
    Preprocessor,
}

/// <summary>Describes a syntax token using UTF-16 document offsets.</summary>
/// <param name="Range">The nonempty source range.</param>
/// <param name="Kind">The token's lexical category.</param>
public readonly record struct CodeToken(RichTextRange Range, CodeTokenKind Kind);

/// <summary>Classifies an immutable source string without accessing a native view.</summary>
public interface ICodeSyntaxHighlighter
{
    /// <summary>Returns ordered, non-overlapping tokens within <paramref name="text"/>.</summary>
    /// <remarks>
    /// Called on a worker thread. Implementations must be thread-safe, observe cancellation,
    /// and leave unclassified text out of the result. Results are checked before rendering.
    /// </remarks>
    /// <param name="text">The complete source text, with normalized line endings.</param>
    /// <param name="cancellationToken">Cancellation for a superseded request.</param>
    /// <returns>The lexical tokens. The returned collection must not subsequently change.</returns>
    IReadOnlyList<CodeToken> Highlight(string text, CancellationToken cancellationToken = default);
}

/// <summary>Provides details of a failed automatic highlighting request.</summary>
/// <param name="exception">The highlighter or token-validation failure.</param>
public sealed class CodeHighlightingFailedEventArgs(Exception exception) : EventArgs
{
    /// <summary>Gets the failure reported by the highlighter.</summary>
    public Exception Exception { get; } = exception;
}
