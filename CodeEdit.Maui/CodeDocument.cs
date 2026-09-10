using System.ComponentModel;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>Describes replacement of a source editor's live document.</summary>
/// <param name="oldDocument">The previous document, or null during initialization.</param>
/// <param name="newDocument">The newly attached document.</param>
public sealed class CodeDocumentReplacedEventArgs(CodeDocument? oldDocument, CodeDocument newDocument) : EventArgs
{
    /// <summary>Gets the previous document.</summary>
    public CodeDocument? OldDocument { get; } = oldDocument;
    /// <summary>Gets the newly attached document.</summary>
    public CodeDocument NewDocument { get; } = newDocument;
}

/// <summary>A live plain-source document with atomic edits, snapshots, and document-owned history.</summary>
/// <remarks>Line endings are normalized to LF. Attached documents are mutated on the editor's UI thread.</remarks>
public sealed partial class CodeDocument : INotifyPropertyChanged
{
    private CodeDocumentSnapshot? _snapshot;
    internal RichTextDocument Source { get; }

    /// <summary>Creates a source document with empty history and a clean saved state.</summary>
    /// <param name="text">The initial source, or null for empty source.</param>
    public CodeDocument(string? text = null)
    {
        Source = RichTextDocument.FromPlainText(text);
        Source.Changed += (_, args) => Changed?.Invoke(this, args);
        Source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Version) or nameof(Revision) or nameof(Length) or nameof(Text) or nameof(CurrentSnapshot) or
                nameof(CanUndo) or nameof(CanRedo) or nameof(UndoDescription) or nameof(RedoDescription) or nameof(IsModified) or nameof(IsUndoGroupOpen))
                PropertyChanged?.Invoke(this, args);
        };
    }

    /// <summary>Creates a source document with normalized line endings.</summary>
    /// <param name="text">The source text.</param>
    /// <returns>A new source document.</returns>
    public static CodeDocument FromPlainText(string? text) => new(text);

    /// <summary>Gets the opaque source-document revision.</summary>
    public RichTextRevision Revision => Source.Revision;
    /// <summary>Gets source position and range tracking.</summary>
    public RichTextTracking Tracking => Source.Tracking;
    /// <summary>Gets the monotonically increasing content revision.</summary>
    public long Version => Source.Version;
    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => Source.Length;
    /// <summary>Gets the current source text.</summary>
    public string Text => Source.Text;
    /// <summary>Gets an immutable source snapshot suitable for background work.</summary>
    public CodeDocumentSnapshot CurrentSnapshot => _snapshot?.Revision == Revision ? _snapshot : _snapshot = new(Revision, Text);
    /// <summary>Gets whether undo is available.</summary>
    public bool CanUndo => Source.CanUndo;
    /// <summary>Gets whether redo is available.</summary>
    public bool CanRedo => Source.CanRedo;
    /// <summary>Gets the next undo description.</summary>
    public string? UndoDescription => Source.UndoDescription;
    /// <summary>Gets the next redo description.</summary>
    public string? RedoDescription => Source.RedoDescription;
    /// <summary>Gets whether source has changed since the marked saved state.</summary>
    public bool IsModified => Source.IsModified;
    /// <summary>Gets whether an undo group is open.</summary>
    public bool IsUndoGroupOpen => Source.IsUndoGroupOpen;

    /// <summary>Occurs once after an atomic source edit, with the attached selection already synchronized.</summary>
    public event EventHandler<RichTextDocumentChangedEventArgs>? Changed;
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applies a transaction of source-only edits.</summary>
    /// <param name="edit">The transaction callback.</param>
    /// <param name="options">History and application metadata.</param>
    /// <returns>The committed change set.</returns>
    public RichTextChangeSet Edit(Action<CodeDocumentEdit> edit, RichTextEditOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Source.Edit(transaction => edit(new CodeDocumentEdit(transaction)), options);
    }

    /// <summary>Reads a bounded UTF-16 source range.</summary>
    /// <param name="range">The range to read.</param>
    /// <returns>The source in that range.</returns>
    public string GetText(RichTextRange range) => Source.GetText(range);
    /// <summary>Undoes the preceding edit, including its editor selection.</summary>
    public void Undo() => Source.Undo();
    /// <summary>Reapplies the next edit, including its editor selection.</summary>
    public void Redo() => Source.Redo();
    /// <summary>Clears history without changing source or its saved state.</summary>
    public void ClearUndoHistory() => Source.ClearUndoHistory();
    /// <summary>Groups separately committed edits into one undo unit.</summary>
    /// <param name="description">The group description.</param>
    /// <returns>A scope disposed in reverse nesting order.</returns>
    public IDisposable BeginUndoGroup(string? description = null) => Source.BeginUndoGroup(description);
    /// <summary>Captures the source state to mark after an asynchronous save succeeds.</summary>
    /// <returns>An opaque save point.</returns>
    public RichTextSavePoint CreateSavePoint() => Source.CreateSavePoint();
    /// <summary>Marks the current source state as saved.</summary>
    public void MarkSaved() => Source.MarkSaved();
    /// <summary>Marks a captured source state as saved, preserving the dirty state of intervening edits.</summary>
    /// <param name="savePoint">The successfully saved state.</param>
    public void MarkSaved(RichTextSavePoint savePoint) => Source.MarkSaved(savePoint);
}

