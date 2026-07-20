namespace RichEdit.Maui;

/// <summary>
/// Provides data for <see cref="RichEditor.ContentChanged"/>.
/// </summary>
public sealed class RichTextContentChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes content-change event data.
    /// </summary>
    /// <param name="changeSet">The committed atomic change set.</param>
    public RichTextContentChangedEventArgs(RichTextChangeSet changeSet)
    {
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
    }

    /// <summary>Gets the committed atomic change set.</summary>
    public RichTextChangeSet ChangeSet { get; }
}

/// <summary>
/// Provides data for <see cref="RichEditor.TextChanged"/>.
/// </summary>
public sealed class RichTextTextChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes text-change event data.
    /// </summary>
    /// <param name="changeSet">The committed change set containing text changes.</param>
    public RichTextTextChangedEventArgs(RichTextChangeSet changeSet)
    {
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
        Changes = changeSet.Changes.OfType<RichTextTextChange>().ToArray();
    }

    /// <summary>Gets the complete atomic change set.</summary>
    public RichTextChangeSet ChangeSet { get; }

    /// <summary>Gets the logical text replacements in transaction order.</summary>
    public IReadOnlyList<RichTextTextChange> Changes { get; }
}

/// <summary>
/// Provides data for <see cref="RichEditor.SelectionChanged"/>.
/// </summary>
public sealed class RichTextSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes selection-change event data.
    /// </summary>
    /// <param name="oldRange">The preceding selection range.</param>
    /// <param name="newRange">The current selection range.</param>
    public RichTextSelectionChangedEventArgs(
        RichTextRange oldRange,
        RichTextRange newRange)
    {
        OldRange = oldRange;
        NewRange = newRange;
    }

    /// <summary>Gets the preceding selection range.</summary>
    public RichTextRange OldRange { get; }

    /// <summary>Gets the current selection range.</summary>
    public RichTextRange NewRange { get; }
}

/// <summary>
/// Provides data when a hyperlink is invoked.
/// </summary>
public sealed class RichTextLinkInvokedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes hyperlink invocation data.
    /// </summary>
    /// <param name="range">The hyperlink display range.</param>
    /// <param name="target">The application-defined target.</param>
    /// <param name="toolTip">The optional hyperlink tooltip.</param>
    public RichTextLinkInvokedEventArgs(
        RichTextRange range,
        string target,
        string? toolTip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Range = range;
        Target = target;
        ToolTip = toolTip;
    }

    /// <summary>Gets the hyperlink display range.</summary>
    public RichTextRange Range { get; }

    /// <summary>Gets the application-defined hyperlink target.</summary>
    public string Target { get; }

    /// <summary>Gets the optional hyperlink tooltip.</summary>
    public string? ToolTip { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the application handled the invocation.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Provides data when an inline object is invoked.
/// </summary>
public sealed class RichTextInlineObjectInvokedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes inline-object invocation data.
    /// </summary>
    /// <param name="image">The invoked image value.</param>
    public RichTextInlineObjectInvokedEventArgs(RichTextImage image)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }

    /// <summary>Gets the invoked image value.</summary>
    public RichTextImage Image { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the application handled the invocation.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Represents immutable rich content being transferred as one bounded fragment.
/// </summary>
public sealed class RichTextDocumentFragment
{
    internal RichTextDocumentFragment(RichTextDocumentSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    /// <summary>Gets the fragment's logical plain text.</summary>
    public string Text => Snapshot.Text;

    /// <summary>Gets the fragment's canonical RTF representation.</summary>
    public string RtfText => Snapshot.RtfText;

    /// <summary>Gets the immutable rich document snapshot.</summary>
    public RichTextDocumentSnapshot Snapshot { get; }

    /// <summary>
    /// Creates a rich fragment from canonical RTF.
    /// </summary>
    /// <param name="rtfText">The RTF value to parse.</param>
    /// <returns>A validated immutable fragment.</returns>
    public static RichTextDocumentFragment FromRtf(string rtfText)
    {
        ArgumentNullException.ThrowIfNull(rtfText);
        return new RichTextDocumentFragment(RtfCodec.Parse(rtfText));
    }

    /// <summary>
    /// Creates a plain-text fragment.
    /// </summary>
    /// <param name="text">The text to include.</param>
    /// <returns>An immutable plain-text fragment.</returns>
    public static RichTextDocumentFragment FromPlainText(string? text) =>
        new(new RichTextDocumentSnapshot(text));

    internal static RichTextDocumentFragment FromRange(
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        range.Validate(snapshot.Length, nameof(range));
        var fragment = snapshot;
        if (range.End < fragment.Length)
        {
            fragment = fragment.Replace(range.End..fragment.Length, string.Empty);
        }

        if (range.Start > 0)
        {
            fragment = fragment.Replace(0..range.Start, string.Empty);
        }

        return new RichTextDocumentFragment(
            fragment.PruneUnreferencedListResources().WithVersion(0));
    }
}

/// <summary>
/// Provides mutable paste interception data.
/// </summary>
public sealed class RichTextPastingEventArgs : EventArgs
{
    private RichTextDocumentFragment _fragment;

    /// <summary>
    /// Initializes paste interception data.
    /// </summary>
    /// <param name="fragment">The portable fragment selected by the handler.</param>
    public RichTextPastingEventArgs(RichTextDocumentFragment fragment)
    {
        _fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
    }

    /// <summary>Gets or sets the portable fragment that will be inserted.</summary>
    public RichTextDocumentFragment Fragment
    {
        get => _fragment;
        set => _fragment = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets a value indicating whether paste is canceled.</summary>
    public bool Cancel { get; set; }
}
