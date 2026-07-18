namespace RichEdit.Maui;

public sealed class RichEditorDocumentChangedEventArgs(
    RichTextDocument oldDocument,
    RichTextDocument newDocument) : EventArgs
{
    public RichTextDocument OldDocument { get; } = oldDocument;

    public RichTextDocument NewDocument { get; } = newDocument;
}

public sealed class RichEditorTextChangedEventArgs(string oldText, string newText) : EventArgs
{
    public string OldText { get; } = oldText;

    public string NewText { get; } = newText;
}

public sealed class RichEditorSelectionChangedEventArgs(
    int start,
    int length,
    RichTextCharacterFormat? characterFormat,
    RichTextParagraphFormat? paragraphFormat,
    RichTextCharacterFormat typingCharacterFormat) : EventArgs
{
    public int Start { get; } = start;

    public int Length { get; } = length;

    public RichTextCharacterFormat? CharacterFormat { get; } = characterFormat;

    public RichTextParagraphFormat? ParagraphFormat { get; } = paragraphFormat;

    public RichTextCharacterFormat TypingCharacterFormat { get; } = typingCharacterFormat;
}

public class RichEditor : View
{
    private static readonly RichTextDocument EmptyDocument = RichTextDocument.FromPlainText(string.Empty);

    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(RichTextDocument),
        typeof(RichEditor),
        EmptyDocument,
        BindingMode.TwoWay,
        validateValue: static (_, value) => value is RichTextDocument,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((RichEditor)bindable).OnDocumentPropertyChanged(
                (RichTextDocument)oldValue,
                (RichTextDocument)newValue));

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(RichEditor),
        string.Empty,
        BindingMode.TwoWay,
        coerceValue: static (_, value) => value ?? string.Empty,
        propertyChanged: static (bindable, _, newValue) =>
            ((RichEditor)bindable).OnTextPropertyChanged((string)newValue));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(RichEditor),
        string.Empty,
        coerceValue: static (_, value) => value ?? string.Empty);

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(RichEditor),
        Colors.Black);

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor),
        typeof(Color),
        typeof(RichEditor),
        Colors.Gray);

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily),
        typeof(string),
        typeof(RichEditor));

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(RichEditor),
        16d,
        validateValue: static (_, value) => double.IsFinite((double)value) && (double)value > 0);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(RichEditor),
        false);

    private RichTextCharacterFormat _typingCharacterFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _typingParagraphFormat = RichTextParagraphFormat.Default;
    private bool _synchronizingProperties;
    private bool _updatingFromPlatform;

    public event EventHandler<RichEditorDocumentChangedEventArgs>? DocumentChanged;

    public event EventHandler<RichEditorTextChangedEventArgs>? TextChanged;

    public event EventHandler<RichEditorSelectionChangedEventArgs>? SelectionChanged;

    public RichTextDocument Document
    {
        get => (RichTextDocument)GetValue(DocumentProperty);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(DocumentProperty, value);
        }
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public Color PlaceholderColor
    {
        get => (Color)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    [System.ComponentModel.TypeConverter(typeof(FontSizeConverter))]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public int SelectionStart { get; private set; }

    public int SelectionLength { get; private set; }

    public RichTextCharacterFormat? SelectedCharacterFormat =>
        Document.GetUniformCharacterFormat(SelectionRange);

    public RichTextParagraphFormat? SelectedParagraphFormat =>
        Document.GetUniformParagraphFormat(SelectionRange);

    public RichTextCharacterFormat TypingCharacterFormat => _typingCharacterFormat;

    public RichTextParagraphFormat TypingParagraphFormat => _typingParagraphFormat;

    public bool? IsBold => SelectedCharacterFormat?.Bold;

    public bool? IsItalic => SelectedCharacterFormat?.Italic;

    public bool? IsUnderlined => SelectedCharacterFormat is { } format
        ? format.Underline != RichTextUnderlineStyle.None
        : null;

    public bool? IsStruckThrough => SelectedCharacterFormat is { } format
        ? format.Strikethrough != RichTextStrikethroughStyle.None
        : null;

    internal bool IsUpdatingFromPlatform => _updatingFromPlatform;

    private Range SelectionRange => SelectionStart..checked(SelectionStart + SelectionLength);

    public void LoadRtf(string rtf) => LoadDocument(RichTextDocument.FromRtf(rtf));

    public string ToRtf() => Document.ToRtf();

    public void LoadDocument(RichTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SetDocumentAndSelection(document, 0, 0);
    }

    public void Select(int start, int length)
    {
        ValidateSelection(start, length, Document.Text.Length);
        if (Handler is IRichEditorHandler handler)
        {
            handler.SetSelection(start, length);
        }
        else
        {
            UpdateSelectionFromPlatform(start, length);
        }
    }

    public void ReplaceSelection(string? text)
    {
        text ??= string.Empty;
        var start = SelectionStart;
        var document = Document.Replace(SelectionRange, text, _typingCharacterFormat);
        SetDocumentAndSelection(document, start + text.Length, 0);
    }

    public void SetSelectedCharacterFormat(RichTextCharacterFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        UpdateSelectedCharacterFormat(_ => format);
    }

    public void UpdateSelectedCharacterFormat(
        Func<RichTextCharacterFormat, RichTextCharacterFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (SelectionLength == 0)
        {
            _typingCharacterFormat = transform(_typingCharacterFormat) ??
                throw new InvalidOperationException("A character-format transform cannot return null.");
            ApplyTypingFormatToPlatform();
            RaiseSelectionChanged();
            return;
        }

        var document = Document.ApplyCharacterFormat(SelectionRange, transform);
        SetDocumentAndSelection(document, SelectionStart, SelectionLength);
    }

    public void SetSelectedParagraphFormat(RichTextParagraphFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        UpdateSelectedParagraphFormat(_ => format);
    }

    public void UpdateSelectedParagraphFormat(
        Func<RichTextParagraphFormat, RichTextParagraphFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var document = Document.ApplyParagraphFormat(SelectionRange, transform);
        SetDocumentAndSelection(document, SelectionStart, SelectionLength);
    }

    public void ToggleBold()
    {
        var enabled = !AllSelectedCharacterFormats(format => format.Bold);
        UpdateSelectedCharacterFormat(format => format with
        {
            FontWeight = enabled ? Math.Max(format.FontWeight, 700) : 400,
        });
    }

    public void ToggleItalic()
    {
        var enabled = !AllSelectedCharacterFormats(format => format.Italic);
        UpdateSelectedCharacterFormat(format => format with { Italic = enabled });
    }

    public void ToggleUnderline()
    {
        var enabled = !AllSelectedCharacterFormats(
            format => format.Underline != RichTextUnderlineStyle.None);
        UpdateSelectedCharacterFormat(format => format with
        {
            Underline = enabled ? RichTextUnderlineStyle.Single : RichTextUnderlineStyle.None,
        });
    }

    public void ToggleStrikethrough()
    {
        var enabled = !AllSelectedCharacterFormats(
            format => format.Strikethrough != RichTextStrikethroughStyle.None);
        UpdateSelectedCharacterFormat(format => format with
        {
            Strikethrough = enabled
                ? RichTextStrikethroughStyle.Single
                : RichTextStrikethroughStyle.None,
        });
    }

    public void ToggleSuperscript() => ToggleScript(RichTextScript.Superscript);

    public void ToggleSubscript() => ToggleScript(RichTextScript.Subscript);

    public void ToggleBulletedList(
        string bulletText,
        string prefix = "",
        string suffix = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(bulletText);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        ToggleList(new RichTextListFormat
        {
            Kind = RichListKind.Bulleted,
            Prefix = prefix,
            Suffix = suffix,
            BulletText = bulletText,
        });
    }

    public void ToggleNumberedList(
        RichListNumberStyle numberStyle,
        int startAt,
        string prefix = "",
        string suffix = ".")
    {
        if (!Enum.IsDefined(numberStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(numberStyle));
        }

        if (startAt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startAt));
        }

        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        ToggleList(new RichTextListFormat
        {
            Kind = RichListKind.Numbered,
            NumberStyle = numberStyle,
            StartAt = startAt,
            Prefix = prefix,
            Suffix = suffix,
        });
    }

    public void ToggleList(RichTextListFormat listFormat)
    {
        ArgumentNullException.ThrowIfNull(listFormat);
        if (listFormat.Id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(listFormat), "A list ID cannot be negative.");
        }

        var remove = AllSelectedParagraphFormats(format =>
            format.List is { } list && HasSameListStyle(list, listFormat));
        if (remove)
        {
            UpdateSelectedParagraphFormat(format => format with { List = null });
            return;
        }

        var appliedListFormat = listFormat.Id == 0
            ? listFormat with
            {
                Id = checked(Document.Paragraphs
                    .Select(paragraph => paragraph.Format.List?.Id ?? 0)
                    .DefaultIfEmpty()
                    .Max() + 1),
            }
            : listFormat;
        UpdateSelectedParagraphFormat(format => format with { List = appliedListFormat });
    }

    public void SetSelectedLink(string target, string? toolTip = null)
    {
        var document = Document.SetLink(SelectionRange, target, toolTip);
        SetDocumentAndSelection(document, SelectionStart, SelectionLength);
    }

    public void RemoveSelectedLinks()
    {
        var document = Document.RemoveLinks(SelectionRange);
        SetDocumentAndSelection(document, SelectionStart, SelectionLength);
    }

    public void InsertImage(RichTextImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var document = Document.Replace(SelectionRange, string.Empty)
            .InsertImage(SelectionStart, image);
        SetDocumentAndSelection(document, SelectionStart + 1, 0);
    }

    internal void UpdateDocumentFromPlatform(
        RichTextDocument document,
        int selectionStart,
        int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateSelection(selectionStart, selectionLength, document.Text.Length);
        var documentChanged = !ReferenceEquals(Document, document);
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
        _updatingFromPlatform = true;
        try
        {
            Document = document;
        }
        finally
        {
            _updatingFromPlatform = false;
        }

        if (!documentChanged)
        {
            UpdateTypingFormats();
            RaiseSelectionChanged();
        }
    }

    internal void UpdateSelectionFromPlatform(int start, int length)
    {
        ValidateSelection(start, length, Document.Text.Length);
        if (SelectionStart == start && SelectionLength == length)
        {
            return;
        }

        SelectionStart = start;
        SelectionLength = length;
        // Platform-originated selection changes are observations. Native rich editors
        // already update their typing attributes, so only mirror them into the model here.
        UpdateTypingFormats();
        RaiseSelectionChanged();
    }

    private void OnDocumentPropertyChanged(
        RichTextDocument oldDocument,
        RichTextDocument newDocument)
    {
        if (_synchronizingProperties)
        {
            return;
        }

        _synchronizingProperties = true;
        try
        {
            Text = newDocument.Text;
        }
        finally
        {
            _synchronizingProperties = false;
        }

        SelectionStart = Math.Clamp(SelectionStart, 0, newDocument.Text.Length);
        SelectionLength = Math.Clamp(
            SelectionLength,
            0,
            newDocument.Text.Length - SelectionStart);
        UpdateTypingFormats();
        DocumentChanged?.Invoke(
            this,
            new RichEditorDocumentChangedEventArgs(oldDocument, newDocument));
        if (!string.Equals(oldDocument.Text, newDocument.Text, StringComparison.Ordinal))
        {
            TextChanged?.Invoke(
                this,
                new RichEditorTextChangedEventArgs(oldDocument.Text, newDocument.Text));
        }

        RaiseSelectionChanged();
    }

    private void OnTextPropertyChanged(string text)
    {
        if (_synchronizingProperties || _updatingFromPlatform)
        {
            return;
        }

        Document = new RichTextDocument(
            text,
            defaultCharacterFormat: Document.DefaultCharacterFormat,
            defaultParagraphFormat: Document.DefaultParagraphFormat,
            metadata: Document.Metadata);
    }

    private void SetDocumentAndSelection(
        RichTextDocument document,
        int selectionStart,
        int selectionLength)
    {
        ValidateSelection(selectionStart, selectionLength, document.Text.Length);
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
        if (ReferenceEquals(Document, document))
        {
            UpdateTypingFormats();
            ApplyDocumentToPlatform();
            RaiseSelectionChanged();
            return;
        }

        Document = document;
    }

    private void ToggleScript(RichTextScript script)
    {
        var enabled = !AllSelectedCharacterFormats(format => format.Script == script);
        UpdateSelectedCharacterFormat(format => format with
        {
            Script = enabled ? script : RichTextScript.Normal,
        });
    }

    private static bool HasSameListStyle(
        RichTextListFormat left,
        RichTextListFormat right) =>
        (right.Id == 0 || left.Id == right.Id) &&
        left.Level == right.Level &&
        left.Kind == right.Kind &&
        left.NumberStyle == right.NumberStyle &&
        left.StartAt == right.StartAt &&
        left.Restart == right.Restart &&
        string.Equals(left.Prefix, right.Prefix, StringComparison.Ordinal) &&
        string.Equals(left.Suffix, right.Suffix, StringComparison.Ordinal) &&
        string.Equals(left.BulletText, right.BulletText, StringComparison.Ordinal) &&
        string.Equals(left.PictureId, right.PictureId, StringComparison.Ordinal);

    private bool AllSelectedCharacterFormats(Func<RichTextCharacterFormat, bool> predicate)
    {
        if (SelectionLength == 0)
        {
            return predicate(_typingCharacterFormat);
        }

        var end = SelectionStart + SelectionLength;
        return Document.Runs
            .Where(run => run.End > SelectionStart && run.Start < end)
            .All(run => predicate(run.Format));
    }

    private bool AllSelectedParagraphFormats(Func<RichTextParagraphFormat, bool> predicate)
    {
        var (start, length) = SelectionRange.GetOffsetAndLength(Document.Text.Length);
        var lastPosition = length == 0 ? start : start + length - 1;
        var firstParagraphStart = start == 0
            ? 0
            : Document.Text.LastIndexOf('\n', start - 1) + 1;
        var lastParagraphStart = lastPosition == 0
            ? 0
            : Document.Text.LastIndexOf('\n', lastPosition - 1) + 1;
        return Document.Paragraphs
            .Where(paragraph =>
                paragraph.Start >= firstParagraphStart && paragraph.Start <= lastParagraphStart)
            .All(paragraph => predicate(paragraph.Format));
    }

    private void UpdateTypingFormats()
    {
        _typingCharacterFormat = Document.GetCaretFormat(SelectionStart);
        _typingParagraphFormat = Document.GetParagraphFormat(SelectionStart);
    }

    private void ApplyDocumentToPlatform()
    {
        if (Handler is IRichEditorHandler handler)
        {
            handler.ApplyDocument(Document, SelectionStart, SelectionLength);
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
    }

    private void ApplyTypingFormatToPlatform()
    {
        if (Handler is IRichEditorHandler handler)
        {
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectionStart));
        OnPropertyChanged(nameof(SelectionLength));
        OnPropertyChanged(nameof(SelectedCharacterFormat));
        OnPropertyChanged(nameof(SelectedParagraphFormat));
        OnPropertyChanged(nameof(TypingCharacterFormat));
        OnPropertyChanged(nameof(TypingParagraphFormat));
        OnPropertyChanged(nameof(IsBold));
        OnPropertyChanged(nameof(IsItalic));
        OnPropertyChanged(nameof(IsUnderlined));
        OnPropertyChanged(nameof(IsStruckThrough));
        SelectionChanged?.Invoke(
            this,
            new RichEditorSelectionChangedEventArgs(
                SelectionStart,
                SelectionLength,
                SelectedCharacterFormat,
                SelectedParagraphFormat,
                _typingCharacterFormat));
    }

    private static void ValidateSelection(int start, int length, int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (start > textLength || length > textLength - start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "The selection must be inside the document text.");
        }
    }
}