/// <summary>An immutable plain-source snapshot.</summary>
public sealed class CodeDocumentSnapshot
{
    private readonly CodeLineMap _lines;
    internal CodeDocumentSnapshot(RichTextRevision revision, string text) { Revision = revision; Text = text; _lines = new(text); }
    /// <summary>Gets the source revision.</summary>
    public long Version => Revision.Version;
    /// <summary>Gets the captured document identity and version.</summary>
    public RichTextRevision Revision { get; }
    /// <summary>Gets the UTF-16 source text.</summary>
    public string Text { get; }
    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => Text.Length;
    /// <summary>Gets the number of logical source lines, including an empty trailing line.</summary>
    public int LineCount => _lines.Count;
    /// <summary>Gets a one-based source line, excluding LF.</summary>
    /// <param name="line">The one-based logical line.</param>
    /// <returns>The line's source range.</returns>
    public RichTextRange GetLineRange(int line) => _lines.GetRange(line - 1);
    /// <summary>Converts a UTF-16 source offset to a one-based line and column.</summary>
    /// <param name="offset">The source offset.</param>
    /// <returns>The source position.</returns>
    public CodePosition GetPosition(int offset) => _lines.GetPosition(offset);
    /// <summary>Converts a one-based line and column to a UTF-16 source offset.</summary>
    /// <param name="position">The source position.</param>
    /// <returns>The source offset.</returns>
    public int GetOffset(CodePosition position)
    {
        var range = GetLineRange(position.Line);
        ArgumentOutOfRangeException.ThrowIfLessThan(position.Column, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position.Column - 1, range.Length);
        return range.Start + position.Column - 1;
    }
}

/// <summary>Describes source-only changes within a CodeDocument transaction.</summary>
/// <remarks>Offsets observe preceding operations in the same callback.</remarks>
public sealed class CodeDocumentEdit
{
    private readonly RichTextDocumentEdit _edit;
    internal CodeDocumentEdit(RichTextDocumentEdit edit) => _edit = edit;
    /// <summary>Inserts source at a UTF-16 offset.</summary>
    /// <param name="position">The insertion offset.</param>
    /// <param name="text">The inserted source.</param>
    public void InsertText(int position, string? text) => _edit.InsertText(position, text);
    /// <summary>Deletes a UTF-16 source range.</summary>
    /// <param name="range">The range to delete.</param>
    public void DeleteText(RichTextRange range) => _edit.DeleteText(range);
    /// <summary>Replaces a UTF-16 range with source text.</summary>
    /// <param name="range">The replaced range.</param>
    /// <param name="text">The replacement source.</param>
    public void ReplaceText(RichTextRange range, string? text) => _edit.ReplaceText(range, text);
}
