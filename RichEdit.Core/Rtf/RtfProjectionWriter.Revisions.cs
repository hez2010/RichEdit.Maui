using System.Globalization;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfProjectionWriter
{
    private async Task Revisions()
    {
        if (snapshot.Revisions.IsEmpty) return;
        await S(@"{\*\rerevisions1");
        foreach (var entry in RtfRevisionExport.Create(snapshot, _cancellation))
        {
            _cancellation.ThrowIfCancellationRequested();
            var revision = entry.Revision;
            await S(@"{\*\rerevision");
            await Value("revid", revision.Id.ToString(CultureInfo.InvariantCulture));
            await Value("regroup", revision.Info.Group.ToString("N"));
            await Value("reauthor", revision.Info.Author);
            var date = revision.Info.Created;
            await C("redate", date.Minute | date.Hour << 6 | date.Day << 11 | date.Month << 16 | (date.Year - 1900) << 20);
            await Ids("redeps", revision.Dependencies);
            await Ids("renchanged", revision.Nodes.Keys.Select(id => id.Value));
            await Ids("reschanged", revision.Styles.Keys.Select(id => (long)id.Value));
            await Ids("reachanged", revision.Assets.Keys.Select(id => id.Value));
            await Ids("rebchanged", revision.Bookmarks.Keys.Select(id => id.Value));
            await Ids("recchanged", revision.Comments.Keys.Select(id => id.Value));
            await C("recd", revision.DefaultCharacter is null ? 0 : 1);
            await C("repd", revision.DefaultParagraph is null ? 0 : 1);
            if (entry.BeforeBlocks is { } before) await Ids("rebeforeblocks", before.Select(id => id.Value), ordered: true);
            if (entry.AfterBlocks is { } after) await Ids("reafterblocks", after.Select(id => id.Value), ordered: true);
            await Context("reold", entry.Before);
            await Context("renew", entry.After);
            await S("}");
        }
        await S("}");

        async Task Value(string name, string value)
        { await S(@"{\*\" + name + " "); await T(value); await S("}"); }
        async Task Ids(string name, IEnumerable<long> values, bool ordered = false)
        {
            await S(@"{\*\" + name + " ");
            foreach (var id in ordered ? values : values.Order()) { await S(id.ToString(CultureInfo.InvariantCulture)); await S(" "); }
            await S("}");
        }
        async Task Context(string name, RtfRevisionContext context)
        {
            await S(@"{\*\" + name + " ");
            await _output.FlushAsync(_cancellation).ConfigureAwait(false);
            await new RtfProjectionWriter(context.Content, output, semantic: true, includeIdentities: true,
                contextScaffolding: context.Scaffolding).WriteAsync(_cancellation).ConfigureAwait(false);
            await S("}");
        }
    }
}
