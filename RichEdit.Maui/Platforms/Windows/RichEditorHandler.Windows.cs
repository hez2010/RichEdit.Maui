using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Maui.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Streams;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private bool _applyingDocument;
    private bool _canReadLanguageTag = true;
    private bool _hasNativeLinks;
    private bool _suppressingNativeEvents;
    private int _nativeEventSuppressionVersion;
    private DispatcherQueueTimer? _nativeReadbackTimer;
    private RichTextCharacterFormat _nativeTypingFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _nativeTypingParagraphFormat = RichTextParagraphFormat.Default;
    private NativeTextSnapshot? _nativeTextSnapshot;

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

    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);
        platformView.TextChanged += OnNativeDocumentChanged;
        platformView.SelectionChanged += OnNativeSelectionChanged;
        platformView.Loaded += OnPlatformViewLoaded;
        _nativeReadbackTimer = platformView.DispatcherQueue.CreateTimer();
        _nativeReadbackTimer.Interval = TimeSpan.FromMilliseconds(16);
        _nativeReadbackTimer.IsRepeating = false;
        _nativeReadbackTimer.Tick += OnNativeReadbackTimerTick;
    }

    protected override void DisconnectHandler(RichEditBox platformView)
    {
        platformView.TextChanged -= OnNativeDocumentChanged;
        platformView.SelectionChanged -= OnNativeSelectionChanged;
        platformView.Loaded -= OnPlatformViewLoaded;
        if (_nativeReadbackTimer is not null)
        {
            _nativeReadbackTimer.Stop();
            _nativeReadbackTimer.Tick -= OnNativeReadbackTimerTick;
            _nativeReadbackTimer = null;
        }

        _nativeEventSuppressionVersion++;
        _suppressingNativeEvents = false;
        _hasNativeLinks = false;
        _nativeTextSnapshot = null;

        base.DisconnectHandler(platformView);
    }

    private partial void ApplyDocumentCore(
        RichTextDocument document,
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
            var nativeDocument = PlatformView.Document;
            nativeDocument.BatchDisplayUpdates();
            try
            {
                _hasNativeLinks = false;
                _nativeTextSnapshot = null;
                var useNativeRtf = document.Images.Count > 0 ||
                    document.Paragraphs.Any(paragraph => paragraph.Format.List is not null);
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
                        LoadRtfDocument(nativeDocument, RtfCodec.SerializeForNativeProjection(document));
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
                        if (run.Format == document.DefaultCharacterFormat)
                        {
                            continue;
                        }

                        if (!characterFormats.TryGetValue(run.Format, out var nativeFormat))
                        {
                            // Runs are fully resolved model formats, not deltas from the
                            // document default. Start from the pristine native format so
                            // a null/transparent background actively clears highlighting.
                            nativeFormat = resetCharacterFormat.GetClone();
                            ApplyCharacterFormat(nativeFormat, run.Format);
                            characterFormats.Add(run.Format, nativeFormat);
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
            _applyingDocument = false;
            SuppressNativeEventsUntilIdle();
        }
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

        _applyingDocument = true;
        try
        {
            var nativeDocument = PlatformView.Document;
            var nativeCharacterFormat = nativeDocument.GetDefaultCharacterFormat().GetClone();
            ApplyCharacterFormat(nativeCharacterFormat, characterFormat);
            nativeDocument.Selection.CharacterFormat.SetClone(nativeCharacterFormat);
            var nativeParagraphFormat = nativeDocument.GetDefaultParagraphFormat().GetClone();
            ApplyParagraphFormat(nativeParagraphFormat, paragraphFormat);
            nativeDocument.Selection.ParagraphFormat.SetClone(nativeParagraphFormat);
        }
        finally
        {
            _applyingDocument = false;
            SuppressNativeEventsUntilIdle();
        }
    }

    private static void LoadRtfDocument(RichEditTextDocument nativeDocument, string rtf)
    {
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

    private partial void UpdatePlaceholder(RichEditor editor)
    {
        PlatformView.PlaceholderText = editor.Placeholder;
        PlatformView.Resources["TextControlPlaceholderForeground"] = editor.PlaceholderColor.ToPlatform();
    }

    private partial void UpdateAppearance(RichEditor editor)
    {
        PlatformView.Foreground = editor.TextColor.ToPlatform();
        PlatformView.FontFamily = new FontFamily(editor.FontFamily ?? "Segoe UI");
        PlatformView.FontSize = editor.FontSize;
        UpdatePlaceholder(editor);

        if (!_applyingDocument)
        {
            ApplyDocumentCore(editor.Document, editor.SelectionStart, editor.SelectionLength);
            ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        }
    }

    private partial void UpdateIsReadOnly(RichEditor editor) =>
        PlatformView.IsReadOnly = editor.IsReadOnly;

    private void ApplyCharacterFormat(
        ITextCharacterFormat native,
        RichTextCharacterFormat format)
    {
        native.Name = format.FontFamily ?? VirtualView.FontFamily ?? "Segoe UI";
        native.Size = (float)(format.FontSize ?? VirtualView.FontSize);
        native.Weight = format.FontWeight;
        native.Italic = format.Italic ? FormatEffect.On : FormatEffect.Off;
        native.Underline = ToNativeUnderline(format.Underline);
        native.Strikethrough = format.Strikethrough == RichTextStrikethroughStyle.None
            ? FormatEffect.Off
            : FormatEffect.On;
        native.ForegroundColor = ToWindowsColor(format.ForegroundColor ?? VirtualView.TextColor);
        // Each caller supplies a pristine default-format clone. Leaving its background
        // untouched and later using SetClone is the WinUI reset path; RichEdit ignores
        // alpha on text backgrounds.
        if (format.BackgroundColor is { Alpha: > 0 } backgroundColor)
        {
            native.BackgroundColor = ToWindowsColor(backgroundColor);
        }

        native.Subscript = format.Script == RichTextScript.Subscript
            ? FormatEffect.On
            : FormatEffect.Off;
        native.Superscript = format.Script == RichTextScript.Superscript
            ? FormatEffect.On
            : FormatEffect.Off;
        native.Position = (float)format.BaselineOffset;
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
        foreach (var tab in format.TabStops.Take(63))
        {
            native.AddTab(
                (float)tab.Position,
                ToNativeTabAlignment(tab.Alignment),
                ToNativeTabLeader(tab.Leader));
        }

        if (format.List is not { } list)
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

    private RichTextDocument ReadDocumentFromPlatform()
    {
        var snapshot = GetNativeTextSnapshot();
        var text = snapshot.Text;
        var nativeDocument = PlatformView.Document;
        var defaultCharacterFormat = ReadCharacterFormat(
            nativeDocument.GetDefaultCharacterFormat());
        var defaultParagraphFormat = ReadParagraphFormat(
            nativeDocument.GetDefaultParagraphFormat(), null);
        var scanRange = nativeDocument.GetRange(0, 0);
        var paragraphs = new List<RichTextParagraph>();
        RichTextListFormat? previousList = null;
        var nextListId = 1;
        for (var start = 0; ;)
        {
            var end = GetParagraphEnd(text, start);
            scanRange.SetRange(
                snapshot.ToNativePosition(start),
                snapshot.ToNativePosition(end));
            var native = scanRange.ParagraphFormat;
            var paragraphFormat = ReadParagraphFormat(native, previousList);
            if (paragraphFormat.List is { } list)
            {
                var continues = previousList is not null &&
                    previousList.Kind == list.Kind &&
                    previousList.NumberStyle == list.NumberStyle &&
                    previousList.Level == list.Level;
                list = list with { Id = continues ? previousList!.Id : nextListId++ };
                paragraphFormat = paragraphFormat with { List = list };
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

        var previous = VirtualView.Document;
        return previous.MergeNativeSnapshot(
            text,
            snapshot.Runs,
            paragraphs,
            null,
            null,
            defaultCharacterFormat,
            defaultParagraphFormat,
            MergeWindowsCharacterFormat,
            MergeWindowsParagraphFormat);
    }

    private NativeTextSnapshot GetNativeTextSnapshot() =>
        _nativeTextSnapshot ??= CreateNativeTextSnapshot();

    private NativeTextSnapshot CreateNativeTextSnapshot()
    {
        var nativeDocument = PlatformView.Document;
        nativeDocument.GetText(TextGetOptions.None, out var rawText);
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
        RichTextCharacterFormat previous)
    {
        var horizontalScale = ToNativeFontStretch(native.HorizontalScale) ==
            ToNativeFontStretch(previous.HorizontalScale)
                ? previous.HorizontalScale
                : native.HorizontalScale;
        return native with
        {
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
        RichTextListFormat? list = native.List;
        if (list is not null && previous.List is { } previousList)
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
            List = list,
        };
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
            if (position >= 0)
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
            TabStops = tabs.MoveToImmutable(),
            List = list,
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

        ApplyDocumentCore(VirtualView.Document, VirtualView.SelectionStart, VirtualView.SelectionLength);
        ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
    }

    private void OnNativeDocumentChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_applyingDocument || _suppressingNativeEvents || VirtualView is null)
        {
            return;
        }

        _nativeTextSnapshot = null;
        if (_nativeReadbackTimer is null)
        {
            ReadNativeDocumentChange();
            return;
        }

        RestartNativeReadbackTimer();
    }

    private void OnNativeReadbackTimerTick(DispatcherQueueTimer sender, object args)
    {
        ReadNativeDocumentChange();
    }

    private void SuppressNativeEventsUntilIdle()
    {
        if (PlatformView is null)
        {
            return;
        }

        _suppressingNativeEvents = true;
        var version = ++_nativeEventSuppressionVersion;
        if (!PlatformView.DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                () =>
                {
                    if (_nativeEventSuppressionVersion == version)
                    {
                        _suppressingNativeEvents = false;
                    }
                }))
        {
            _suppressingNativeEvents = false;
        }
    }

    private void RestartNativeReadbackTimer()
    {
        _nativeReadbackTimer!.Stop();
        _nativeReadbackTimer.Start();
    }

    private void ReadNativeDocumentChange()
    {
        if (_applyingDocument || _suppressingNativeEvents || VirtualView is null)
        {
            return;
        }

        var selection = PlatformView.Document.Selection;
        var nativeStart = Math.Min(selection.StartPosition, selection.EndPosition);
        var nativeEnd = Math.Max(selection.StartPosition, selection.EndPosition);
        var document = ReadDocumentFromPlatform();
        var snapshot = GetNativeTextSnapshot();
        var start = snapshot.ToLogicalPosition(nativeStart);
        var end = snapshot.ToLogicalPosition(nativeEnd);
        var length = end - start;
        VirtualView.UpdateDocumentFromPlatform(document, start, length);
    }

    private void OnNativeSelectionChanged(object sender, RoutedEventArgs eventArgs)
    {
        if (_applyingDocument || _suppressingNativeEvents || VirtualView is null)
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
