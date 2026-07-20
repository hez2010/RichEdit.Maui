using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Platform;
using RichEdit.Maui.Platforms.Android;
using TextAlignment = Android.Text.Layout.Alignment;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private bool _applyingDocument;
    private ColorStateList? _defaultHintTextColors;
    private ColorStateList? _defaultTextColors;
    private float _defaultTextSize;
    private Typeface? _defaultTypeface;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private IKeyListener? _editableKeyListener;

    /// <inheritdoc />
    protected override RichEditText CreatePlatformView()
    {
        var editor = new RichEditText(MauiContext!.Context!)
        {
            Gravity = GravityFlags.Top | GravityFlags.Start,
            InputType = InputTypes.ClassText |
                        InputTypes.TextFlagMultiLine |
                        InputTypes.TextFlagCapSentences |
                        InputTypes.TextFlagAutoCorrect,
            OverScrollMode = OverScrollMode.Always,
            VerticalScrollBarEnabled = true,
        };

        editor.SetSingleLine(false);
        editor.SetHorizontallyScrolling(false);
        _defaultHintTextColors = editor.HintTextColors;
        _defaultTextColors = editor.TextColors;
        _defaultTextSize = editor.TextSize;
        _defaultTypeface = editor.Typeface;
        var density = editor.Resources?.DisplayMetrics?.Density ?? 1f;
        editor.SetPadding(
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)),
            checked((int)Math.Round(12 * density)),
            checked((int)Math.Round(10 * density)));
        _editableKeyListener = editor.KeyListener;
        return editor;
    }

    /// <inheritdoc />
    protected override void ConnectHandler(RichEditText platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanged += OnNativeDocumentChanged;
        platformView.NativeSelectionChanged += OnNativeSelectionChanged;
        platformView.EditingCompleted += OnNativeEditingCompleted;
        platformView.PasteRequested = OnPlatformPasteAsync;
        platformView.UndoRequested = OnPlatformUndo;
        platformView.RedoRequested = OnPlatformRedo;
        platformView.LinkInvoked = OnPlatformLinkInvoked;
        platformView.InlineObjectInvoked = OnPlatformInlineObjectInvoked;
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(RichEditText platformView)
    {
        platformView.TextChanged -= OnNativeDocumentChanged;
        platformView.NativeSelectionChanged -= OnNativeSelectionChanged;
        platformView.EditingCompleted -= OnNativeEditingCompleted;
        platformView.PasteRequested = null;
        platformView.UndoRequested = null;
        platformView.RedoRequested = null;
        platformView.LinkInvoked = null;
        platformView.InlineObjectInvoked = null;
        base.DisconnectHandler(platformView);
    }

    private Task OnPlatformPasteAsync() =>
        VirtualView is null || VirtualView.IsReadOnly
            ? Task.CompletedTask
            : VirtualView.PasteAsync();

    private void OnPlatformUndo()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Undo();
        }
    }

    private void OnPlatformRedo()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Redo();
        }
    }

    private bool OnPlatformLinkInvoked(string target)
    {
        if (VirtualView is null)
        {
            return true;
        }

        var position = Math.Clamp(PlatformView.SelectionStart, 0, VirtualView.Document.Length);
        var link = VirtualView.Document.CurrentSnapshot.Links.FirstOrDefault(candidate =>
            candidate.Start <= position && position < candidate.End &&
            string.Equals(candidate.Target, target, StringComparison.Ordinal));
        if (link is null)
        {
            link = VirtualView.Document.CurrentSnapshot.Links.FirstOrDefault(candidate =>
                string.Equals(candidate.Target, target, StringComparison.Ordinal));
        }

        return link is null || VirtualView.RaiseLinkInvoked(link);
    }

    private bool OnPlatformInlineObjectInvoked(int position)
    {
        if (VirtualView is null)
        {
            return true;
        }

        var image = VirtualView.Document.CurrentSnapshot.Images.FirstOrDefault(candidate =>
            candidate.Position == position);
        return image is null || VirtualView.RaiseInlineObjectInvoked(image);
    }

    private partial void ApplyDocumentCore(
        RichTextDocumentSnapshot document,
        int selectionStart,
        int selectionLength)
    {
        if (PlatformView is null)
        {
            return;
        }

        _applyingDocument = true;
        try
        {
            var builder = new SpannableStringBuilder(document.Text);
            foreach (var run in document.Runs)
            {
                ApplyCharacterFormat(
                    builder,
                    run.Start,
                    run.End,
                    run.Format,
                    document.DefaultCharacterFormat);
            }

            var listPictures = new Dictionary<string, Drawable?>(StringComparer.Ordinal);

            var listNumbers = new Dictionary<(int Id, int Level), int>();
            var allJustified = true;
            var allHyphenated = true;
            var allLeftToRight = true;
            var allRightToLeft = true;
            foreach (var paragraph in document.Paragraphs)
            {
                allJustified &= paragraph.Format.Alignment is
                    RichTextAlignment.Justified or RichTextAlignment.Distributed;
                allHyphenated &= paragraph.Format.Hyphenation;
                allLeftToRight &= paragraph.Format.Direction == RichTextDirection.LeftToRight;
                allRightToLeft &= paragraph.Format.Direction == RichTextDirection.RightToLeft;
                var end = GetParagraphEnd(document.Text, paragraph.Start);
                string? listMarker = null;
                if (paragraph.Format.NativeList is { } list)
                {
                    if (list.Kind == RichListKind.Bulleted)
                    {
                        listMarker = list.BulletText;
                    }
                    else
                    {
                        var key = (list.Id, list.Level);
                        if (list.Restart || !listNumbers.TryGetValue(key, out var number))
                        {
                            number = list.StartAt;
                        }

                        listMarker = RichTextListFormatter.FormatMarker(list, number);
                        listNumbers[key] = number == int.MaxValue ? number : number + 1;
                    }
                }

                Drawable? listPicture = null;
                if (paragraph.Format.NativeList?.PictureId is { } pictureId &&
                    !listPictures.TryGetValue(pictureId, out listPicture))
                {
                    var picture = document.ListPictures[pictureId];
                    listPicture = CreateBitmapDrawable(
                        picture.Data,
                        picture.Width,
                        picture.Height);
                    listPictures.Add(pictureId, listPicture);
                }

                ApplyParagraphFormat(
                    builder,
                    paragraph.Start,
                    end,
                    paragraph.Format,
                    listMarker,
                    listPicture);
            }

            foreach (var link in document.Links)
            {
                builder.SetSpan(
                    new URLSpan(link.Target),
                    link.Start,
                    link.End,
                    SpanTypes.ExclusiveExclusive);
            }

            foreach (var image in document.Images)
            {
                ApplyImage(builder, image);
            }

            PlatformView.JustificationMode = allJustified
                ? JustificationMode.InterWord
                : JustificationMode.None;
            PlatformView.HyphenationFrequency = allHyphenated
                ? global::Android.Text.HyphenationFrequency.Normal
                : global::Android.Text.HyphenationFrequency.None;
            PlatformView.TextDirection = allRightToLeft
                ? TextDirection.Rtl
                : allLeftToRight
                    ? TextDirection.Ltr
                    : TextDirection.FirstStrong;
            PlatformView.SetText(builder, TextView.BufferType.Spannable);
            SetSelectionCore(selectionStart, selectionLength);
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private partial void ApplyIncrementalChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        if (PlatformView?.EditableText is not { } editable)
        {
            return;
        }

        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
        {
            ApplyDocumentCore(
                VirtualView.Document.CurrentSnapshot,
                selection.Start,
                selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            return;
        }

        var snapshot = VirtualView.Document.CurrentSnapshot;
        _applyingDocument = true;
        try
        {
            foreach (var textChange in changes.Changes.OfType<RichTextTextChange>())
            {
                using var replacement = new Java.Lang.String(textChange.InsertedText);
                editable.Replace(
                    textChange.OldRange.Start,
                    textChange.OldRange.End,
                    replacement);
            }

            var characterRange = GetAffectedRange(changes, snapshot.Length);
            if (changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.CharacterFormat or
                    RichTextChangeKind.DefaultFormat))
            {
                ApplyCharacterFormatsIncrementally(editable, snapshot, characterRange);
            }

            var paragraphRange = GetAffectedParagraphRange(changes, snapshot.Text);
            if (changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.ParagraphFormat or
                    RichTextChangeKind.List or
                    RichTextChangeKind.DefaultFormat))
            {
                ApplyParagraphFormatsIncrementally(editable, snapshot, paragraphRange);
                UpdateGlobalParagraphProjection(snapshot);
            }

            if (changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or RichTextChangeKind.Link))
            {
                RemoveSpans<URLSpan>(editable, characterRange.Start, characterRange.End);
                foreach (var link in snapshot.Links.Where(link =>
                             link.End > characterRange.Start &&
                             link.Start < characterRange.End))
                {
                    editable.SetSpan(
                        new URLSpan(link.Target),
                        link.Start,
                        link.End,
                        SpanTypes.ExclusiveExclusive);
                }
            }

            if (changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or RichTextChangeKind.Image))
            {
                RemoveSpans<RichImageMetadataSpan>(
                    editable,
                    characterRange.Start,
                    characterRange.End);
                RemoveSpans<ImageSpan>(editable, characterRange.Start, characterRange.End);
                foreach (var image in snapshot.Images.Where(image =>
                             image.Position >= characterRange.Start &&
                             image.Position < characterRange.End))
                {
                    ApplyImage(editable, image);
                }
            }

            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            PlatformView.RequestLayout();
            PlatformView.Invalidate();
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private void ApplyCharacterFormatsIncrementally(
        ISpannable editable,
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        if (range.IsEmpty || snapshot.Length == 0)
        {
            return;
        }

        range = ExpandCharacterSpanRange(editable, range, snapshot.Length);
        RemoveCharacterSpans(editable, range.Start, range.End);
        for (var index = snapshot.FindRunIndex(range.Start);
             index < snapshot.Runs.Length;
             index++)
        {
            var run = snapshot.Runs[index];
            if (run.Start >= range.End)
            {
                break;
            }

            var start = Math.Max(run.Start, range.Start);
            var end = Math.Min(run.End, range.End);
            if (end > start)
            {
                ApplyCharacterFormat(
                    editable,
                    start,
                    end,
                    run.Format,
                    snapshot.DefaultCharacterFormat);
            }
        }
    }

    private void ApplyParagraphFormatsIncrementally(
        ISpannable editable,
        RichTextDocumentSnapshot snapshot,
        RichTextRange range)
    {
        RemoveParagraphSpans(editable, range.Start, range.End);
        var firstIndex = snapshot.FindParagraphIndex(range.Start);
        var counters = new Dictionary<(int Id, int Level), int>();
        for (var index = 0; index < firstIndex; index++)
        {
            _ = AdvanceListMarker(snapshot.Paragraphs[index].Format.NativeList, counters);
        }

        var pictures = new Dictionary<string, Drawable?>(StringComparer.Ordinal);
        for (var index = firstIndex;
             index < snapshot.Paragraphs.Length;
             index++)
        {
            var paragraph = snapshot.Paragraphs[index];
            if (paragraph.Range.Start > range.End ||
                (!range.IsEmpty && paragraph.Range.Start == range.End))
            {
                break;
            }

            if (paragraph.Range.End < range.Start)
            {
                continue;
            }

            var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
            var list = paragraph.Format.NativeList;
            var marker = AdvanceListMarker(list, counters);
            Drawable? picture = null;
            if (list?.PictureId is { } pictureId &&
                !pictures.TryGetValue(pictureId, out picture) &&
                snapshot.ListPictures.TryGetValue(pictureId, out var modelPicture))
            {
                picture = CreateBitmapDrawable(
                    modelPicture.Data,
                    modelPicture.Width,
                    modelPicture.Height);
                pictures.Add(pictureId, picture);
            }

            ApplyParagraphFormat(
                editable,
                paragraph.Start,
                end,
                paragraph.Format,
                marker,
                picture);
        }
    }

    private static string? AdvanceListMarker(
        RichTextListFormat? list,
        Dictionary<(int Id, int Level), int> counters)
    {
        if (list is null)
        {
            return null;
        }

        if (list.Kind == RichListKind.Bulleted)
        {
            return list.BulletText;
        }

        var key = (list.Id, list.Level);
        if (list.Restart || !counters.TryGetValue(key, out var number))
        {
            number = list.StartAt;
        }

        var marker = RichTextListFormatter.FormatMarker(list, number);
        counters[key] = number == int.MaxValue ? number : number + 1;
        return marker;
    }

    private void UpdateGlobalParagraphProjection(RichTextDocumentSnapshot snapshot)
    {
        var allJustified = snapshot.Paragraphs.All(paragraph =>
            paragraph.Format.Alignment is RichTextAlignment.Justified or
                RichTextAlignment.Distributed);
        var allHyphenated = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Hyphenation);
        var allLeftToRight = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Direction == RichTextDirection.LeftToRight);
        var allRightToLeft = snapshot.Paragraphs.All(static paragraph =>
            paragraph.Format.Direction == RichTextDirection.RightToLeft);
        PlatformView.JustificationMode = allJustified
            ? JustificationMode.InterWord
            : JustificationMode.None;
        PlatformView.HyphenationFrequency = allHyphenated
            ? global::Android.Text.HyphenationFrequency.Normal
            : global::Android.Text.HyphenationFrequency.None;
        PlatformView.TextDirection = allRightToLeft
            ? TextDirection.Rtl
            : allLeftToRight
                ? TextDirection.Ltr
                : TextDirection.FirstStrong;
    }

    private static RichTextRange GetAffectedRange(
        RichTextChangeSet changes,
        int documentLength) =>
        changes.GetAffectedRange(documentLength);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextChangeSet changes,
        string text) =>
        GetAffectedParagraphRange(GetAffectedRange(changes, text.Length), text);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextRange range,
        string text)
    {
        range = range.Clamp(text.Length);
        var start = range.Start == 0 ? 0 : text.LastIndexOf('\n', range.Start - 1) + 1;
        var newline = text.IndexOf('\n', range.End);
        var end = newline < 0 ? text.Length : newline + 1;
        return new RichTextRange(start, end - start);
    }

    private static RichTextRange ExpandCharacterSpanRange(
        ISpanned text,
        RichTextRange range,
        int documentLength)
    {
        var start = range.Start;
        var end = range.End;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var span in EnumerateCharacterSpans(text, start, end))
            {
                var spanStart = text.GetSpanStart(span);
                var spanEnd = text.GetSpanEnd(span);
                if (spanStart >= 0 && spanStart < start)
                {
                    start = spanStart;
                    changed = true;
                }

                if (spanEnd > end)
                {
                    end = spanEnd;
                    changed = true;
                }
            }
        }

        start = Math.Clamp(start, 0, documentLength);
        end = Math.Clamp(end, start, documentLength);
        return new RichTextRange(start, end - start);
    }

    private static IEnumerable<Java.Lang.Object> EnumerateCharacterSpans(
        ISpanned text,
        int start,
        int end) =>
        GetSpans<CharacterStyle>(text, start, end)
            .Where(static span => span is
                RichCharacterMetadataSpan or
                StyleSpan or
                UnderlineSpan or
                StrikethroughSpan or
                TypefaceSpan or
                AbsoluteSizeSpan or
                RelativeSizeSpan or
                SuperscriptSpan or
                SubscriptSpan or
                ForegroundColorSpan or
                BackgroundColorSpan or
                ScaleXSpan or
                RichLetterSpacingSpan or
                RichBaselineOffsetSpan or
                LocaleSpan)
            .Cast<Java.Lang.Object>();

    private static void RemoveCharacterSpans(ISpannable text, int start, int end)
    {
        foreach (var span in EnumerateCharacterSpans(text, start, end).ToArray())
        {
            text.RemoveSpan(span);
        }
    }

    private static void RemoveParagraphSpans(ISpannable text, int start, int end)
    {
        RemoveIntersectingParagraphSpans<RichParagraphMetadataSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<AlignmentSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<LeadingMarginSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<RichLineHeightSpan>(text, start, end);
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            RemoveIntersectingParagraphSpans<LineHeightSpanStandard>(text, start, end);
        }
        RemoveIntersectingParagraphSpans<TabStopSpanStandard>(text, start, end);
        RemoveIntersectingParagraphSpans<RichListMarkerSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<BulletSpan>(text, start, end);
        RemoveIntersectingParagraphSpans<RichParagraphDecorationSpan>(text, start, end);
    }

    private static void RemoveIntersectingParagraphSpans<T>(
        ISpannable text,
        int start,
        int end)
        where T : Java.Lang.Object
    {
        foreach (var span in GetSpans<T>(text, start, end).ToArray())
        {
            var spanStart = text.GetSpanStart(span);
            var spanEnd = text.GetSpanEnd(span);
            var intersects = start == end
                ? spanStart == start || spanStart < start && spanEnd > start
                : spanStart < end && spanEnd > start ||
                  // Android can collapse a paragraph span onto the edit boundary
                  // when the paragraph delimiter is inserted or removed.
                  spanStart == spanEnd && spanStart >= start && spanStart <= end;
            if (intersects)
            {
                text.RemoveSpan(span);
            }
        }
    }

    private static void RemoveSpans<T>(ISpannable text, int start, int end)
        where T : Java.Lang.Object
    {
        foreach (var span in GetSpans<T>(text, start, end).ToArray())
        {
            text.RemoveSpan(span);
        }
    }

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
    }

    private partial void SetSelectionCore(int start, int length)
    {
        if (PlatformView is null)
        {
            return;
        }

        var textLength = PlatformView.Text?.Length ?? 0;
        start = Math.Clamp(start, 0, textLength);
        length = Math.Clamp(length, 0, textLength - start);
        PlatformView.SetSelection(start, start + length);
    }

    private partial bool TryCutCore() => false;

    // Android's public TextView API does not expose its internal undo manager or
    // CanUndo/CanRedo state, so the portable document history remains the fallback.
    private partial bool SupportsNativeUndoCore() => false;

    private partial bool CanUndoCore() => VirtualView?.Document.CanUndo == true;

    private partial bool CanRedoCore() => VirtualView?.Document.CanRedo == true;

    private partial void UndoCore() => OnPlatformUndo();

    private partial void RedoCore() => OnPlatformRedo();

    private partial void ClearUndoHistoryCore()
    {
        VirtualView?.Document.ClearUndoHistory();
        VirtualView?.UpdateUndoStateFromPlatform();
    }

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.PlaceholderProperty))
        {
            PlatformView.Hint = editor.Placeholder;
        }

        if (editor.IsSet(RichEditor.PlaceholderColorProperty))
        {
            if (editor.PlaceholderColor is { } placeholderColor)
            {
                PlatformView.SetHintTextColor(placeholderColor.ToPlatform());
            }
            else if (_defaultHintTextColors is not null)
            {
                PlatformView.SetHintTextColor(_defaultHintTextColors);
            }
        }
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.TextColorProperty) && editor.TextColor is { } textColor)
        {
            PlatformView.SetTextColor(textColor.ToPlatform());
        }
        else if (editor.IsSet(RichEditor.TextColorProperty) && _defaultTextColors is not null)
        {
            PlatformView.SetTextColor(_defaultTextColors);
        }

        if (editor.IsSet(RichEditor.FontSizeProperty))
        {
            if (editor.FontSize is { } fontSize)
            {
                PlatformView.SetTextSize(ComplexUnitType.Sp, (float)fontSize);
            }
            else
            {
                PlatformView.SetTextSize(ComplexUnitType.Px, _defaultTextSize);
            }
        }

        if (editor.IsSet(RichEditor.FontFamilyProperty))
        {
            PlatformView.Typeface = string.IsNullOrWhiteSpace(editor.FontFamily)
                ? _defaultTypeface
                : Typeface.Create(editor.FontFamily, TypefaceStyle.Normal);
        }

        if (!_applyingDocument)
        {
            _applyingDocument = true;
            try
            {
                if (PlatformView.EditableText is { } editable)
                {
                    ApplyCharacterFormatsIncrementally(
                        editable,
                        editor.Document.CurrentSnapshot,
                        new RichTextRange(0, editor.Document.Length));
                }

                SetSelectionCore(editor.SelectedRange.Start, editor.SelectedRange.Length);
                PlatformView.RequestLayout();
                PlatformView.Invalidate();
            }
            finally
            {
                _applyingDocument = false;
            }

            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
    }

    private partial void UpdateInputConfiguration(RichEditor editor)
    {
        if (_editableKeyListener is null && PlatformView.KeyListener is not null)
        {
            _editableKeyListener = PlatformView.KeyListener;
        }

        PlatformView.KeyListener = editor.IsReadOnly ? null : _editableKeyListener;
        PlatformView.SetTextIsSelectable(editor.IsReadOnly);
        PlatformView.SetCursorVisible(!editor.IsReadOnly);
        var inputType = ReferenceEquals(editor.Keyboard, Keyboard.Numeric)
            ? InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned
            : ReferenceEquals(editor.Keyboard, Keyboard.Telephone)
                ? InputTypes.ClassPhone
                : ReferenceEquals(editor.Keyboard, Keyboard.Email)
                    ? InputTypes.ClassText | InputTypes.TextVariationEmailAddress
                    : ReferenceEquals(editor.Keyboard, Keyboard.Url)
                        ? InputTypes.ClassText | InputTypes.TextVariationUri
                        : InputTypes.ClassText | InputTypes.TextFlagMultiLine;
        if (editor.IsTextPredictionEnabled)
        {
            inputType |= InputTypes.TextFlagCapSentences;
        }

        if (editor.IsSpellCheckEnabled)
        {
            inputType |= InputTypes.TextFlagAutoCorrect;
        }

        PlatformView.InputType = inputType;
        PlatformView.AcceptsTab = editor.AcceptsTab;
        PlatformView.SetFilters(editor.MaxLength < 0
            ? []
            : [new InputFilterLengthFilter(editor.MaxLength)]);
    }

    private void ApplyCharacterFormat(
        ISpannable text,
        int start,
        int end,
        RichTextCharacterFormat format,
        RichTextCharacterFormat? inheritedFormat = null)
    {
        if (end <= start)
        {
            return;
        }

        var authoredFormat = format;
        if (inheritedFormat is not null)
        {
            format = format with
            {
                FontFamily = format.FontFamily ?? inheritedFormat.FontFamily,
                FontSize = format.FontSize ?? inheritedFormat.FontSize,
                ForegroundColor = format.ForegroundColor ?? inheritedFormat.ForegroundColor,
            };
        }

        text.SetSpan(
            new RichCharacterMetadataSpan(authoredFormat),
            start,
            end,
            SpanTypes.ExclusiveExclusive);

        var style = TypefaceStyle.Normal;
        if (format.Bold)
        {
            style |= TypefaceStyle.Bold;
        }

        if (format.Italic)
        {
            style |= TypefaceStyle.Italic;
        }

        if (style != TypefaceStyle.Normal)
        {
            text.SetSpan(new StyleSpan(style), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.Underline != RichTextUnderlineStyle.None)
        {
            text.SetSpan(new UnderlineSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.Strikethrough != RichTextStrikethroughStyle.None)
        {
            text.SetSpan(new StrikethroughSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (!string.IsNullOrWhiteSpace(format.FontFamily))
        {
            text.SetSpan(
                new TypefaceSpan(format.FontFamily),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.FontSize is > 0)
        {
            text.SetSpan(
                new AbsoluteSizeSpan(
                    Math.Max(checked((int)Math.Round(format.FontSize.Value)), 1),
                    true),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.Script == RichTextScript.Superscript)
        {
            text.SetSpan(new SuperscriptSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }
        else if (format.Script == RichTextScript.Subscript)
        {
            text.SetSpan(new SubscriptSpan(), start, end, SpanTypes.ExclusiveExclusive);
        }

        if (format.ForegroundColor is not null)
        {
            text.SetSpan(
                new ForegroundColorSpan(format.ForegroundColor.ToPlatform()),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.BackgroundColor is not null)
        {
            text.SetSpan(
                new BackgroundColorSpan(format.BackgroundColor.ToPlatform()),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.HorizontalScale != 1d)
        {
            text.SetSpan(
                new ScaleXSpan((float)format.HorizontalScale),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.CharacterSpacing != 0)
        {
            var fontSize = ResolveFontSize(format.FontSize);
            text.SetSpan(
                new RichLetterSpacingSpan((float)(format.CharacterSpacing / fontSize)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (format.BaselineOffset != 0)
        {
            text.SetSpan(
                new RichBaselineOffsetSpan(ToPixels(format.BaselineOffset)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }

        if (!string.IsNullOrWhiteSpace(format.LanguageTag))
        {
            text.SetSpan(
                new LocaleSpan(Java.Util.Locale.ForLanguageTag(format.LanguageTag)),
                start,
                end,
                SpanTypes.ExclusiveExclusive);
        }
    }

    private void ApplyParagraphFormat(
        ISpannable text,
        int start,
        int end,
        RichTextParagraphFormat format,
        string? listMarker,
        Drawable? listPicture)
    {
        if (end <= start)
        {
            return;
        }

        text.SetSpan(
            new RichParagraphMetadataSpan(format),
            start,
            end,
            SpanTypes.Paragraph);

        var alignment = format.Alignment switch
        {
            RichTextAlignment.Center => TextAlignment.AlignCenter,
            RichTextAlignment.Right => TextAlignment.AlignOpposite,
            RichTextAlignment.Justified or RichTextAlignment.Distributed => TextAlignment.AlignNormal,
            _ => TextAlignment.AlignNormal,
        };
        if (alignment != TextAlignment.AlignNormal)
        {
            text.SetSpan(
                new AlignmentSpanStandard(alignment!),
                start,
                end,
                SpanTypes.Paragraph);
        }

        var firstMargin = ToPixels(format.LeadingIndent + format.FirstLineIndent);
        var remainingMargin = ToPixels(format.LeadingIndent);
        if (firstMargin != 0 || remainingMargin != 0)
        {
            text.SetSpan(
                new LeadingMarginSpanStandard(firstMargin, remainingMargin),
                start,
                end,
                SpanTypes.Paragraph);
        }

        if (RichLineHeightSpan.IsNeeded(format))
        {
            text.SetSpan(
                new RichLineHeightSpan(
                    start,
                    end,
                    format.LineSpacingRule,
                    format.LineSpacingRule is
                        RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly
                            ? ToPixels(format.LineSpacing)
                            : format.LineSpacing,
                    format.MinimumLineHeight is > 0 ? ToPixels(format.MinimumLineHeight.Value) : null,
                    format.MaximumLineHeight is > 0 ? ToPixels(format.MaximumLineHeight.Value) : null,
                    ToPixels(format.SpaceBefore),
                    ToPixels(format.SpaceAfter)),
                start,
                end,
                SpanTypes.Paragraph);
        }

        foreach (var tab in format.TabStops.Where(tab => tab.Alignment == RichTextTabAlignment.Left))
        {
            text.SetSpan(
                new TabStopSpanStandard(ToPixels(tab.Position)),
                start,
                end,
                SpanTypes.Paragraph);
        }

        if (format.NativeList is { } list && !string.IsNullOrEmpty(listMarker))
        {
            ApplyListMarkerSpan(text, start, end, list, listMarker, listPicture);
        }

        if (format.BackgroundColor is not null ||
            format.Border is { Sides: not RichTextBorderSides.None, Style: not RichTextBorderStyle.None })
        {
            var border = format.Border;
            text.SetSpan(
                new RichParagraphDecorationSpan(
                    start,
                    end,
                    format.BackgroundColor?.ToPlatform(),
                    border?.Sides ?? RichTextBorderSides.None,
                    border?.Style ?? RichTextBorderStyle.None,
                    border is null
                        ? 0
                        : Math.Max(
                            (float)(border.Width *
                                (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f)),
                            1f),
                    ResolveTextColor(border?.Color)),
                start,
                end,
                SpanTypes.Paragraph);
        }
    }

    private void ApplyListMarkerSpan(
        ISpannable text,
        int start,
        int end,
        RichTextListFormat list,
        string marker,
        Drawable? picture)
    {
        if (end <= start)
        {
            return;
        }

        var markerWidth = picture?.Bounds.Width() ??
            (PlatformView is { Paint: { } paint }
                ? (int)Math.Min(Math.Ceiling(paint.MeasureText(marker)), int.MaxValue)
                : 16);
        text.SetSpan(
            new RichListMarkerSpan(
                list,
                marker,
                picture,
                markerWidth,
                ToPixels(8),
                ToPixels(18 * list.Level)),
            start,
            end,
            SpanTypes.Paragraph);
    }

    private void ApplyImage(ISpannable text, RichTextImage image)
    {
        if (image.Position < 0 || image.Position >= text.Length())
        {
            return;
        }

        text.SetSpan(
            new RichImageMetadataSpan(image),
            image.Position,
            image.Position + 1,
            SpanTypes.ExclusiveExclusive);
        if (image.Data.IsDefaultOrEmpty)
        {
            return;
        }

        var drawable = CreateBitmapDrawable(image.Data, image.Width, image.Height);
        if (drawable is null)
        {
            return;
        }

        var alignment = image.VerticalAlignment == RichTextImageVerticalAlignment.Baseline
            ? SpanAlign.Baseline
            : SpanAlign.Bottom;
        var imageSpan = new ImageSpan(drawable, image.Source ?? string.Empty, alignment);
        if (!string.IsNullOrEmpty(image.AlternativeText) &&
            OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            imageSpan.ContentDescription = image.AlternativeText;
        }

        text.SetSpan(
            imageSpan,
            image.Position,
            image.Position + 1,
            SpanTypes.ExclusiveExclusive);
    }

    private BitmapDrawable? CreateBitmapDrawable(
        ImmutableArray<byte> data,
        double width,
        double height)
    {
        if (data.IsDefaultOrEmpty)
        {
            return null;
        }

        var bytes = ImmutableCollectionsMarshal.AsArray(data);
        if (bytes is null)
        {
            return null;
        }

        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        if (bitmap is null)
        {
            return null;
        }

        var drawable = new BitmapDrawable(PlatformView.Resources, bitmap);
        var pixelWidth = width > 0 ? ToPixels(width) : bitmap.Width;
        var pixelHeight = height > 0 ? ToPixels(height) : bitmap.Height;
        drawable.SetBounds(0, 0, Math.Max(pixelWidth, 1), Math.Max(pixelHeight, 1));
        return drawable;
    }

    private RichTextDocumentSnapshot ReadDocumentFromPlatform(string text)
    {
        if (PlatformView.EditableText is not { } editable)
        {
            return RichTextDocumentSnapshot.FromPlainText(text);
        }

        var previous = VirtualView.Document.CurrentSnapshot;
        var remappedPrevious = previous.RemapText(text);
        var defaultCharacterFormat = previous.DefaultCharacterFormat;
        var inheritedCharacterFormat =
            RichTextDocumentSnapshot.CreateInheritedCharacterFormat(defaultCharacterFormat);
        var runs = new List<RichTextRun>();
        for (var position = 0; position < text.Length;)
        {
            var end = editable.NextSpanTransition(
                position,
                text.Length,
                SpanType<CharacterStyle>.Value);
            if (end <= position)
            {
                end = position + 1;
            }

            var format = ReadCharacterFormat(editable, position, inheritedCharacterFormat);
            if (runs.Count > 0 && runs[^1].Format == format)
            {
                runs[^1] = runs[^1] with { Length = runs[^1].Length + end - position };
            }
            else
            {
                runs.Add(new RichTextRun(position, end - position, format));
            }

            position = end;
        }

        var paragraphs = new List<RichTextParagraph>();
        RichTextListFormat? previousList = null;
        var assignedListIds = new HashSet<int>();
        var nextListId = checked(previous.Lists.Keys
            .Select(static id => id.Value)
            .DefaultIfEmpty()
            .Max() + 1);
        for (var start = 0; ;)
        {
            var end = GetParagraphEnd(text, start);
            var format = ReadParagraphFormat(
                editable,
                start,
                end,
                previous.DefaultParagraphFormat);
            if (format.NativeList is { } list)
            {
                if (list.Id <= 0)
                {
                    var continues = previousList is not null &&
                        previousList.Kind == list.Kind &&
                        previousList.Level == list.Level;
                    var priorId = remappedPrevious.GetParagraphFormat(
                        Math.Min(start, remappedPrevious.Length)).List?.ListId.Value;
                    var id = continues
                        ? previousList!.Id
                        : priorId is > 0 && assignedListIds.Add(priorId.Value)
                            ? priorId.Value
                            : nextListId++;
                    assignedListIds.Add(id);
                    list = list with { Id = id };
                    format = format with { NativeList = list };
                }
                else
                {
                    assignedListIds.Add(list.Id);
                }

                previousList = list;
            }
            else
            {
                previousList = null;
            }

            paragraphs.Add(new RichTextParagraph(start, format));
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        var links = ReadLinks(editable, text.Length);
        var images = ReadImages(editable, text);
        return previous.MergeNativeSnapshot(
            text,
            runs,
            paragraphs,
            links,
            images,
            defaultCharacterFormat,
            previous.DefaultParagraphFormat,
            remappedSnapshot: remappedPrevious);
    }

    private RichTextCharacterFormat ReadCharacterFormat(
        ISpanned text,
        int position,
        RichTextCharacterFormat defaultFormat)
    {
        var format = GetSpans<RichCharacterMetadataSpan>(text, position, position + 1)
            .LastOrDefault()?.Format ?? defaultFormat;

        foreach (var span in GetSpans<StyleSpan>(text, position, position + 1))
        {
            if (span.Style == TypefaceStyle.Normal)
            {
                format = format with { FontWeight = 400, Italic = false };
                continue;
            }

            if ((span.Style & TypefaceStyle.Bold) != 0)
            {
                format = format with { FontWeight = Math.Max(format.FontWeight, 700) };
            }

            if ((span.Style & TypefaceStyle.Italic) != 0)
            {
                format = format with { Italic = true };
            }
        }

        if (GetSpans<UnderlineSpan>(text, position, position + 1).Any())
        {
            format = format with
            {
                Underline = format.Underline == RichTextUnderlineStyle.None
                    ? RichTextUnderlineStyle.Single
                    : format.Underline,
            };
        }

        if (GetSpans<StrikethroughSpan>(text, position, position + 1).Any())
        {
            format = format with
            {
                Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None
                    ? RichTextStrikethroughStyle.Single
                    : format.Strikethrough,
            };
        }

        var family = GetSpans<TypefaceSpan>(text, position, position + 1)
            .Select(span => span.Family)
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (family is not null)
        {
            format = format with { FontFamily = family };
        }

        var absoluteSize = GetSpans<AbsoluteSizeSpan>(text, position, position + 1).LastOrDefault();
        if (absoluteSize is not null)
        {
            var size = absoluteSize.Dip
                ? absoluteSize.Size
                : FromPixels(absoluteSize.Size);
            format = format with { FontSize = Math.Max(size, 1) };
        }

        foreach (var span in GetSpans<RelativeSizeSpan>(text, position, position + 1))
        {
            format = format with
            {
                FontSize = Math.Max(ResolveFontSize(format.FontSize) * span.SizeChange, 1),
            };
        }

        if (GetSpans<SuperscriptSpan>(text, position, position + 1).Any())
        {
            format = format with { Script = RichTextScript.Superscript };
        }
        else if (GetSpans<SubscriptSpan>(text, position, position + 1).Any())
        {
            format = format with { Script = RichTextScript.Subscript };
        }

        var foreground = GetSpans<ForegroundColorSpan>(text, position, position + 1).LastOrDefault();
        if (foreground is not null)
        {
            format = format with
            {
                ForegroundColor = FromAndroidColor(
                    new Android.Graphics.Color(foreground.ForegroundColor)),
            };
        }

        var background = GetSpans<BackgroundColorSpan>(text, position, position + 1).LastOrDefault();
        if (background is not null)
        {
            format = format with
            {
                BackgroundColor = FromAndroidColor(
                    new Android.Graphics.Color(background.BackgroundColor)),
            };
        }

        var scale = GetSpans<ScaleXSpan>(text, position, position + 1).LastOrDefault();
        if (scale is not null && scale.ScaleX > 0)
        {
            format = format with { HorizontalScale = scale.ScaleX };
        }

        var spacing = GetSpans<RichLetterSpacingSpan>(text, position, position + 1).LastOrDefault();
        if (spacing is not null)
        {
            format = format with
            {
                CharacterSpacing = spacing.Em * ResolveFontSize(format.FontSize),
            };
        }

        var baseline = GetSpans<RichBaselineOffsetSpan>(text, position, position + 1).LastOrDefault();
        if (baseline is not null)
        {
            format = format with { BaselineOffset = FromPixels(baseline.Pixels) };
        }

        var locale = GetSpans<LocaleSpan>(text, position, position + 1).LastOrDefault()?.Locale;
        if (locale is not null)
        {
            format = format with { LanguageTag = locale.ToLanguageTag() };
        }

        return format;
    }

    private RichTextParagraphFormat ReadParagraphFormat(
        ISpanned text,
        int start,
        int end,
        RichTextParagraphFormat defaultFormat)
    {
        var format = GetSpans<RichParagraphMetadataSpan>(text, start, end)
            .LastOrDefault()?.Format ?? defaultFormat;
        var alignment = GetSpans<AlignmentSpanStandard>(text, start, end).LastOrDefault();
        if (alignment is not null)
        {
            var nativeAlignment = alignment.Alignment;
            format = format with
            {
                Alignment = nativeAlignment?.Equals(TextAlignment.AlignCenter) == true
                    ? RichTextAlignment.Center
                    : nativeAlignment?.Equals(TextAlignment.AlignOpposite) == true
                        ? RichTextAlignment.Right
                        : RichTextAlignment.Left,
            };
        }

        var margin = GetSpans<LeadingMarginSpanStandard>(text, start, end).LastOrDefault();
        if (margin is not null)
        {
            var first = FromPixels(margin.GetLeadingMargin(true));
            var rest = FromPixels(margin.GetLeadingMargin(false));
            format = format with
            {
                LeadingIndent = rest,
                FirstLineIndent = first - rest,
            };
        }

        var richLineHeight = GetSpans<RichLineHeightSpan>(text, start, end).LastOrDefault();
        if (richLineHeight is not null)
        {
            format = format with
            {
                LineSpacingRule = richLineHeight.Rule,
                LineSpacing = richLineHeight.Rule is
                    RichTextLineSpacingRule.AtLeast or RichTextLineSpacingRule.Exactly
                        ? FromPixels(richLineHeight.Value)
                        : richLineHeight.Value,
                MinimumLineHeight = richLineHeight.MinimumHeight is { } minimum
                    ? FromPixels(minimum)
                    : null,
                MaximumLineHeight = richLineHeight.MaximumHeight is { } maximum
                    ? FromPixels(maximum)
                    : null,
                SpaceBefore = FromPixels(richLineHeight.SpaceBefore),
                SpaceAfter = FromPixels(richLineHeight.SpaceAfter),
            };
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var lineHeight = GetSpans<LineHeightSpanStandard>(text, start, end).LastOrDefault();
            if (lineHeight is not null)
            {
                format = format with
                {
                    LineSpacingRule = RichTextLineSpacingRule.Exactly,
                    LineSpacing = FromPixels(lineHeight.Height),
                };
            }
        }

        var tabs = GetSpans<TabStopSpanStandard>(text, start, end)
            .Select(span => new RichTextTabStop(FromPixels(span.TabStop)))
            .OrderBy(tab => tab.Position)
            .DistinctBy(tab => tab.Position)
            .ToImmutableArray();
        if (!tabs.IsDefaultOrEmpty)
        {
            format = format with { TabStops = tabs };
        }

        var listMarker = GetSpans<RichListMarkerSpan>(text, start, end).LastOrDefault();
        if (listMarker is not null)
        {
            format = format with
            {
                NativeList = listMarker.ListFormat,
                List = RichTextListConversions.ToItem(listMarker.ListFormat),
            };
        }
        else if (GetSpans<BulletSpan>(text, start, end).Any())
        {
            format = format with
            {
                NativeList = (format.NativeList ?? new RichTextListFormat { Id = 0 }) with
                {
                    Kind = RichListKind.Bulleted,
                },
            };
        }
        else
        {
            // RichParagraphMetadataSpan preserves model-only paragraph values,
            // but the marker span is the native authority for active list state.
            // Android removes or splits that marker as the user edits paragraph
            // boundaries, so never resurrect a list from stale metadata.
            format = format with { List = null, NativeList = null };
        }

        return format;
    }

    private static IReadOnlyList<RichTextLink> ReadLinks(ISpanned text, int textLength)
    {
        var links = new List<RichTextLink>();
        var previousEnd = 0;
        foreach (var span in GetSpans<URLSpan>(text, 0, textLength)
                     .OrderBy(span => text.GetSpanStart(span)))
        {
            var start = text.GetSpanStart(span);
            var end = text.GetSpanEnd(span);
            if (start < previousEnd || start < 0 || end <= start || end > textLength ||
                string.IsNullOrWhiteSpace(span.URL))
            {
                continue;
            }

            links.Add(new RichTextLink(start, end - start, span.URL));
            previousEnd = end;
        }

        return links;
    }

    private IReadOnlyList<RichTextImage> ReadImages(ISpanned text, string plainText)
    {
        var images = new Dictionary<int, RichTextImage>();
        foreach (var span in GetSpans<RichImageMetadataSpan>(text, 0, plainText.Length))
        {
            var position = text.GetSpanStart(span);
            if (position >= 0 && position < plainText.Length &&
                plainText[position] == RichTextDocument.ObjectReplacementCharacter)
            {
                images[position] = span.Image with { Position = position };
            }
        }

        foreach (var span in GetSpans<ImageSpan>(text, 0, plainText.Length))
        {
            var position = text.GetSpanStart(span);
            if (images.ContainsKey(position) || position < 0 || position >= plainText.Length ||
                plainText[position] != RichTextDocument.ObjectReplacementCharacter)
            {
                continue;
            }

            var bounds = span.Drawable?.Bounds;
            images[position] = new RichTextImage
            {
                Position = position,
                MediaType = "application/octet-stream",
                Source = span.Source,
                Width = bounds is null ? 0 : FromPixels(bounds.Width()),
                Height = bounds is null ? 0 : FromPixels(bounds.Height()),
                AlternativeText = OperatingSystem.IsAndroidVersionAtLeast(30)
                    ? span.ContentDescription
                    : null,
            };
        }

        return [.. images.Values.OrderBy(image => image.Position)];
    }

    private void OnNativeDocumentChanged(object? sender, Android.Text.TextChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null || PlatformView?.EditableText is not { } editable)
        {
            return;
        }

        var previousDocument = VirtualView.Document.CurrentSnapshot;
        var previousText = previousDocument.Text;
        var removedStart = Math.Clamp(eventArgs.Start, 0, previousText.Length);
        var removedLength = Math.Clamp(
            eventArgs.BeforeCount,
            0,
            previousText.Length - removedStart);
        var nativeLength = editable.Length();
        var insertedStart = Math.Clamp(eventArgs.Start, 0, nativeLength);
        var insertedLength = Math.Clamp(eventArgs.AfterCount, 0, nativeLength - insertedStart);
        var insertedText = Java.Lang.ICharSequenceExtensions.SubSequence(
            editable,
            insertedStart,
            insertedStart + insertedLength);

        var paragraphStructureChanged = previousText
            .AsSpan(removedStart, removedLength)
            .Contains('\n') ||
            insertedText.Contains('\n');

        var containsPastedRichContent = insertedLength > 0 &&
            ContainsPastedRichContent(editable, insertedStart, insertedStart + insertedLength);
        var containsPastedListContent = insertedLength > 0 &&
            ContainsPastedListContent(editable, insertedStart, insertedStart + insertedLength);
        var requiresNativeSnapshot = paragraphStructureChanged ||
            containsPastedRichContent ||
            containsPastedListContent;
        RichTextDocumentSnapshot document;
        if (requiresNativeSnapshot)
        {
            // Paragraph spans move and split as part of Android's own edit. Read
            // their resulting ranges instead of predicting or rewriting them.
            // Ordinary edits that do not change paragraph structure stay incremental.
            document = ReadDocumentFromPlatform(editable.ToString() ?? string.Empty);
        }
        else
        {
            document = previousDocument.Replace(
                removedStart..(removedStart + removedLength),
                insertedText,
                insertedLength == 0 ? null : _nativeTypingFormat);
        }

        insertedStart = Math.Clamp(insertedStart, 0, document.Text.Length);
        insertedLength = Math.Clamp(insertedLength, 0, document.Text.Length - insertedStart);

        if (!requiresNativeSnapshot && insertedLength > 0)
        {
            ApplyInsertedTypingFormat(
                editable,
                document,
                new RichTextRange(insertedStart, insertedLength));
        }

        var start = Math.Clamp(PlatformView.SelectionStart, 0, document.Text.Length);
        var end = Math.Clamp(PlatformView.SelectionEnd, start, document.Text.Length);
        VirtualView.UpdateDocumentFromPlatform(document, start, end - start, _sourceToken);
        UpdateTypingFormatsFromPlatform();
    }

    private void OnNativeSelectionChanged(object? sender, NativeSelectionChangedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var start = Math.Clamp(
            Math.Min(eventArgs.Start, eventArgs.End),
            0,
            VirtualView.Document.Text.Length);
        var end = Math.Clamp(
            Math.Max(eventArgs.Start, eventArgs.End),
            start,
            VirtualView.Document.Text.Length);
        VirtualView.UpdateSelectionFromPlatform(start, end - start);
        UpdateTypingFormatsFromPlatform();
    }

    private void OnNativeEditingCompleted(object? sender, EventArgs eventArgs) =>
        VirtualView?.RaiseCompleted();

    private void UpdateTypingFormatsFromPlatform()
    {
        if (VirtualView is null || PlatformView.EditableText is not { } editable)
        {
            return;
        }

        if (editable.Length() == 0)
        {
            _nativeTypingFormat = VirtualView.TypingCharacterFormat;
            _nativeTypingParagraphFormat = VirtualView.TypingParagraphFormat;
        }
        else
        {
            var caret = Math.Clamp(PlatformView.SelectionStart, 0, editable.Length());
            var characterPosition = caret == editable.Length() ? caret - 1 : caret;
            _nativeTypingFormat = ReadCharacterFormat(
                editable,
                characterPosition,
                VirtualView.TypingCharacterFormat);
            var paragraphStart = GetParagraphStart(editable, caret);
            var paragraphEnd = GetParagraphEnd(editable, paragraphStart);
            _nativeTypingParagraphFormat = ReadParagraphFormat(
                editable,
                paragraphStart,
                paragraphEnd,
                VirtualView.TypingParagraphFormat);
            if (_nativeTypingParagraphFormat.NativeList is { Id: <= 0 } nativeList &&
                VirtualView.TypingParagraphFormat.NativeList is { } previousList)
            {
                nativeList = nativeList with { Id = previousList.Id };
                _nativeTypingParagraphFormat = _nativeTypingParagraphFormat with
                {
                    NativeList = nativeList,
                    List = RichTextListConversions.ToItem(nativeList),
                };
            }
        }

        VirtualView.UpdateTypingFormatsFromPlatform(
            _nativeTypingFormat,
            _nativeTypingParagraphFormat);
    }

    private static bool ContainsPastedRichContent(ISpanned text, int start, int end)
    {
        if (end <= start)
        {
            return false;
        }

        return GetSpans<CharacterStyle>(text, start, end)
            .Where(span => span is not RichCharacterMetadataSpan)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);
    }

    private static bool ContainsPastedListContent(ISpanned text, int start, int end) =>
        GetSpans<RichListMarkerSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end) ||
        GetSpans<BulletSpan>(text, start, end)
            .Any(span => text.GetSpanStart(span) >= start && text.GetSpanEnd(span) <= end);

    private static int GetParagraphStart(Java.Lang.ICharSequence text, int position)
    {
        position = Math.Clamp(position, 0, text.Length());
        while (position > 0 && text.CharAt(position - 1) != '\n')
        {
            position--;
        }

        return position;
    }

    private static int GetParagraphEnd(Java.Lang.ICharSequence text, int start)
    {
        var length = text.Length();
        start = Math.Clamp(start, 0, length);
        while (start < length && text.CharAt(start++) != '\n')
        {
        }

        return start;
    }

    private void ApplyInsertedTypingFormat(
        ISpannable text,
        RichTextDocumentSnapshot document,
        RichTextRange range)
    {
        _applyingDocument = true;
        try
        {
            ApplyCharacterFormatsIncrementally(text, document, range);
            if (!GetSpans<RichParagraphMetadataSpan>(text, range.Start, range.End)
                    .Any(span =>
                    {
                        var spanStart = text.GetSpanStart(span);
                        var spanEnd = text.GetSpanEnd(span);
                        return spanStart <= range.Start && spanEnd >= range.End;
                    }))
            {
                // A final empty paragraph has no character on which Android can
                // retain a Paragraph span. Materialize its document format once
                // the first character arrives; existing paragraphs remain wholly
                // under Android's native span-editing behavior.
                ApplyParagraphFormatsIncrementally(
                    text,
                    document,
                    GetAffectedParagraphRange(range, document.Text));
                UpdateGlobalParagraphProjection(document);
            }
        }
        finally
        {
            _applyingDocument = false;
        }
    }

    private static IEnumerable<T> GetSpans<T>(ISpanned text, int start, int end)
        where T : Java.Lang.Object
    {
        foreach (var value in text.GetSpans(
                     Math.Max(start, 0),
                     Math.Max(end, start),
                     SpanType<T>.Value) ?? [])
        {
            if (value is T span)
            {
                yield return span;
            }
        }
    }

    private static class SpanType<T>
        where T : Java.Lang.Object
    {
        public static readonly Java.Lang.Class Value = Java.Lang.Class.FromType(typeof(T));
    }

    private static Microsoft.Maui.Graphics.Color FromAndroidColor(Android.Graphics.Color color) =>
        Microsoft.Maui.Graphics.Color.FromRgba(color.R, color.G, color.B, color.A);

    private Android.Graphics.Color ResolveTextColor(Microsoft.Maui.Graphics.Color? color)
    {
        if (color is not null)
        {
            return color.ToPlatform();
        }

        if (VirtualView.TextColor is { } textColor)
        {
            return textColor.ToPlatform();
        }

        return new Android.Graphics.Color(PlatformView.CurrentTextColor);
    }

    private double ResolveFontSize(double? fontSize)
    {
        if (fontSize is not null)
        {
            return fontSize.Value;
        }

        if (VirtualView.FontSize is { } viewFontSize)
        {
            return viewFontSize;
        }

        var density = PlatformView.Resources?.DisplayMetrics?.Density ?? 1f;
        return Math.Max(PlatformView.TextSize / density, 1f);
    }

    private static int GetParagraphStart(string text, int position) =>
        position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;

    private static int GetParagraphEnd(string text, int start)
    {
        var newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }

    private int ToPixels(double value)
    {
        var pixels = Math.Round(
            value * (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f));
        return pixels switch
        {
            >= int.MaxValue => int.MaxValue,
            <= int.MinValue => int.MinValue,
            _ => (int)pixels,
        };
    }

    private double FromPixels(double value) =>
        value / (PlatformView.Resources?.DisplayMetrics?.Density ?? 1f);
}
