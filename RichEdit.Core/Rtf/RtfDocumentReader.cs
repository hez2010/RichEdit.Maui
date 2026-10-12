using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using TextEdit.Core;

namespace RichEdit.Core.Rtf;

internal sealed partial class RtfDocumentReader
{
    private readonly RtfTokenReader _reader;
    private readonly RtfReadOptions options;
    private readonly RtfReadBudget _budget;
    private readonly bool _retained;
    internal RtfDocumentReader(Stream input, RtfReadOptions options) : this(
        new(input, options.MaximumBytes, options.MaximumDepth, options.MaximumAssetBytes), options, new(), false) { }
    private RtfDocumentReader(RtfTokenReader reader, RtfReadOptions options, RtfReadBudget budget, bool retained)
    {
        _reader = reader; this.options = options; _budget = budget; _retained = retained;
        ChargeResource(); // Every parsed document has one body story.
    }
    private readonly List<RtfDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _reported = [];
    private readonly Stack<State> _states = [];
    private State _state = new();
    private readonly List<byte> _bytes = [];
    private Decoder? _decoder;
    private int _decoderCodePage;
    private readonly Dictionary<int, (string Name, int CodePage)> _fonts = [];
    private readonly List<RichColor?> _colors = [];
    private readonly Dictionary<int, RichStyleDefinition> _styles = [];
    private RichCharacterStyle _defaultCharacter = new();
    private int _defaultFont = -1;
    private RichParagraphStyle _defaultParagraph = new();
    private readonly List<Block> _body = [];
    private List<Block> _container = [];
    private Paragraph? _paragraph;
    private readonly Dictionary<RichAssetId, RichImageAsset> _assets = [];
    private readonly Dictionary<string, RichAssetId> _assetNames = [];
    private readonly Dictionary<string, Anchor> _bookmarkStarts = [];
    private readonly Dictionary<string, Anchor> _commentStarts = [];
    private readonly Dictionary<string, RichTextSelection> _commentRanges = [];
    private readonly List<RichBookmark> _bookmarks = [];
    private readonly List<RichComment> _comments = [];
    private long _nextId = 1;
    private bool _newTextRun;
    private int _fallback;
    private bool _rtf;
    private bool _semantic;
    private bool _closed;
    private bool _structuralGap;
    private long _offset;
    private CancellationToken _cancellation;

