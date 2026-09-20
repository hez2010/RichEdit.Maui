using Android.Graphics.Drawables;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using RichEdit.Maui.Platforms.Android;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial void ApplyDisplayDocumentCore(
        RichTextDocumentSnapshot document,
        int selectionStart,
        int selectionLength)
    {
        // The shared projection has already composed source appearance and reservations.
        if (PlatformView is null)
        {
            return;
        }

        _applyingDocument = true;
        _projectionGeneration++;
        var filters = PlatformView.GetFilters();
        try
        {
            // MaxLength constrains user input, not an already committed document.
            PlatformView.SetFilters([]);
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
            WatchNativeFormats();
            SetSelectionCore(selectionStart, selectionLength);
        }
        finally
        {
            PlatformView.SetFilters(filters);
            _applyingDocument = false;
        }
    }

    private partial void ApplyDecorationsCore(RichTextChangeSet changes) => ApplyDisplayChangesCore(
        changes, VirtualView.SelectedRange, VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat,
        GetPreviousDecorationSnapshot(changes));

    private partial void ApplyDisplayChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
        => ApplyDisplayChangesCore(changes, selection, typingCharacterFormat, typingParagraphFormat, GetPreviousFormattingSnapshot(changes));

    private void ApplyDisplayChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat,
        RichTextDocumentSnapshot? previousSnapshot)
    {
        if (PlatformView?.EditableText is not { } editable)
        {
            return;
        }

        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
        {
            ApplyCurrentDocument(
                selection.Start,
                selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            return;
        }

        var snapshot = DisplayPresentationSnapshot;
        _applyingDocument = true;
        _projectionGeneration++;
        var filters = editable.GetFilters();
        PlatformView.BeginBatchEdit();
        try
        {
            editable.SetFilters([]);
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
                ApplyCharacterFormatsIncrementally(editable, snapshot, characterRange, previousSnapshot);
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
                var surviving = GetSurvivingImages(changes);
                var current = snapshot.Images.ToDictionary(static image => image.Position);
                foreach (var span in GetSpans<Java.Lang.Object>(editable, characterRange.Start, characterRange.End)
                    .Where(static span => span is RichImageMetadataSpan or ImageSpan or RichAdornmentSpan).ToArray())
                {
                    var position = editable.GetSpanStart(span);
                    if (current.TryGetValue(position, out var image) && surviving.GetValueOrDefault(position) == image &&
                        editable.GetSpanEnd(span) == position + 1)
                        continue;

                    editable.RemoveSpan(span);
                }
                foreach (var image in snapshot.Images.Where(image =>
                    image.Position >= characterRange.Start &&
                    image.Position < characterRange.End && surviving.GetValueOrDefault(image.Position) != image))
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
            editable.SetFilters(filters);
            PlatformView.EndBatchEdit();
            _applyingDocument = false;
        }
    }
}
