using Microsoft.UI.Text;
using Windows.Storage.Streams;
using Windows.Win32.UI.Controls.RichEdit;
using ITextSelection = Microsoft.UI.Text.ITextSelection;
using ITextRange = Microsoft.UI.Text.ITextRange;

namespace RichEdit.Maui.Platforms.Windows.NativeText;

internal sealed unsafe partial class WindowsTextDocument : WindowsTextObject<ITextDocument2>
{
    private static readonly Guid InterfaceId = new("C241F5E0-7206-11D8-A2C7-00A0D1D6C6B3");
    internal WindowsTextDocument(WindowsTextServices owner) : base(owner, owner.QueryInterface(in InterfaceId)) { }

    internal ITextSelection Selection
    {
        get { ITextSelection2* value; Pointer->GetSelection2(&value); return new WindowsTextRange(Owner, (nint)value); }
    }
    internal float DefaultTabStop { get { float value; Pointer->GetDefaultTabStop(&value); return value; } set => Pointer->SetDefaultTabStop(value); }
    internal ITextRange GetRange(int start, int end)
    {
        ITextRange2* value; Pointer->Range2(start, end, &value); return new WindowsTextRange(Owner, (nint)value);
    }
    internal ITextRange GetRangeFromPoint(global::Windows.Foundation.Point point, PointOptions options)
    {
        ITextRange2* value; Pointer->RangeFromPoint2((int)Math.Round(point.X), (int)Math.Round(point.Y), (int)options, &value);
        return new WindowsTextRange(Owner, (nint)value);
    }
    internal ITextCharacterFormat GetDefaultCharacterFormat()
    {
        ITextFont2* value; Pointer->GetDocumentFont(&value); return new WindowsTextFont(Owner, (nint)value);
    }
    internal ITextParagraphFormat GetDefaultParagraphFormat()
    {
        ITextPara2* value; Pointer->GetDocumentPara(&value); return new WindowsTextParagraph(Owner, (nint)value);
    }
    internal void SetDefaultCharacterFormat(ITextCharacterFormat value) => Pointer->SetDocumentFont(WindowsTextFont.RequireNative(value).Pointer);
    internal void SetDefaultParagraphFormat(ITextParagraphFormat value) => Pointer->SetDocumentPara(WindowsTextParagraph.RequireNative(value).Pointer);
    internal void GetText(TextGetOptions options, out string text)
    {
        using var range = (WindowsTextRange)GetRange(0, int.MaxValue);
        range.GetText(options, out text);
    }
    internal void SetText(TextSetOptions options, string text)
    {
        using var range = (WindowsTextRange)GetRange(0, int.MaxValue);
        range.SetText(options, text);
    }
    internal void LoadFromStream(TextSetOptions options, IRandomAccessStream stream)
    {
        using var range = (WindowsTextRange)GetRange(0, int.MaxValue);
        range.SetTextViaStream(options, stream);
    }
    internal void SaveToStream(TextGetOptions options, IRandomAccessStream stream)
    {
        using var range = (WindowsTextRange)GetRange(0, int.MaxValue);
        range.GetTextViaStream(options, stream);
    }
    internal int BatchDisplayUpdates() { int value; Pointer->Freeze(&value); return value; }
    internal int ApplyDisplayUpdates() { int value; Pointer->Unfreeze(&value); return value; }
    internal void BeginUndoGroup() => Pointer->BeginEditCollection();
    internal void EndUndoGroup() => Pointer->EndEditCollection();
    internal void ClearUndoRedoHistory() => Owner.Send(global::Windows.Win32.PInvoke.EM_EMPTYUNDOBUFFER);
}