    static RtfDocumentReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    internal async Task<RtfReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        _cancellation = cancellationToken;
        _container = _body;
        while (true)
        {
            var token = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _offset = token.Offset;
            if (token.Kind == RtfTokenKind.End) break;
            if (_closed)
            {
                // Native clipboard and RichEdit exports may include trailing
                // transport whitespace and a NUL terminator after the RTF root.
                if (token.Kind == RtfTokenKind.Text && token.Bytes.Span.IndexOfAnyExcept("\0 \t\r\n"u8) < 0) continue;
                throw Error("Content follows the root RTF group");
            }
            if (token.Kind is RtfTokenKind.Text or RtfTokenKind.HexByte)
            {
                if (_states.Count == 0) throw Error("RTF must begin with a group");
                if (token.Kind == RtfTokenKind.HexByte) Byte(token.Symbol);
                else foreach (var value in token.Bytes.Span) Byte(value);
                continue;
            }
            FlushBytes();
            switch (token.Kind)
            {
                case RtfTokenKind.StartGroup:
                    _fallback = 0;
                    _states.Push(_state);
                    _state = _state.Child();
                    break;
                case RtfTokenKind.EndGroup:
                    _fallback = 0;
                    var child = _state;
                    _state = _states.Pop();
                    EndGroup(child);
                    if (_states.Count == 0)
                    {
                        // Without a paragraph mark, the native RTF reader uses
                        // the restored outer paragraph state for the final line.
                        if (!_semantic && _paragraph is not null) CaptureParagraphProperties();
                        _closed = true;
                    }
                    break;
                case RtfTokenKind.Control:
                    if (_states.Count == 0) throw Error("An RTF control is outside the root group");
                    if (token.Word is "reold" or "renew" && _state.Destination == "rerevision")
                    {
                        await ReadRevisionContextAsync(token.Word, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                    Control(token.Word!, token.Number);
                    break;
                case RtfTokenKind.Symbol:
                    if (token.Symbol == '*') _state.Ignorable = true;
                    else if (_fallback > 0) _fallback--;
                    else if (token.Symbol is (byte)'{' or (byte)'}' or (byte)'\\') Text(((char)token.Symbol).ToString());
                    else if (token.Symbol == '~') Text("\u00a0");
                    else if (token.Symbol == '_') Text("\u2011");
                    else if (token.Symbol == '-') Text("\u00ad");
                    else if (token.Symbol is (byte)'\r' or (byte)'\n') EndParagraph();
                    else Report("symbol", "An unsupported RTF control symbol was ignored.");
                    break;
                case RtfTokenKind.Binary:
                    if (_state.Destination == "pict") AddPictureBytes(token.Bytes.Span);
                    else if (_state.Destination != "skip") Report("binary", "An unsupported binary payload was omitted.");
                    break;
            }
            if (_closed && _retained) break;
        }
        FlushBytes();
        if (!_rtf || !_closed) throw Error("Input is not a complete RTF document");
        if (_structuralGap) throw Error("A structural RTF gap has no paragraph mark");
        CloseTables(0);
        if (!_semantic) while (_listStack.Count != 0 && _listStack.Peek().External) EndList();
        if (_listStack.Count != 0) throw Error("Unclosed semantic list");
        if (_body.Count == 0) EnsureParagraph();
        foreach (var unmatched in _bookmarkStarts) _bookmarks.Add(new(new(Allocate().Value), unmatched.Key, null));
        var nodes = ImmutableDictionary.CreateBuilder<RichNodeId, RichNode>();
        var body = new RichNodeId(1);
        nodes.Add(body, new RichStory(body, Materialize(_body, body, nodes)));
        RichDocumentSnapshot snapshot;
        try
        {
            snapshot = new(body, nodes.ToImmutable(), new(_styles.Values), assets: _assets.ToImmutableDictionary(),
                bookmarks: _bookmarks.ToImmutableDictionary(value => value.Id), comments: _comments.ToImmutableDictionary(value => value.Id),
                defaultCharacter: _semantic ? _defaultCharacter : ExternalCharacterDefaults(), defaultParagraph: _defaultParagraph);
        }
        catch (ArgumentException error) { throw new FormatException("RTF contains invalid document structure or resource references.", error); }
        CompleteIdentities(snapshot);
        snapshot = RestoreRevisions(snapshot);
        var diagnostics = _diagnostics.ToImmutableArray();
        if (options.RequireLossless && !diagnostics.IsEmpty) throw new RtfFidelityException(diagnostics);
        return new(snapshot, diagnostics);
    }

    private RichCharacterStyle ExternalCharacterDefaults() => _defaultCharacter.FontFamily.Kind == StyleValueKind.Inherit &&
        _fonts.TryGetValue(_defaultFont, out var font) ? _defaultCharacter with { FontFamily = font.Name } : _defaultCharacter;

    private void Byte(byte value)
    {
        if (_fallback > 0) { _fallback--; return; }
        if (_state.Destination == "skip") return;
        if (_state.Destination == "pict")
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') return;
            var digit = value switch { >= (byte)'0' and <= (byte)'9' => value - '0', >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => value - 'A' + 10, _ => -1 };
            if (digit < 0) throw Error("Invalid picture hexadecimal data");
            var picture = _state.Picture!;
            if (picture.Nibble is { } high) { AddPictureBytes([(byte)(high * 16 + digit)]); picture.Nibble = null; }
            else picture.Nibble = digit;
            return;
        }
        // A literal semicolon terminates table entries; an escaped Unicode
        // semicolon is part of the authored name.
        if (value == ';' && _state.Destination is "fonttbl" or "colortbl" or "stylesheet")
        {
            FlushBytes();
            FinishEntry();
            return;
        }
        _bytes.Add(value);
        if (_bytes.Count >= 4096) FlushBytes(final: false);
    }

