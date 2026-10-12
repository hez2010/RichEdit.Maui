using Android.OS;
using Android.Content;
using Android.Text;
using System.Globalization;
using Android.Views.Accessibility;
using Android.Views.InputMethods;
using TextSnapshot = Android.Views.InputMethods.TextSnapshot;
using System.Runtime.Versioning;
using AccessibilityAction = Android.Views.Accessibility.Action;
using NativeAutofillValue = Android.Views.Autofill.AutofillValue;

namespace RichEdit.Maui.Platforms.Android;

public partial class RichEditText
{
    private ObservedInputConnection? _sourceInputConnection;
    private bool _inputUpdateQueued;
    private TextEdit.Core.TextSnapshot? _accessibleSnapshot;
    private const int MaximumServiceTextLength = 8192;
    private const int MaximumAutofillTextLength = 65536;

    /// <inheritdoc />
    public override NativeAutofillValue? AutofillValue
    {
        get
        {
            // Autofill is a source-text boundary. TextView's default implementation
            // copies and sorts every native span, including private reservations.
            var snapshot = ProjectionHandler?.VirtualView.InputSnapshot;
            if (snapshot?.Length > MaximumAutofillTextLength) return null;
            var value = snapshot?.Text ?? Text ?? "";
            if (value.Length > MaximumAutofillTextLength) return null;
            using var text = new Java.Lang.String(value);
            return NativeAutofillValue.ForText(text);
        }
    }

    /// <inheritdoc />
    public override void Autofill(NativeAutofillValue? value)
    {
        if (!Enabled || value is not { IsText: true })
            return;
        if (ProjectionHandler is not { } handler)
        {
            base.Autofill(value);
            return;
        }

        var editor = handler.VirtualView;
        editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(new(0, editor.InputSnapshot.Length), value.TextValue ?? "")]);
    }

    private static AccessibilityEvent CreateSourceEvent(EventTypes type)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            return new AccessibilityEvent((int)type);
#pragma warning disable CA1422 // Event constructors were introduced after the minimum supported API.
        return AccessibilityEvent.Obtain(type)!;
