namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly Dictionary<int, RichStyleId> _styleIdentities = [];
    private readonly HashSet<int> _usedStyleIdentities = [];
    private readonly HashSet<int> _additiveStyles = [];
    private int _nextStyleIdentity = 1;
    private RichStyleSheet? _resolvedStyles;

    private RichStyleSheet StyleSheet()
    {
        if (_resolvedStyles is not null) return _resolvedStyles;
        if (!_semantic && !_styles.ContainsKey(0) && _styleIdentities.TryGetValue(0, out var normal) &&
            _styles.Values.Any(style => style.NextStyle == normal || style.Kind == RichStyleKind.Paragraph && style.BasedOn == normal))
        {
            ChargeResource();
            _styles.Add(0, new(normal, "Normal", RichStyleKind.Paragraph));
        }
        try { return _resolvedStyles = new(_styles.Values); }
        catch (ArgumentException error) { throw new FormatException("RTF contains an invalid style graph.", error); }
    }

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
