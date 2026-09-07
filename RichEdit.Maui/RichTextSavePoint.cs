namespace RichEdit.Maui;

/// <summary>An opaque document state captured for an asynchronous save operation.</summary>
/// <remarks>A save point belongs to one document and remains valid across edits and undo/redo.</remarks>
public readonly struct RichTextSavePoint
{
    internal RichTextSavePoint(object owner, long stateId) { Owner = owner; StateId = stateId; }
    internal object? Owner { get; }
    internal long StateId { get; }
}
