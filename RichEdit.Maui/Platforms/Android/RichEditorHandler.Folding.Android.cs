using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Views;
using RichEdit.Maui.Platforms.Android;
using View = Android.Views.View;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private FoldTransformation? _foldTransformation;

    private partial void ApplyFoldingCore()
    {
        if (PlatformView is null) return;
        var folding = VirtualView.Folding;
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            var anchor = PlatformView.SelectionStart;
            var active = PlatformView.SelectionEnd;
            if (folding.EffectiveRanges.IsEmpty)
            {
                if (_foldTransformation is not null && PlatformView.TransformationMethod?.Handle == _foldTransformation.Handle)
                    PlatformView.TransformationMethod = SoftLineBreakTransformation;
                _foldTransformation?.Disconnect();
                _foldTransformation?.Dispose();
                _foldTransformation = null;
            }
            else
            {
                _foldTransformation ??= new FoldTransformation(this);
                if (PlatformView.TransformationMethod?.Handle != _foldTransformation.Handle)
                    PlatformView.TransformationMethod = _foldTransformation;
                else if (PlatformView.EditableText is { } text && text.Length() > 0)
                {
                    _foldTransformation.Refresh();
                    // DynamicLayout observes UpdateLayout spans. Reflow without replacing
                    // the editable buffer or disturbing its composition/selection spans.
                    using var layoutChange = new FoldLayoutChange();
                    text.SetSpan(layoutChange, 0, text.Length(), SpanTypes.InclusiveInclusive);
                    text.RemoveSpan(layoutChange);
                }
            }
            if (anchor >= 0 && active >= 0 && (PlatformView.SelectionStart != anchor || PlatformView.SelectionEnd != active))
                PlatformView.SetSelection(anchor, active);
            WatchNativeFormats();
            PlatformView.RequestLayout();
            PlatformView.Invalidate();
        }
        finally { _applyingDocument = wasApplying; }
    }

    private void DisconnectFolding()
    {
        if (_foldTransformation is null) return;
        PlatformView.TransformationMethod = SoftLineBreakTransformation;
        _foldTransformation.Disconnect();
        _foldTransformation.Dispose();
        _foldTransformation = null;
    }

    private sealed class FoldLayoutChange : MetricAffectingSpan
    {
        public override void UpdateDrawState(TextPaint? paint) { }
        public override void UpdateMeasureState(TextPaint? paint) { }
    }

    private sealed class FoldTransformation(RichEditorHandler owner) : Java.Lang.Object, ITransformationMethod
    {
        private Java.Lang.ICharSequence? _source;
        private ISpannable? _spannable;
        private SpannableStringBuilder? _display;
        private DisplayWatcher? _watcher;
        private bool _refreshing;

        public Java.Lang.ICharSequence? GetTransformationFormatted(Java.Lang.ICharSequence? source, View? view)
        {
            Disconnect();
            if (source is null) return null;
            _source = source;
            _spannable = source as ISpannable;
            _display = new SpannableStringBuilder();
            Refresh();
            if (_spannable is not null)
            {
                _watcher = new DisplayWatcher(this);
                // Update the display before TextView and DynamicLayout observe a native
                // edit, so both buffers always have identical UTF-16 lengths.
                _spannable.SetSpan(_watcher, 0, source.Length(), SpanTypes.InclusiveInclusive | (SpanTypes)(255 << 16));
            }
            return _display;
        }

        public void OnFocusChanged(View? view, Java.Lang.ICharSequence? source, bool focused,
            FocusSearchDirection direction, global::Android.Graphics.Rect? previouslyFocusedRect) { }

        internal void Refresh()
        {
            if (_source is null || _display is null || _refreshing) return;
            _refreshing = true;
            try
            {
                var characters = new char[_source.Length()];
                TextUtils.GetChars(_source, 0, characters.Length, characters, 0);
                for (var index = 0; index < characters.Length; index++)
                    if (characters[index] == RichTextDocument.SoftLineBreakCharacter) characters[index] = '\n';
                foreach (var range in owner.DisplayFoldRanges)
                {
                    var end = Math.Min(range.End, characters.Length);
                    if (range.Start < end) Array.Fill(characters, '\uFEFF', range.Start, end - range.Start);
                }
                // The display is a native Spanned buffer. Copy only appearance spans;
                // source watchers, selection, and IME ownership stay on the editable.
                _display.ClearSpans();
                using var text = new Java.Lang.String(new string(characters));
                _display.Replace(0, _display.Length(), text);
                if (_spannable is not null)
                {
                    TextUtils.CopySpansFrom(_spannable, 0, characters.Length, Java.Lang.Class.FromType(typeof(CharacterStyle)), _display, 0);
                    foreach (var span in _spannable.GetSpans(0, characters.Length, Java.Lang.Class.FromType(typeof(IParagraphStyle))) ?? [])
                        SynchronizeParagraph(span, _spannable.GetSpanStart(span), _spannable.GetSpanEnd(span));
                    foreach (var range in owner.DisplayFoldRanges)
                    {
                        var end = Math.Min(range.End, characters.Length);
                        if (range.Start >= end) continue;
                        foreach (var span in _display.GetSpans(range.Start, end, Java.Lang.Class.FromType(typeof(ReplacementSpan))) ?? [])
                            if (_display.GetSpanStart(span) >= range.Start && _display.GetSpanEnd(span) <= end) _display.RemoveSpan(span);
                    }
                }
            }
            finally { _refreshing = false; }
        }

        internal void Disconnect()
        {
            if (_watcher is not null)
            {
                _spannable?.RemoveSpan(_watcher);
                // Android retains a watcher snapshot until the current edit ends.
                // Detach now; let the JNI bridge release it after those callbacks.
                _watcher = null;
            }
            _source = null;
            _spannable = null;
            _display = null;
        }

        private void SynchronizeSpan(Java.Lang.Object? span, int start, int end)
        {
            if (_refreshing || _display is null || _spannable is null || span is not (CharacterStyle or IParagraphStyle)) return;
            if (span is IParagraphStyle)
            {
                SynchronizeParagraph(span, start, end);
                return;
            }
            if (start < 0 || end > _display.Length() || span is ReplacementSpan &&
                owner.FindDisplayFoldRange(start) is { } range && end <= range.End) _display.RemoveSpan(span);
            else _display.SetSpan(span, start, end, _spannable.GetSpanFlags(span));
        }

        private void SynchronizeParagraph(Java.Lang.Object span, int start, int end)
        {
            if (_display is null || _spannable is null || span is RichParagraphMetadataSpan) return;
            _display.RemoveSpan(span);
            if (start < 0 || end > _display.Length()) return;
            // Collapsing a newline joins paragraphs. Use the first visible source
            // paragraph's style and anchor it to the display's paragraph boundaries.
            var displayStart = TextUtils.LastIndexOf(_display, '\n', start - 1) + 1;
            var firstVisible = displayStart;
            while (owner.FindDisplayFoldRange(firstVisible) is { } hidden) firstVisible = hidden.End;
            if (firstVisible < start || firstVisible >= end) return;
            var terminator = TextUtils.IndexOf(_display, '\n', Math.Max(start, end - 1));
            _display.SetSpan(span, displayStart, terminator < 0 ? _display.Length() : terminator + 1, _spannable.GetSpanFlags(span));
        }

        private sealed class DisplayWatcher(FoldTransformation owner) : Java.Lang.Object, ITextWatcher, ISpanWatcher, INoCopySpan
        {
            public void BeforeTextChanged(Java.Lang.ICharSequence? text, int start, int count, int after) { }
            public void OnTextChanged(Java.Lang.ICharSequence? text, int start, int before, int count) => owner.Refresh();
            public void AfterTextChanged(IEditable? text) { }
            public void OnSpanAdded(ISpannable? text, Java.Lang.Object? span, int start, int end) => owner.SynchronizeSpan(span, start, end);
            public void OnSpanChanged(ISpannable? text, Java.Lang.Object? span, int oldStart, int oldEnd, int start, int end) => owner.SynchronizeSpan(span, start, end);
            public void OnSpanRemoved(ISpannable? text, Java.Lang.Object? span, int start, int end) => owner._display?.RemoveSpan(span);
        }
    }
}
