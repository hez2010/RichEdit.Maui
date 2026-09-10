using Android.OS;
using Android.Content;
using Android.Text;
using System.Globalization;
using Android.Views.Accessibility;
using Android.Views.InputMethods;
using System.Runtime.Versioning;
using AccessibilityAction = Android.Views.Accessibility.Action;
using NativeAutofillValue = Android.Views.Autofill.AutofillValue;

namespace RichEdit.Maui.Platforms.Android;

public partial class RichEditText
{
    private ObservedInputConnection? _sourceInputConnection;
    private bool _inputUpdateQueued;
    private string _accessibleText = "";

    /// <inheritdoc />
    public override NativeAutofillValue? AutofillValue
    {
        get
        {
            // Autofill is a source-text boundary. TextView's default implementation
            // copies and sorts every native span, including private reservations.
            using var text = new Java.Lang.String(ProjectionHandler?.VirtualView.Document.Text ?? Text ?? "");
            return NativeAutofillValue.ForText(text);
        }
    }

    /// <inheritdoc />
    public override void Autofill(NativeAutofillValue? value)
    {
        if (!Enabled || value is not { IsText: true }) return;
        if (ProjectionHandler is not { } handler) { base.Autofill(value); return; }
        var editor = handler.VirtualView;
        editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(new(0, editor.Document.Length), value.TextValue ?? "")]);
    }
    private static AccessibilityEvent CreateSourceEvent(EventTypes type)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) return new AccessibilityEvent((int)type);
#pragma warning disable CA1422 // Event constructors were introduced after the minimum supported API.
        return AccessibilityEvent.Obtain(type)!;
