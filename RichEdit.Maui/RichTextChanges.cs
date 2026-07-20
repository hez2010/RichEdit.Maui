using System.Collections.Immutable;

namespace RichEdit.Maui;

/// <summary>
/// Identifies the source of a committed document change.
/// </summary>
public enum RichTextChangeOrigin
{
    /// <summary>The change was made through a document or selection API.</summary>
    Programmatic,

    /// <summary>The change originated in a native editor interaction.</summary>
    User,

    /// <summary>The change restores an earlier undo state.</summary>
    Undo,

    /// <summary>The change reapplies a previously undone state.</summary>
    Redo,
}

/// <summary>
/// Identifies the kind of document content affected by a change.
/// </summary>
public enum RichTextChangeKind
{
    /// <summary>Logical text was inserted, removed, or replaced.</summary>
    Text,

    /// <summary>Character formatting changed.</summary>
    CharacterFormat,

    /// <summary>Paragraph formatting changed.</summary>
    ParagraphFormat,

    /// <summary>A document default format changed.</summary>
    DefaultFormat,

    /// <summary>A hyperlink changed.</summary>
    Link,

    /// <summary>A field changed.</summary>
    Field,

    /// <summary>An inline image changed.</summary>
    Image,

    /// <summary>A list definition or list item changed.</summary>
    List,

    /// <summary>Application metadata changed.</summary>
    Metadata,

    /// <summary>The complete document content was replaced.</summary>
    Reset,
}

/// <summary>
/// Controls how an atomic edit interacts with document undo history.
/// </summary>
public enum RichTextUndoBehavior
{
    /// <summary>Create one undo unit for the transaction.</summary>
    CreateUnit,

    /// <summary>Merge the transaction with the preceding compatible undo unit.</summary>
    MergeWithPrevious,

    /// <summary>
    /// Commit the transaction without recording it and invalidate snapshot-based undo
    /// history that cannot be safely replayed across the unrecorded state.
    /// </summary>
    DoNotRecord,

    /// <summary>Commit the transaction and clear existing undo and redo history.</summary>
    ClearHistory,
}

/// <summary>
/// Configures an atomic document edit.
/// </summary>
public readonly record struct RichTextEditOptions
{
    /// <summary>
    /// Initializes edit options.
    /// </summary>
    /// <param name="undoBehavior">The requested undo-history behavior.</param>
    /// <param name="undoDescription">An optional user-facing undo description.</param>
    /// <param name="tag">Optional application data propagated to the change set.</param>
    public RichTextEditOptions(
        RichTextUndoBehavior undoBehavior = RichTextUndoBehavior.CreateUnit,
        string? undoDescription = null,
        object? tag = null)
    {
        UndoBehavior = undoBehavior;
        UndoDescription = undoDescription;
        Tag = tag;
    }

    /// <summary>Gets the requested undo-history behavior.</summary>
    public RichTextUndoBehavior UndoBehavior { get; }

    /// <summary>Gets an optional user-facing undo description.</summary>
    public string? UndoDescription { get; }

    /// <summary>Gets optional application data propagated to the change event.</summary>
    public object? Tag { get; }
}

/// <summary>
/// Describes one bounded part of an atomic rich-text change.
/// </summary>
public abstract record RichTextChange
{
    private protected RichTextChange(
        RichTextChangeKind kind,
        RichTextRange oldRange,
        RichTextRange newRange)
    {
        Kind = kind;
        OldRange = oldRange;
        NewRange = newRange;
    }

    /// <summary>Gets the kind of content that changed.</summary>
    public RichTextChangeKind Kind { get; }

    /// <summary>Gets the affected range before the change.</summary>
    public RichTextRange OldRange { get; }

    /// <summary>Gets the affected range after the change.</summary>
    public RichTextRange NewRange { get; }
}

/// <summary>
/// Describes a logical text replacement.
/// </summary>
public sealed record RichTextTextChange : RichTextChange
{
    /// <summary>
    /// Initializes a text change.
    /// </summary>
    /// <param name="oldRange">The replaced range in the preceding document version.</param>
    /// <param name="insertedText">The normalized text inserted in its place.</param>
    public RichTextTextChange(RichTextRange oldRange, string insertedText)
        : base(
            RichTextChangeKind.Text,
            oldRange,
            new RichTextRange(oldRange.Start, (insertedText ?? throw new ArgumentNullException(nameof(insertedText))).Length))
    {
        InsertedText = insertedText;
    }

    /// <summary>Gets the normalized inserted text.</summary>
    public string InsertedText { get; }

    /// <summary>Gets the number of removed UTF-16 code units.</summary>
    public int RemovedLength => OldRange.Length;
}