    private void FlushBytes(bool final = true)
    {
        if (_bytes.Count == 0 && _decoder is null) return;
        var codePage = _state.CodePage;
        if (_state.Font >= 0 && _fonts.TryGetValue(_state.Font, out var font) && font.CodePage != 0) codePage = font.CodePage;
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        catch (ArgumentException) { Report("codepage", $"Code page {codePage} is unavailable; Windows-1252 was used."); encoding = Encoding.GetEncoding(1252); }
        if (_decoder is null || _decoderCodePage != codePage) { _decoder = encoding.GetDecoder(); _decoderCodePage = codePage; }
        var characters = new char[encoding.GetMaxCharCount(_bytes.Count + 4)];
        string text;
        try { text = new(characters, 0, _decoder.GetChars(_bytes.ToArray(), characters, final)); }
        catch (DecoderFallbackException)
        {
            Report("encoding", "Invalid encoded bytes were replaced during import.");
            text = Encoding.GetEncoding(encoding.CodePage).GetString(_bytes.ToArray());
            _decoder = null;
        }
        _bytes.Clear();
        if (final) _decoder = null;
        Text(text);
    }

    private void Text(string text)
    {
        if (_state.Destination == "skip" || text.Length == 0) return;
        if (_structuralGap) throw Error("A structural RTF gap must be followed by its paragraph mark");
        _budget.TextLength = checked(_budget.TextLength + text.Length);
        if (_budget.TextLength > options.MaximumTextLength) throw Error("RTF text limit exceeded");
        if (_state.Capture is { } capture) { capture.Append(text); return; }
        if (_state.Destination is not ("body" or "nesttableprops" or "fldrslt")) return;
        _state.AtStart = false;
        var pieces = text.Split(['\r', '\n', '\u2028']);
        if (pieces.Length > 1)
        {
            // Escaped control characters still obey the authored rich schema.
            var start = 0;
            for (var index = 0; index < text.Length; index++)
                if (text[index] is '\r' or '\n' or '\u2028')
                {
                    AddText(text[start..index]);
                    if (text[index] == '\u2028') SoftBreak(); else EndParagraph();
                    start = index + 1;
                }
            AddText(text[start..]);
        }
        else AddText(text);
    }

    private void AddText(string text)
    {
        if (text.Length == 0) return;
        var paragraph = EnsureParagraph();
        var format = _state.Character;
        var style = _state.CharacterStyle;
        if (!_newTextRun && paragraph.Inlines.LastOrDefault() is RichText run && run.Format == format && run.Style == style && run.Link == _state.Link)
            paragraph.Inlines[^1] = run with { Content = run.Content.Concat(TextSnapshot.FromText(text)) };
        else { var id = Allocate(); paragraph.Inlines.Add(new RichText(id, paragraph.Id, TextSnapshot.FromText(text)) { Format = format, Style = style, Link = _state.Link }); }
        _newTextRun = false;
        paragraph.Length = checked(paragraph.Length + text.Length);
    }

    private Paragraph EnsureParagraph()
    {
        if (_paragraph is not null) return _paragraph;
        if (!_state.InTable) CloseTables(0);
        else
        {
            var level = Math.Max(1, _state.TableLevel);
            CloseTables(level);
            BeginInheritedRow(level);
        }
        if (!_semantic) ApplyExternalList();
        var paragraph = new Paragraph(Allocate()) { Format = _state.Paragraph, Character = _state.ParagraphCharacter, Style = _state.ParagraphStyle };
        _container.Add(paragraph);
        return _paragraph = paragraph;
    }

