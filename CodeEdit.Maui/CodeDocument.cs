using System.ComponentModel;
using RichEdit.Maui;

namespace CodeEdit.Maui;

/// <summary>A live plain-source document with atomic edits, snapshots, and document-owned history.</summary>
/// <remarks>Line endings are normalized to LF. Attached documents are mutated on the editor's UI thread.</remarks>
public sealed partial class CodeDocument : INotifyPropertyChanged
{
    private CodeDocumentSnapshot? _snapshot;
    internal RichTextDocument Source { get; }

    /// <summary>Creates a source document with empty history and a clean saved state.</summary>
    /// <param name="text">The initial source, or null for empty source.</param>
    /// <param name="uri">A stable absolute URI for LSP, or null to create a unique untitled URI.</param>
    /// <param name="languageId">The standard LSP language identifier.</param>
    public CodeDocument(string? text = null, Uri? uri = null, string languageId = "plaintext")
    {
        if (uri is { IsAbsoluteUri: false }) throw new ArgumentException("The document URI must be absolute.", nameof(uri));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        Uri = uri ?? new Uri($"untitled:{Guid.NewGuid():N}");
        LanguageId = languageId;
        Source = RichTextDocument.FromPlainText(text);
        Source.Changed += (_, args) => Changed?.Invoke(this, args);
        Source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Version) or nameof(Length) or nameof(Text) or nameof(CurrentSnapshot) or
                nameof(CanUndo) or nameof(CanRedo) or nameof(UndoDescription) or nameof(RedoDescription) or nameof(IsModified) or nameof(IsUndoGroupOpen))
                PropertyChanged?.Invoke(this, args);
        };
    }

    /// <summary>Creates a source document with normalized line endings.</summary>
    /// <param name="text">The source text.</param>
    /// <param name="uri">The absolute document URI, or null for an untitled document.</param>
    /// <param name="languageId">The standard LSP language identifier.</param>
    /// <returns>A new source document.</returns>
    public static CodeDocument FromPlainText(string? text, Uri? uri = null, string languageId = "plaintext") => new(text, uri, languageId);

    /// <summary>Gets the stable LSP document URI.</summary>
    public Uri Uri { get; }
    /// <summary>Gets the standard LSP language identifier.</summary>
    public string LanguageId { get; }
    /// <summary>Gets the monotonically increasing content revision.</summary>
    public long Version => Source.Version;
    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => Source.Length;
    /// <summary>Gets the current source text.</summary>
    public string Text => Source.Text;
    /// <summary>Gets an immutable source snapshot suitable for background work.</summary>
    public CodeDocumentSnapshot CurrentSnapshot => _snapshot?.Version == Version ? _snapshot : _snapshot = new(Version, Text);
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
    internal CodeDocumentSnapshot(long version, string text) { Version = version; Text = text; }
    /// <summary>Gets the source revision.</summary>
    public long Version { get; }
    /// <summary>Gets the UTF-16 source text.</summary>
    public string Text { get; }
    /// <summary>Gets the UTF-16 source length.</summary>
    public int Length => Text.Length;
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
