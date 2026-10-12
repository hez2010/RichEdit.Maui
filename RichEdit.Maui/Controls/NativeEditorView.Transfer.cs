using System.Text;
using RichEdit.Core;

namespace RichEdit.Maui;

public partial class NativeEditorView
{
    internal sealed record TransferContent(string Text, string? Rtf);

    internal Task<TransferContent?> CaptureTransferAsync()
    {
        VerifyAccess();
        if (!HasClipboardSelection || Composition.IsActive) return Task.FromResult<TransferContent?>(null);
        if (SourceSession is not null)
            return Task.FromResult<TransferContent?>(new(string.Join("\n", Selections.Select(selection =>
                Document.GetText(selection.Range) + new string(' ', selection.Range.IsEmpty
                    ? Math.Abs(selection.ActiveVirtualSpaces - selection.AnchorVirtualSpaces) : selection.EndVirtualSpaces))), null));
        if (StructuredSession is not null)
        {
            var fragment = CreateStructuredClipboard().Payload;
            return Task.Run<TransferContent?>(async () =>
            {
                using var stream = new MemoryStream();
                await RichEdit.Core.Rtf.RtfCodec.WriteAsync(fragment.Content, stream);
                return new(fragment.Content.ProjectText().Text.ToString(), Encoding.ASCII.GetString(stream.GetBuffer(), 0, checked((int)stream.Length)));
            });
        }
        var legacy = CreateSelectionClipboard(Document.CurrentSnapshot, Selections).Payload;
        return Task.Run<TransferContent?>(() => new(legacy.Text, legacy.RtfText));
    }

    // The caller supplies data from a drag operation, never through the system
    // clipboard. Freeze the destination identity before awaiting external data.
    internal async Task<bool> InsertTransferAsync(int position, Func<Task<TransferContent>> read)
    {
        VerifyAccess();
        new TextSpan(position, 0).Validate(Document.Length);
        if (IsReadOnly || Document.HasActiveComposition) return false;
        var document = Document;
        var revision = document.Revision;
        var selections = Selections;
        var handler = Handler;
        var attachment = (handler as RichEditorHandler)?.LayoutAttachment;
        var source = SourceSession;
        var structured = StructuredSession;
        var sourceRevision = source?.Document.Revision;
        var richRevision = structured?.Document.Revision;
        var generation = source?.SelectionGeneration ?? structured?.SelectionGeneration;
        bool Current() => !IsReadOnly && !Document.HasActiveComposition && ReferenceEquals(Document, document) &&
            Document.Revision == revision && ReferenceEquals(Selections, selections) &&
            ReferenceEquals(Handler, handler) && ReferenceEquals((Handler as RichEditorHandler)?.LayoutAttachment, attachment) &&
            ReferenceEquals(SourceSession, source) && ReferenceEquals(StructuredSession, structured);
        var content = await read();
        if (!Current()) return false;
        if (source is not null)
        {
            var args = new TextPastingEventArgs(content.Text);
            SourcePasting?.Invoke(this, args);
            ArgumentNullException.ThrowIfNull(args.Text);
            if (args.Cancel || !Current() || MaxLength >= 0 && (long)document.Length + args.Text.Length > MaxLength) return false;
            return source.Edit(edit =>
            {
                edit.Replace(new(position, 0), args.Text);
                edit.SetSelections(TextSelectionSet.Caret(position + args.Text.Length));
            }, new(Description: "Drop", Origin: TextChangeOrigin.User), sourceRevision, generation).Accepted;
        }
        if (structured is not null)
        {
            var imported = await RichTextClipboard.ReadStructuredTransferAsync(content.Rtf, content.Text);
            if (imported is null || !Current()) return false;
            var args = new RichPastingEventArgs(imported.Fragment, imported.IsPlainText, imported.Diagnostics);
            StructuredPasting?.Invoke(this, args);
            if (args.Cancel || !Current() || document.StructuredProjection is not { } projection) return false;
            var target = RichTextDocument.ToStructuredSelection(projection, new(position, position));
            try
            {
                return structured.Edit(edit =>
                {
                    var caret = args.UsePlainText
                        ? edit.ReplaceSelection(target, args.Fragment.Content.ProjectText().Text.ToString(), structured.TypingCharacterFormat)
                        : edit.ReplaceSelection(target, args.Fragment);
                    if (!AcceptsStructuredInputLength(edit.Snapshot)) throw new StructuredInputLimitException();
                    edit.SetSelection(caret);
                }, new("Drop", Origin: TextChangeOrigin.User), richRevision, generation).Accepted;
            }
            catch (StructuredInputLimitException) { return false; }
        }
        var fragment = RichTextDocumentFragment.FromPlainText(content.Text);
        if (content.Rtf is { } rtf)
            try { fragment = RichTextDocumentFragment.FromRtf(rtf); }
            catch (Exception error) when (error is FormatException or ArgumentException) { }
        var legacyArgs = new RichTextPastingEventArgs(fragment);
        Pasting?.Invoke(this, legacyArgs);
        if (legacyArgs.Cancel || !Current() || LimitFragmentToMaxLength(legacyArgs.Fragment, new(position, 0)) is not { } accepted) return false;
        Document.Edit(edit => edit.ReplaceFragment(new(position, 0), accepted));
        SelectedRange = new(position + accepted.Text.Length, 0);
        return true;
    }
}