    private void CaptureParagraphProperties()
    {
        ReportBorderFidelity(_state);
        var paragraph = EnsureParagraph();
        paragraph.Format = _state.Paragraph;
        paragraph.Style = _state.ParagraphStyle;
    }
    private void EndParagraph() { CaptureParagraphProperties(); _paragraph = null; }
    private void SoftBreak()
    {
        var paragraph = EnsureParagraph();
        paragraph.Inlines.Add(new RichSoftBreak(Allocate(), paragraph.Id) { Style = _state.CharacterStyle, Format = _state.Character, Link = _state.Link });
        paragraph.Length++;
    }
    private RichTextPoint Point() { var p = EnsureParagraph(); return new(new(1), p.Id, p.Length); }
    private RichNodeId Allocate()
    {
        ChargeResource();
        return new(++_nextId);
    }
    private void ChargeResource()
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_budget.Resources >= options.MaximumNodes) throw Error("RTF node/resource limit exceeded");
        _budget.Resources++;
    }
    private void Report(string feature, string message)
    {
        if (_reported.Contains(feature)) return;
        ChargeResource();
        _reported.Add(feature);
        _diagnostics.Add(new(feature, message, _offset));
    }
    private FormatException Error(string text) => new($"{text} at byte {_offset}.");

    private abstract class Block(RichNodeId id) { internal RichNodeId Id { get; } = id; }
    private sealed class Paragraph(RichNodeId id) : Block(id)
    {
        internal readonly List<RichInline> Inlines = [];
        internal RichParagraphStyle Format = new();
        internal RichCharacterStyle Character = new();
        internal RichStyleId? Style;
        internal int Length;
    }
    private sealed record Anchor(RichTextPoint? Point, bool Reverse, CaretAffinity AnchorAffinity, CaretAffinity ActiveAffinity, long? WireId = null);

    private sealed class State
    {
        internal string Destination = "body";
        internal bool OwnDestination;
        internal bool AtStart = true;
        internal bool Ignorable;
        internal int CodePage = 1252, UnicodeSkip = 1, Font = -1, FontCodePage;
        internal RichCharacterStyle Character = new(), ParagraphCharacter = new();
        internal RichParagraphStyle Paragraph = new();
        internal RichStyleId? CharacterStyle, ParagraphStyle;
        internal StringBuilder? Capture;
        internal int Red, Green, Blue;
        internal bool ColorSpecified;
        internal int StyleId = -1, BasedOn = -1, NextStyle = -1;
        internal bool CharacterDefinition;
        internal int Inherit, Initial, Null;
        internal int? PropertyCount;
        internal int LineSpacing;
        internal bool LineMultiple;
        internal RichTabAlignment TabAlignment;
        internal RichTabLeader TabLeader;
        internal RichBorderSides BorderSide;
        internal ImmutableDictionary<RichBorderSides, RichBorder> Borders = ImmutableDictionary<RichBorderSides, RichBorder>.Empty;
        internal bool InTable;
        internal int TableLevel = 1;
        internal int ListId, ListOverride, ListLevel;
        internal ListDefinition? List;
        internal ListLevelDefinition? ListLevelDefinition;
        internal Picture? Picture;
        internal bool ResourceOnly, Orphan;
        internal bool Reverse;
        internal long? WireAnchor;
        internal RevisionRecord? RevisionRecord;
        internal CaretAffinity AnchorAffinity = CaretAffinity.Downstream, ActiveAffinity = CaretAffinity.Downstream;
        internal Annotation? Annotation;
        internal Field? Field;
        internal RichHyperlink? Link;
        internal State Child()
        {
            var child = (State)MemberwiseClone();
            child.AtStart = true;
            child.OwnDestination = false;
            child.Ignorable = false;
            if (Destination is "fonttbl" or "stylesheet") child.Capture = new();
            return child;
        }
    }
}
