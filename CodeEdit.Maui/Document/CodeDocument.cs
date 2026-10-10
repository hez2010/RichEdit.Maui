using System.ComponentModel;

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
/// <remarks>CRLF, LF, and CR line endings are preserved exactly. Attached documents are mutated on the editor's UI thread.</remarks>
public sealed partial class CodeDocument : INotifyPropertyChanged
{
    private CodeDocumentSnapshot? _snapshot;

    internal TextDocument Source { get; }

    /// <summary>Creates a source document with empty history and a clean saved state.</summary>
    /// <param name="text">The initial source, or null for empty source.</param>
    public CodeDocument(string? text = null)
    {
        Source = new TextDocument(text ?? "");
        Source.Changed += (_, args) =>
        {
            Changed?.Invoke(this, args);
            foreach (var name in new[] { nameof(Version), nameof(Revision), nameof(Length), nameof(Text), nameof(CurrentSnapshot) })
                PropertyChanged?.Invoke(this, new(name));
            NotifyState();
        };
        Source.StateChanged += (_, _) => NotifyState();
    }

    /// <summary>Creates a source document preserving its exact line endings.</summary>
    /// <param name="text">The source text.</param>
    /// <returns>A new source document.</returns>
    public static CodeDocument FromPlainText(string? text) => new(text);

    /// <summary>Gets the opaque source-document revision.</summary>
    public DocumentRevision Revision => Source.Revision;

    /// <summary>Gets source position and range tracking.</summary>
    public TextTracking Tracking => Source.Tracking;

    /// <summary>Gets the monotonically increasing content revision.</summary>
    public long Version => Source.Revision.Version;

    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => Source.Length;

    /// <summary>Gets the current source text.</summary>
    public string Text => Source.Text;