/// <summary>
/// Describes a non-text change over a bounded document range.
/// </summary>
public sealed record RichTextRangeChange : RichTextChange
{
    /// <summary>
    /// Initializes a bounded non-text change.
    /// </summary>
    /// <param name="kind">The affected content kind.</param>
    /// <param name="oldRange">The affected range before the change.</param>
    /// <param name="newRange">The affected range after the change.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is <see cref="RichTextChangeKind.Text"/>.
    /// </exception>
    public RichTextRangeChange(
        RichTextChangeKind kind,
        RichTextRange oldRange,
        RichTextRange newRange)
        : base(kind, oldRange, newRange)
    {
        if (kind == RichTextChangeKind.Text)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Use RichTextTextChange for logical text replacements.");
        }
    }
}

/// <summary>
/// Describes one atomic transition between document versions.
/// </summary>
public sealed class RichTextChangeSet
{
    internal RichTextChangeSet(
        long versionBefore,
        long versionAfter,
        RichTextChangeOrigin origin,
        ImmutableArray<RichTextChange> changes,
        object? tag,
        object? sourceToken = null)
    {
        VersionBefore = versionBefore;
        VersionAfter = versionAfter;
        Origin = origin;
        Changes = changes;
        Tag = tag;
        SourceToken = sourceToken;
    }

    /// <summary>Gets the document version before the transaction.</summary>
    public long VersionBefore { get; }

    /// <summary>Gets the document version after the transaction.</summary>
    public long VersionAfter { get; }

    /// <summary>Gets the source category of the transaction.</summary>
    public RichTextChangeOrigin Origin { get; }

    /// <summary>Gets the ordered bounded changes in the transaction.</summary>
    public IReadOnlyList<RichTextChange> Changes { get; }

    /// <summary>Gets optional application data supplied in the edit options.</summary>
    public object? Tag { get; }

    /// <summary>Gets a value indicating whether the transaction made no changes.</summary>
    public bool IsEmpty => VersionBefore == VersionAfter || Changes.Count == 0;

    /// <summary>Gets a value indicating whether logical text changed.</summary>
    public bool IsTextChanged => Changes.Any(static change => change.Kind == RichTextChangeKind.Text);

    internal RichTextRange GetAffectedRange(int documentLength)
    {
        RichTextRange? affected = null;
        var hasNonemptyAffectedChange = Changes.Any(static change =>
            change.Kind != RichTextChangeKind.Metadata && !change.NewRange.IsEmpty);
        foreach (var change in Changes)
        {
            if (change is RichTextTextChange textChange && affected is { } preceding)
            {
                affected = MapRange(preceding, textChange);
            }

            if (change.Kind == RichTextChangeKind.Metadata ||
                change.Kind == RichTextChangeKind.List &&
                change.NewRange.IsEmpty &&
                hasNonemptyAffectedChange)
            {
                continue;
            }

            affected = affected is { } current
                ? Union(current, change.NewRange)
                : change.NewRange;
        }

        if (affected is not { } result)
        {
            return RichTextRange.Empty;
        }

        result = result.Clamp(documentLength);
        if (result.IsEmpty && documentLength > 0)
        {
            var start = Math.Min(result.Start, documentLength - 1);
            return new RichTextRange(start, 1);
        }

        return result;
    }

    private static RichTextRange MapRange(
        RichTextRange range,
        RichTextTextChange change)
    {
        var start = MapPosition(range.Start, change);
        var end = MapPosition(range.End, change);
        return new RichTextRange(Math.Min(start, end), Math.Abs(end - start));
    }

    private static int MapPosition(int position, RichTextTextChange change)
    {
        if (position <= change.OldRange.Start)
        {
            return position;
        }

        if (position >= change.OldRange.End)
        {
            return checked(position + change.NewRange.Length - change.OldRange.Length);
        }

        return change.NewRange.End;
    }

    private static RichTextRange Union(RichTextRange first, RichTextRange second)
    {
        var start = Math.Min(first.Start, second.Start);
        var end = Math.Max(first.End, second.End);
        return new RichTextRange(start, end - start);
    }

    internal object? SourceToken { get; }
}

/// <summary>
/// Provides data for <see cref="RichTextDocument.Changed"/>.
/// </summary>
public sealed class RichTextDocumentChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes document-change event data.
    /// </summary>
    /// <param name="changeSet">The committed atomic change set.</param>
    public RichTextDocumentChangedEventArgs(RichTextChangeSet changeSet)
    {
        ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
    }

    /// <summary>Gets the committed atomic change set.</summary>
    public RichTextChangeSet ChangeSet { get; }
}
