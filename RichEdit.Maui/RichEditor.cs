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
        {
            var editor = (RichEditor)bindable;
            editor.VerifyAccess();
            if (value is not RichTextDocument document || !document.CanAttachEditor(bindable)) return false;
            if (!ReferenceEquals(editor._attachedDocument, document))
            {
                editor._attachedDocument?.VerifyAttachmentChange();
                document.VerifyAttachmentChange();
            }
            return true;
        },
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((RichEditor)bindable).OnDocumentPropertyChanged(
                (RichTextDocument?)oldValue,
                (RichTextDocument)newValue));

    /// <summary>Identifies the directional <see cref="SelectionState"/> bindable property.</summary>
    public static readonly BindableProperty SelectionStateProperty = BindableProperty.Create(
        nameof(SelectionState), typeof(RichTextSelectionState), typeof(RichEditor), default(RichTextSelectionState), BindingMode.TwoWay,
        coerceValue: static (bindable, value) =>
        {
            var editor = (RichEditor)bindable;
            editor.VerifyAccess();
            return ((RichTextSelectionState)value).Clamp(editor.Document.Length);
        },
        propertyChanged: static (bindable, before, after) => ((RichEditor)bindable).OnSelectionStateChanged(
            (RichTextSelectionState)before, (RichTextSelectionState)after));

    /// <summary>Identifies the <see cref="SelectedRange"/> bindable property.</summary>
    public static readonly BindableProperty SelectedRangeProperty = BindableProperty.Create(
        nameof(SelectedRange),
        typeof(RichTextRange),
        typeof(RichEditor),
        RichTextRange.Empty,
        BindingMode.TwoWay,
        coerceValue: static (bindable, value) =>
        {
            ((RichEditor)bindable).VerifyAccess();
            var length = ((RichEditor)bindable).Document.Length;
            var range = value is RichTextRange richRange ? richRange : RichTextRange.Empty;
            var start = Math.Clamp(range.Start, 0, length);
            return new RichTextRange(start, Math.Clamp(range.Length, 0, length - start));
        },
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

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _synchronizingSelection;
    private bool _updatingSelectionRange;
    private bool _deferNotifications;
    private readonly List<string> _deferredProperties = [];
    private RichTextSelectionState _selectionBeforeNotification;
    private RichTextDocument? _attachedDocument;
    private RichTextSelectionState? _pendingPlatformSelection;
    private RichTextSelectionState? _pendingProgrammaticSelection;
    private RichTextCharacterFormat _typingCharacterFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _typingParagraphFormat = RichTextParagraphFormat.Default;

    /// <summary>
    /// Initializes a rich editor.
    /// </summary>
    public RichEditor()
    {
        Decorations = new RichTextDecorations(this);
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
            RefreshUndoState();
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
    /// one editor at a time; the document owns content history and saved-state identity.
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

    /// <summary>
    /// Gets or sets the selected UTF-16 document range. Assigned values are clamped
    /// to the current document bounds.
    /// </summary>
    public RichTextRange SelectedRange
    {
        get => (RichTextRange)GetValue(SelectedRangeProperty);
        set => SelectionState = RichTextSelectionState.FromRange(value);
    }

    /// <summary>Gets or sets the anchor and active UTF-16 offsets, clamped to the document.</summary>
    public RichTextSelectionState SelectionState
    {
        get => (RichTextSelectionState)GetValue(SelectionStateProperty);
        set { VerifyAccess(); SetValue(SelectionStateProperty, value); }
    }

    /// <summary>Gets the stable selection and selection-format facade.</summary>
    public RichTextSelection Selection { get; }

    /// <summary>Gets the view-owned layers for syntax colors, diagnostics, and other nonpersistent appearance.</summary>
    public RichTextDecorations Decorations { get; }

    internal RichTextDocumentSnapshot PresentationSnapshot => Decorations.Project(Document.CurrentSnapshot);

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

    /// <summary>Undoes the most recent edit.</summary>
    public void Undo()
    {
        VerifyAccess();
        if (IsReadOnly)
        {
            return;
        }

        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.Undo();
        }
        else
        {
            Document.Undo();
        }
    }

    /// <summary>Reapplies the most recently undone edit.</summary>
    public void Redo()
    {
        VerifyAccess();
        if (IsReadOnly)
        {
            return;
        }

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
        VerifyAccess();
        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.ClearUndoHistory();
        }

        Document.ClearUndoHistory();
        RefreshUndoState();
    }

    /// <summary>Selects the complete logical document.</summary>
    public void SelectAll() => SelectedRange = new RichTextRange(0, Document.Length);

    /// <summary>Brings a document range into view without changing the selection.</summary>
    /// <param name="range">The bounded UTF-16 range to reveal.</param>
    public void ScrollIntoView(RichTextRange range)
    {
        VerifyAccess();
        range.Validate(Document.Length, nameof(range));
        (Handler as IRichEditorHandler)?.ScrollIntoView(range);
    }

    /// <summary>Cuts the selected text to the system clipboard.</summary>
    /// <returns>A task that completes after the clipboard and document are updated.</returns>
    public async Task CutAsync()
    {
        VerifyAccess();
        var document = Document;
        var snapshot = document.CurrentSnapshot;
        var range = SelectedRange;
        if (IsReadOnly || range.IsEmpty)
        {
            return;
        }

        var fragment = RichTextDocumentFragment.FromRange(snapshot, range);
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
        VerifyAccess();
        var snapshot = Document.CurrentSnapshot;
        var range = SelectedRange;
        return range.IsEmpty
            ? Task.CompletedTask
            : RichTextClipboard.SetAsync(RichTextDocumentFragment.FromRange(snapshot, range));
    }

    /// <summary>Pastes a portable fragment from the system clipboard.</summary>
    /// <returns>A task that completes after paste is committed or canceled.</returns>
    public Task PasteAsync() => PasteAsync(asPlainText: false);

    internal async Task PasteAsync(bool asPlainText)
    {
        VerifyAccess();
        var document = Document;
        var version = document.Version;
        var range = SelectedRange;
        if (IsReadOnly)
        {
            return;
        }

        var fragment = await RichTextClipboard.GetAsync(asPlainText);
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
            SelectedRange == range &&
            LimitFragmentToMaxLength(args.Fragment, range) is { } accepted)
        {
            Selection.ReplaceFragment(accepted);
        }
    }

    private RichTextDocumentFragment? LimitFragmentToMaxLength(
        RichTextDocumentFragment fragment,
        RichTextRange replacedRange)
    {
        var maxLength = MaxLength;
        if (maxLength < 0)
        {
            return fragment;
        }

        var available = maxLength - (Document.Length - replacedRange.Length);
        if (fragment.Text.Length <= available)
        {
            return fragment;
        }

        if (available > 0 && char.IsHighSurrogate(fragment.Text[available - 1]))
        {
            available--;
        }

        return available <= 0
            ? null
            : fragment.IsPlainText
                ? RichTextDocumentFragment.FromPlainText(fragment.Text[..available])
                : RichTextDocumentFragment.FromRange(fragment.Snapshot, new RichTextRange(0, available));
    }

    internal void UpdateDocumentFromPlatform(
        RichTextDocumentSnapshot snapshot,
        int selectionStart,
        int selectionLength,
        object sourceToken,
        RichTextChangeOrigin origin = RichTextChangeOrigin.User,
        bool mergeWithPrevious = false,
        long? projectedVersion = null,
        RichTextSelectionState? selectionState = null)
    {
        VerifyAccess();
        snapshot = Decorations.RestoreAuthoredSnapshot(snapshot, SelectedRange);
        var selection = selectionState ?? RichTextSelectionState.FromRange(new RichTextRange(selectionStart, selectionLength));
        selection.Range.Validate(snapshot.Text.Length, nameof(selectionLength));
        _pendingPlatformSelection = selection;
        try
        {
            var nativeUndoOwned =
                Handler is IRichEditorHandler { SupportsNativeUndo: true };
            var changes = Document.ReplaceSnapshotFromNative(
                snapshot,
                sourceToken,
                nativeUndoOwned,
                origin,
                mergeWithPrevious,
                projectedVersion);
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
        UpdateSelectionFromPlatform(RichTextSelectionState.FromRange(new RichTextRange(start, length)));

    internal void UpdateSelectionFromPlatform(RichTextSelectionState selection) => SetSelectionFromPlatform(selection);

    internal RichTextSelectionState InferSelectionState(RichTextRange range)
    {
        var previous = SelectionState;
        if (previous.Range == range) return previous;
        if (range.End == previous.Anchor) return new RichTextSelectionState(range.End, range.Start);
        if (range.Start == previous.Anchor) return new RichTextSelectionState(range.Start, range.End);
        if (range.End == previous.Range.End) return new RichTextSelectionState(range.End, range.Start);
        return RichTextSelectionState.FromRange(range);
    }

    internal void UpdateUndoStateFromPlatform() => RefreshUndoState();

    internal void RestoreDocumentFromNativeUndo(
        RichTextDocumentSnapshot snapshot,
        RichTextRange selection,
        RichTextChangeOrigin origin,
        object? sourceToken = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        selection.Validate(snapshot.Length, nameof(selection));
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = RichTextSelectionState.FromRange(selection);
        RichTextChangeSet changes;
        try
        {
            changes = Document.RestoreSnapshotFromNativeUndo(snapshot, origin, sourceToken);
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
        RichTextRange resultingSelection) => EditDocument(edit, RichTextSelectionState.FromRange(resultingSelection));

    internal RichTextChangeSet EditDocument(
        Action<RichTextDocumentEdit> edit,
        RichTextSelectionState resultingSelection,
        RichTextEditOptions options = default)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(edit);
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = resultingSelection;
        RichTextChangeSet changes;
        try
        {
            changes = Document.Edit(edit, options);
        }
        finally
        {
            _pendingProgrammaticSelection = previousPendingSelection;
        }

        resultingSelection.Range.Validate(Document.Length, nameof(resultingSelection));
        if (changes.IsEmpty)
        {
            SelectionState = resultingSelection;
        }

        return changes;
    }

    internal void SetTypingCharacterFormat(RichTextCharacterFormat format)
    {
        VerifyAccess();
        _typingCharacterFormat = RichTextDocumentSnapshot.Validate(format);
        Document.BreakUndoGroup();
        ApplyTypingFormatToHandler();
        RaiseSelectionFormatChanged();
    }

    internal void SetTypingParagraphFormat(RichTextParagraphFormat format)
    {
        VerifyAccess();
        _typingParagraphFormat = RichTextDocumentSnapshot.Validate(format);
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
        if (_deferNotifications) return;
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
        Decorations.Reset();
        var selection = SelectionState.Clamp(newDocument.Length);
        var selectionChanged = SelectionState != selection;
        // The handler's Document mapper projects the new text and selection
        // together. Updating the old native text's selection here can report
        // that old content back into the newly attached document.
        SetSelectionCore(selection, fromPlatform: true);
        RefreshTypingFormats();
        RefreshUndoState();
        if (!selectionChanged)
        {
            RaiseSelectionFormatChanged();
        }
    }

    private void OnSelectedRangePropertyChanged(RichTextRange oldRange, RichTextRange newRange)
    {
        if (!_updatingSelectionRange) SelectionState = RichTextSelectionState.FromRange(newRange);
    }

    private void OnSelectionStateChanged(RichTextSelectionState before, RichTextSelectionState after)
    {
        _updatingSelectionRange = true;
        try { SetValue(SelectedRangeProperty, after.Range); }
        finally { _updatingSelectionRange = false; }
        if (_pendingPlatformSelection is null && _pendingProgrammaticSelection is null && !_deferNotifications)
            Document.BreakUndoGroupForSelection(after.Range);
        RefreshTypingFormats();
        if (!_synchronizingSelection && Handler is IRichEditorHandler handler)
        {
            handler.SetSelection(after);
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
        if (_deferNotifications) return;
        SelectionChanged?.Invoke(this, new RichTextSelectionChangedEventArgs(before, after));
        RaiseSelectionFormatChanged();
    }

    private void AttachDocument(RichTextDocument document)
    {
        if (ReferenceEquals(_attachedDocument, document)) return;
        document.AttachEditor(this);
        _attachedDocument = document;
    }

    private void DetachDocument(RichTextDocument? document)
    {
        document?.DetachEditor(this);
        if (ReferenceEquals(_attachedDocument, document)) _attachedDocument = null;
    }

    private void ClampSelectionToDocument() => SetSelectionCore(SelectionState.Clamp(Document.Length), fromPlatform: false);

    internal RichTextSelectionState GetSelectionAfterEdit(IEnumerable<RichTextChange> changes, int length)
    {
        if (_pendingProgrammaticSelection is { } requested) requested.Range.Validate(length, nameof(requested));
        return (_pendingPlatformSelection ?? _pendingProgrammaticSelection ?? SelectionState.Map(changes, length)).Clamp(length);
    }

    internal void SynchronizeDocumentChange(RichTextChangeSet changeSet)
    {
        _deferNotifications = true;
        _selectionBeforeNotification = SelectionState;
        Decorations.MapThrough(changeSet);
        var selection = changeSet.SelectionAfter ?? GetSelectionAfterEdit(changeSet.Changes, Document.Length);
        SetSelectionCore(selection, fromPlatform: true);
        var handler = Handler as IRichEditorHandler;
        if (handler is not null && !ReferenceEquals(changeSet.SourceToken, handler.SourceToken))
        {
            if (changeSet.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
                handler.ApplySnapshot(Document.CurrentSnapshot, selection.Range, _typingCharacterFormat, _typingParagraphFormat);
            else
                handler.ApplyChanges(changeSet, selection.Range, _typingCharacterFormat, _typingParagraphFormat);
        }
        RefreshUndoState();
        if (AutoSize == EditorAutoSizeOption.TextChanges && changeSet.IsTextChanged) InvalidateMeasure();
    }

    internal void ApplyDecorationChanges(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after, RichTextRange? dirtyRange = null)
    {
        VerifyAccess();
        var changes = RichTextDocument.CreateDelta(before, after).ToList();
        if (dirtyRange is { IsEmpty: false } range)
            changes.Add(new RichTextRangeChange(RichTextChangeKind.CharacterFormat, range, range));
        if (changes.Count == 0 || Handler is not IRichEditorHandler handler) return;
        var changeSet = new RichTextChangeSet(Document.Version, Document.Version, RichTextChangeOrigin.Programmatic,
            [.. changes], tag: null, beforeSnapshot: before, afterSnapshot: after);
        handler.ApplyDecorations(changeSet);
    }

    internal void PublishContentChange(RichTextChangeSet changeSet)
    {
        ContentChanged?.Invoke(this, new RichTextContentChangedEventArgs(changeSet));
        if (changeSet.IsTextChanged) TextChanged?.Invoke(this, new RichTextTextChangedEventArgs(changeSet));
    }

    internal void PublishSelectionChange()
    {
        _deferNotifications = false;
        foreach (var property in _deferredProperties) base.OnPropertyChanged(property);
        _deferredProperties.Clear();
        if (_selectionBeforeNotification != SelectionState)
            SelectionChanged?.Invoke(this, new RichTextSelectionChangedEventArgs(_selectionBeforeNotification, SelectionState));
        RaiseSelectionFormatChanged();
        Commands.Refresh();
        Decorations.NotifyChanged();
    }

    internal void EndDocumentChange()
    {
        _deferNotifications = false;
        _deferredProperties.Clear();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        if (_deferNotifications && propertyName is not null)
        {
            if (!_deferredProperties.Contains(propertyName)) _deferredProperties.Add(propertyName);
        }
        else base.OnPropertyChanged(propertyName);
    }

    internal void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("An attached document and its editor must be mutated on the editor's owning UI thread. Use an immutable snapshot for background work.");
    }

    private void SetSelectionFromPlatform(RichTextSelectionState selection) => SetSelectionCore(selection, fromPlatform: true);

    private void SetSelectionCore(RichTextSelectionState selection, bool fromPlatform)
    {
        selection.Range.Validate(Document.Length, nameof(selection));
        if (SelectionState == selection) { RefreshTypingFormats(); return; }
        _synchronizingSelection = fromPlatform;
        try { SetValue(SelectionStateProperty, selection); }
        finally { _synchronizingSelection = false; }
    }

    private void RefreshTypingFormats()
    {
        var snapshot = Document.CurrentSnapshot;
        _typingCharacterFormat = snapshot.GetCaretFormat(SelectionState.Active);
        _typingParagraphFormat = snapshot.GetParagraphFormat(SelectionState.Active);
    }

    private void ApplyTypingFormatToHandler()
    {
        if (Handler is IRichEditorHandler handler)
        {
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
    }

    internal void RefreshUndoState()
    {
        var handler = Handler as IRichEditorHandler;
        var useNative = handler?.SupportsNativeUndo == true;
        SetValue(CanUndoPropertyKey, useNative ? handler!.CanUndo : Document.CanUndo);
        SetValue(CanRedoPropertyKey, useNative ? handler!.CanRedo : Document.CanRedo);
        if (!_deferNotifications) Commands.Refresh();
    }

    private void OnAppearanceChanged()
    {
        EffectiveAppearanceChanged?.Invoke(this, EventArgs.Empty);
        RaiseSelectionFormatChanged();
    }

    private static bool IsValidAppearanceColor(object? value) =>
        value is null ||
        value is Color color &&
        float.IsFinite(color.Red) &&
        float.IsFinite(color.Green) &&
        float.IsFinite(color.Blue) &&
        float.IsFinite(color.Alpha);

}
