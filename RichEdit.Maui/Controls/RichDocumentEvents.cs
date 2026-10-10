using RichEdit.Core;

namespace RichEdit.Maui;

/// <summary>A control's document replacement, separate from an authored change inside either document.</summary>
public sealed class RichDocumentReplacedEventArgs(RichDocument? previous, RichDocument current) : EventArgs
{
    /// <summary>Gets the previously attached document, when available.</summary>
    public RichDocument? Previous { get; } = previous;
    /// <summary>Gets the newly attached document.</summary>
    public RichDocument Current { get; } = current;
}

/// <summary>An authored hyperlink invocation with typed document addresses.</summary>
public sealed class RichHyperlinkInvokedEventArgs(RichEdit.Core.RichTextSelection range, RichHyperlink link) : EventArgs
{
    /// <summary>Gets the authored hyperlink's text range.</summary>
    public RichEdit.Core.RichTextSelection Range { get; } = range;
    /// <summary>Gets the retained target and optional tooltip.</summary>
    public RichHyperlink Link { get; } = link;
    /// <summary>Gets or sets whether the application handled the invocation.</summary>
    public bool Handled { get; set; }
}

/// <summary>An authored inline-object invocation.</summary>
public sealed class RichObjectInvokedEventArgs(RichInline node) : EventArgs
{
    /// <summary>Gets the selected immutable object with its stable document identity.</summary>
    public RichInline Object { get; } = node;
    /// <summary>Gets or sets whether the application handled the invocation.</summary>
    public bool Handled { get; set; }
}
