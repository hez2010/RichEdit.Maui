using System.Text;
using Microsoft.UI.Text;
using Windows.Storage.Streams;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.RichEdit;
using NativeRange = Windows.Win32.UI.Controls.RichEdit.ITextRange;
using TextRange = Microsoft.UI.Text.ITextRange;
using ITextSelection = Microsoft.UI.Text.ITextSelection;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal sealed unsafe partial class WindowsTextRange(WindowsTextServices owner, nint pointer)
    : WindowsTextObject<ITextRange2>(owner, pointer), ITextSelection
{
    public int StartPosition { get { int value; Pointer->GetStart(&value); return value; } set => Pointer->SetStart(value); }
    public int EndPosition { get { int value; Pointer->GetEnd(&value); return value; } set => Pointer->SetEnd(value); }
    public int Length => EndPosition - StartPosition;
    public int StoryLength { get { int value; Pointer->GetStoryLength(&value); return value; } }
    public char Character { get { int value; Pointer->GetChar(&value); return (char)value; } set => Pointer->SetChar(value); }
    public RangeGravity Gravity { get { int value; Pointer->GetGravity(&value); return (RangeGravity)value; } set => Pointer->SetGravity((int)value); }
    public SelectionOptions Options { get { int value; Pointer->GetFlags(&value); return (SelectionOptions)value; } set => Pointer->SetFlags((int)value); }
    public SelectionType Type { get { int value; Pointer->GetType(&value); return (SelectionType)value; } }
    public string Text { get { GetText(TextGetOptions.None, out var text); return text; } set => SetText(TextSetOptions.None, value); }
    public string Link
    {
        get { BSTR value; Pointer->GetURL(&value); return WindowsTextValues.TakeString(value); }
        set { using var text = new WindowsTextValues.Text(value); Pointer->SetURL(text.Value); }
    }
    public ITextCharacterFormat CharacterFormat
    {
        get { ITextFont2* value; Pointer->GetFont2(&value); return new WindowsTextFont(Owner, (nint)value); }
        set => Pointer->SetFont((ITextFont*)WindowsTextFont.RequireNative(value).Pointer);
    }
    public ITextParagraphFormat ParagraphFormat
    {
        get { ITextPara2* value; Pointer->GetPara2(&value); return new WindowsTextParagraph(Owner, (nint)value); }
        set => Pointer->SetPara((ITextPara*)WindowsTextParagraph.RequireNative(value).Pointer);
    }
    public TextRange FormattedText
    {
        get { ITextRange2* value; Pointer->GetFormattedText2(&value); return new WindowsTextRange(Owner, (nint)value); }
        set => Pointer->SetFormattedText((NativeRange*)RequireNative(value).Pointer);
    }
    public TextRange GetClone()
    {
        ITextRange2* value; Pointer->GetDuplicate2(&value); return new WindowsTextRange(Owner, (nint)value);
    }
    public void GetCharacterUtf32(out uint value, int offset)
    {
        int character; Pointer->GetChar2(&character, offset); value = (uint)character;
    }
    public void SetRange(int start, int end) { using var operation = Owner.BeginOperation(); Pointer->SetRange(start, end); }
    public void Collapse(bool value) => Pointer->Collapse(value ? (int)tomConstants.tomStart : (int)tomConstants.tomEnd);
    public void SetIndex(TextRangeUnit unit, int index, bool extend) => Pointer->SetIndex(Unit(unit), index, extend ? 1 : 0);
    public int GetIndex(TextRangeUnit unit) { int value; Pointer->GetIndex(Unit(unit), &value); return value; }
    public int Expand(TextRangeUnit unit) { int value; Pointer->Expand(Unit(unit), &value); return value; }
    public int Move(TextRangeUnit unit, int count) { int value; Pointer->Move(Unit(unit), count, &value); return value; }
    public int MoveStart(TextRangeUnit unit, int count) { int value; Pointer->MoveStart(Unit(unit), count, &value); return value; }
    public int MoveEnd(TextRangeUnit unit, int count) { int value; Pointer->MoveEnd(Unit(unit), count, &value); return value; }
    public int StartOf(TextRangeUnit unit, bool extend) { int value; Pointer->StartOf(Unit(unit), extend ? 1 : 0, &value); return value; }
    public int EndOf(TextRangeUnit unit, bool extend) { int value; Pointer->EndOf(Unit(unit), extend ? 1 : 0, &value); return value; }
    public int HomeKey(TextRangeUnit unit, bool extend) { int value; Pointer->HomeKey((tomConstants)Unit(unit), extend ? 1 : 0, &value); return value; }
    public int EndKey(TextRangeUnit unit, bool extend) { int value; Pointer->EndKey(Unit(unit), extend ? 1 : 0, &value); return value; }
    public int MoveLeft(TextRangeUnit unit, int count, bool extend) { int value; Pointer->MoveLeft(Unit(unit), count, extend ? 1 : 0, &value); return value; }
    public int MoveRight(TextRangeUnit unit, int count, bool extend) { int value; Pointer->MoveRight(Unit(unit), count, extend ? 1 : 0, &value); return value; }
    public int MoveUp(TextRangeUnit unit, int count, bool extend) { int value; Pointer->MoveUp(Unit(unit), count, extend ? 1 : 0, &value); return value; }
    public int MoveDown(TextRangeUnit unit, int count, bool extend) { int value; Pointer->MoveDown(Unit(unit), count, extend ? 1 : 0, &value); return value; }
    public int Delete(TextRangeUnit unit, int count) { int value; Pointer->Delete(Unit(unit), count, &value); return value; }
    public void ChangeCase(LetterCase value) => Pointer->ChangeCase((tomConstants)value);
    public void MatchSelection()
    {
        using var document = new WindowsTextDocument(Owner);
        using var selection = (WindowsTextRange)document.Selection;
        SetRange(selection.StartPosition, selection.EndPosition);
    }
    public void ScrollIntoView(PointOptions options) => Pointer->ScrollIntoView((int)options);
    public void TypeText(string value) { using var operation = Owner.BeginOperation(); using var text = new WindowsTextValues.Text(value); Pointer->TypeText(text.Value); }
    public int FindText(string value, int length, FindOptions options)
    {
        using var text = new WindowsTextValues.Text(value);
        int result; Pointer->FindText(text.Value, length, (tomConstants)options, &result); return result;
    }
    public bool IsEqual(TextRange range)
    {
        if (range is not WindowsTextRange native || !ReferenceEquals(Owner, native.Owner)) return false;
        int value; Pointer->IsEqual((NativeRange*)native.Pointer, &value); return value != 0;
    }
    public bool InRange(TextRange range)
    {
        if (range is not WindowsTextRange native || !ReferenceEquals(Owner, native.Owner)) return false;
        int value; Pointer->InRange((NativeRange*)native.Pointer, &value); return value != 0;
    }
    public bool InStory(TextRange range)
    {
        if (range is not WindowsTextRange native || !ReferenceEquals(Owner, native.Owner)) return false;
        int value; Pointer->InStory((NativeRange*)native.Pointer, &value); return value != 0;
    }
    public void GetRect(PointOptions options, out global::Windows.Foundation.Rect rect, out int hit)
    {
        int left, top, right, bottom, h;
        Pointer->GetRect((int)options, &left, &top, &right, &bottom, &h);
        rect = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)); hit = h;
    }
    public void GetPoint(HorizontalCharacterAlignment horizontal, VerticalCharacterAlignment vertical, PointOptions options, out global::Windows.Foundation.Point point)
    {
        var xAlign = horizontal switch { HorizontalCharacterAlignment.Left => 0, HorizontalCharacterAlignment.Right => 2, HorizontalCharacterAlignment.Center => 6, _ => throw new ArgumentOutOfRangeException(nameof(horizontal)) };
        var yAlign = vertical switch { VerticalCharacterAlignment.Top => 0, VerticalCharacterAlignment.Baseline => 24, VerticalCharacterAlignment.Bottom => 8, _ => throw new ArgumentOutOfRangeException(nameof(vertical)) };
        int x, y; Pointer->GetPoint((tomConstants)((int)options | xAlign | yAlign), &x, &y); point = new(x, y);
    }
    public void SetPoint(global::Windows.Foundation.Point point, PointOptions options, bool extend) =>
        Pointer->SetPoint((int)Math.Round(point.X), (int)Math.Round(point.Y), (tomConstants)options, extend ? 1 : 0);

    public void GetText(TextGetOptions options, out string value)
    {
        BSTR text; Pointer->GetText2((int)options, &text);
        value = options.HasFlag(TextGetOptions.FormatRtf) ? Encoding.Latin1.GetString(WindowsTextValues.TakeBytes(text)) : WindowsTextValues.TakeString(text);
    }
    public void SetText(TextSetOptions options, string value)
    {
        using var operation = Owner.BeginOperation();
        if (options.HasFlag(TextSetOptions.FormatRtf))
        {
            if (value.Any(character => character > 255)) throw new ArgumentException("RTF must encode Unicode with its Unicode escape controls.", nameof(value));
            SetBytes(options, Encoding.Latin1.GetBytes(value));
        }
        else { using var text = new WindowsTextValues.Text(value); Pointer->SetText2((int)options, text.Value); }
    }
    private void SetBytes(TextSetOptions options, byte[] value)
    {
        using var operation = Owner.BeginOperation();
        using var bytes = new WindowsTextValues.Bytes(value); Pointer->SetText2((int)options, bytes.Value);
    }
    public void GetTextViaStream(TextGetOptions options, IRandomAccessStream stream)
    {
        BSTR text; Pointer->GetText2((int)options, &text);
        var bytes = options.HasFlag(TextGetOptions.FormatRtf) ? WindowsTextValues.TakeBytes(text) : Encoding.Unicode.GetBytes(WindowsTextValues.TakeString(text));
        using var writer = new DataWriter(stream.GetOutputStreamAt(stream.Position));
        writer.WriteBytes(bytes);
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
        writer.DetachStream();
    }
    public void SetTextViaStream(TextSetOptions options, IRandomAccessStream stream)
    {
        var bytes = ReadBytes(stream);
        if (options.HasFlag(TextSetOptions.FormatRtf)) SetBytes(options, bytes);
        else SetText(options, Encoding.Unicode.GetString(bytes));
    }
    private static byte[] ReadBytes(IRandomAccessStream stream)
    {
        var length = checked((int)(stream.Size - stream.Position));
        var bytes = new byte[length];
        using var reader = new DataReader(stream.GetInputStreamAt(stream.Position));
        var loaded = reader.LoadAsync((uint)length).AsTask().GetAwaiter().GetResult();
        if (loaded != length) throw new EndOfStreamException();
        reader.ReadBytes(bytes);
        return bytes;
    }

    public bool CanPaste(int format) { int value; Pointer->CanPaste(null, format, &value); return value != 0; }
    public void Copy() => Pointer->Copy(null);
    public void Cut() => Pointer->Cut(null);
    public void Paste(int format) => Pointer->Paste(null, format);

    public void InsertImage(int width, int height, int ascent, VerticalCharacterAlignment alignment, string alternativeText, IRandomAccessStream stream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(ascent);
        var type = alignment switch { VerticalCharacterAlignment.Top => 0, VerticalCharacterAlignment.Baseline => 24, VerticalCharacterAlignment.Bottom => 8, _ => throw new ArgumentOutOfRangeException(nameof(alignment)) };
        using var document = new WindowsTextDocument(Owner);
        using var selection = (WindowsTextRange)document.Selection;
        using var saved = (WindowsTextRange)selection.GetClone();
        var options = selection.Options;
        nint streamPointer = 0;
        global::Windows.Win32.System.Com.IStream* nativeStream = null;
        var frozen = false;
        var changedSelection = false;
        try
        {
            document.BatchDisplayUpdates();
            frozen = true;
            streamPointer = WinRT.MarshalInterface<IRandomAccessStream>.FromManaged(stream);
            var streamId = new Guid("0000000c-0000-0000-C000-000000000046");
            void* result;
            global::Windows.Win32.PInvoke.CreateStreamOverRandomAccessStream((global::Windows.Win32.System.Com.IUnknown*)streamPointer, &streamId, &result).ThrowOnFailure();
            nativeStream = (global::Windows.Win32.System.Com.IStream*)result;
            changedSelection = true;
            selection.SetRange(StartPosition, EndPosition);
            fixed (char* description = alternativeText)
            {
                var image = new NativeImageParameters
                {
                    xWidth = checked((int)Math.Round(width * 2540d / 96)),
                    yHeight = checked((int)Math.Round(height * 2540d / 96)),
                    Ascent = checked((int)Math.Round(ascent * 2540d / 96)),
                    Type = type, AlternateText = description, Stream = nativeStream,
                };
                var status = Owner.Send(global::Windows.Win32.PInvoke.EM_INSERTIMAGE, 0, (nint)(&image));
                System.Runtime.InteropServices.Marshal.ThrowExceptionForHR((int)status);
            }
        }
        finally
        {
            try
            {
                if (changedSelection)
                {
                    selection.SetRange(saved.StartPosition, saved.EndPosition);
                    selection.Options = options;
                }
            }
            finally
            {
                try { if (frozen) document.ApplyDisplayUpdates(); }
                finally
                {
                    if (nativeStream is not null) nativeStream->Release();
                    if (streamPointer != 0) System.Runtime.InteropServices.Marshal.Release(streamPointer);
                }
            }
        }
    }

    // RICHEDIT_IMAGE_PARAMETERS from richedit.h. Pointer-sized members keep
    // this small public ABI layout valid for both 32- and 64-bit consumers.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeImageParameters
    {
        internal int xWidth, yHeight, Ascent, Type;
        internal char* AlternateText;
        internal global::Windows.Win32.System.Com.IStream* Stream;
    }

    private static WindowsTextRange RequireNative(TextRange value) => value as WindowsTextRange
        ?? throw new ArgumentException("The range must come from the same Windows text backend.", nameof(value));

    private static int Unit(TextRangeUnit value) => (int)(value switch
    {
        TextRangeUnit.Character => tomConstants.tomCharacter, TextRangeUnit.Word => tomConstants.tomWord,
        TextRangeUnit.Sentence => tomConstants.tomSentence, TextRangeUnit.Paragraph => tomConstants.tomParagraph,
        TextRangeUnit.Line => tomConstants.tomLine, TextRangeUnit.Story => tomConstants.tomStory,
        TextRangeUnit.Screen => tomConstants.tomScreen, TextRangeUnit.Section => tomConstants.tomSection,
        TextRangeUnit.Window => tomConstants.tomWindow, TextRangeUnit.CharacterFormat => tomConstants.tomCharFormat,
        TextRangeUnit.ParagraphFormat => tomConstants.tomParaFormat, TextRangeUnit.Object => tomConstants.tomObject,
        TextRangeUnit.HardParagraph => tomConstants.tomHardParagraph, TextRangeUnit.Cluster => tomConstants.tomCluster,
        TextRangeUnit.Bold => tomConstants.tomBold, TextRangeUnit.Italic => tomConstants.tomItalic,
        TextRangeUnit.Underline => tomConstants.tomUnderline, TextRangeUnit.Strikethrough => tomConstants.tomStrikeout,
        TextRangeUnit.ProtectedText => tomConstants.tomProtected, TextRangeUnit.Link => tomConstants.tomLink,
        TextRangeUnit.SmallCaps => tomConstants.tomSmallCaps, TextRangeUnit.AllCaps => tomConstants.tomAllCaps,
        TextRangeUnit.Hidden => tomConstants.tomHidden, TextRangeUnit.Outline => tomConstants.tomOutline,
        TextRangeUnit.Shadow => tomConstants.tomShadow, TextRangeUnit.Imprint => tomConstants.tomImprint,
        TextRangeUnit.Disabled => tomConstants.tomDisabled, TextRangeUnit.Revised => tomConstants.tomRevised,
        TextRangeUnit.Subscript => tomConstants.tomSubscriptCF, TextRangeUnit.Superscript => tomConstants.tomSuperscriptCF,
        TextRangeUnit.FontBound => tomConstants.tomFontBound, TextRangeUnit.LinkProtected => tomConstants.tomLinkProtected,
        TextRangeUnit.ContentLink => tomConstants.tomLink,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    });
}
