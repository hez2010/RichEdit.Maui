namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly Dictionary<int, RichStyleId> _styleIdentities = [];
    private readonly HashSet<int> _usedStyleIdentities = [];
    private int _nextStyleIdentity = 1;

    private RichStyleId StyleIdentity(int handle)
    {
        if (_styleIdentities.TryGetValue(handle, out var identity)) return identity;
        if (handle is < short.MinValue or > ushort.MaxValue) throw Error("Invalid RTF style handle");
        // RTF handles may be zero or negative. Document identities are positive;
        // reserve a unique identity for definitions and forward references alike.
        var candidate = handle == 0 ? short.MaxValue : handle;
        if (candidate <= 0 || candidate > short.MaxValue || !_usedStyleIdentities.Add(candidate))
        {
            while (_nextStyleIdentity <= short.MaxValue && !_usedStyleIdentities.Add(_nextStyleIdentity)) _nextStyleIdentity++;
            if (_nextStyleIdentity > short.MaxValue) throw Error("RTF style capacity exceeded");
            candidate = _nextStyleIdentity++;
        }
        ChargeResource();
        identity = new(candidate);
        _styleIdentities.Add(handle, identity);
        return identity;
    }
}
