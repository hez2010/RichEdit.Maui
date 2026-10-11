using System.Collections.Immutable;

namespace RichEdit.Core;

public sealed partial class RichDocumentEdit
{
    private RichDocumentSnapshot? _resolutionExpected;
    private readonly RichDocumentSnapshot _resolutionInitial;

    private void BeginRevisionResolution()
    {
        if (!Candidate(validate: false).ContentEquals(_resolutionExpected ?? _resolutionInitial))
            throw new InvalidOperationException("Revision resolution cannot be combined with other authored changes in one transaction.");
    }
    /// <summary>Accepts a change group and its pending prerequisites atomically, releasing retained before-content.</summary>
    public void AcceptRevision(Guid group)
    {
        VerifyOpen();
        BeginRevisionResolution();
        var resolution = Candidate(validate: false).GetRevisionResolution(group, accept: true);
        var removed = resolution.Select(revision => revision.Id).ToHashSet();
        foreach (var id in removed) _revisions.Remove(id);
        foreach (var revision in _revisions.Values.ToArray())
        {
            var dependencies = revision.Dependencies.Except(removed);
            if (!ReferenceEquals(dependencies, revision.Dependencies))
                _revisions[revision.Id] = new(revision.Id, revision.Info, revision.Delta, dependencies);
        }
        _resolutionExpected = Candidate(validate: false);
    }

    /// <summary>Rejects a change group and dependent pending groups atomically, restoring retained content and resource relationships.</summary>
    public void RejectRevision(Guid group)
    {
        VerifyOpen();
        BeginRevisionResolution();
        var resolution = Candidate(validate: false).GetRevisionResolution(group, accept: false);
        foreach (var revision in resolution) _revisions.Remove(revision.Id);
        foreach (var revision in resolution.Reverse()) RestoreRevision(revision.Delta);
        _resolutionExpected = Candidate(validate: false);
    }

    private void RestoreRevision(RichRevisionDelta delta)
    {
        var before = Candidate(validate: false);
        var removedParagraphs = delta.Nodes.Where(pair => pair.Value.Before is null && pair.Value.After is RichParagraph).Select(pair => pair.Key).ToImmutableHashSet();
        if (!removedParagraphs.IsEmpty) Record(new RichAddressMap.Remove(removedParagraphs));
        // Retained paragraph identities keep other sessions' carets meaningful.
        // Compute text displacement before restoring annotations, whose precise
        // retained anchors must not be mapped a second time.
        var affected = delta.Nodes.Values.SelectMany(value => new[] { value.Before, value.After })
            .OfType<RichNode>().Select(node => node is RichParagraph ? node.Id : node is RichInline ? node.Parent : null)
            .OfType<RichNodeId>().Distinct().Where(id => before.Nodes.GetValueOrDefault(id) is RichParagraph).ToArray();
        Restore(_nodes, delta.Nodes);
        var restored = Candidate(validate: false);
        foreach (var paragraph in affected)
        {
            if (restored.Nodes.GetValueOrDefault(paragraph) is not RichParagraph) continue;
            var oldText = before.ProjectText(paragraph).Text;
            var newText = restored.ProjectText(paragraph).Text;
            if (oldText.FindDifference(newText, out _) is { } difference)
                Record(new RichAddressMap.Text(paragraph, difference.Before.Start, difference.Before.Length, difference.After.Length));
        }
        var definitions = _styles.Definitions.ToBuilder();
        Restore(definitions, delta.Styles);
        _styles = new(definitions.Values);
        Restore(_assets, delta.Assets);
        Restore(_bookmarks, delta.Bookmarks);
        Restore(_comments, delta.Comments);
        if (delta.DefaultCharacter is { } character) _defaultCharacter = character.Before!;
        if (delta.DefaultParagraph is { } paragraphFormat) _defaultParagraph = paragraphFormat.Before!;
    }

    private static void Restore<TKey, TValue>(ImmutableDictionary<TKey, TValue>.Builder destination,
        ImmutableDictionary<TKey, RichRevisionValue<TValue>> values) where TKey : notnull where TValue : class
    {
        foreach (var pair in values)
            if (pair.Value.Before is { } value) destination[pair.Key] = value;
            else destination.Remove(pair.Key);
    }
}
