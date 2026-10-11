using System.Globalization;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private List<(long Id, int Kind)>? _wireNodes;
    internal RtfIdentityMap Identities { get; } = new();

    private void ReadNodeIdentities(string text)
    {
        if (_wireNodes is not null) throw Error("Duplicate retained node identities");
        _wireNodes = [];
        var seen = new HashSet<long>();
        foreach (var piece in text.AsSpan().Split(' '))
        {
            var value = text.AsSpan()[piece];
            if (value.IsEmpty) continue;
            var separator = value.IndexOf('/');
            if (separator < 1) throw Error("Invalid retained node identity");
            var id = RtfIdentityMap.Number(value[..separator], zero: _retained);
            var kind = RtfIdentityMap.Number(value[(separator + 1)..]);
            if (kind > 11 || id != 0 && !seen.Add(id)) throw Error("Duplicate or invalid retained node identity");
            ChargeResource();
            _wireNodes.Add((id, (int)kind));
        }
    }

    private void CompleteIdentities(RichDocumentSnapshot snapshot)
    {
        if (_wireNodes is null) return;
        var nodes = RtfIdentityMap.Preorder(snapshot).ToArray();
        if (nodes.Length != _wireNodes.Count) throw Error("Retained identities do not match the RTF content");
        for (var index = 0; index < nodes.Length; index++)
        {
            if (RtfIdentityMap.Kind(nodes[index]) != _wireNodes[index].Kind) throw Error("Retained node kind does not match its RTF content");
            Identities.Nodes.Add(nodes[index].Id, _wireNodes[index].Id);
        }
        foreach (var pair in _assetNames)
        {
            var name = pair.Key.AsSpan();
            if (name.Length != 32 || name[..16].IndexOfAnyExcept('0') >= 0 ||
                !long.TryParse(name[16..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw Error("Invalid retained picture identity");
            Identities.Assets.Add(pair.Value, id);
        }
        if (Identities.Assets.Count != snapshot.Assets.Count || Identities.Bookmarks.Count != snapshot.Bookmarks.Count ||
            Identities.Comments.Count != snapshot.Comments.Count || Identities.Bookmarks.Values.Concat(Identities.Comments.Values).Distinct().Count() !=
            snapshot.Bookmarks.Count + snapshot.Comments.Count)
            throw Error("Retained resources require unique identities");
    }
}
