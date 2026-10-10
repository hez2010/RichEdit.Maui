using System.Runtime.CompilerServices;

namespace RichEdit.Maui;

public sealed partial class RichTextDocument
{
    private static readonly ConditionalWeakTable<TextDocument, RichTextDocument> SourceProjections = new();
    internal TextDocument? SourceDocument { get; }
    private readonly IDisposable? _sourceSubscription;
    private object? _sourceOrigin;
    private RichTextChangeSet? _sourceCorrections;
    private RichTextChangeSet? _lastSourceChange;
    internal LfTextProjection? SourceLineEndings { get; private set; }
    private RichTextChangeSet? _sourceDisplayChange;

    internal RichTextChangeSet? GetSourceDisplayChange(RichTextChangeSet change) =>
        ReferenceEquals(change, _lastSourceChange) ? _sourceDisplayChange : null;

    internal static RichTextDocument ProjectSource(TextDocument source) => SourceProjections.GetValue(source, static document => new(document));

    private RichTextDocument(TextDocument source)
    {
        SourceDocument = source;
        SourceLineEndings = new(source.CurrentSnapshot);
        _snapshot = RichTextDocumentSnapshot.CreateSourceProjection(source.CurrentSnapshot);
        Tracking = source.Tracking;
        _sourceSubscription = source.SubscribeProjection(PublishSource);
        source.StateChanged += (_, _) => RaiseUndoStateChanged();
    }

    private void PublishSource(TextDocumentChangedEventArgs change)
    {
        var before = _snapshot;
        var previousLf = SourceLineEndings!;
        SourceLineEndings = previousLf.Apply(change, out var displayEdits);
        _sourceDisplayChange = new(change.Before.Revision.Version, change.After.Revision.Version,
            (RichTextChangeOrigin)change.Options.Origin,
            [.. displayEdits.Select(edit => new RichTextTextChange(edit.Range, edit.Text))], change.Options.Tag,
            _sourceOrigin, RichTextDocumentSnapshot.CreateSourceProjection(previousLf.Display),
            RichTextDocumentSnapshot.CreateSourceProjection(SourceLineEndings.Display), undoDescription: change.Options.Description);
        _snapshot = RichTextDocumentSnapshot.CreateSourceProjection(change.After);
        _lastSourceChange = new(change.Before.Revision.Version, change.After.Revision.Version,
            (RichTextChangeOrigin)change.Options.Origin,
            [.. change.Changes.Select(edit => new RichTextTextChange(edit.OldSpan, edit.Inserted.ToString()))],
            change.Options.Tag, _sourceOrigin, before, _snapshot, undoDescription: change.Options.Description)
        { NativeCorrections = _sourceCorrections };
        InvalidateRtfCache();
        RaiseChanged(_lastSourceChange, before, undoStateChanged: true);
    }

    private RichTextChangeSet CommitSourceChanges(IReadOnlyList<RichTextChange> changes, RichTextEditOptions options,
        RichTextChangeOrigin origin, object? sourceToken, RichTextChangeSet? corrections = null)
    {
        if (changes.Any(change => change.Kind != RichTextChangeKind.Text))
            throw new InvalidOperationException("Source documents accept text operations only.");
        var source = SourceDocument!;
        var view = FindEditingView(sourceToken);
        var version = Version;
        _sourceOrigin = sourceToken;
        _sourceCorrections = corrections;
        try
        {
            void Apply(TextDocumentEdit edit)
            {
                foreach (var change in changes.Cast<RichTextTextChange>())
                    edit.Replace(change.OldRange, change.InsertedText);
                if (view is not null) edit.SetSelections(view.GetSelectionsAfterEdit(changes, edit.Snapshot.Length));
            }
            var metadata = new TextEditOptions(options.UndoDescription, options.Tag, (TextChangeOrigin)origin);
            if (view?.SourceSession is { } session)
            {
                if (view.IsReadOnly) return new(version, version, origin, [], options.Tag);
                var result = session.Edit(Apply, metadata);
                if (!result.Accepted) throw new InvalidOperationException($"The native source edit was rejected: {result.Rejection}.");
            }
            else source.Edit(Apply, metadata);
            return Version == version ? new(version, version, origin, [], options.Tag) : _lastSourceChange!;
        }
        finally { _sourceOrigin = null; _sourceCorrections = null; }
    }
}
