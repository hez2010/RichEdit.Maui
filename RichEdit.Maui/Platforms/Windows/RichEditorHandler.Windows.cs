using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Streams;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private static readonly byte[] ImagePlaceholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private bool _applyingDocument;
    private bool _canReadLanguageTag = true;
    private bool _hasCompletedInitialLoad;
    private bool _hasNativeLinks;
    private bool _nativeFormatReadbackQueued;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private NativeTextSnapshot? _nativeTextSnapshot;
    private TextCommandBarFlyout? _contextFlyout;
    private TextCommandBarFlyout? _selectionFlyout;

    /// <inheritdoc />
    protected override RichEditBox CreatePlatformView() => new()
    {
        AcceptsReturn = true,
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
        IsSpellCheckEnabled = true,
        Padding = new Microsoft.UI.Xaml.Thickness(12, 10, 12, 10),
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
    };

    /// <inheritdoc />
    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanging += OnNativeDocumentChanged;
        platformView.SelectionChanged += OnNativeSelectionChanged;
        platformView.ActualThemeChanged += OnPlatformThemeChanged;
        platformView.Loaded += OnPlatformViewLoaded;
        platformView.LostFocus += OnPlatformViewLostFocus;
        platformView.PreviewKeyDown += OnPlatformKeyDown;
        platformView.Paste += OnPlatformPaste;
        platformView.CopyingToClipboard += OnPlatformCopy;
        platformView.CuttingToClipboard += OnPlatformCut;
        platformView.Tapped += OnPlatformTapped;
        _contextFlyout = new TextCommandBarFlyout();
        _selectionFlyout = new TextCommandBarFlyout();
        _contextFlyout.Opening += OnTextFlyoutOpening;
        _selectionFlyout.Opening += OnTextFlyoutOpening;
        platformView.ContextFlyout = _contextFlyout;
        platformView.SelectionFlyout = _selectionFlyout;
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(RichEditBox platformView)
    {
        VirtualView?.Commands.Disconnect();
        platformView.TextChanging -= OnNativeDocumentChanged;
        platformView.SelectionChanged -= OnNativeSelectionChanged;
        platformView.ActualThemeChanged -= OnPlatformThemeChanged;
        platformView.Loaded -= OnPlatformViewLoaded;
        platformView.LostFocus -= OnPlatformViewLostFocus;
        platformView.PreviewKeyDown -= OnPlatformKeyDown;
        platformView.Paste -= OnPlatformPaste;
        platformView.CopyingToClipboard -= OnPlatformCopy;
        platformView.CuttingToClipboard -= OnPlatformCut;
        platformView.Tapped -= OnPlatformTapped;
        if (_contextFlyout is not null)
        {
            _contextFlyout.Opening -= OnTextFlyoutOpening;
        }

        if (_selectionFlyout is not null)
        {
            _selectionFlyout.Opening -= OnTextFlyoutOpening;
        }

        _contextFlyout = null;
        _selectionFlyout = null;
        _hasCompletedInitialLoad = false;
        _hasNativeLinks = false;
        _nativeTextSnapshot = null;

        base.DisconnectHandler(platformView);
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
                    nativeDocument.SetText(TextSetOptions.None, document.Text);
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
                            FontSize = ResolveFontSize(),
                            ForegroundColor = ResolveTextColor(),
                        };
                        var rtf = RtfCodec.SerializeForNativeProjection(
                            document,
                            nativeDefaultCharacterFormat);
                        LoadRtfDocument(nativeDocument, rtf);
                        loadedNativeRtf = true;
                    }
                    catch (Exception exception) when (exception is ArgumentException or COMException)
                    {
                        // Invalid or unsupported native RTF must not take down the editor.
                        // Keep image object characters and list text as plain placeholders.
                        nativeDocument.SetText(TextSetOptions.None, document.Text);
                    }
                }
                var formattingRange = nativeDocument.GetRange(0, 0);
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
                        nativeDocument.GetRange(position, position + 1).Character != '\uFFFC')
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

    private partial void ApplyIncrementalChangesCore(
        RichTextChangeSet changes,
        RichTextRange selection,
        RichTextCharacterFormat typingCharacterFormat,
        RichTextParagraphFormat typingParagraphFormat)
    {
        if (PlatformView is null)
        {
            return;
        }

        var snapshot = VirtualView.Document.CurrentSnapshot;
        var affectedRange = changes.GetAffectedRange(snapshot.Length);
        var hasListChanges = changes.Changes.Any(
            static change => change.Kind == RichTextChangeKind.List);
        var requiresRtfImages = changes.Changes.Any(change => change.Kind == RichTextChangeKind.Image) &&
            snapshot.Images.Any(image => image.Position >= affectedRange.Start && image.Position < affectedRange.End &&
                (image.Rotation != 0 || image.Crop != default));
        if (changes.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset) ||
            _hasNativeLinks && changes.Changes.Any(static change => change.Kind is
                RichTextChangeKind.Text or RichTextChangeKind.CharacterFormat or RichTextChangeKind.Link or RichTextChangeKind.DefaultFormat) ||
            hasListChanges && RequiresRtfListProjection(snapshot, affectedRange) || requiresRtfImages)
        {
            // TOM formatting resets can strand hidden hyperlink instructions.
            // Rebuild linked content atomically, as for custom lists/image geometry.
            ApplyDocumentCore(snapshot, selection.Start, selection.Length);
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
            foreach (var textChange in changes.Changes.OfType<RichTextTextChange>())
            {
                var positions = GetNativeTextSnapshot();
                var range = nativeDocument.GetRange(
                    positions.ToNativePosition(textChange.OldRange.Start),
                    positions.ToNativePosition(textChange.OldRange.End));
                range.SetText(TextSetOptions.None, textChange.InsertedText);
                _nativeTextSnapshot = null;
            }

            if (changes.Changes.Any(static change =>
                    change.Kind is RichTextChangeKind.Text or RichTextChangeKind.Image))
            {
                ApplyImagesIncrementally(snapshot, changes, affectedRange);
            }

            if (changes.Changes.Any(static change => change.Kind is
                    RichTextChangeKind.Text or
                    RichTextChangeKind.CharacterFormat or
                    RichTextChangeKind.Image or
                    RichTextChangeKind.DefaultFormat))
            {
                ApplyCharacterFormatsIncrementally(
                    snapshot,
                    affectedRange);
            }

            if (changes.Changes.Any(static change => change.Kind is
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

    private void ApplyImagesIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextChangeSet changes,
        RichTextRange affectedRange)
    {
        var imageChanges = changes.Changes
            .Where(static change => change.Kind == RichTextChangeKind.Image)
            .ToArray();
        var imagePositions = snapshot.Images.Select(image => image.Position).ToHashSet();
        foreach (var image in snapshot.Images.Where(image =>
                     image.Position >= affectedRange.Start &&
                     image.Position < affectedRange.End))
        {
            ApplyImageIncrementally(image);
        }

        for (var position = affectedRange.Start; position < affectedRange.End; position++)
        {
            if (snapshot.Text[position] == '\uFFFC' && !imagePositions.Contains(position))
            {
                ApplyImageIncrementally(new RichTextImage { Position = position });
            }
        }

        if (changes.IsTextChanged)
        {
            return;
        }

        // An image can be removed while its logical U+FFFC position remains. In
        // that case replace the native object itself with the literal placeholder.
        foreach (var change in imageChanges)
        {
            var range = change.OldRange.Clamp(snapshot.Length);
            for (var position = range.Start; position < range.End; position++)
            {
                if (snapshot.Text[position] == RichTextDocument.ObjectReplacementCharacter &&
                    !imagePositions.Contains(position))
                {
                    ReplaceNativeImageWithPlaceholder(position);
                }
            }
        }
    }

    private void ApplyImageIncrementally(RichTextImage image)
    {
        var positions = GetNativeTextSnapshot();
        var range = PlatformView.Document.GetRange(
            positions.ToNativePosition(image.Position),
            positions.ToNativePosition(image.Position + 1));
        var insertionPosition = range.StartPosition;
        range.SetText(TextSetOptions.None, string.Empty);
        range.SetRange(insertionPosition, insertionPosition);
        var lengthBeforeInsert = range.StoryLength;
        try
        {
            if (!image.Data.IsDefaultOrEmpty)
            {
                InsertImage(ImmutableCollectionsMarshal.AsArray(image.Data)!, image.Width, image.Height);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or COMException)
        {
            // Unsupported payloads remain owned by the managed snapshot.
        }

        if (range.StoryLength == lengthBeforeInsert ||
            PlatformView.Document.GetRange(insertionPosition, insertionPosition + 1).Character != '\uFFFC')
        {
            range.SetRange(insertionPosition, insertionPosition + Math.Max(0, range.StoryLength - lengthBeforeInsert));
            range.SetText(TextSetOptions.None, string.Empty);
            range.SetRange(insertionPosition, insertionPosition);
            InsertImage(ImagePlaceholder, 12, 12);
        }

        _nativeTextSnapshot = null;

        void InsertImage(byte[] data, double width, double height)
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(data);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            stream.Seek(0);
            range.InsertImage(ToNativeImageSize(width), ToNativeImageSize(height), 0,
                image.VerticalAlignment switch
                {
                    RichTextImageVerticalAlignment.Top => VerticalCharacterAlignment.Top,
                    RichTextImageVerticalAlignment.Bottom => VerticalCharacterAlignment.Bottom,
                    _ => VerticalCharacterAlignment.Baseline,
                }, GetWindowsImageAlternativeText(image.AlternativeText), stream);
        }
    }

    private void ReplaceNativeImageWithPlaceholder(int position)
    {
        ApplyImageIncrementally(new RichTextImage { Position = position });
    }

    private static int ToNativeImageSize(double points)
    {
        if (points <= 0)
        {
            return 0;
        }

        // WinUI's projected ITextRange API takes DIPs, while the document model
        // uses the RTF unit of points.
        var deviceIndependentPixels = Math.Round(points * 96d / 72d);
        return (int)Math.Clamp(deviceIndependentPixels, 1d, int.MaxValue);
    }

    // WinUI returns E_INVALIDARG for an empty alternate-text HSTRING. Use the
    // same neutral character that represents the inline object in logical text.
    internal static string GetWindowsImageAlternativeText(string? alternativeText) =>
        string.IsNullOrEmpty(alternativeText)
            ? RichTextDocument.ObjectReplacementCharacter.ToString()
            : alternativeText;

    private void ApplyCharacterFormatsIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange)
    {
        if (snapshot.Length == 0 || affectedRange.IsEmpty)
        {
            return;
        }

        var positions = GetNativeTextSnapshot();
        var nativeDocument = PlatformView.Document;
        var reset = nativeDocument.GetDefaultCharacterFormat();
        for (var index = snapshot.FindRunIndex(affectedRange.Start);
             index < snapshot.Runs.Length;
             index++)
        {
            var run = snapshot.Runs[index];
            if (run.Start >= affectedRange.End)
            {
                break;
            }

            var start = Math.Max(run.Start, affectedRange.Start);
            var end = Math.Min(run.End, affectedRange.End);
            if (end <= start)
            {
                continue;
            }

            var nativeFormat = reset.GetClone();
            ApplyCharacterFormat(nativeFormat, snapshot.ResolveCharacterFormat(run.Format));
            var nativeRange = nativeDocument.GetRange(
                positions.ToNativePosition(start),
                positions.ToNativePosition(end));
            nativeRange.CharacterFormat.SetClone(nativeFormat);
        }
    }

    private void ApplyParagraphFormatsIncrementally(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange)
    {
        var positions = GetNativeTextSnapshot();
        var nativeDocument = PlatformView.Document;
        var reset = nativeDocument.GetDefaultParagraphFormat();
        for (var index = snapshot.FindParagraphIndex(affectedRange.Start);
             index < snapshot.Paragraphs.Length;
             index++)
        {
            var paragraph = snapshot.Paragraphs[index];
            if (paragraph.Range.Start > affectedRange.End)
            {
                break;
            }

            if (paragraph.Range.End < affectedRange.Start)
            {
                continue;
            }

            var end = GetParagraphEnd(snapshot.Text, paragraph.Start);
            var nativeFormat = reset.GetClone();
            ApplyParagraphFormat(nativeFormat, paragraph.Format);
            var nativeRange = nativeDocument.GetRange(
                positions.ToNativePosition(paragraph.Start),
                positions.ToNativePosition(end));
            nativeRange.ParagraphFormat.SetClone(nativeFormat);
        }
    }

    private void ApplyLinksIncrementally(RichTextDocumentSnapshot snapshot, RichTextRange affectedRange)
    {
        var nativeDocument = PlatformView.Document;
        foreach (var link in snapshot.Links.Where(link =>
                     link.End > affectedRange.Start && link.Start < affectedRange.End))
        {
            var positions = GetNativeTextSnapshot();
            var range = nativeDocument.GetRange(
                positions.ToNativePosition(link.Start),
                positions.ToNativePosition(link.End));
            try
            {
                range.Link = ToNativeLink(link.Target);
            }
            catch (Exception exception) when (exception is ArgumentException or COMException)
            {
                // Preserve the managed link when TOM rejects its target.
            }

            _nativeTextSnapshot = null;
        }

        _hasNativeLinks = snapshot.Links.Length != 0;
    }

    private static bool RequiresRtfListProjection(
        RichTextDocumentSnapshot snapshot,
        RichTextRange affectedRange)
    {
        var paragraphRange = GetAffectedParagraphRange(affectedRange, snapshot.Text);
        return snapshot.Paragraphs.Any(paragraph =>
            (paragraphRange.IsEmpty
                ? paragraph.Start == paragraphRange.Start
                : paragraph.Start >= paragraphRange.Start &&
                    paragraph.Start < paragraphRange.End) &&
            paragraph.Format.NativeList is { } list &&
            !CanProjectListWithTom(list));
    }

    private static bool CanProjectListWithTom(RichTextListFormat list)
    {
        if (list.PictureId is not null || !string.IsNullOrEmpty(list.Prefix))
        {
            return false;
        }

        return list.Kind == RichListKind.Bulleted
            ? string.Equals(list.BulletText, "•", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(list.Suffix)
            : list.Suffix is "." or ")" or "-" or "";
    }

    private static RichTextRange GetAffectedRange(
        RichTextChangeSet changes,
        int documentLength) =>
        changes.GetAffectedRange(documentLength);

    private static RichTextRange GetAffectedParagraphRange(
        RichTextChangeSet changes,
        string text)
    {
        return GetAffectedParagraphRange(GetAffectedRange(changes, text.Length), text);
    }

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

    private partial void ApplyTypingFormatCore(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _nativeTypingFormat = characterFormat;
        _nativeTypingParagraphFormat = paragraphFormat;
        if (PlatformView is null || PlatformView.Document.Selection.Length != 0)
        {
            return;
        }

        var wasApplyingDocument = _applyingDocument;
        _applyingDocument = true;
        try
        {
            var nativeDocument = PlatformView.Document;
            var nativeCharacterFormat = nativeDocument.GetDefaultCharacterFormat().GetClone();
            ApplyCharacterFormat(
                nativeCharacterFormat,
                VirtualView.Document.CurrentSnapshot.ResolveCharacterFormat(characterFormat));
            nativeDocument.Selection.CharacterFormat.SetClone(nativeCharacterFormat);
            var nativeParagraphFormat = nativeDocument.GetDefaultParagraphFormat().GetClone();
            ApplyParagraphFormat(nativeParagraphFormat, paragraphFormat);
            nativeDocument.Selection.ParagraphFormat.SetClone(nativeParagraphFormat);
        }
        finally
        {
            PlatformView.Document.ClearUndoRedoHistory();
            _applyingDocument = wasApplyingDocument;
        }
    }

    private static void LoadRtfDocument(RichEditTextDocument nativeDocument, string rtf)
    {
        // RichEdit consumes the last paragraph mark as its mandatory story
        // terminator. Supply it separately from logical trailing paragraph breaks.
        rtf = rtf.Insert(rtf.Length - 1, @"\par");
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(Encoding.ASCII.GetBytes(rtf));
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }

        stream.Seek(0);
        nativeDocument.LoadFromStream(TextSetOptions.FormatRtf, stream);
    }

    private partial void SetSelectionCore(int start, int length)
    {
        if (PlatformView is null)
        {
            return;
        }

        var textLength = VirtualView?.Document.Text.Length ?? 0;
        start = Math.Clamp(start, 0, textLength);
        length = Math.Clamp(length, 0, textLength - start);
        if (_hasNativeLinks)
        {
            var snapshot = GetNativeTextSnapshot();
            PlatformView.Document.Selection.SetRange(
                snapshot.ToNativePosition(start),
                snapshot.ToNativePosition(start + length));
        }
        else
        {
            PlatformView.Document.Selection.SetRange(start, start + length);
        }
    }

    private partial bool SupportsNativeUndoCore() => false;

    private partial bool CanUndoCore() => VirtualView?.Document.CanUndo == true;

    private partial bool CanRedoCore() => VirtualView?.Document.CanRedo == true;

    private partial void UndoCore()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Undo();
        }
    }

    private partial void RedoCore()
    {
        if (VirtualView is { IsReadOnly: false })
        {
            VirtualView.Redo();
        }
    }

    private partial void ClearUndoHistoryCore()
    {
        PlatformView?.Document.ClearUndoRedoHistory();
        VirtualView?.UpdateUndoStateFromPlatform();
    }

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.PlaceholderProperty))
        {
            PlatformView.PlaceholderText = editor.Placeholder;
        }

        if (editor.IsSet(RichEditor.PlaceholderColorProperty))
        {
            if (editor.PlaceholderColor is { } placeholderColor)
            {
                PlatformView.Resources["TextControlPlaceholderForeground"] =
                    placeholderColor.ToPlatform();
            }
            else
            {
                PlatformView.Resources.Remove("TextControlPlaceholderForeground");
            }
        }
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        if (editor.IsSet(RichEditor.TextColorProperty) && editor.TextColor is { } textColor)
        {
            PlatformView.Foreground = textColor.ToPlatform();
        }
        else if (editor.IsSet(RichEditor.TextColorProperty))
        {
            PlatformView.ClearValue(Microsoft.UI.Xaml.Controls.Control.ForegroundProperty);
        }

        if (editor.IsSet(RichEditor.FontFamilyProperty))
        {
            if (string.IsNullOrWhiteSpace(editor.FontFamily))
            {
                PlatformView.ClearValue(Microsoft.UI.Xaml.Controls.Control.FontFamilyProperty);
            }
            else
            {
                PlatformView.FontFamily = new FontFamily(editor.FontFamily);
            }
        }

        if (editor.IsSet(RichEditor.FontSizeProperty))
        {
            if (editor.FontSize is { } fontSize)
            {
                PlatformView.FontSize = fontSize;
            }
            else
            {
                PlatformView.ClearValue(Microsoft.UI.Xaml.Controls.Control.FontSizeProperty);
            }
        }

        if (!_applyingDocument)
        {
            if (_hasNativeLinks)
            {
                ApplyDocumentCore(editor.Document.CurrentSnapshot, editor.SelectedRange.Start, editor.SelectedRange.Length);
                ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
                return;
            }

            _applyingDocument = true;
            var nativeDocument = PlatformView.Document;
            nativeDocument.BatchDisplayUpdates();
            try
            {
                ApplyCharacterFormatsIncrementally(
                    editor.Document.CurrentSnapshot,
                    new RichTextRange(0, editor.Document.Length));
                SetSelectionCore(editor.SelectedRange.Start, editor.SelectedRange.Length);
            }
            finally
            {
                nativeDocument.ApplyDisplayUpdates();
                nativeDocument.ClearUndoRedoHistory();
                _applyingDocument = false;
            }

            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
    }

    private partial void UpdateInputConfiguration(RichEditor editor)
    {
        PlatformView.IsReadOnly = editor.IsReadOnly;
        PlatformView.IsSpellCheckEnabled = editor.IsSpellCheckEnabled;
        PlatformView.IsTextPredictionEnabled = editor.IsTextPredictionEnabled;
        PlatformView.AcceptsReturn = true;
        PlatformView.MaxLength = editor.MaxLength < 0 ? 0 : editor.MaxLength;
        var scope = ReferenceEquals(editor.Keyboard, Keyboard.Numeric)
            ? InputScopeNameValue.Number
            : ReferenceEquals(editor.Keyboard, Keyboard.Telephone)
                ? InputScopeNameValue.TelephoneNumber
                : ReferenceEquals(editor.Keyboard, Keyboard.Email)
                    ? InputScopeNameValue.EmailSmtpAddress
                    : ReferenceEquals(editor.Keyboard, Keyboard.Url)
                        ? InputScopeNameValue.Url
                        : InputScopeNameValue.Default;
        PlatformView.InputScope = new InputScope
        {
            Names = { new InputScopeName(scope) },
        };
    }

    private void OnPlatformKeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (VirtualView is null)
        {
            return;
        }

        if (IsControlKeyDown() && !IsAltKeyDown())
        {
            var clipboardCommand = eventArgs.Key switch
            {
                Windows.System.VirtualKey.C or Windows.System.VirtualKey.Insert => VirtualView.Commands.Copy,
                Windows.System.VirtualKey.X => VirtualView.Commands.Cut,
                Windows.System.VirtualKey.V => VirtualView.Commands.Paste,
                _ => null,
            };
            if (clipboardCommand is not null)
            {
                if (clipboardCommand.CanExecute(null))
                {
                    clipboardCommand.Execute(null);
                }

                eventArgs.Handled = true;
                return;
            }
        }

        if (IsControlKeyDown() && !IsAltKeyDown() && !VirtualView.IsReadOnly)
        {
            if (eventArgs.Key == Windows.System.VirtualKey.Z)
            {
                if (IsShiftKeyDown())
                {
                    RedoCore();
                }
                else
                {
                    UndoCore();
                }

                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Windows.System.VirtualKey.Y)
            {
                RedoCore();
                eventArgs.Handled = true;
                return;
            }
        }

        if (eventArgs.Key == Windows.System.VirtualKey.Tab &&
            VirtualView.AcceptsTab && !VirtualView.IsReadOnly &&
            !IsControlKeyDown() && !IsAltKeyDown() && !IsShiftKeyDown())
        {
            if (VirtualView.MaxLength < 0 ||
                VirtualView.Document.Length - VirtualView.SelectedRange.Length < VirtualView.MaxLength)
            {
                VirtualView.Selection.ReplaceText("\t");
            }
            eventArgs.Handled = true;
        }
    }

    private async void OnPlatformPaste(object sender, TextControlPasteEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null || VirtualView.IsReadOnly)
        {
            return;
        }

        // Route every user paste through the portable fragment and cancellable
        // Pasting event. The complete fragment becomes one document undo unit.
        eventArgs.Handled = true;
        await RichEditorCommands.ExecuteAsync(VirtualView.PasteAsync);
    }

    private async void OnPlatformCopy(RichEditBox sender, TextControlCopyingToClipboardEventArgs args)
    {
        args.Handled = true;
        await RichEditorCommands.ExecuteAsync(VirtualView.CopyAsync);
    }

    private async void OnPlatformCut(RichEditBox sender, TextControlCuttingToClipboardEventArgs args)
    {
        args.Handled = true;
        await RichEditorCommands.ExecuteAsync(VirtualView.CutAsync);
    }

    private void OnTextFlyoutOpening(object? sender, object args)
    {
        if (sender is not TextCommandBarFlyout flyout || VirtualView is null)
        {
            return;
        }

        ConfigureTextFlyoutCommands(flyout);
    }

    internal void ConfigureTextFlyoutCommands(TextCommandBarFlyout flyout)
    {
        // Native flyout copy/cut call TOM directly and bypass the control's
        // clipboard events. Keep native formatting/proofing, but route editing
        // commands through the portable clipboard and snapshot history.
        var kinds = new HashSet<StandardUICommandKind>();
        foreach (var button in flyout.PrimaryCommands.Concat(flyout.SecondaryCommands).OfType<AppBarButton>())
        {
            if (button.Command is StandardUICommand native && GetCommand(native.Kind) is { } command)
            {
                kinds.Add(native.Kind);
                BindCommand(button, native.Kind, command);
            }
        }

        AddCommand(StandardUICommandKind.Undo, VirtualView.Commands.Undo);
        AddCommand(StandardUICommandKind.Redo, VirtualView.Commands.Redo);
        AddCommand(StandardUICommandKind.Paste, VirtualView.Commands.Paste);

        System.Windows.Input.ICommand? GetCommand(StandardUICommandKind kind) => kind switch
        {
            StandardUICommandKind.Copy => VirtualView.Commands.Copy,
            StandardUICommandKind.Cut => VirtualView.Commands.Cut,
            StandardUICommandKind.Paste => VirtualView.Commands.Paste,
            StandardUICommandKind.Undo => VirtualView.Commands.Undo,
            StandardUICommandKind.Redo => VirtualView.Commands.Redo,
            _ => null,
        };

        void BindCommand(AppBarButton button, StandardUICommandKind kind, System.Windows.Input.ICommand command)
        {
            var nativeCommand = new StandardUICommand { Kind = kind };
            nativeCommand.CanExecuteRequested += (_, args) => args.CanExecute = command.CanExecute(null);
            nativeCommand.ExecuteRequested += (_, _) =>
            {
                if (command.CanExecute(null))
                {
                    command.Execute(null);
                }

                flyout.Hide();
            };
            button.Command = nativeCommand;
        }

        void AddCommand(StandardUICommandKind kind, System.Windows.Input.ICommand command)
        {
            if (!kinds.Contains(kind) && command.CanExecute(null))
            {
                var button = new AppBarButton();
                BindCommand(button, kind, command);
                flyout.SecondaryCommands.Add(button);
            }
        }
    }

    private void OnPlatformTapped(object sender, TappedRoutedEventArgs eventArgs)
    {
        if (VirtualView is null)
        {
            return;
        }

        var point = eventArgs.GetPosition(PlatformView);
        ITextRange nativeRange;
        try
        {
            nativeRange = PlatformView.Document.GetRangeFromPoint(
                new Windows.Foundation.Point(point.X, point.Y),
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
        }
        catch (COMException)
        {
            return;
        }

        var position = Math.Min(nativeRange.StartPosition, nativeRange.EndPosition);
        if (_hasNativeLinks)
        {
            position = GetNativeTextSnapshot().ToLogicalPosition(position);
        }

        position = Math.Clamp(position, 0, VirtualView.Document.Length);
        var snapshot = VirtualView.Document.CurrentSnapshot;
        var image = snapshot.Images.FirstOrDefault(candidate => candidate.Position == position);
        if (image is not null)
        {
            eventArgs.Handled = !VirtualView.RaiseInlineObjectInvoked(image);
            return;
        }

        var controlDown = IsControlKeyDown();
        if (!VirtualView.IsReadOnly && !controlDown)
        {
            return;
        }

        var link = snapshot.Links.FirstOrDefault(candidate =>
            candidate.Start <= position && position < candidate.End);
        if (link is not null)
        {
            eventArgs.Handled = !VirtualView.RaiseLinkInvoked(link);
        }
    }

    private static bool IsControlKeyDown() =>
        (GetNativeKeyState(VirtualKeyControl) & KeyPressedMask) != 0;

    private static bool IsAltKeyDown() => (GetNativeKeyState(0x12) & KeyPressedMask) != 0;

    private static bool IsShiftKeyDown() =>
        (GetNativeKeyState(VirtualKeyShift) & KeyPressedMask) != 0;

    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyShift = 0x10;
    private const int KeyPressedMask = 0x8000;

    [DllImport("user32.dll", EntryPoint = "GetKeyState")]
    private static extern short GetNativeKeyState(int virtualKey);

    private void ApplyCharacterFormat(
        ITextCharacterFormat native,
        RichTextCharacterFormat format)
    {
        if ((format.FontFamily ?? VirtualView.FontFamily) is { } fontFamily)
        {
            native.Name = fontFamily;
        }

        if ((format.FontSize ?? VirtualView.FontSize) is { } fontSize)
        {
            native.Size = (float)fontSize;
        }
        native.Weight = format.FontWeight;
        native.Italic = format.Italic ? FormatEffect.On : FormatEffect.Off;
        native.Underline = ToNativeUnderline(format.Underline);
        native.Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None
            ? FormatEffect.Off
            : FormatEffect.On;
        if ((format.ForegroundColor ?? ResolveTextColor()) is { } foregroundColor)
        {
            native.ForegroundColor = ToWindowsColor(foregroundColor);
        }
        // Each caller supplies a pristine default-format clone. Leaving its background
        // untouched and later using SetClone is the WinUI reset path; RichEdit ignores
        // alpha on text backgrounds.
        if (format.BackgroundColor is { Alpha: > 0 } backgroundColor)
        {
            native.BackgroundColor = ToWindowsColor(backgroundColor);
        }

        native.Position = (float)format.BaselineOffset;
        // TOM treats subscript and superscript as mutually exclusive effects.
        // Clear the opposite effect first so that the requested effect is the
        // final write instead of immediately being cancelled.
        switch (format.Script)
        {
            case RichTextScript.Subscript:
                native.Superscript = FormatEffect.Off;
                native.Subscript = FormatEffect.On;
                break;
            case RichTextScript.Superscript:
                native.Subscript = FormatEffect.Off;
                native.Superscript = FormatEffect.On;
                break;
            default:
                native.Subscript = FormatEffect.Off;
                native.Superscript = FormatEffect.Off;
                break;
        }

        native.Spacing = (float)format.CharacterSpacing;
        native.FontStretch = ToNativeFontStretch(format.HorizontalScale);
        native.SmallCaps = format.SmallCaps ? FormatEffect.On : FormatEffect.Off;
        native.AllCaps = format.AllCaps ? FormatEffect.On : FormatEffect.Off;
        native.Outline = format.Outline ? FormatEffect.On : FormatEffect.Off;
        native.Hidden = format.Hidden ? FormatEffect.On : FormatEffect.Off;
        if (!string.IsNullOrWhiteSpace(format.LanguageTag))
        {
            native.LanguageTag = format.LanguageTag;
        }

        if (format.Kerning != RichTextFeatureMode.Automatic)
        {
            native.Kerning = format.Kerning == RichTextFeatureMode.Enabled ? 1f : 0f;
        }
    }

    private static void ApplyParagraphFormat(
        ITextParagraphFormat native,
        RichTextParagraphFormat format)
    {
        native.Alignment = format.Alignment switch
        {
            RichTextAlignment.Center => ParagraphAlignment.Center,
            RichTextAlignment.Right => ParagraphAlignment.Right,
            RichTextAlignment.Justified or RichTextAlignment.Distributed => ParagraphAlignment.Justify,
            _ => ParagraphAlignment.Left,
        };
        native.RightToLeft = format.Direction == RichTextDirection.RightToLeft
            ? FormatEffect.On
            : FormatEffect.Off;
        native.SetIndents(
            (float)format.FirstLineIndent,
            (float)format.LeadingIndent,
            (float)format.TrailingIndent);
        native.SpaceBefore = (float)format.SpaceBefore;
        native.SpaceAfter = (float)format.SpaceAfter;
        native.SetLineSpacing(ToNativeLineSpacing(format.LineSpacingRule), (float)format.LineSpacing);
        native.ClearAllTabs();
        var tabCount = 0;
        foreach (var tab in format.TabStops)
        {
            if (!TryConvertWindowsTabPosition(tab.Position, out var position))
            {
                continue;
            }

            native.AddTab(
                position,
                ToNativeTabAlignment(tab.Alignment),
                ToNativeTabLeader(tab.Leader));
            if (++tabCount == 63)
            {
                break;
            }
        }

        if (format.NativeList is not { } list)
        {
            native.ListType = MarkerType.None;
            native.ListLevelIndex = 0;
            return;
        }

        native.ListType = list.Kind == RichListKind.Bulleted
            ? MarkerType.Bullet
            : list.NumberStyle switch
            {
                RichListNumberStyle.UpperRoman => MarkerType.UppercaseRoman,
                RichListNumberStyle.LowerRoman => MarkerType.LowercaseRoman,
                RichListNumberStyle.UpperLetter => MarkerType.UppercaseEnglishLetter,
                RichListNumberStyle.LowerLetter => MarkerType.LowercaseEnglishLetter,
                _ => MarkerType.Arabic,
            };
        native.ListStyle = list.Kind == RichListKind.Bulleted
            ? MarkerStyle.Plain
            : ToNativeListStyle(list.Suffix);
        native.ListStart = list.StartAt;
        native.ListLevelIndex = list.Level + 1;
    }

    private RichTextDocumentSnapshot ReadDocumentFromPlatform()
    {
        var snapshot = GetNativeTextSnapshot();
        var text = snapshot.Text;
        var nativeDocument = PlatformView.Document;
        var previous = VirtualView.Document.CurrentSnapshot;
        var remappedPrevious = previous.RemapText(text, VirtualView.SelectedRange);
        var defaultCharacterFormat = ReadCharacterFormat(
            nativeDocument.GetDefaultCharacterFormat()) with
        {
            FontFamily = previous.DefaultCharacterFormat.FontFamily,
            FontSize = previous.DefaultCharacterFormat.FontSize,
            ForegroundColor = previous.DefaultCharacterFormat.ForegroundColor,
        };
        var defaultParagraphFormat = ReadParagraphFormat(
            nativeDocument.GetDefaultParagraphFormat(), null);
        var scanRange = nativeDocument.GetRange(0, 0);
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
            scanRange.SetRange(
                snapshot.ToNativePosition(start),
                snapshot.ToNativePosition(end));
            var native = scanRange.ParagraphFormat;
            var paragraphFormat = ReadParagraphFormat(native, previousList);
            if (paragraphFormat.NativeList is { } list)
            {
                var continues = previousList is not null &&
                    previousList.Kind == list.Kind &&
                    previousList.NumberStyle == list.NumberStyle &&
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
                paragraphFormat = paragraphFormat with { NativeList = list };
                previousList = list;
            }
            else
            {
                previousList = null;
            }

            paragraphs.Add(new RichTextParagraph(start, paragraphFormat));
            var newline = text.IndexOf('\n', start);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }

        var fontFamily = previous.DefaultCharacterFormat.FontFamily ?? ResolveFontFamily();
        var fontSize = previous.DefaultCharacterFormat.FontSize ?? ResolveFontSize();
        var textColor = previous.DefaultCharacterFormat.ForegroundColor ?? ResolveTextColor();
        return previous.MergeNativeSnapshot(
            text,
            snapshot.Runs,
            paragraphs,
            null,
            null,
            defaultCharacterFormat,
            defaultParagraphFormat,
            (native, prior) => MergeWindowsCharacterFormat(
                native,
                prior,
                fontFamily,
                fontSize,
                textColor),
            MergeWindowsParagraphFormat,
            remappedPrevious);
    }

    private NativeTextSnapshot GetNativeTextSnapshot() =>
        _nativeTextSnapshot ??= CreateNativeTextSnapshot();

    private NativeTextSnapshot CreateNativeTextSnapshot()
    {
        var nativeDocument = PlatformView.Document;
        var rawText = ReadNativeStoryText(nativeDocument);
        var nativeContentLength = rawText.EndsWith('\r')
            ? rawText.Length - 1
            : rawText.Length;
        var text = new StringBuilder(nativeContentLength);
        var nativeToLogical = new int[rawText.Length + 1];
        var logicalToNative = new List<int>(nativeContentLength + 1);
        var runs = new List<RichTextRun>();
        var scanRange = nativeDocument.GetRange(0, 0);
        for (var nativePosition = 0; nativePosition < nativeContentLength;)
        {
            scanRange.SetRange(nativePosition, nativePosition + 1);
            scanRange.Expand(TextRangeUnit.CharacterFormat);
            var nativeEnd = Math.Clamp(
                scanRange.EndPosition,
                nativePosition + 1,
                nativeContentLength);
            var characterFormat = scanRange.CharacterFormat;
            var omitFromLogicalText = IsHiddenLinkInstruction(scanRange, characterFormat);
            var logicalStart = text.Length;
            for (var position = nativePosition; position < nativeEnd; position++)
            {
                nativeToLogical[position] = text.Length;
                if (!omitFromLogicalText)
                {
                    logicalToNative.Add(position);
                    text.Append(rawText[position] switch
                    {
                        '\r' => '\n',
                        '\v' => RichTextDocument.SoftLineBreakCharacter,
                        var character => character,
                    });
                }

                nativeToLogical[position + 1] = text.Length;
            }

            if (!omitFromLogicalText && text.Length > logicalStart)
            {
                runs.Add(new RichTextRun(
                    logicalStart,
                    text.Length - logicalStart,
                    ReadCharacterFormat(characterFormat)));
            }

            nativePosition = nativeEnd;
        }

        for (var position = nativeContentLength; position < nativeToLogical.Length; position++)
        {
            nativeToLogical[position] = text.Length;
        }

        logicalToNative.Add(nativeContentLength);
        return new NativeTextSnapshot(
            text.ToString(),
            [.. runs],
            nativeToLogical,
            [.. logicalToNative]);
    }

    private static bool IsHiddenLinkInstruction(
        ITextRange range,
        ITextCharacterFormat format)
    {
        if (format.Hidden != FormatEffect.On)
        {
            return false;
        }

        try
        {
            return !string.IsNullOrWhiteSpace(range.Link);
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string ToNativeLink(string target) =>
        $"\"{target.Replace("\"", "%22", StringComparison.Ordinal)}\"";

    private RichTextCharacterFormat ReadCharacterFormat(ITextCharacterFormat native) => new()
    {
        FontFamily = string.IsNullOrWhiteSpace(native.Name) ? null : native.Name,
        FontSize = native.Size > 0 ? native.Size : null,
        FontWeight = Math.Clamp(native.Weight, 1, 1000),
        Italic = native.Italic == FormatEffect.On,
        Underline = FromNativeUnderline(native.Underline),
        Strikethrough = native.Strikethrough == FormatEffect.On
            ? RichTextStrikethroughStyle.Single
            : RichTextStrikethroughStyle.None,
        ForegroundColor = FromWindowsColor(native.ForegroundColor),
        BackgroundColor = FromWindowsColor(native.BackgroundColor, transparentAsNull: true),
        Script = native.Superscript == FormatEffect.On
            ? RichTextScript.Superscript
            : native.Subscript == FormatEffect.On
                ? RichTextScript.Subscript
                : RichTextScript.Normal,
        BaselineOffset = native.Position,
        CharacterSpacing = native.Spacing,
        HorizontalScale = FromNativeFontStretch(native.FontStretch),
        SmallCaps = native.SmallCaps == FormatEffect.On,
        AllCaps = native.AllCaps == FormatEffect.On,
        Outline = native.Outline == FormatEffect.On,
        Hidden = native.Hidden == FormatEffect.On,
        LanguageTag = ReadLanguageTag(native),
        Kerning = native.Kerning > 0
            ? RichTextFeatureMode.Enabled
            : RichTextFeatureMode.Disabled,
    };

    private string? ReadLanguageTag(ITextCharacterFormat native)
    {
        if (!_canReadLanguageTag)
        {
            return null;
        }

        try
        {
            var languageTag = native.LanguageTag;
            return string.IsNullOrWhiteSpace(languageTag) ? null : languageTag;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // RichEdit can report E_FAIL for range language tags. Do not pay the
            // exception cost again for every character in the document snapshot.
            _canReadLanguageTag = false;
            return null;
        }
    }

    internal static RichTextCharacterFormat MergeWindowsCharacterFormat(
        RichTextCharacterFormat native,
        RichTextCharacterFormat previous) =>
        MergeWindowsCharacterFormat(native, previous, null, null, null);

    private static RichTextCharacterFormat MergeWindowsCharacterFormat(
        RichTextCharacterFormat native,
        RichTextCharacterFormat previous,
        string? fontFamily,
        double? fontSize,
        Color? textColor)
    {
        var horizontalScale = ToNativeFontStretch(native.HorizontalScale) ==
            ToNativeFontStretch(previous.HorizontalScale)
                ? previous.HorizontalScale
                : native.HorizontalScale;
        return native with
        {
            FontFamily = previous.FontFamily is null &&
                string.Equals(native.FontFamily, fontFamily, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : native.FontFamily,
            FontSize = previous.FontSize is null &&
                native.FontSize is { } nativeFontSize &&
                fontSize is not null &&
                Math.Abs(nativeFontSize - fontSize.Value) < 0.01d
                    ? null
                    : native.FontSize,
            ForegroundColor = previous.ForegroundColor is null &&
                native.ForegroundColor is { } nativeForeground &&
                textColor is not null &&
                ToWindowsColor(nativeForeground).Equals(ToWindowsColor(textColor))
                    ? null
                    : native.ForegroundColor,
            UnderlineColor = previous.UnderlineColor,
            Strikethrough = native.Strikethrough != RichTextStrikethroughStyle.None &&
                previous.Strikethrough == RichTextStrikethroughStyle.Double
                    ? RichTextStrikethroughStyle.Double
                    : native.Strikethrough,
            StrikethroughColor = previous.StrikethroughColor,
            HorizontalScale = horizontalScale,
            Shadow = previous.Shadow,
            LanguageTag = native.LanguageTag ?? previous.LanguageTag,
            Direction = previous.Direction,
            Kerning = previous.Kerning == RichTextFeatureMode.Automatic
                ? RichTextFeatureMode.Automatic
                : native.Kerning,
            Ligatures = previous.Ligatures,
            Shading = previous.Shading,
            ShadingForegroundColor = previous.ShadingForegroundColor,
            ShadingBackgroundColor = previous.ShadingBackgroundColor,
            StyleName = previous.StyleName,
        };
    }

    internal static RichTextParagraphFormat MergeWindowsParagraphFormat(
        RichTextParagraphFormat native,
        RichTextParagraphFormat previous)
    {
        var alignment = native.Alignment == RichTextAlignment.Justified &&
            previous.Alignment == RichTextAlignment.Distributed
                ? RichTextAlignment.Distributed
                : native.Alignment;
        var direction = native.Direction == RichTextDirection.LeftToRight &&
            previous.Direction == RichTextDirection.Automatic
                ? RichTextDirection.Automatic
                : native.Direction;
        RichTextListFormat? list = native.NativeList;
        if (list is not null && previous.NativeList is { } previousList)
        {
            list = list with
            {
                Id = previousList.Id,
                Restart = previousList.Restart,
                Prefix = previousList.Prefix,
                Suffix = HasEquivalentWindowsListSuffix(list.Suffix, previousList.Suffix)
                    ? previousList.Suffix
                    : list.Suffix,
                BulletText = previousList.BulletText,
                PictureId = list.Kind == RichListKind.Bulleted
                    ? previousList.PictureId
                    : null,
            };
        }

        return native with
        {
            Alignment = alignment,
            Direction = direction,
            MinimumLineHeight = previous.MinimumLineHeight,
            MaximumLineHeight = previous.MaximumLineHeight,
            Hyphenation = previous.Hyphenation,
            BackgroundColor = previous.BackgroundColor,
            Shading = previous.Shading,
            ShadingForegroundColor = previous.ShadingForegroundColor,
            ShadingBackgroundColor = previous.ShadingBackgroundColor,
            Border = previous.Border,
            StyleName = previous.StyleName,
            NativeList = list,
        };
    }

    internal static bool TryConvertWindowsTabPosition(double position, out float nativePosition)
    {
        nativePosition = (float)position;
        return nativePosition > 0 && float.IsFinite(nativePosition);
    }

    private static bool HasEquivalentWindowsListSuffix(string first, string second) =>
        ToNativeListStyle(first) == ToNativeListStyle(second);

    private static MarkerStyle ToNativeListStyle(string suffix) => suffix switch
    {
        ")" => MarkerStyle.Parenthesis,
        "-" => MarkerStyle.Minus,
        "" => MarkerStyle.Plain,
        _ => MarkerStyle.Period,
    };

    private static RichTextParagraphFormat ReadParagraphFormat(
        ITextParagraphFormat native,
        RichTextListFormat? previousList)
    {
        var tabs = ImmutableArray.CreateBuilder<RichTextTabStop>(Math.Max(native.TabCount, 0));
        for (var index = 0; index < native.TabCount; index++)
        {
            native.GetTab(index, out var position, out var alignment, out var leader);
            if (position > 0 && float.IsFinite(position))
            {
                tabs.Add(new RichTextTabStop(
                    position,
                    FromNativeTabAlignment(alignment),
                    FromNativeTabLeader(leader)));
            }
        }

        RichTextListFormat? list = null;
        if (native.ListType is not (MarkerType.None or MarkerType.Undefined))
        {
            var kind = native.ListType is MarkerType.Bullet or
                MarkerType.BlackCircleWingding or MarkerType.WhiteCircleWingding
                ? RichListKind.Bulleted
                : RichListKind.Numbered;
            list = new RichTextListFormat
            {
                Id = previousList?.Id ?? 1,
                Level = Math.Max(native.ListLevelIndex - 1, 0),
                Kind = kind,
                NumberStyle = native.ListType switch
                {
                    MarkerType.UppercaseRoman => RichListNumberStyle.UpperRoman,
                    MarkerType.LowercaseRoman => RichListNumberStyle.LowerRoman,
                    MarkerType.UppercaseEnglishLetter => RichListNumberStyle.UpperLetter,
                    MarkerType.LowercaseEnglishLetter => RichListNumberStyle.LowerLetter,
                    _ => RichListNumberStyle.Arabic,
                },
                StartAt = Math.Max(native.ListStart, 1),
                Suffix = native.ListStyle switch
                {
                    MarkerStyle.Parenthesis => ")",
                    MarkerStyle.Minus => "-",
                    MarkerStyle.Plain or MarkerStyle.NoNumber => string.Empty,
                    _ => ".",
                },
            };
        }

        return new RichTextParagraphFormat
        {
            Alignment = native.Alignment switch
            {
                ParagraphAlignment.Center => RichTextAlignment.Center,
                ParagraphAlignment.Right => RichTextAlignment.Right,
                ParagraphAlignment.Justify => RichTextAlignment.Justified,
                _ => RichTextAlignment.Left,
            },
            Direction = native.RightToLeft == FormatEffect.On
                ? RichTextDirection.RightToLeft
                : RichTextDirection.LeftToRight,
            LeadingIndent = native.LeftIndent,
            TrailingIndent = native.RightIndent,
            FirstLineIndent = native.FirstLineIndent,
            SpaceBefore = Math.Max(native.SpaceBefore, 0),
            SpaceAfter = Math.Max(native.SpaceAfter, 0),
            LineSpacingRule = FromNativeLineSpacing(native.LineSpacingRule),
            LineSpacing = Math.Max(native.LineSpacing, 0),
            TabStops = tabs.DrainToImmutable(),
            NativeList = list,
        };
    }

    private static UnderlineType ToNativeUnderline(RichTextUnderlineStyle value) => value switch
    {
        RichTextUnderlineStyle.None => UnderlineType.None,
        RichTextUnderlineStyle.Words => UnderlineType.Words,
        RichTextUnderlineStyle.Double => UnderlineType.Double,
        RichTextUnderlineStyle.Dotted => UnderlineType.Dotted,
        RichTextUnderlineStyle.Dash => UnderlineType.Dash,
        RichTextUnderlineStyle.DashDot => UnderlineType.DashDot,
        RichTextUnderlineStyle.DashDotDot => UnderlineType.DashDotDot,
        RichTextUnderlineStyle.Wave => UnderlineType.Wave,
        RichTextUnderlineStyle.Thick => UnderlineType.Thick,
        RichTextUnderlineStyle.DoubleWave => UnderlineType.DoubleWave,
        RichTextUnderlineStyle.HeavyWave => UnderlineType.HeavyWave,
        RichTextUnderlineStyle.LongDash => UnderlineType.LongDash,
        _ => UnderlineType.Single,
    };

    private static RichTextUnderlineStyle FromNativeUnderline(UnderlineType value) => value switch
    {
        UnderlineType.None or UnderlineType.Undefined => RichTextUnderlineStyle.None,
        UnderlineType.Words => RichTextUnderlineStyle.Words,
        UnderlineType.Double => RichTextUnderlineStyle.Double,
        UnderlineType.Dotted or UnderlineType.ThickDotted => RichTextUnderlineStyle.Dotted,
        UnderlineType.Dash or UnderlineType.ThickDash => RichTextUnderlineStyle.Dash,
        UnderlineType.DashDot or UnderlineType.ThickDashDot => RichTextUnderlineStyle.DashDot,
        UnderlineType.DashDotDot or UnderlineType.ThickDashDotDot => RichTextUnderlineStyle.DashDotDot,
        UnderlineType.Wave => RichTextUnderlineStyle.Wave,
        UnderlineType.Thick => RichTextUnderlineStyle.Thick,
        UnderlineType.DoubleWave => RichTextUnderlineStyle.DoubleWave,
        UnderlineType.HeavyWave => RichTextUnderlineStyle.HeavyWave,
        UnderlineType.LongDash or UnderlineType.ThickLongDash => RichTextUnderlineStyle.LongDash,
        _ => RichTextUnderlineStyle.Single,
    };

    private static LineSpacingRule ToNativeLineSpacing(RichTextLineSpacingRule value) => value switch
    {
        RichTextLineSpacingRule.OneAndHalf => LineSpacingRule.OneAndHalf,
        RichTextLineSpacingRule.Double => LineSpacingRule.Double,
        RichTextLineSpacingRule.AtLeast => LineSpacingRule.AtLeast,
        RichTextLineSpacingRule.Exactly => LineSpacingRule.Exactly,
        RichTextLineSpacingRule.Multiple => LineSpacingRule.Multiple,
        _ => LineSpacingRule.Single,
    };

    private static RichTextLineSpacingRule FromNativeLineSpacing(LineSpacingRule value) => value switch
    {
        LineSpacingRule.OneAndHalf => RichTextLineSpacingRule.OneAndHalf,
        LineSpacingRule.Double => RichTextLineSpacingRule.Double,
        LineSpacingRule.AtLeast => RichTextLineSpacingRule.AtLeast,
        LineSpacingRule.Exactly => RichTextLineSpacingRule.Exactly,
        LineSpacingRule.Multiple or LineSpacingRule.Percent => RichTextLineSpacingRule.Multiple,
        LineSpacingRule.Single => RichTextLineSpacingRule.Single,
        _ => RichTextLineSpacingRule.Automatic,
    };

    // Microsoft.UI.Text.ITextCharacterFormat exposes this Windows SDK value type;
    // WinAppSDK 1.8 does not define a Microsoft.UI.Text.FontStretch replacement.
    private static Windows.UI.Text.FontStretch ToNativeFontStretch(double scale) => scale switch
    {
        < 0.5625d => Windows.UI.Text.FontStretch.UltraCondensed,
        < 0.6875d => Windows.UI.Text.FontStretch.ExtraCondensed,
        < 0.8125d => Windows.UI.Text.FontStretch.Condensed,
        < 0.9375d => Windows.UI.Text.FontStretch.SemiCondensed,
        < 1.0625d => Windows.UI.Text.FontStretch.Normal,
        < 1.1875d => Windows.UI.Text.FontStretch.SemiExpanded,
        < 1.375d => Windows.UI.Text.FontStretch.Expanded,
        < 1.75d => Windows.UI.Text.FontStretch.ExtraExpanded,
        _ => Windows.UI.Text.FontStretch.UltraExpanded,
    };

    private static double FromNativeFontStretch(Windows.UI.Text.FontStretch stretch) => stretch switch
    {
        Windows.UI.Text.FontStretch.UltraCondensed => 0.5d,
        Windows.UI.Text.FontStretch.ExtraCondensed => 0.625d,
        Windows.UI.Text.FontStretch.Condensed => 0.75d,
        Windows.UI.Text.FontStretch.SemiCondensed => 0.875d,
        Windows.UI.Text.FontStretch.SemiExpanded => 1.125d,
        Windows.UI.Text.FontStretch.Expanded => 1.25d,
        Windows.UI.Text.FontStretch.ExtraExpanded => 1.5d,
        Windows.UI.Text.FontStretch.UltraExpanded => 2d,
        _ => 1d,
    };

    private static TabAlignment ToNativeTabAlignment(RichTextTabAlignment value) => value switch
    {
        RichTextTabAlignment.Center => TabAlignment.Center,
        RichTextTabAlignment.Right => TabAlignment.Right,
        RichTextTabAlignment.Decimal => TabAlignment.Decimal,
        _ => TabAlignment.Left,
    };

    private static RichTextTabAlignment FromNativeTabAlignment(TabAlignment value) => value switch
    {
        TabAlignment.Center => RichTextTabAlignment.Center,
        TabAlignment.Right => RichTextTabAlignment.Right,
        TabAlignment.Decimal => RichTextTabAlignment.Decimal,
        _ => RichTextTabAlignment.Left,
    };

    private static TabLeader ToNativeTabLeader(RichTextTabLeader value) => value switch
    {
        RichTextTabLeader.Dots => TabLeader.Dots,
        RichTextTabLeader.Hyphens => TabLeader.Dashes,
        RichTextTabLeader.Underline => TabLeader.Lines,
        RichTextTabLeader.ThickLine => TabLeader.ThickLines,
        RichTextTabLeader.Equals => TabLeader.Equals,
        _ => TabLeader.Spaces,
    };

    private static RichTextTabLeader FromNativeTabLeader(TabLeader value) => value switch
    {
        TabLeader.Dots => RichTextTabLeader.Dots,
        TabLeader.Dashes => RichTextTabLeader.Hyphens,
        TabLeader.Lines => RichTextTabLeader.Underline,
        TabLeader.ThickLines => RichTextTabLeader.ThickLine,
        TabLeader.Equals => RichTextTabLeader.Equals,
        _ => RichTextTabLeader.None,
    };

    // WinUI 3 text-format color properties likewise use the Windows SDK Color value type.
    private static Windows.UI.Color ToWindowsColor(Color color) => Windows.UI.Color.FromArgb(
        (byte)Math.Round(color.Alpha * byte.MaxValue),
        (byte)Math.Round(color.Red * byte.MaxValue),
        (byte)Math.Round(color.Green * byte.MaxValue),
        (byte)Math.Round(color.Blue * byte.MaxValue));

    private static Color? FromWindowsColor(
        Windows.UI.Color color,
        bool transparentAsNull = false) =>
        transparentAsNull && color.A == 0
            ? null
            : Color.FromRgba(color.R, color.G, color.B, color.A);

    private Color? ResolveTextColor()
    {
        if (VirtualView.TextColor is { } textColor)
        {
            return textColor;
        }

        if (PlatformView.Foreground is Microsoft.UI.Xaml.Media.SolidColorBrush foreground)
        {
            return FromWindowsColor(foreground.Color);
        }

        return FromWindowsColor(PlatformView.Document.GetDefaultCharacterFormat().ForegroundColor);
    }

    private string? ResolveFontFamily()
    {
        if (!string.IsNullOrWhiteSpace(VirtualView.FontFamily))
        {
            return VirtualView.FontFamily;
        }

        return PlatformView.FontFamily?.Source;
    }

    private double? ResolveFontSize()
    {
        if (VirtualView.FontSize is { } fontSize)
        {
            return fontSize;
        }

        return double.IsFinite(PlatformView.FontSize) && PlatformView.FontSize > 0
            ? PlatformView.FontSize
            : null;
    }

    private static int GetParagraphEnd(string text, int start)
    {
        var newline = text.IndexOf('\n', start);
        return newline < 0 ? text.Length : newline + 1;
    }

    private void OnPlatformViewLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (VirtualView is null)
        {
            return;
        }

        if (_hasCompletedInitialLoad)
        {
            _nativeTextSnapshot = null;
            if (string.Equals(
                    GetNativeTextSnapshot().Text,
                    VirtualView.Document.Text,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        ApplyDocumentCore(
            VirtualView.Document.CurrentSnapshot,
            VirtualView.SelectedRange.Start,
            VirtualView.SelectedRange.Length);
        ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        ClearUndoHistoryCore();
        _hasCompletedInitialLoad = true;
    }

    private void OnPlatformViewLostFocus(object sender, RoutedEventArgs eventArgs) =>
        VirtualView?.RaiseCompleted();

    private void OnPlatformThemeChanged(FrameworkElement sender, object args)
    {
        if (VirtualView is null || VirtualView.TextColor is not null)
        {
            return;
        }

        UpdateAppearance(VirtualView);
        VirtualView.NotifyNativeAppearanceChanged();
    }

    private void OnNativeDocumentChanged(RichEditBox sender, RichEditBoxTextChangingEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }
        if (!eventArgs.IsContentChanging)
        {
            QueueNativeFormatReadback();
            return;
        }

        _nativeTextSnapshot = null;
        // TextChanging is synchronous. TextChanged runs after rendering, when
        // the projection guard has already been released and edits can coalesce.
        ReadNativeDocumentChange();
    }

    private void QueueNativeFormatReadback()
    {
        if (_nativeFormatReadbackQueued) return;
        var document = VirtualView.Document;
        var version = document.Version;
        _nativeFormatReadbackQueued = true;
        PlatformView.DispatcherQueue.TryEnqueue(() =>
        {
            _nativeFormatReadbackQueued = false;
            if (_applyingDocument || !ReferenceEquals(VirtualView?.Document, document) || document.Version != version) return;
            // Formatting also raises IsContentChanging=false. Once the native
            // operation finishes, only a real edit adds native undo state;
            // focus/selection notifications do not. Projection clears that state.
            if (PlatformView.Document.CanUndo())
            {
                _nativeTextSnapshot = null;
                ReadNativeDocumentChange();
            }
        });
    }

    private void ReadNativeDocumentChange()
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        RichTextDocumentSnapshot document;
        int start;
        int end;
        if (!_hasNativeLinks &&
            TryReadIncrementalNativeDocument(nativeStart, nativeEnd, out document, out start, out end))
        {
            _nativeTextSnapshot = null;
        }
        else
        {
            document = ReadDocumentFromPlatform();
            var snapshot = GetNativeTextSnapshot();
            start = snapshot.ToLogicalPosition(nativeStart);
            end = snapshot.ToLogicalPosition(nativeEnd);
        }

        _hasNativeLinks = document.Links.Length != 0;
        if (VirtualView.MaxLength >= 0 && document.Length > VirtualView.MaxLength &&
            document.Length > VirtualView.Document.Length)
        {
            // WinUI interprets MaxLength=0 as unlimited; the portable API uses -1.
            // Also cover text services that bypass the native input length limit.
            ApplyDocumentCore(VirtualView.Document.CurrentSnapshot,
                VirtualView.SelectedRange.Start, VirtualView.SelectedRange.Length);
            ApplyTypingFormatCore(VirtualView.TypingCharacterFormat, VirtualView.TypingParagraphFormat);
            return;
        }

        var length = end - start;
        VirtualView.UpdateDocumentFromPlatform(document, start, length, _sourceToken);
        PlatformView.Document.ClearUndoRedoHistory();
        VirtualView.UpdateUndoStateFromPlatform();
        UpdateTypingFormatsFromPlatform();
    }

    private bool TryReadIncrementalNativeDocument(
        int nativeSelectionStart,
        int nativeSelectionEnd,
        out RichTextDocumentSnapshot document,
        out int selectionStart,
        out int selectionEnd)
    {
        var previous = VirtualView.Document.CurrentSnapshot;
        var text = ReadNativePlainText();
        selectionStart = Math.Clamp(nativeSelectionStart, 0, text.Length);
        selectionEnd = Math.Clamp(nativeSelectionEnd, selectionStart, text.Length);

        var prefixLength = previous.Text.AsSpan().CommonPrefixLength(text);
        var suffixLength = 0;
        var maximumSuffixLength = Math.Min(previous.Length, text.Length) - prefixLength;
        while (suffixLength < maximumSuffixLength &&
               previous.Text[^(suffixLength + 1)] == text[^(suffixLength + 1)])
        {
            suffixLength++;
        }

        var oldEnd = previous.Length - suffixLength;
        var newEnd = text.Length - suffixLength;
        var insertedText = text.Substring(prefixLength, newEnd - prefixLength);
        var replacedRange = VirtualView.SelectedRange;
        if (RichTextDocumentSnapshot.TryGetReplacement(previous.Text, text, replacedRange, out var knownInsertion))
        {
            prefixLength = replacedRange.Start;
            oldEnd = replacedRange.End;
            insertedText = knownInsertion;
        }
        var textChanged = !string.Equals(previous.Text, text, StringComparison.Ordinal);
        document = textChanged
            ? previous.Replace(prefixLength..oldEnd, insertedText, _nativeTypingFormat)
            : previous;

        // A content callback with unchanged text can represent a native formatting
        // command. In that case inspect only the current selection (or its caret
        // neighbor), never the complete story.
        var characterRange = new RichTextRange(prefixLength, insertedText.Length);
        if (characterRange.IsEmpty)
        {
            characterRange = selectionEnd > selectionStart
                ? new RichTextRange(selectionStart, selectionEnd - selectionStart)
                : GetCaretInspectionRange(selectionStart, text.Length);
        }

        if (!TryOverlayNativeCharacterFormats(document, characterRange, out document))
        {
            return false;
        }

        var paragraphSeed = insertedText.Length != 0
            ? characterRange
            : textChanged
                ? new RichTextRange(Math.Min(prefixLength, text.Length), 0)
                : selectionEnd > selectionStart
                    ? new RichTextRange(selectionStart, selectionEnd - selectionStart)
                    : new RichTextRange(selectionStart, 0);
        var paragraphRange = GetAffectedParagraphRange(paragraphSeed, text);
        return TryOverlayNativeParagraphFormats(document, paragraphRange, out document);
    }

    private string ReadNativePlainText()
    {
        var rawText = ReadNativeStoryText(PlatformView.Document);
        var length = rawText.EndsWith('\r') ? rawText.Length - 1 : rawText.Length;
        var content = rawText.AsSpan(0, length);
        if (content.IndexOfAny('\r', '\v') < 0)
        {
            return length == rawText.Length ? rawText : rawText[..length];
        }

        return string.Create(length, rawText, static (destination, source) =>
        {
            for (var index = 0; index < destination.Length; index++)
            {
                destination[index] = source[index] switch
                {
                    '\r' => '\n',
                    '\v' => RichTextDocument.SoftLineBreakCharacter,
                    var character => character,
                };
            }
        });
    }

    private static string ReadNativeStoryText(RichEditTextDocument nativeDocument)
    {
        var range = nativeDocument.GetRange(0, 0);
        range.SetRange(0, range.StoryLength);
        return range.Text ?? string.Empty;
    }

    private bool TryOverlayNativeCharacterFormats(
        RichTextDocumentSnapshot source,
        RichTextRange range,
        out RichTextDocumentSnapshot result)
    {
        result = source;
        range = range.Clamp(source.Length);
        if (range.IsEmpty)
        {
            return true;
        }

        var nativeRange = PlatformView.Document.GetRange(0, 0);
        var fontFamily = source.DefaultCharacterFormat.FontFamily ?? ResolveFontFamily();
        var fontSize = source.DefaultCharacterFormat.FontSize ?? ResolveFontSize();
        var textColor = source.DefaultCharacterFormat.ForegroundColor ?? ResolveTextColor();
        for (var position = range.Start; position < range.End;)
        {
            nativeRange.SetRange(position, position + 1);
            nativeRange.Expand(TextRangeUnit.CharacterFormat);
            var end = Math.Clamp(nativeRange.EndPosition, position + 1, range.End);
            var nativeFormat = nativeRange.CharacterFormat;
            if (IsHiddenLinkInstruction(nativeRange, nativeFormat))
            {
                return false;
            }

            var previousFormat = result.GetCharacterFormat(position);
            var merged = MergeWindowsCharacterFormat(
                ReadCharacterFormat(nativeFormat),
                previousFormat,
                fontFamily,
                fontSize,
                textColor);
            result = result.ApplyCharacterFormat(position..end, _ => merged);
            position = end;
        }

        return true;
    }

    private bool TryOverlayNativeParagraphFormats(
        RichTextDocumentSnapshot source,
        RichTextRange range,
        out RichTextDocumentSnapshot result)
    {
        result = source;
        var text = source.Text;
        var scanRange = PlatformView.Document.GetRange(0, 0);
        var lastParagraphStart = range.End == 0
            ? 0
            : text.LastIndexOf('\n', Math.Min(range.End, text.Length) - 1) + 1;
        for (var start = range.Start; ;)
        {
            var end = GetParagraphEnd(text, start);
            var previousFormat = result.GetParagraphFormat(start);
            scanRange.SetRange(start, end);
            var nativeFormat = ReadParagraphFormat(
                scanRange.ParagraphFormat,
                previousFormat.NativeList);
            var merged = MergeWindowsParagraphFormat(nativeFormat, previousFormat);

            RichTextListItemFormat? item = null;
            if (nativeFormat.NativeList is { } nativeList)
            {
                if (previousFormat.List is not { } previousItem ||
                    !result.Lists.TryGetValue(previousItem.ListId, out var definition) ||
                    (uint)nativeList.Level >= (uint)definition.Levels.Length)
                {
                    // A newly introduced native list needs the full list-definition
                    // discovery path. Ordinary continuation, termination, level, and
                    // restart edits remain bounded here.
                    return false;
                }

                var preservesListIdentity =
                    previousFormat.NativeList is { } previousNativeList &&
                    previousItem.ListId.Value == nativeList.Id &&
                    previousNativeList.Id == nativeList.Id &&
                    previousNativeList.Kind == nativeList.Kind &&
                    previousNativeList.NumberStyle == nativeList.NumberStyle &&
                    previousItem.Level == nativeList.Level;
                item = new RichTextListItemFormat(
                    previousItem.ListId,
                    nativeList.Level,
                    nativeList.Restart
                        ? nativeList.StartAt
                        : preservesListIdentity
                            ? previousItem.RestartAt
                            : null);
            }

            merged = merged with { List = item };
            var paragraphRange = end == start ? start..start : start..end;
            result = result.ApplyParagraphFormat(paragraphRange, _ => merged);
            if (start >= lastParagraphStart)
            {
                break;
            }

            if (end == start)
            {
                break;
            }

            start = end;
        }

        return true;
    }

    private static RichTextRange GetCaretInspectionRange(int position, int textLength)
    {
        if (textLength == 0)
        {
            return RichTextRange.Empty;
        }

        var start = position == textLength ? textLength - 1 : position;
        return new RichTextRange(start, 1);
    }

    private void OnNativeSelectionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_applyingDocument || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        var start = nativeStart;
        var end = nativeEnd;
        if (_hasNativeLinks)
        {
            var snapshot = GetNativeTextSnapshot();
            start = snapshot.ToLogicalPosition(nativeStart);
            end = snapshot.ToLogicalPosition(nativeEnd);
        }

        start = Math.Clamp(start, 0, VirtualView.Document.Text.Length);
        end = Math.Clamp(end, start, VirtualView.Document.Text.Length);
        var length = end - start;
        VirtualView.UpdateSelectionFromPlatform(start, length);
        UpdateTypingFormatsFromPlatform();
    }

    private void UpdateTypingFormatsFromPlatform()
    {
        if (VirtualView is null)
        {
            return;
        }

        var previousCharacter = VirtualView.TypingCharacterFormat;
        var nativeCharacter = ReadCharacterFormat(PlatformView.Document.Selection.CharacterFormat);
        var defaultCharacter = VirtualView.Document.DefaultCharacterFormat;
        _nativeTypingFormat = MergeWindowsCharacterFormat(
            nativeCharacter,
            previousCharacter,
            defaultCharacter.FontFamily ?? ResolveFontFamily(),
            defaultCharacter.FontSize ?? ResolveFontSize(),
            defaultCharacter.ForegroundColor ?? ResolveTextColor());
        var previousParagraph = VirtualView.TypingParagraphFormat;
        var nativeParagraph = ReadParagraphFormat(
            PlatformView.Document.Selection.ParagraphFormat,
            previousParagraph.NativeList);
        _nativeTypingParagraphFormat = MergeWindowsParagraphFormat(
            nativeParagraph,
            previousParagraph);
        VirtualView.UpdateTypingFormatsFromPlatform(
            _nativeTypingFormat,
            _nativeTypingParagraphFormat);
    }

    private sealed class NativeTextSnapshot(
        string text,
        RichTextRun[] runs,
        int[] nativeToLogical,
        int[] logicalToNative)
    {
        public string Text { get; } = text;

        public IReadOnlyList<RichTextRun> Runs { get; } = runs;

        public int ToLogicalPosition(int nativePosition) =>
            nativeToLogical[Math.Clamp(nativePosition, 0, nativeToLogical.Length - 1)];

        public int ToNativePosition(int logicalPosition) =>
            logicalToNative[Math.Clamp(logicalPosition, 0, logicalToNative.Length - 1)];
    }
}