#pragma warning restore CA1422
    }

    internal void PublishSourceState()
    {
        if (ProjectionHandler is not { } handler)
            return;

        var editor = handler.VirtualView;
        var snapshot = editor.InputSnapshot.TextBuffer;
        var previous = _accessibleSnapshot;
        _accessibleSnapshot = snapshot;
        var accessibilityEnabled = Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true };
        // Keep a shared tree root as the event baseline. Neither ordinary caret
        // movement nor an inactive accessibility service needs contiguous text.
        if (accessibilityEnabled && (previous ?? TextEdit.Core.TextSnapshot.Empty).FindDifference(snapshot, out _) is { } difference)
        {
            using var changed = CreateSourceEvent(EventTypes.ViewTextChanged);
            changed.BeforeText = previous?.ToString() ?? "";
            changed.FromIndex = difference.Before.Start;
            changed.RemovedCount = difference.Before.Length;
            changed.AddedCount = difference.After.Length;
            changed.Text?.Add(new Java.Lang.String(snapshot.ToString()));
            changed.SetSource(this);
            changed.ClassName = Class.Name;
            changed.PackageName = Context?.PackageName;
            base.SendAccessibilityEventUnchecked(changed);
        }
        if (_inputUpdateQueued)
            return;

        _inputUpdateQueued = true;
        Post(() =>
        {
            _inputUpdateQueued = false;
            if (ProjectionHandler is null || Context?.GetSystemService(Context.InputMethodService) is not InputMethodManager input)
                return;

            var composition = editor.Composition.Range;
            input.UpdateSelection(this, editor.InputSelection.Anchor, editor.InputSelection.Active, composition?.Start ?? -1, composition?.End ?? -1);
            _sourceInputConnection?.PublishExtractedText(input);
        });
    }

    private Java.Lang.ICharSequence SourceAccessibilityText()
    {
        if (ProjectionHandler is not { } handler)
            return TextFormatted ?? new Java.Lang.String("");

        var native = TextFormatted;
        if (native is not ISpanned styled || !handler.NativeProjection.IsStructured && native.Length() != handler.VirtualView.InputSnapshot.Length + handler.NativeProjection.Reservations.Sum(static item => item.Text.Length))
            return new Java.Lang.String(handler.VirtualView.InputSnapshot.Text);

        var source = new SpannableString(handler.VirtualView.InputSnapshot.Text);
        // Copy semantic styles with logical ranges. A copied editable also contains
        // native observers; deleting its reservations can replay text callbacks and
        // repeatedly rebuild its span index during a read-only accessibility query.
        foreach (var kind in new[] { Java.Lang.Class.FromType(typeof(global::Android.Text.Style.CharacterStyle)),
            Java.Lang.Class.FromType(typeof(global::Android.Text.Style.IParagraphStyle)) })
        {
            foreach (var span in styled.GetSpans(0, native.Length(), kind) ?? [])
            {
                var start = handler.NativeProjection.ToSource(styled.GetSpanStart(span));
                var end = handler.NativeProjection.ToSource(styled.GetSpanEnd(span));
                if (end > start)
                    source.SetSpan(span, start, end, styled.GetSpanFlags(span));
            }
        }

        if (handler.VirtualView.ScopedInputProjection is { } projection)
            foreach (var item in projection.Objects)
                if (projection.Snapshot.Get<RichEdit.Core.RichInline>(item.Object) is RichEdit.Core.RichField field)
                    source.SetSpan(new global::Android.Text.Style.TtsSpan.TextBuilder(field.Result).Build(), item.Position, item.Position + 1, SpanTypes.ExclusiveExclusive);

        return source;
    }

    /// <inheritdoc />
    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null || ProjectionHandler is not { } handler)
            return;

        var editor = handler.VirtualView;
        if (StructuredTableCell is { } cell)
        {
            var selected = editor.StructuredSession?.Selection is RichEdit.Core.RichCellSelection cells && cells.Table == cell.Table &&
                cell.Row >= cells.Row && cell.Row < cells.Row + cells.RowCount && cell.Column >= cells.Column && cell.Column < cells.Column + cells.ColumnCount;
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                using var builder = new AccessibilityNodeInfo.CollectionItemInfo.Builder();
                builder.SetRowIndex(cell.Row); builder.SetRowSpan(cell.RowSpan); builder.SetColumnIndex(cell.Column); builder.SetColumnSpan(cell.ColumnSpan);
                builder.SetHeading(false); builder.SetSelected(selected);
                info.SetCollectionItemInfo(builder.Build());
            }
            else info.SetCollectionItemInfo(AccessibilityNodeInfo.CollectionItemInfo.Obtain(cell.Row, cell.RowSpan, cell.Column, cell.ColumnSpan, false, selected));
        }
        info.TextFormatted = SourceAccessibilityText();
        info.SetTextSelection(editor.InputSelection.Anchor, editor.InputSelection.Active);
    }

    internal RichNativeTableCell? StructuredTableCell { get; set; }

    /// <inheritdoc />
    public override void SendAccessibilityEventUnchecked(AccessibilityEvent? e)
    {
        if (Context?.GetSystemService(Context.AccessibilityService) is not AccessibilityManager { IsEnabled: true }) return;
        if (e is not null && ProjectionHandler is { } handler)
        {
            if (e.EventType == EventTypes.ViewTextChanged)
                return;

            var editor = handler.VirtualView;
            e.Text?.Clear();
            e.Text?.Add(new Java.Lang.String(editor.InputSnapshot.Text));
            e.ItemCount = editor.InputSnapshot.Length;
            if (e.EventType == EventTypes.ViewTextSelectionChanged)
            {
                e.FromIndex = editor.InputSelection.Anchor;
                e.ToIndex = editor.InputSelection.Active;
            }
        }

        base.SendAccessibilityEventUnchecked(e);
    }

    /// <inheritdoc />
    public override bool PerformAccessibilityAction(AccessibilityAction action, Bundle? arguments)
    {
        if (ProjectionHandler is { } handler)
        {
            var editor = handler.VirtualView;
            if (action == AccessibilityAction.SetSelection)
            {
                var start = arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionStartInt, -1) ?? -1;
                var end = arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionEndInt, -1) ?? -1;
                if (start < 0 || end < 0 || start > editor.InputSnapshot.Length || end > editor.InputSnapshot.Length)
                    return false;

                editor.InputSelection = new(start, end);
                return true;
            }
            if (action == AccessibilityAction.SetText)
                return editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(new(0, editor.InputSnapshot.Length),
                    arguments?.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence) ?? "")]);
            if (action is AccessibilityAction.NextAtMovementGranularity or AccessibilityAction.PreviousAtMovementGranularity)
            {
                var forward = action == AccessibilityAction.NextAtMovementGranularity;
                var source = editor.InputSnapshot.Text;
                var unit = (MovementGranularity)(arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentMovementGranularityInt) ?? 1);
                var boundaries = unit == MovementGranularity.Character ? StringInfo.ParseCombiningCharacters(source) :
                    Enumerable.Range(0, source.Length).Where(index => index == 0 || (unit == MovementGranularity.Word ?
                        char.IsWhiteSpace(source[index - 1]) != char.IsWhiteSpace(source[index]) : source[index - 1] == '\n')).ToArray();
                var active = editor.InputSelection.Active;
                var next = forward ? boundaries.Append(source.Length).FirstOrDefault(position => position > active, active) : boundaries.LastOrDefault(position => position < active, active);
                if (next == active)
                    return false;

                var extend = arguments?.GetBoolean(AccessibilityNodeInfo.ActionArgumentExtendSelectionBoolean) == true;
                editor.InputSelection = new(extend ? editor.InputSelection.Anchor : next, next);
                using var traversed = CreateSourceEvent(EventTypes.ViewTextTraversedAtMovementGranularity);
                traversed.FromIndex = Math.Min(active, next);
                traversed.ToIndex = Math.Max(active, next);
                traversed.MovementGranularity = unit;
                traversed.SetAction((global::Android.AccessibilityServices.GlobalAction)(int)action);
                traversed.Text?.Add(SourceAccessibilityText());
                traversed.SetSource(this);
                if (Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true })
                    base.SendAccessibilityEventUnchecked(traversed);

                return true;
            }
        }

        return base.PerformAccessibilityAction(action, arguments);
    }

    private sealed partial class ObservedInputConnection
    {
        private RichEditorHandler? Projection => owner.ProjectionHandler is { NativeProjection.IsEmpty: false } handler ? handler : null;

        private int? _extractedToken;

        private int Display(int position) => owner.ProjectionHandler?.NativeProjection.ToDisplayCaret(position) ?? position;

        private int DisplayCursorPosition(int cursor, TextSpan? replacement = null)
        {
            if (Projection is not { } handler || owner.EditableText is not { } text)
                return cursor;

            var start = replacement?.Start ?? BaseInputConnection.GetComposingSpanStart(text);
            var end = replacement?.End ?? BaseInputConnection.GetComposingSpanEnd(text);
            if (start < 0 || end < 0)
            {
                start = Math.Max(0, Math.Min(owner.SelectionStart, owner.SelectionEnd));
                end = Math.Max(start, Math.Max(owner.SelectionStart, owner.SelectionEnd));
            }
            if (start > end)
                (start, end) = (end, start);

            // Android sets this caret before replacing the old range. Translate the
            // relative movement against that same pre-edit buffer, including clamps.
            var anchor = cursor > 0 ? end : start;
            var sourceAnchor = handler.NativeProjection.ToSource(anchor);
            var target = (int)Math.Clamp((long)sourceAnchor + cursor - (cursor > 0 ? 1 : 0), 0, handler.VirtualView.InputSnapshot.Length);
            var display = target == sourceAnchor ? anchor : handler.NativeProjection.ToDisplayCaret(target);
            return (int)Math.Clamp((long)display - anchor + (cursor > 0 ? 1 : 0), int.MinValue, int.MaxValue);
        }

        [SupportedOSPlatform("android34.0")]
        public override bool ReplaceText(int start, int end, Java.Lang.ICharSequence text, int newCursorPosition, TextAttribute? textAttribute) => Observe(false, () =>
        {
            if (Projection is not { } handler)
                return base.ReplaceText(start, end, text, newCursorPosition, textAttribute);
            if (start < 0 || end < 0)
                return false;

            start = Math.Min(start, handler.VirtualView.InputSnapshot.Length);
            end = Math.Min(end, handler.VirtualView.InputSnapshot.Length);
            var range = handler.NativeProjection.ToDisplay(new TextSpan(Math.Min(start, end), Math.Abs(end - start)));
            var cursor = DisplayCursorPosition(newCursorPosition, range);
            return base.ReplaceText(start <= end ? range.Start : range.End, start <= end ? range.End : range.Start, text, cursor, textAttribute);
        });

        public override bool SetSelection(int start, int end) => base.SetSelection(Display(start), Display(end));

        public override Java.Lang.ICharSequence? GetTextBeforeCursorFormatted(int n, GetTextFlags flags)
        {
            if (owner.ProjectionHandler is not { } handler)
                return base.GetTextBeforeCursorFormatted(n, flags);

            var end = handler.VirtualView.InputSelection.Range.Start;
            var start = Math.Max(0, end - Math.Clamp(n, 0, MaximumServiceTextLength));
            return new Java.Lang.String(handler.VirtualView.InputSnapshot.TextBuffer.GetText(new(start, end - start)));
        }

        public override Java.Lang.ICharSequence? GetTextAfterCursorFormatted(int n, GetTextFlags flags)
        {
            if (owner.ProjectionHandler is not { } handler)
                return base.GetTextAfterCursorFormatted(n, flags);

            var start = handler.VirtualView.InputSelection.Range.End;
            var text = handler.VirtualView.InputSnapshot.TextBuffer;
            var length = Math.Min(Math.Clamp(n, 0, MaximumServiceTextLength), text.Length - start);
            return new Java.Lang.String(text.GetText(new(start, length)));
        }

        public override Java.Lang.ICharSequence? GetSelectedTextFormatted(GetTextFlags flags) => owner.ProjectionHandler is { } handler ?
            handler.VirtualView.InputSelection.Range.Length > MaximumServiceTextLength ? null :
            new Java.Lang.String(handler.VirtualView.InputSnapshot.TextBuffer.GetText(handler.VirtualView.InputSelection.Range)) : base.GetSelectedTextFormatted(flags);

        public override ExtractedText? GetExtractedText(ExtractedTextRequest? request, GetTextFlags flags)
        {
            if (owner.ProjectionHandler is not { } handler)
                return base.GetExtractedText(request, flags);
            if (((int)flags & 1) != 0)
                _extractedToken = request?.Token;
            var editor = handler.VirtualView;
            var selected = editor.InputSelection.Range;
            if (selected.Length > MaximumServiceTextLength) return null;
            var available = MaximumServiceTextLength - selected.Length;
            var start = Math.Max(0, selected.Start - available / 2);
            var end = Math.Min(editor.InputSnapshot.Length, start + MaximumServiceTextLength);
            return new()
            {
                Text = new Java.Lang.String(editor.InputSnapshot.TextBuffer.GetText(new(start, end - start))),
                StartOffset = start,
                PartialStartOffset = -1,
                PartialEndOffset = -1,
                SelectionStart = editor.InputSelection.Anchor - start,
                SelectionEnd = editor.InputSelection.Active - start,
            };
        }

        internal void PublishExtractedText(InputMethodManager input)
        {
            if (_extractedToken is { } token)
                input.UpdateExtractedText(owner, token, GetExtractedText(null, GetTextFlags.None));
        }

        public override bool RequestCursorUpdates(int cursorUpdateMode) => Projection is null && base.RequestCursorUpdates(cursorUpdateMode);

        [SupportedOSPlatform("android34.0")]
        public override bool RequestCursorUpdates(int cursorUpdateMode, int cursorUpdateFilter) => Projection is null && base.RequestCursorUpdates(cursorUpdateMode, cursorUpdateFilter);

        [SupportedOSPlatform("android34.0")]
        public override TextSnapshot? TakeSnapshot()
        {
            if (owner.ProjectionHandler is not { } handler)
                return base.TakeSnapshot();

            var composition = handler.VirtualView.Composition.Range;
            return GetSurroundingText(int.MaxValue, int.MaxValue, 0) is { } surrounding
                ? new(surrounding, composition?.Start ?? -1, composition?.End ?? -1, 0) : null;
        }

        [SupportedOSPlatform("android31.0")]
        public override SurroundingText? GetSurroundingText(int beforeLength, int afterLength, int flags)
        {
            if (owner.ProjectionHandler is not { } handler)
                return base.GetSurroundingText(beforeLength, afterLength, flags);

            var editor = handler.VirtualView;
            var selection = editor.InputSelection.Range;
            if (selection.Length > MaximumServiceTextLength) return null;
            var budget = MaximumServiceTextLength - selection.Length;
            var before = Math.Min(Math.Max(0, beforeLength), budget / 2);
            var after = Math.Min(Math.Max(0, afterLength), budget - before);
            var start = Math.Max(0, selection.Start - before);
            var end = (int)Math.Min(editor.InputSnapshot.Length, (long)selection.End + after);
            return new(new Java.Lang.String(editor.InputSnapshot.TextBuffer.GetText(new(start, end - start))), editor.InputSelection.Anchor - start, editor.InputSelection.Active - start, start);
        }

        public override bool DeleteSurroundingText(int beforeLength, int afterLength) => DeleteSource(beforeLength, afterLength, false);

        public override bool DeleteSurroundingTextInCodePoints(int beforeLength, int afterLength) => DeleteSource(beforeLength, afterLength, true);

        private bool DeleteSource(int beforeLength, int afterLength, bool codePoints)
        {
            if (beforeLength == 1 && afterLength == 0 && owner.ProjectionHandler?.VirtualView.TryHandleStructuredListKey(EditorKey.Backspace) == true)
                return true;
            if (Projection is not { } handler)
                return codePoints ? base.DeleteSurroundingTextInCodePoints(beforeLength, afterLength) : base.DeleteSurroundingText(beforeLength, afterLength);
            if (beforeLength < 0 || afterLength < 0)
                return false;

            var editor = handler.VirtualView;
            var range = editor.SelectedRange;
            var start = range.Start;
            var end = range.End;
            var text = editor.InputSnapshot.Text;
            if (codePoints)
            {
                for (var i = 0; i < beforeLength && start > 0; i++)
                {
                    start--;
                    if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
                        start--;
                }

                for (var i = 0; i < afterLength && end < text.Length; i++)
                {
                    end++;
                    if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
                        end++;
                }
            }
            else
            {
                start = Math.Max(0, start - beforeLength);
                end = (int)Math.Min(text.Length, (long)end + afterLength);
            }

            var nativeStart = Math.Min(owner.SelectionStart, owner.SelectionEnd);
            var nativeEnd = Math.Max(owner.SelectionStart, owner.SelectionEnd);
            // A source boundary can be inside a CRLF pair represented by one
            // native LF. Include that character at either edge of the deletion.
            return base.DeleteSurroundingText(start == range.Start ? 0 : Math.Max(0, nativeStart - handler.NativeProjection.ToDisplay(start, false)),
                end == range.End ? 0 : Math.Max(0, handler.NativeProjection.ToDisplay(end) - nativeEnd));
        }
    }
}