#pragma warning restore CA1422
    }
    internal void PublishSourceState()
    {
        if (ProjectionHandler is not { } handler) return;
        var editor = handler.VirtualView;
        var text = editor.Document.Text;
        if (_accessibleText != text)
        {
            var start = 0;
            while (start < _accessibleText.Length && start < text.Length && _accessibleText[start] == text[start]) start++;
            var oldEnd = _accessibleText.Length;
            var newEnd = text.Length;
            while (oldEnd > start && newEnd > start && _accessibleText[oldEnd - 1] == text[newEnd - 1]) { oldEnd--; newEnd--; }
            using var changed = CreateSourceEvent(EventTypes.ViewTextChanged);
            changed.BeforeText = _accessibleText;
            changed.FromIndex = start;
            changed.RemovedCount = oldEnd - start;
            changed.AddedCount = newEnd - start;
            changed.Text?.Add(new Java.Lang.String(text));
            changed.SetSource(this);
            changed.ClassName = Class.Name;
            changed.PackageName = Context?.PackageName;
            _accessibleText = text;
            if (Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true }) base.SendAccessibilityEventUnchecked(changed);
        }
        if (_inputUpdateQueued) return;
        _inputUpdateQueued = true;
        Post(() =>
        {
            _inputUpdateQueued = false;
            if (ProjectionHandler is null || Context?.GetSystemService(Context.InputMethodService) is not InputMethodManager input) return;
            var composition = editor.Composition.Range;
            input.UpdateSelection(this, editor.SelectionState.Anchor, editor.SelectionState.Active, composition?.Start ?? -1, composition?.End ?? -1);
            _sourceInputConnection?.PublishExtractedText(input);
        });
    }

    private Java.Lang.ICharSequence SourceAccessibilityText()
    {
        if (ProjectionHandler is not { } handler) return TextFormatted ?? new Java.Lang.String("");
        var native = TextFormatted;
        if (native is not ISpanned styled || native.Length() != handler.VirtualView.Document.Length + handler.NativeProjection.Reservations.Sum(static item => item.Text.Length))
            return new Java.Lang.String(handler.VirtualView.Document.Text);
        var source = new SpannableString(handler.VirtualView.Document.Text);
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
                if (end > start) source.SetSpan(span, start, end, styled.GetSpanFlags(span));
            }
        }
        return source;
    }
    /// <inheritdoc />
    public override void OnInitializeAccessibilityNodeInfo(AccessibilityNodeInfo? info)
    {
        base.OnInitializeAccessibilityNodeInfo(info);
        if (info is null || ProjectionHandler is not { } handler) return;
        var editor = handler.VirtualView;
        info.TextFormatted = SourceAccessibilityText();
        info.SetTextSelection(editor.SelectionState.Anchor, editor.SelectionState.Active);
    }

    /// <inheritdoc />
    public override void SendAccessibilityEventUnchecked(AccessibilityEvent? e)
    {
        if (e is not null && ProjectionHandler is { } handler)
        {
            if (e.EventType == EventTypes.ViewTextChanged) return;
            var editor = handler.VirtualView;
            e.Text?.Clear();
            e.Text?.Add(new Java.Lang.String(editor.Document.Text));
            e.ItemCount = editor.Document.Length;
            if (e.EventType == EventTypes.ViewTextSelectionChanged)
            {
                e.FromIndex = editor.SelectionState.Anchor;
                e.ToIndex = editor.SelectionState.Active;
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
                if (start < 0 || end < 0 || start > editor.Document.Length || end > editor.Document.Length) return false;
                editor.SelectionState = new(start, end);
                return true;
            }
            if (action == AccessibilityAction.SetText)
                return editor.TryApplyEdits(editor.Document.Revision, (RichTextEdit[])[new(new(0, editor.Document.Length),
                    arguments?.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence) ?? "")]);
            if (action is AccessibilityAction.NextAtMovementGranularity or AccessibilityAction.PreviousAtMovementGranularity)
            {
                var forward = action == AccessibilityAction.NextAtMovementGranularity;
                var source = editor.Document.Text;
                var unit = (MovementGranularity)(arguments?.GetInt(AccessibilityNodeInfo.ActionArgumentMovementGranularityInt) ?? 1);
                var boundaries = unit == MovementGranularity.Character ? StringInfo.ParseCombiningCharacters(source) :
                    Enumerable.Range(0, source.Length).Where(index => index == 0 || (unit == MovementGranularity.Word ?
                        char.IsWhiteSpace(source[index - 1]) != char.IsWhiteSpace(source[index]) : source[index - 1] == '\n')).ToArray();
                var active = editor.SelectionState.Active;
                var next = forward ? boundaries.Append(source.Length).FirstOrDefault(position => position > active, active) : boundaries.LastOrDefault(position => position < active, active);
                if (next == active) return false;
                var extend = arguments?.GetBoolean(AccessibilityNodeInfo.ActionArgumentExtendSelectionBoolean) == true;
                editor.SelectionState = new(extend ? editor.SelectionState.Anchor : next, next);
                using var traversed = CreateSourceEvent(EventTypes.ViewTextTraversedAtMovementGranularity);
                traversed.FromIndex = Math.Min(active, next);
                traversed.ToIndex = Math.Max(active, next);
                traversed.MovementGranularity = unit;
                traversed.SetAction((global::Android.AccessibilityServices.GlobalAction)(int)action);
                traversed.Text?.Add(SourceAccessibilityText());
                traversed.SetSource(this);
                if (Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true }) base.SendAccessibilityEventUnchecked(traversed);
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
        public override bool SetSelection(int start, int end) => base.SetSelection(Display(start), Display(end));
        public override Java.Lang.ICharSequence? GetTextBeforeCursorFormatted(int n, GetTextFlags flags)
        {
            if (Projection is not { } handler) return base.GetTextBeforeCursorFormatted(n, flags);
            var end = handler.VirtualView.SelectedRange.Start;
            return new Java.Lang.String(handler.VirtualView.Document.Text[Math.Max(0, end - Math.Max(0, n))..end]);
        }
        public override Java.Lang.ICharSequence? GetTextAfterCursorFormatted(int n, GetTextFlags flags)
        {
            if (Projection is not { } handler) return base.GetTextAfterCursorFormatted(n, flags);
            var start = handler.VirtualView.SelectedRange.End;
            var text = handler.VirtualView.Document.Text;
            return new Java.Lang.String(text[start..Math.Min(text.Length, start + Math.Max(0, n))]);
        }
        public override Java.Lang.ICharSequence? GetSelectedTextFormatted(GetTextFlags flags) => Projection is { } handler ?
            new Java.Lang.String(handler.VirtualView.Selection.Text) : base.GetSelectedTextFormatted(flags);
        public override ExtractedText? GetExtractedText(ExtractedTextRequest? request, GetTextFlags flags)
        {
            if (owner.ProjectionHandler is not { } handler) return base.GetExtractedText(request, flags);
            if (((int)flags & 1) != 0) _extractedToken = request?.Token;
            return new()
            {
                Text = new Java.Lang.String(handler.VirtualView.Document.Text),
                StartOffset = 0, PartialStartOffset = -1, PartialEndOffset = -1,
                SelectionStart = handler.VirtualView.SelectionState.Anchor, SelectionEnd = handler.VirtualView.SelectionState.Active,
            };
        }
        internal void PublishExtractedText(InputMethodManager input)
        {
            if (_extractedToken is { } token) input.UpdateExtractedText(owner, token, GetExtractedText(null, GetTextFlags.None));
        }
        public override bool RequestCursorUpdates(int cursorUpdateMode) => Projection is null && base.RequestCursorUpdates(cursorUpdateMode);
        [SupportedOSPlatform("android34.0")]
        public override bool RequestCursorUpdates(int cursorUpdateMode, int cursorUpdateFilter) => Projection is null && base.RequestCursorUpdates(cursorUpdateMode, cursorUpdateFilter);
        [SupportedOSPlatform("android34.0")]
        public override TextSnapshot? TakeSnapshot()
        {
            if (Projection is not { } handler) return base.TakeSnapshot();
            var composition = handler.VirtualView.Composition.Range;
            return new(GetSurroundingText(int.MaxValue, int.MaxValue, 0)!, composition?.Start ?? -1, composition?.End ?? -1, 0);
        }
        [SupportedOSPlatform("android31.0")]
        public override SurroundingText? GetSurroundingText(int beforeLength, int afterLength, int flags)
        {
            if (Projection is not { } handler) return base.GetSurroundingText(beforeLength, afterLength, flags);
            var editor = handler.VirtualView;
            var selection = editor.SelectedRange;
            var start = Math.Max(0, selection.Start - Math.Max(0, beforeLength));
            var end = (int)Math.Min(editor.Document.Length, (long)selection.End + Math.Max(0, afterLength));
            return new(new Java.Lang.String(editor.Document.Text[start..end]), editor.SelectionState.Anchor - start, editor.SelectionState.Active - start, start);
        }
        public override bool DeleteSurroundingText(int beforeLength, int afterLength) => DeleteSource(beforeLength, afterLength, false);
        public override bool DeleteSurroundingTextInCodePoints(int beforeLength, int afterLength) => DeleteSource(beforeLength, afterLength, true);
        private bool DeleteSource(int beforeLength, int afterLength, bool codePoints)
        {
            if (Projection is not { } handler) return codePoints ? base.DeleteSurroundingTextInCodePoints(beforeLength, afterLength) : base.DeleteSurroundingText(beforeLength, afterLength);
            if (beforeLength < 0 || afterLength < 0) return false;
            var editor = handler.VirtualView;
            var range = editor.SelectedRange;
            var start = range.Start;
            var end = range.End;
            var text = editor.Document.Text;
            if (codePoints)
            {
                for (var i = 0; i < beforeLength && start > 0; i++)
                {
                    start--;
                    if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
                }
                for (var i = 0; i < afterLength && end < text.Length; i++)
                {
                    end++;
                    if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end++;
                }
            }
            else { start = Math.Max(0, start - beforeLength); end = (int)Math.Min(text.Length, (long)end + afterLength); }
            var nativeStart = Math.Min(owner.SelectionStart, owner.SelectionEnd);
            var nativeEnd = Math.Max(owner.SelectionStart, owner.SelectionEnd);
            return base.DeleteSurroundingText(start == range.Start ? 0 : Math.Max(0, nativeStart - handler.NativeProjection.ToDisplay(start)),
                end == range.End ? 0 : Math.Max(0, handler.NativeProjection.ToDisplay(end, false) - nativeEnd));
        }
    }
}
