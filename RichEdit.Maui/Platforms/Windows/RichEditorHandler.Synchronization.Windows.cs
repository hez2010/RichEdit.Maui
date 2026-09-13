using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.UI.Text;

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
        var wasReadOnly = PlatformView.IsReadOnly;
        try
        {
            // Read-only restricts user input, not projection of an authored document.
            PlatformView.IsReadOnly = false;
            var nativeDocument = PlatformView.Document;
            nativeDocument.BatchDisplayUpdates();
            try
            {
                _hasNativeLinks = false;
                _nativeTextSnapshot = null;
                var useNativeRtf = document.Images.Length > 0 ||
                    document.Paragraphs.Any(paragraph => paragraph.Format.NativeList is not null);
                var loadedNativeRtf = false;
                if (!useNativeRtf)
                {
                    nativeDocument.SetText(TextSetOptions.None, document.Text.Replace('\uFFFC', ' '));
                }
                else
                {
                    // One native RTF load preserves list definitions and inline pictures
                    // without repeatedly shifting or reformatting later ranges.
                    try
                    {
                        var nativeDefaultCharacterFormat = RichTextCharacterFormat.Default with
                        {
                            FontFamily = ResolveFontFamily(),
                            FontSize = VirtualView.FontSize ?? nativeDocument.GetDefaultCharacterFormat().Size,
                            ForegroundColor = ResolveTextColor(),
                        };
                        var imagePositions = document.Images.Select(static image => image.Position).ToHashSet();
                        var literals = document.Text.Select((character, position) => (character, position))
                            .Where(item => item.character == '\uFFFC' && !imagePositions.Contains(item.position))
                            .Select(item => new RichTextImage { Position = item.position, Data = [.. ImagePlaceholder], Width = 12, Height = 12 });
                        var rtf = RtfCodec.SerializeForNativeProjection(
                            document.With(images: document.Images.Select(static image => image.Adornment is null ? image :
                                image with { Width = image.Width * 0.75, Height = image.Height * 0.75 }).Concat(literals)),
                            nativeDefaultCharacterFormat);
                        LoadRtfDocument(nativeDocument, rtf);
                        loadedNativeRtf = true;
                    }
                    catch (Exception exception) when (exception is ArgumentException or COMException)
                    {
                        // Invalid or unsupported native RTF must not take down the editor.
                        // Keep image object characters and list text as plain placeholders.
                        nativeDocument.SetText(TextSetOptions.None, document.Text.Replace('\uFFFC', ' '));
                    }
                }

                var formattingRange = nativeDocument.GetRange(0, 0);
                var repairedObjectPositions = new HashSet<int>();
                if (loadedNativeRtf)
                {
                    // Some RichEdit picture decoders omit unsupported objects entirely. Restore
                    // their slots before using source offsets to replace pictures or apply links.
                    var observed = ReadNativeStoryText(nativeDocument);
                    if (observed.EndsWith('\r'))
                        observed = observed[..^1];

                    observed = observed.Replace('\r', '\n').Replace('\v', RichTextDocument.SoftLineBreakCharacter);
                    if (observed.Length < document.Length && observed.Replace("\uFFFC", "", StringComparison.Ordinal) == document.Text.Replace("\uFFFC", "", StringComparison.Ordinal))
                    {
                        for (var position = 0; position < document.Length; position++)
                        {
                            if (document.Text[position] != '\uFFFC' || nativeDocument.GetRange(position, position + 1).Character == '\uFFFC')
                                continue;

                            nativeDocument.GetRange(position, position).SetText(TextSetOptions.None, " ");
                            _nativeTextSnapshot = null;
                            var first = position;
                            while (first > 0 && document.Text[first - 1] == '\uFFFC')
                                first--;

                            var last = position;
                            while (last + 1 < document.Length && document.Text[last + 1] == '\uFFFC')
                                last++;

                            for (var adjacent = first; adjacent <= last; adjacent++)
                                repairedObjectPositions.Add(adjacent);
                        }
                    }
                }
                if (!loadedNativeRtf)
                {
                    var fullRange = nativeDocument.GetRange(0, document.Text.Length);
                    // Keep the pristine native default separate from the document default.
                    // SetClone from this unhighlighted format is the TOM reset operation;
                    // assigning an alpha-zero BackgroundColor would leave the old highlight.
                    var resetCharacterFormat = nativeDocument.GetDefaultCharacterFormat();
                    var defaultCharacterFormat = resetCharacterFormat.GetClone();
                    ApplyCharacterFormat(defaultCharacterFormat, document.DefaultCharacterFormat);
                    fullRange.CharacterFormat.SetClone(defaultCharacterFormat);
                    var characterFormats = new Dictionary<RichTextCharacterFormat, ITextCharacterFormat>();
                    foreach (var run in document.Runs)
                    {
                        var effectiveFormat = document.ResolveCharacterFormat(run.Format);
                        if (effectiveFormat == document.DefaultCharacterFormat)
                        {
                            continue;
                        }

                        if (!characterFormats.TryGetValue(effectiveFormat, out var nativeFormat))
                        {
                            // Start from the pristine native format so a null/transparent
                            // background actively clears highlighting. Inheritable values
                            // have already been resolved through the document default.
                            nativeFormat = resetCharacterFormat.GetClone();
                            ApplyCharacterFormat(nativeFormat, effectiveFormat);
                            characterFormats.Add(effectiveFormat, nativeFormat);
                        }

                        formattingRange.SetRange(run.Start, run.End);
                        formattingRange.CharacterFormat.SetClone(nativeFormat);
                    }

                    var defaultParagraphFormat = nativeDocument.GetDefaultParagraphFormat().GetClone();
                    ApplyParagraphFormat(defaultParagraphFormat, document.DefaultParagraphFormat);
                    fullRange.ParagraphFormat.SetClone(defaultParagraphFormat);
                    var paragraphFormats = new Dictionary<RichTextParagraphFormat, ITextParagraphFormat>();
                    foreach (var paragraph in document.Paragraphs)
                    {
                        if (paragraph.Format == document.DefaultParagraphFormat)
                        {
                            continue;
                        }

                        if (!paragraphFormats.TryGetValue(paragraph.Format, out var nativeFormat))
                        {
                            nativeFormat = defaultParagraphFormat.GetClone();
                            ApplyParagraphFormat(nativeFormat, paragraph.Format);
                            paragraphFormats.Add(paragraph.Format, nativeFormat);
                        }

                        var end = GetParagraphEnd(document.Text, paragraph.Start);
                        formattingRange.SetRange(paragraph.Start, end);
                        formattingRange.ParagraphFormat.SetClone(nativeFormat);
                    }
                }

                var imagesByPosition = document.Images.ToDictionary(image => image.Position);
                for (var position = 0; position < document.Length; position++)
                {
                    // RichEdit substitutes a space for an unsupported picture or
                    // a literal U+FFFC. Keep a real native object at that position.
                    if (document.Text[position] == '\uFFFC' &&
                        (repairedObjectPositions.Contains(position) || imagesByPosition.GetValueOrDefault(position)?.Adornment?.Options.Baseline is not null ||
                            nativeDocument.GetRange(position, position + 1).Character != '\uFFFC'))
                    {
                        ApplyImageIncrementally(imagesByPosition.GetValueOrDefault(position) ??
                            new RichTextImage { Position = position });
                    }
                }

                foreach (var link in document.Links.OrderByDescending(link => link.Start))
                {
                    formattingRange.SetRange(link.Start, link.End);
                    try
                    {
                        formattingRange.Link = ToNativeLink(link.Target);
                        _hasNativeLinks = true;
                    }
                    catch (Exception exception) when (exception is ArgumentException or COMException)
                    {
                        // Keep the model link when TOM rejects an unsupported target.
                    }
                }

                _nativeTextSnapshot = null;
                SetSelectionCore(selectionStart, selectionLength);
            }
            finally
            {
                nativeDocument.ApplyDisplayUpdates();
            }
        }
        finally
        {
            PlatformView.Document.ClearUndoRedoHistory();
            PlatformView.IsReadOnly = wasReadOnly;
            _applyingDocument = false;
        }
    }

    private partial void ApplyDecorationsCore(RichTextChangeSet changes) =>
        ApplyDisplayChangesCore(changes, VirtualView.SelectedRange, VirtualView.TypingCharacterFormat,
            VirtualView.TypingParagraphFormat, GetPreviousDecorationSnapshot(changes));

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
        if (PlatformView is null)
        {
            return;
        }

        var snapshot = DisplayPresentationSnapshot;
        var affectedRange = changes.GetAffectedRange(snapshot.Length);
        var hasListChanges = changes.Changes.Any(
            static change => change.Kind == RichTextChangeKind.List);
        var requiresRtfImages = changes.Changes.Any(change => change.Kind == RichTextChangeKind.Image) &&
            snapshot.Images.Any(image => image.Position >= affectedRange.Start && image.Position < affectedRange.End &&
                (image.Rotation != 0 || image.Crop != default));
        var bulkImages = snapshot.Images.Count(static image => image.Adornment is null) >
            (changes.BeforeSnapshot?.Images.Count(static image => image.Adornment is null) ?? 0) + 24;
        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset) ||
            _hasNativeLinks && changes.Changes.Any(static change => change.Kind is
                RichTextChangeKind.Text or RichTextChangeKind.CharacterFormat or RichTextChangeKind.Link or RichTextChangeKind.DefaultFormat) ||
            hasListChanges && RequiresRtfListProjection(snapshot, affectedRange) || requiresRtfImages || bulkImages)
        {
            // TOM formatting resets can strand hidden hyperlink instructions.
            // Rebuild linked content atomically, as for custom lists/image geometry.
            ApplyCurrentDocument(selection.Start, selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
            return;
        }

        _applyingDocument = true;
        var wasReadOnly = PlatformView.IsReadOnly;
        var nativeDocument = PlatformView.Document;
        var displayUpdatesBatched = false;
        try
        {
            PlatformView.IsReadOnly = false;
            nativeDocument.BatchDisplayUpdates();
            displayUpdatesBatched = true;
            var loadedImages = new HashSet<int>();
            var loadedRanges = new List<RichTextRange>();
            foreach (var textChange in changes.Changes.OfType<RichTextTextChange>())
            {
                var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
                var range = nativeDocument.GetRange(
                    positions?.ToNativePosition(textChange.OldRange.Start) ?? textChange.OldRange.Start,
                    positions?.ToNativePosition(textChange.OldRange.End) ?? textChange.OldRange.End);
                var images = snapshot.Images.Where(image => image.Position >= textChange.NewRange.Start && image.Position < textChange.NewRange.End).ToArray();
                if (images.Length > 0 && images.All(static image => image.Adornment is { Options.Baseline: null }) &&
                    images.Length == textChange.InsertedText.Count(static character => character == '\uFFFC'))
                {
                    var fragment = RichTextDocumentFragment.FromRange(snapshot, textChange.NewRange).Snapshot;
                    fragment = fragment.With(images: fragment.Images.Select(static image => image with
                    {
                        Width = image.Width * 0.75,
                        Height = image.Height * 0.75
                    }));
                    var nativeDefault = RichTextCharacterFormat.Default with
                    {
                        FontFamily = ResolveFontFamily(),
                        FontSize = VirtualView.FontSize ?? nativeDocument.GetDefaultCharacterFormat().Size,
                        ForegroundColor = ResolveTextColor()
                    };
                    range.SetText(TextSetOptions.FormatRtf, RtfCodec.SerializeForNativeProjection(fragment, nativeDefault));
                    foreach (var image in images)
                        loadedImages.Add(image.Position);

                    loadedRanges.Add(textChange.NewRange);
                }
                else
                    range.SetText(TextSetOptions.None, textChange.InsertedText.Replace('\uFFFC', ' '));

                _nativeTextSnapshot = null;
            }

            if (changes.Changes.Any(static change =>
                change.Kind is RichTextChangeKind.Text or RichTextChangeKind.Image))
            {
                ApplyImagesIncrementally(snapshot, changes, affectedRange, loadedImages);
            }

            var formattingLoaded = loadedRanges.Any(range => changes.Changes.Where(static change => change.Kind is
                RichTextChangeKind.Text or RichTextChangeKind.CharacterFormat or RichTextChangeKind.Image or RichTextChangeKind.DefaultFormat)
                .All(change => change.NewRange.Start >= range.Start && change.NewRange.End <= range.End));
            if (!formattingLoaded && changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.CharacterFormat or
                    RichTextChangeKind.Image or
                    RichTextChangeKind.DefaultFormat))
            {
                ApplyCharacterFormatsIncrementally(
                    snapshot,
                    affectedRange,
                    previousSnapshot);
            }

            var paragraphsLoaded = loadedRanges.Any(range => GetAffectedParagraphRange(range, snapshot.Text) is var paragraphs &&
                changes.Changes.Where(static change => change.Kind is RichTextChangeKind.Text or RichTextChangeKind.ParagraphFormat or
                    RichTextChangeKind.List or RichTextChangeKind.Image or RichTextChangeKind.DefaultFormat)
                    .All(change => change.NewRange.Start >= paragraphs.Start && change.NewRange.End <= paragraphs.End));
            if (!paragraphsLoaded && changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.ParagraphFormat or
                    RichTextChangeKind.List or
                    RichTextChangeKind.Image or
                    RichTextChangeKind.DefaultFormat))
            {
                ApplyParagraphFormatsIncrementally(
                    snapshot,
                    GetAffectedParagraphRange(changes, snapshot.Text));
            }

            if (changes.Changes.Any(static change => change.Kind is RichTextChangeKind.Text or RichTextChangeKind.Link))
            {
                ApplyLinksIncrementally(snapshot, affectedRange);
            }

            _nativeTextSnapshot = null;
            SetSelectionCore(selection.Start, selection.Length);
            ApplyTypingFormatCore(typingCharacterFormat, typingParagraphFormat);
        }
        finally
        {
            try
            {
                if (displayUpdatesBatched)
                {
                    nativeDocument.ApplyDisplayUpdates();
                }
            }
            finally
            {
                try
                {
                    // Only the complete document snapshot owns undo state.
                    nativeDocument.ClearUndoRedoHistory();
                }
                finally
                {
                    PlatformView.IsReadOnly = wasReadOnly;
                    _applyingDocument = false;
                }
            }
        }
    }
}
