using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>
/// A cross-platform native rich-text editor for .NET MAUI.
/// </summary>
public sealed class RichEditor : View
{
    /// <summary>Identifies the <see cref="Document"/> bindable property.</summary>
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(RichTextDocument),
        typeof(RichEditor),
        defaultValueCreator: static _ => new RichTextDocument(),
        defaultBindingMode: BindingMode.OneWay,
        validateValue: static (bindable, value) =>
            value is RichTextDocument document && document.CanAttachEditor(bindable),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((RichEditor)bindable).OnDocumentPropertyChanged(
                (RichTextDocument?)oldValue,
                (RichTextDocument)newValue));

    /// <summary>Identifies the <see cref="SelectedRange"/> bindable property.</summary>
    public static readonly BindableProperty SelectedRangeProperty = BindableProperty.Create(
        nameof(SelectedRange),
        typeof(RichTextRange),
        typeof(RichEditor),
        RichTextRange.Empty,
        BindingMode.TwoWay,
        validateValue: static (bindable, value) =>
            value is RichTextRange range && range.End <= ((RichEditor)bindable).Document.Length,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((RichEditor)bindable).OnSelectedRangePropertyChanged(
                (RichTextRange)oldValue,
                (RichTextRange)newValue));

    /// <summary>Identifies the <see cref="Placeholder"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(RichEditor),
        string.Empty,
        coerceValue: static (_, value) => value ?? string.Empty);

    /// <summary>Identifies the <see cref="TextColor"/> bindable property.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(RichEditor),
        validateValue: static (_, value) => IsValidAppearanceColor(value),
        propertyChanged: static (bindable, _, _) => ((RichEditor)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="PlaceholderColor"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor),
        typeof(Color),
        typeof(RichEditor),
        validateValue: static (_, value) => IsValidAppearanceColor(value));

    /// <summary>Identifies the <see cref="FontFamily"/> bindable property.</summary>
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily),
        typeof(string),
        typeof(RichEditor),
        propertyChanged: static (bindable, _, _) => ((RichEditor)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="FontSize"/> bindable property.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double?),
        typeof(RichEditor),
        validateValue: static (_, value) => value is null ||
            value is double size && double.IsFinite(size) && size > 0 && size <= float.MaxValue,
        propertyChanged: static (bindable, _, _) => ((RichEditor)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="IsReadOnly"/> bindable property.</summary>
    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(RichEditor),
        false,
        propertyChanged: static (bindable, _, _) => ((RichEditor)bindable).Commands.Refresh());

    /// <summary>Identifies the <see cref="IsSpellCheckEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled),
        typeof(bool),
        typeof(RichEditor),
        true);

    /// <summary>Identifies the <see cref="IsTextPredictionEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(
        nameof(IsTextPredictionEnabled),
        typeof(bool),
        typeof(RichEditor),
        true);

    /// <summary>Identifies the <see cref="Keyboard"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard),
        typeof(Keyboard),
        typeof(RichEditor),
        Keyboard.Default,
        validateValue: static (_, value) => value is Keyboard);

    /// <summary>Identifies the <see cref="MaxLength"/> bindable property.</summary>
    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength),
        typeof(int),
        typeof(RichEditor),
        -1,
        validateValue: static (_, value) => value is int length && length >= -1);

    /// <summary>Identifies the <see cref="AutoSize"/> bindable property.</summary>
    public static readonly BindableProperty AutoSizeProperty = BindableProperty.Create(
        nameof(AutoSize),
        typeof(EditorAutoSizeOption),
        typeof(RichEditor),
        EditorAutoSizeOption.Disabled,
        validateValue: static (_, value) =>
            value is EditorAutoSizeOption option && Enum.IsDefined(option),
        propertyChanged: static (bindable, _, _) => ((RichEditor)bindable).InvalidateMeasure());

    /// <summary>Identifies the <see cref="AcceptsTab"/> bindable property.</summary>
    public static readonly BindableProperty AcceptsTabProperty = BindableProperty.Create(
        nameof(AcceptsTab),
        typeof(bool),
        typeof(RichEditor),
        false);

    /// <summary>Identifies the <see cref="ReturnCommand"/> bindable property.</summary>
    public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(
        nameof(ReturnCommand),
        typeof(ICommand),
        typeof(RichEditor));

    /// <summary>Identifies the <see cref="ReturnCommandParameter"/> bindable property.</summary>
    public static readonly BindableProperty ReturnCommandParameterProperty = BindableProperty.Create(
        nameof(ReturnCommandParameter),
        typeof(object),
        typeof(RichEditor));

    private static readonly BindablePropertyKey CanUndoPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CanUndo),
        typeof(bool),
        typeof(RichEditor),
        false);

    /// <summary>Identifies the read-only <see cref="CanUndo"/> bindable property.</summary>
    public static readonly BindableProperty CanUndoProperty = CanUndoPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanRedoPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CanRedo),
        typeof(bool),
        typeof(RichEditor),
        false);

    /// <summary>Identifies the read-only <see cref="CanRedo"/> bindable property.</summary>
    public static readonly BindableProperty CanRedoProperty = CanRedoPropertyKey.BindableProperty;

    private bool _synchronizingSelection;
    private RichTextDocument? _attachedDocument;
    private EventHandler<RichTextDocumentChangedEventArgs>? _documentChangedSubscription;
    private EventHandler? _undoStateChangedSubscription;
    private RichTextRange? _pendingPlatformSelection;
    private RichTextRange? _pendingProgrammaticSelection;
    private RichTextCharacterFormat _typingCharacterFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _typingParagraphFormat = RichTextParagraphFormat.Default;

    /// <summary>
    /// Initializes a rich editor.
    /// </summary>
    public RichEditor()
    {
        Selection = new RichTextSelection(this);
        Commands = new RichEditorCommands(this);
        AttachDocument(Document);
        RefreshTypingFormats();
        RefreshUndoState();
    }

    /// <inheritdoc />
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.OldHandler is not null && args.NewHandler is null)
        {
            Commands.Disconnect();
        }

        base.OnHandlerChanging(args);
    }

    /// <inheritdoc />
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            return;
        }

        Commands.Connect();
        ClampSelectionToDocument();
        RefreshTypingFormats();
        RefreshUndoState();
    }

    /// <summary>Occurs after an atomic content transaction is committed.</summary>
    public event EventHandler<RichTextContentChangedEventArgs>? ContentChanged;

    /// <summary>Occurs when logical text changes.</summary>
    public event EventHandler<RichTextTextChangedEventArgs>? TextChanged;

    /// <summary>Occurs when the selection or caret range changes.</summary>
    public event EventHandler<RichTextSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Occurs when selection formatting or typing attributes change.</summary>
    public event EventHandler? SelectionFormatChanged;

    /// <summary>Occurs when view or native-theme fallbacks change effective appearance.</summary>
    public event EventHandler? EffectiveAppearanceChanged;

    /// <summary>Occurs when the user invokes a hyperlink.</summary>
    public event EventHandler<RichTextLinkInvokedEventArgs>? LinkInvoked;

    /// <summary>Occurs when the user invokes an inline object.</summary>
    public event EventHandler<RichTextInlineObjectInvokedEventArgs>? InlineObjectInvoked;

    /// <summary>Occurs before a portable rich fragment is pasted.</summary>
    public event EventHandler<RichTextPastingEventArgs>? Pasting;

    /// <summary>Occurs when the native input system reports an editing completion action.</summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Gets or sets the stable live rich-text document. A document can be attached to
    /// only one editor at a time because native undo history belongs to that editor.
    /// </summary>
    public RichTextDocument Document
    {
        get => (RichTextDocument)GetValue(DocumentProperty);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(DocumentProperty, value);
        }
    }

    /// <summary>Gets or sets the selected UTF-16 document range.</summary>
    public RichTextRange SelectedRange
    {
        get => (RichTextRange)GetValue(SelectedRangeProperty);
        set => SetValue(SelectedRangeProperty, value);
    }

    /// <summary>Gets the stable selection and selection-format facade.</summary>
    public RichTextSelection Selection { get; }

    /// <summary>Gets or sets the empty-document placeholder text.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets the inherited text-color fallback for this view.</summary>
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>Gets or sets the placeholder color, or null for the native default.</summary>
    public Color? PlaceholderColor
    {
        get => (Color?)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    /// <summary>Gets or sets the inherited font-family fallback for this view.</summary>
    public string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Gets or sets the inherited font-size fallback for this view.</summary>
    [System.ComponentModel.TypeConverter(typeof(FontSizeConverter))]
    public double? FontSize
    {
        get => (double?)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Gets or sets whether the user can modify content.</summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Gets or sets whether native spell checking is enabled.</summary>
    public bool IsSpellCheckEnabled
    {
        get => (bool)GetValue(IsSpellCheckEnabledProperty);
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>Gets or sets whether native text prediction is enabled.</summary>
    public bool IsTextPredictionEnabled
    {
        get => (bool)GetValue(IsTextPredictionEnabledProperty);
        set => SetValue(IsTextPredictionEnabledProperty, value);
    }

    /// <summary>Gets or sets the native keyboard configuration.</summary>
    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetValue(KeyboardProperty, value);
        }
    }

    /// <summary>Gets or sets the maximum logical UTF-16 length, or -1 for unlimited.</summary>
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>Gets or sets the MAUI editor automatic-sizing behavior.</summary>
    public EditorAutoSizeOption AutoSize
    {
        get => (EditorAutoSizeOption)GetValue(AutoSizeProperty);
        set => SetValue(AutoSizeProperty, value);
    }

    /// <summary>Gets or sets whether a Tab key inserts a tab character.</summary>
    public bool AcceptsTab
    {
        get => (bool)GetValue(AcceptsTabProperty);
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>Gets or sets the command invoked by a native completion action.</summary>
    public ICommand? ReturnCommand
    {
        get => (ICommand?)GetValue(ReturnCommandProperty);
        set => SetValue(ReturnCommandProperty, value);
    }

    /// <summary>Gets or sets the parameter supplied to <see cref="ReturnCommand"/>.</summary>
    public object? ReturnCommandParameter
    {
        get => GetValue(ReturnCommandParameterProperty);
        set => SetValue(ReturnCommandParameterProperty, value);
    }

    /// <summary>Gets a value indicating whether an undo unit is available.</summary>
    public bool CanUndo => (bool)GetValue(CanUndoProperty);

    /// <summary>Gets a value indicating whether a redo unit is available.</summary>
    public bool CanRedo => (bool)GetValue(CanRedoProperty);

    /// <summary>Gets the stable MVVM command set.</summary>
    public RichEditorCommands Commands { get; }

    internal RichTextCharacterFormat TypingCharacterFormat => _typingCharacterFormat;

    internal RichTextParagraphFormat TypingParagraphFormat => _typingParagraphFormat;

    /// <summary>Undoes the most recent native edit, or the document edit when detached.</summary>
    public void Undo()
    {
        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.Undo();
        }
        else
        {
            Document.Undo();
        }
    }

    /// <summary>Redoes the most recent native undo, or the document undo when detached.</summary>
    public void Redo()
    {
        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.Redo();
        }
        else
        {
            Document.Redo();
        }
    }

    /// <summary>Clears native and managed undo and redo history without changing content.</summary>
    public void ClearUndoHistory()
    {
        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.ClearUndoHistory();
        }

        Document.ClearUndoHistory();
        RefreshUndoState();
    }

    /// <summary>Selects the complete logical document.</summary>
    public void SelectAll() => SelectedRange = new RichTextRange(0, Document.Length);

    /// <summary>Cuts the selected text to the system clipboard.</summary>
    /// <returns>A task that completes after the clipboard and document are updated.</returns>
    public async Task CutAsync()
    {
        var document = Document;
        var snapshot = document.CurrentSnapshot;
        var range = SelectedRange;
        if (IsReadOnly || range.IsEmpty)
        {
            return;
        }

        var fragment = RichTextDocumentFragment.FromRange(snapshot, range);
        if (Handler is IRichEditorHandler handler && handler.TryCut())
        {
            // Keep the portable fragment (including fields and library metadata)
            // while the platform performs the actual cut and owns its undo unit.
            await RichTextClipboard.SetAsync(fragment);
            return;
        }

        await RichTextClipboard.SetAsync(fragment);
        if (IsReadOnly ||
            !ReferenceEquals(Document, document) ||
            document.Version != snapshot.Version ||
            SelectedRange != range)
        {
            return;
        }

        Selection.ReplaceText(string.Empty);
    }

    /// <summary>Copies the selected text to the system clipboard.</summary>
    /// <returns>A task that completes after the clipboard is updated.</returns>
    public Task CopyAsync()
    {
        var snapshot = Document.CurrentSnapshot;
        var range = SelectedRange;
        return range.IsEmpty
            ? Task.CompletedTask
            : RichTextClipboard.SetAsync(RichTextDocumentFragment.FromRange(snapshot, range));
    }

    /// <summary>Pastes a portable fragment from the system clipboard.</summary>
    /// <returns>A task that completes after paste is committed or canceled.</returns>
    public async Task PasteAsync()
    {
        var document = Document;
        var version = document.Version;
        var range = SelectedRange;
        if (IsReadOnly)
        {
            return;
        }

        var fragment = await RichTextClipboard.GetAsync();
        if (fragment is null)
        {
            return;
        }

        var args = new RichTextPastingEventArgs(fragment);
        Pasting?.Invoke(this, args);
        if (!args.Cancel &&
            !IsReadOnly &&
            ReferenceEquals(Document, document) &&
            document.Version == version &&
            SelectedRange == range)
        {
            Selection.ReplaceFragment(args.Fragment);
        }
    }

    internal void UpdateDocumentFromPlatform(
        RichTextDocumentSnapshot snapshot,
        int selectionStart,
        int selectionLength,
        object sourceToken)
    {
        var selection = new RichTextRange(selectionStart, selectionLength);
        selection.Validate(snapshot.Text.Length, nameof(selectionLength));
        _pendingPlatformSelection = selection;
        try
        {
            var nativeUndoOwned =
                Handler is IRichEditorHandler { SupportsNativeUndo: true };
            var changes = Document.ReplaceSnapshotFromNative(
                snapshot,
                sourceToken,
                nativeUndoOwned);
            if (changes.IsEmpty)
            {
                SetSelectionFromPlatform(selection);
            }
        }
        finally
        {
            _pendingPlatformSelection = null;
        }
    }

    internal void UpdateSelectionFromPlatform(int start, int length) =>
        SetSelectionFromPlatform(new RichTextRange(start, length));

    internal void UpdateUndoStateFromPlatform() => RefreshUndoState();

    internal void RestoreDocumentFromNativeUndo(
        RichTextDocumentSnapshot snapshot,
        RichTextRange selection,
        RichTextChangeOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        selection.Validate(snapshot.Length, nameof(selection));
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = selection;
        RichTextChangeSet changes;
        try
        {
            changes = Document.RestoreSnapshotFromNativeUndo(snapshot, origin);
        }
        finally
        {
            _pendingProgrammaticSelection = previousPendingSelection;
        }

        if (changes.IsEmpty)
        {
            SelectedRange = selection;
        }
    }

    internal RichTextChangeSet EditDocument(
        Action<RichTextDocumentEdit> edit,
        RichTextRange resultingSelection)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = resultingSelection;
        RichTextChangeSet changes;
        try
        {
            changes = Document.Edit(edit);
        }
        finally
        {
            _pendingProgrammaticSelection = previousPendingSelection;
        }

        resultingSelection.Validate(Document.Length, nameof(resultingSelection));
        if (changes.IsEmpty)
        {
            SelectedRange = resultingSelection;
        }

        return changes;
    }

    internal void SetTypingCharacterFormat(RichTextCharacterFormat format)
    {
        _typingCharacterFormat = format ?? throw new ArgumentNullException(nameof(format));
        ApplyTypingFormatToHandler();
        RaiseSelectionFormatChanged();
    }

    internal void SetTypingParagraphFormat(RichTextParagraphFormat format)
    {
        _typingParagraphFormat = format ?? throw new ArgumentNullException(nameof(format));
        ApplyTypingFormatToHandler();
        RaiseSelectionFormatChanged();
    }

    internal void UpdateTypingFormatsFromPlatform(
        RichTextCharacterFormat characterFormat,
        RichTextParagraphFormat paragraphFormat)
    {
        _typingCharacterFormat = characterFormat ??
            throw new ArgumentNullException(nameof(characterFormat));
        _typingParagraphFormat = paragraphFormat ??
            throw new ArgumentNullException(nameof(paragraphFormat));
        RaiseSelectionFormatChanged();
    }

    internal bool RaiseLinkInvoked(RichTextLink link)
    {
        var args = new RichTextLinkInvokedEventArgs(
            new RichTextRange(link.Start, link.Length),
            link.Target,
            link.ToolTip);
        LinkInvoked?.Invoke(this, args);
        return !args.Handled;
    }

    internal bool RaiseInlineObjectInvoked(RichTextImage image)
    {
        var args = new RichTextInlineObjectInvokedEventArgs(image);
        InlineObjectInvoked?.Invoke(this, args);
        return !args.Handled;
    }

    internal void RaiseCompleted()
    {
        if (ReturnCommand?.CanExecute(ReturnCommandParameter) == true)
        {
            ReturnCommand.Execute(ReturnCommandParameter);
        }

        Completed?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifyNativeAppearanceChanged()
    {
        EffectiveAppearanceChanged?.Invoke(this, EventArgs.Empty);
        RaiseSelectionFormatChanged();
    }

    internal void RaiseSelectionFormatChanged()
    {
        Selection.RefreshFormatting();
        SelectionFormatChanged?.Invoke(this, EventArgs.Empty);
        Commands.Refresh();
    }

    private void OnDocumentPropertyChanged(
        RichTextDocument? oldDocument,
        RichTextDocument newDocument)
    {
        if (ReferenceEquals(oldDocument, newDocument))
        {
            return;
        }

        newDocument.VerifyCanAttachEditor(this);
        DetachDocument(oldDocument);
        AttachDocument(newDocument);
        var clampedStart = Math.Clamp(SelectedRange.Start, 0, newDocument.Length);
        var clampedLength = Math.Clamp(
            SelectedRange.Length,
            0,
            newDocument.Length - clampedStart);
        var selection = new RichTextRange(clampedStart, clampedLength);
        var selectionChanged = SelectedRange != selection;
        SetSelectionCore(selection, fromPlatform: false);
        RefreshTypingFormats();
        RefreshUndoState();
        if (!selectionChanged)
        {
            RaiseSelectionFormatChanged();
        }
    }

    private void OnSelectedRangePropertyChanged(
        RichTextRange oldRange,
        RichTextRange newRange)
    {
        RefreshTypingFormats();
        if (!_synchronizingSelection && Handler is IRichEditorHandler handler)
        {
            var characterFormat = _typingCharacterFormat;
            var paragraphFormat = _typingParagraphFormat;
            handler.SetSelection(newRange);
            handler.ApplyTypingFormat(characterFormat, paragraphFormat);
        }

        SelectionChanged?.Invoke(
            this,
            new RichTextSelectionChangedEventArgs(oldRange, newRange));
        RaiseSelectionFormatChanged();
    }

    private void AttachDocument(RichTextDocument document)
    {
        if (ReferenceEquals(_attachedDocument, document))
        {
            return;
        }

        document.AttachEditor(this);
        _attachedDocument = document;
        var weakEditor = new WeakReference<RichEditor>(this);
        EventHandler<RichTextDocumentChangedEventArgs>? changed = null;
        changed = (sender, eventArgs) =>
        {
            if (weakEditor.TryGetTarget(out var editor))
            {
                editor.OnDocumentChanged(sender, eventArgs);
            }
            else if (sender is RichTextDocument source)
            {
                source.Changed -= changed;
            }
        };
        EventHandler? undoStateChanged = null;
        undoStateChanged = (sender, eventArgs) =>
        {
            if (weakEditor.TryGetTarget(out var editor))
            {
                editor.OnUndoStateChanged(sender, eventArgs);
            }
            else if (sender is RichTextDocument source)
            {
                source.UndoStateChanged -= undoStateChanged;
            }
        };
        _documentChangedSubscription = changed;
        _undoStateChangedSubscription = undoStateChanged;
        document.Changed += changed;
        document.UndoStateChanged += undoStateChanged;
    }

    private void DetachDocument(RichTextDocument? document)
    {
        if (document is null)
        {
            return;
        }

        if (_documentChangedSubscription is not null)
        {
            document.Changed -= _documentChangedSubscription;
            _documentChangedSubscription = null;
        }

        if (_undoStateChangedSubscription is not null)
        {
            document.UndoStateChanged -= _undoStateChangedSubscription;
            _undoStateChangedSubscription = null;
        }
        document.DetachEditor(this);
        if (ReferenceEquals(_attachedDocument, document))
        {
            _attachedDocument = null;
        }
    }

    private void ClampSelectionToDocument()
    {
        var start = Math.Clamp(SelectedRange.Start, 0, Document.Length);
        var length = Math.Clamp(SelectedRange.Length, 0, Document.Length - start);
        SetSelectionCore(new RichTextRange(start, length), fromPlatform: false);
    }

    private void OnDocumentChanged(object? sender, RichTextDocumentChangedEventArgs eventArgs)
    {
        var changeSet = eventArgs.ChangeSet;
        var resultingSelection = _pendingPlatformSelection ??
            _pendingProgrammaticSelection ??
            MapSelection(SelectedRange, changeSet, Document.Length);

        var snapshot = Document.CurrentSnapshot;
        var typingCharacterFormat = snapshot.GetCaretFormat(resultingSelection.Start);
        var typingParagraphFormat = snapshot.GetParagraphFormat(resultingSelection.Start);

        var handler = Handler as IRichEditorHandler;
        var appliedToPlatform = handler is not null &&
            !ReferenceEquals(changeSet.SourceToken, handler.SourceToken);
        if (appliedToPlatform)
        {
            if (changeSet.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
            {
                handler!.ApplySnapshot(
                    snapshot,
                    resultingSelection,
                    typingCharacterFormat,
                    typingParagraphFormat);
            }
            else
            {
                handler!.ApplyChanges(
                    changeSet,
                    resultingSelection,
                    typingCharacterFormat,
                    typingParagraphFormat);
            }
        }

        var selectionChanged = SelectedRange != resultingSelection;
        SetSelectionCore(
            resultingSelection,
            fromPlatform: appliedToPlatform ||
                changeSet.Origin == RichTextChangeOrigin.User);

        RefreshUndoState();
        if (AutoSize == EditorAutoSizeOption.TextChanges && changeSet.IsTextChanged)
        {
            InvalidateMeasure();
        }

        if (!selectionChanged)
        {
            RaiseSelectionFormatChanged();
        }

        // Public observers must see one coherent committed state. In particular,
        // a destructive edit can make the previous selection invalid, so publish
        // content events only after the selection has been advanced to the new
        // document version.
        ContentChanged?.Invoke(this, new RichTextContentChangedEventArgs(changeSet));
        if (changeSet.IsTextChanged)
        {
            TextChanged?.Invoke(this, new RichTextTextChangedEventArgs(changeSet));
        }
    }

    private void OnUndoStateChanged(object? sender, EventArgs eventArgs) => RefreshUndoState();

    private void SetSelectionFromPlatform(RichTextRange range)
    {
        range.Validate(Document.Length, nameof(range));
        SetSelectionCore(range, fromPlatform: true);
    }

    private void SetSelectionCore(RichTextRange range, bool fromPlatform)
    {
        range.Validate(Document.Length, nameof(range));
        if (SelectedRange == range)
        {
            RefreshTypingFormats();
            return;
        }

        _synchronizingSelection = fromPlatform;
        try
        {
            SetValue(SelectedRangeProperty, range);
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void RefreshTypingFormats()
    {
        var snapshot = Document.CurrentSnapshot;
        _typingCharacterFormat = snapshot.GetCaretFormat(SelectedRange.Start);
        _typingParagraphFormat = snapshot.GetParagraphFormat(SelectedRange.Start);
    }

    private void ApplyTypingFormatToHandler()
    {
        if (Handler is IRichEditorHandler handler)
        {
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
    }

    private void RefreshUndoState()
    {
        var handler = Handler as IRichEditorHandler;
        var useNative = handler?.SupportsNativeUndo == true;
        SetValue(CanUndoPropertyKey, useNative ? handler!.CanUndo : Document.CanUndo);
        SetValue(CanRedoPropertyKey, useNative ? handler!.CanRedo : Document.CanRedo);
        Commands.Refresh();
    }

    private void OnAppearanceChanged()
    {
        EffectiveAppearanceChanged?.Invoke(this, EventArgs.Empty);
        RaiseSelectionFormatChanged();
    }

    private static RichTextRange MapSelection(
        RichTextRange selection,
        RichTextChangeSet changes,
        int documentLength)
    {
        var start = selection.Start;
        var end = selection.End;
        foreach (var textChange in changes.Changes.OfType<RichTextTextChange>())
        {
            start = MapPosition(start, textChange);
            end = MapPosition(end, textChange);
        }

        start = Math.Clamp(start, 0, documentLength);
        end = Math.Clamp(end, start, documentLength);
        return new RichTextRange(start, end - start);
    }

    private static bool IsValidAppearanceColor(object? value) =>
        value is null ||
        value is Color color &&
        float.IsFinite(color.Red) &&
        float.IsFinite(color.Green) &&
        float.IsFinite(color.Blue) &&
        float.IsFinite(color.Alpha);

    private static int MapPosition(int position, RichTextTextChange change)
    {
        if (position <= change.OldRange.Start)
        {
            return position;
        }

        if (position >= change.OldRange.End)
        {
            return checked(position + change.NewRange.Length - change.OldRange.Length);
        }

        return change.NewRange.End;
    }
}