    /// <summary>Gets an immutable source snapshot suitable for background work.</summary>
    public CodeDocumentSnapshot CurrentSnapshot
    {
        get
        {
            var source = Source.CurrentSnapshot;
            var snapshot = Volatile.Read(ref _snapshot);
            if (snapshot?.Revision == source.Revision) return snapshot;
            snapshot = new(source.Revision, source);
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

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
    public event EventHandler<TextDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Applies a transaction of source-only edits.</summary>
    /// <param name="edit">The transaction callback.</param>
    /// <param name="options">History and application metadata.</param>
    /// <returns>The committed change set.</returns>
    public TextDocumentChangedEventArgs? Edit(Action<CodeDocumentEdit> edit, TextEditOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return Source.Edit(transaction => edit(new CodeDocumentEdit(transaction)), options);
    }

    /// <summary>Reads a bounded UTF-16 source range.</summary>
    /// <param name="range">The range to read.</param>
    /// <returns>The source in that range.</returns>
    public string GetText(TextSpan range) => Source.CurrentSnapshot.GetText(range);

    /// <summary>Undoes the preceding edit and maps each attached view's selection.</summary>
    public void Undo() => Source.Undo();

    /// <summary>Reapplies the next edit and maps each attached view's selection.</summary>
    public void Redo() => Source.Redo();

    /// <summary>Clears history without changing source or its saved state.</summary>
    public void ClearUndoHistory() => Source.ClearHistory();

    /// <summary>Groups separately committed edits into one undo unit.</summary>
    /// <param name="description">The group description.</param>
    /// <returns>A scope disposed in reverse nesting order.</returns>
    public IDisposable BeginUndoGroup(string? description = null) => Source.BeginUndoGroup(description);

    /// <summary>Captures an immutable snapshot and its paired state to acknowledge after an asynchronous save succeeds.</summary>
    /// <returns>The snapshot paired with its save token.</returns>
    public TextSaveCapture CaptureSave() => Source.CaptureSave();

    /// <summary>Marks the current source state as saved.</summary>
    public void MarkSaved() => Source.MarkSaved(Source.CaptureSave());

    /// <summary>Marks a captured source state as saved, preserving the dirty state of intervening edits.</summary>
    /// <param name="savePoint">The successfully saved state.</param>
    public void MarkSaved(TextSaveCapture savePoint) => Source.MarkSaved(savePoint);

    /// <summary>Gets or sets the maximum retained undo units.</summary>
    public int HistoryLimit { get => Source.HistoryLimit; set => Source.HistoryLimit = value; }

    /// <summary>Gets or sets the estimated retained-history memory budget.</summary>
    public long HistoryByteLimit { get => Source.HistoryByteLimit; set => Source.HistoryByteLimit = value; }

    /// <summary>Gets the estimated history storage, excluding application-held snapshots.</summary>
    public long RetainedHistoryByteEstimate => Source.RetainedHistoryByteEstimate;

    private void NotifyState()
    {
        foreach (var name in new[] { nameof(CanUndo), nameof(CanRedo), nameof(UndoDescription), nameof(RedoDescription),
            nameof(IsModified), nameof(IsUndoGroupOpen), nameof(RetainedHistoryByteEstimate) })
            PropertyChanged?.Invoke(this, new(name));
    }

}

/// <summary>An immutable plain-source snapshot.</summary>
public sealed class CodeDocumentSnapshot
{
    private readonly CodeLineMap _lines;
    internal CodeLineMap Lines => _lines;

    internal CodeDocumentSnapshot(DocumentRevision revision, TextSnapshot text)
    {
        Revision = revision;
        TextBuffer = text;
        _lines = new(text);
    }

    /// <summary>Gets the source revision.</summary>
    public long Version => Revision.Version;

    /// <summary>Gets the captured document identity and version.</summary>
    public DocumentRevision Revision { get; }

    /// <summary>Gets the UTF-16 source text.</summary>
    public string Text => TextBuffer.ToString();

    /// <summary>Gets the persistent text storage represented by this snapshot.</summary>
    public TextSnapshot TextBuffer { get; }

    /// <summary>Reads a bounded UTF-16 range without materializing the complete source.</summary>
    public string GetText(TextSpan range) => TextBuffer.GetText(range);

    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => TextBuffer.Length;

    /// <summary>Gets the number of logical source lines, including an empty trailing line.</summary>
    public int LineCount => _lines.Count;

    /// <summary>Gets a zero-based source line, excluding its terminator.</summary>
    /// <param name="line">The zero-based logical line.</param>
    /// <returns>The line's source range.</returns>
    public TextSpan GetLineRange(int line) => _lines.GetRange(line);

    /// <summary>Gets a zero-based source line with its exact terminator length.</summary>
    public TextLine GetLine(int line) => TextBuffer.GetLine(line);

    /// <summary>Converts a UTF-16 source offset to a zero-based line and column.</summary>
    /// <param name="offset">The source offset.</param>
    /// <returns>The source position.</returns>
    public TextPosition GetPosition(int offset) => _lines.GetPosition(offset);

    /// <summary>Converts a zero-based line and UTF-16 column to an exact source offset.</summary>
    /// <param name="position">The source position.</param>
    /// <returns>The source offset.</returns>
    public int GetOffset(TextPosition position) => TextBuffer.GetOffset(position);
}

/// <summary>Describes source-only changes within a CodeDocument transaction.</summary>
/// <remarks>Offsets observe preceding operations in the same callback.</remarks>
public sealed class CodeDocumentEdit
{
    private readonly TextDocumentEdit _edit;

    internal CodeDocumentEdit(TextDocumentEdit edit) => _edit = edit;

    /// <summary>Inserts source at a UTF-16 offset.</summary>
    /// <param name="position">The insertion offset.</param>
    /// <param name="text">The inserted source.</param>
    public void InsertText(int position, string? text) => _edit.Insert(position, text ?? "");

    /// <summary>Deletes a UTF-16 source range.</summary>
    /// <param name="range">The range to delete.</param>
    public void DeleteText(TextSpan range) => _edit.Delete(range);

    /// <summary>Replaces a UTF-16 range with source text.</summary>
    /// <param name="range">The replaced range.</param>
    /// <param name="text">The replacement source.</param>
    public void ReplaceText(TextSpan range, string? text) => _edit.Replace(range, text ?? "");

    /// <summary>Explicitly replaces every source line terminator with LF, CRLF, or CR in this undoable transaction.</summary>
    public void NormalizeLineEndings(string lineEnding) => _edit.NormalizeLineEndings(lineEnding);
}
