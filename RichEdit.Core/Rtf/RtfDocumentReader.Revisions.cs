using System.Collections.Immutable;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private bool _revisionEnvelope;
    private readonly List<RevisionRecord> _revisionRecords = [];

    private sealed class RevisionRecord
    {
        internal readonly Dictionary<string, string> Values = [];
        internal readonly Dictionary<string, ImmutableArray<long>> Lists = [];
        internal int? Date;
        internal bool? CharacterDefaults, ParagraphDefaults;
        internal RetainedContext? Before, After;
    }

    private sealed record RetainedContext(RichDocumentSnapshot Snapshot, RtfIdentityMap Identities);

    private void ReadRevisionMetadata(string name, string value, RevisionRecord record)
    {
        if (record.Values.ContainsKey(name) || record.Lists.ContainsKey(name)) throw Error("Duplicate revision metadata");
        if (name is "revid" or "regroup" or "reauthor") { record.Values.Add(name, value); return; }
        var values = ImmutableArray.CreateBuilder<long>();
        var seen = new HashSet<long>();
        foreach (var part in value.AsSpan().Split(' '))
        {
            var item = value.AsSpan()[part];
            if (item.IsEmpty) continue;
            var id = RtfIdentityMap.Number(item);
            if (!seen.Add(id)) throw Error("Duplicate retained revision reference");
            ChargeResource();
            values.Add(id);
        }
        record.Lists.Add(name, values.ToImmutable());
    }

    private async Task ReadRevisionContextAsync(string word, CancellationToken cancellationToken)
    {
        if (!_state.AtStart || !_state.Ignorable || _state.RevisionRecord is not { } record || _retained)
            throw Error("Invalid retained RTF context");
        var reader = new RtfDocumentReader(_reader, options, _budget, retained: true);
        var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (reader._wireNodes is null) throw Error("A retained RTF context requires identities");
        var context = new RetainedContext(result.Snapshot, reader.Identities);
        if (word == "reold")
        {
            if (record.Before is not null) throw Error("Duplicate retained before-content");
            record.Before = context;
        }
        else
        {
            if (record.After is not null) throw Error("Duplicate retained after-content");
            record.After = context;
        }
        _diagnostics.AddRange(result.Diagnostics);
        var closing = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        _offset = closing.Offset;
        if (closing.Kind != RtfTokenKind.EndGroup) throw Error("A retained context must contain exactly one RTF document");
        _state = _states.Pop();
    }

    private RichDocumentSnapshot RestoreRevisions(RichDocumentSnapshot snapshot)
    {
        if (!_revisionEnvelope) return snapshot;
        if (_wireNodes is null || _revisionRecords.Count == 0) throw Error("A revision envelope requires identified content and revisions");
        try
        {
            var importer = new RtfRevisionImporter(new(snapshot, Identities));
            foreach (var record in _revisionRecords)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (record.Before is null || record.After is null) throw Error("A revision requires both retained contexts");
                importer.Include(new(record.Before.Snapshot, record.Before.Identities));
                importer.Include(new(record.After.Snapshot, record.After.Identities));
            }
            var revisions = ImmutableDictionary.CreateBuilder<long, RichRevision>();
            foreach (var record in _revisionRecords)
            {
                _cancellation.ThrowIfCancellationRequested();
                var id = RtfIdentityMap.Number(Required("revid"));
                if (!Guid.TryParseExact(Required("regroup"), "N", out var group) || record.Date is not { } packed) throw Error("Invalid revision authorship");
                var date = new DateTimeOffset(1900 + ((packed >> 20) & 511), (packed >> 16) & 15, (packed >> 11) & 31,
                    (packed >> 6) & 31, packed & 63, 0, TimeSpan.Zero);
                var info = new RichRevisionInfo(Required("reauthor"), group, date);
                var before = new RtfRevisionImporter.Context(record.Before!.Snapshot, record.Before.Identities);
                var after = new RtfRevisionImporter.Context(record.After!.Snapshot, record.After.Identities);
                if (record.CharacterDefaults is not { } characterDefaults || record.ParagraphDefaults is not { } paragraphDefaults)
                    throw Error("Missing retained default-format flags");
                var delta = importer.ReadDelta(before, after, List("renchanged"), List("reschanged"), List("reachanged"), List("rebchanged"), List("recchanged"),
                    record.Lists.GetValueOrDefault("rebeforeblocks"), record.Lists.GetValueOrDefault("reafterblocks"), characterDefaults, paragraphDefaults);
                if (delta.IsEmpty || !revisions.TryAdd(id, new(id, info, delta, List("redeps").ToImmutableHashSet())))
                    throw Error("Empty or duplicate retained revision");
                string Required(string name) => record.Values.TryGetValue(name, out var value) ? value : throw Error("Missing revision metadata");
                ImmutableArray<long> List(string name) => record.Lists.TryGetValue(name, out var value) ? value : throw Error("Missing revision references");
            }
            var result = importer.MainSnapshot().WithRevisions(revisions.ToImmutable());
            var seen = new List<RichRevision>();
            foreach (var revision in revisions.Values.OrderBy(revision => revision.Id))
            {
                ChargeRevisionWork(seen.Count + revision.Nodes.Count + revision.Styles.Count + revision.Assets.Count + revision.Bookmarks.Count + revision.Comments.Count);
                if (!revision.Dependencies.SetEquals(seen.Where(previous => revision.Delta.Overlaps(previous.Delta)).Select(previous => previous.Id)))
                    throw Error("Revision dependencies do not match retained content");
                if (seen.Any(previous => previous.Info.Group == revision.Info.Group && previous.Info != revision.Info))
                    throw Error("Conflicting authorship within a revision group");
                seen.Add(revision);
            }
            var state = result;
            foreach (var revision in seen.AsEnumerable().Reverse())
            {
                _cancellation.ThrowIfCancellationRequested();
                ChargeRevisionWork((long)state.Nodes.Count + state.Assets.Count + state.Styles.Definitions.Count + state.Bookmarks.Count + state.Comments.Count);
                importer.ValidateAfter(state, revision.Delta);
                state = RtfRevisionContext.Before(state, revision.Delta);
            }
            return result;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { throw new FormatException("Invalid retained RTF revisions.", error); }
    }

    private void ChargeRevisionWork(long entries)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (entries > options.MaximumRevisionWork - _budget.RevisionWork) throw Error("Retained revision validation limit exceeded");
        _budget.RevisionWork += entries;
    }
}
