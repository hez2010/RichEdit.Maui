using System.Windows.Input;

namespace RichEdit.Maui;

/// <summary>
/// The shared native editing surface used by typed MAUI editor controls.
/// </summary>
public partial class NativeEditorView : View
{
    internal object EditSourceToken { get; } = new();

    /// <summary>Identifies the <see cref="Document"/> bindable property.</summary>
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(RichTextDocument),
        typeof(NativeEditorView),
        defaultValueCreator: static _ => new RichTextDocument(),
        defaultBindingMode: BindingMode.OneWay,
        validateValue: static (bindable, value) =>
        {
            var editor = (NativeEditorView)bindable;
            editor.VerifyAccess();
            if (value is not RichTextDocument document || !document.CanAttachEditor(bindable))
                return false;
            if (!ReferenceEquals(editor._attachedDocument, document))
            {
                editor._attachedDocument?.VerifyAttachmentChange();
                document.VerifyAttachmentChange();
            }

            return true;
        },
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((NativeEditorView)bindable).OnDocumentPropertyChanged(
                (RichTextDocument?)oldValue,
                (RichTextDocument)newValue));

    /// <summary>Identifies the directional <see cref="SelectionState"/> bindable property.</summary>
    public static readonly BindableProperty SelectionStateProperty = BindableProperty.Create(
        nameof(SelectionState), typeof(TextSelectionState), typeof(NativeEditorView), default(TextSelectionState), BindingMode.TwoWay,
        coerceValue: static (bindable, value) =>
        {
            var editor = (NativeEditorView)bindable;
            editor.VerifyAccess();
            editor.VerifySelectionMutation();
            return ((TextSelectionState)value).Clamp(editor.Document.Length);
        },
        propertyChanged: static (bindable, before, after) => ((NativeEditorView)bindable).OnSelectionStateChanged(
            (TextSelectionState)before, (TextSelectionState)after));

    /// <summary>Identifies the <see cref="SelectedRange"/> bindable property.</summary>
    public static readonly BindableProperty SelectedRangeProperty = BindableProperty.Create(
        nameof(SelectedRange),
        typeof(TextSpan),
        typeof(NativeEditorView),
        TextSpan.Empty,
        BindingMode.TwoWay,
        coerceValue: static (bindable, value) =>
        {
            ((NativeEditorView)bindable).VerifyAccess();
            ((NativeEditorView)bindable).VerifySelectionMutation();
            var length = ((NativeEditorView)bindable).Document.Length;
            var range = value is TextSpan richRange ? richRange : TextSpan.Empty;
            var start = Math.Clamp(range.Start, 0, length);
            return new TextSpan(start, Math.Clamp(range.Length, 0, length - start));
        },
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((NativeEditorView)bindable).OnSelectedRangePropertyChanged(
                (TextSpan)oldValue,
                (TextSpan)newValue));

    /// <summary>Identifies the <see cref="Placeholder"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(NativeEditorView),
        string.Empty,
        coerceValue: static (_, value) => value ?? string.Empty);

    /// <summary>Identifies the <see cref="TextColor"/> bindable property.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(NativeEditorView),
        validateValue: static (_, value) => IsValidAppearanceColor(value),
        propertyChanged: static (bindable, _, _) => ((NativeEditorView)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="PlaceholderColor"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor),
        typeof(Color),
        typeof(NativeEditorView),
        validateValue: static (_, value) => IsValidAppearanceColor(value));

    /// <summary>Identifies the <see cref="FontFamily"/> bindable property.</summary>
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily),
        typeof(string),
        typeof(NativeEditorView),
        propertyChanged: static (bindable, _, _) => ((NativeEditorView)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="FontSize"/> bindable property.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double?),
        typeof(NativeEditorView),
        validateValue: static (_, value) => value is null ||
            value is double size && double.IsFinite(size) && size > 0 && size <= float.MaxValue,
        propertyChanged: static (bindable, _, _) => ((NativeEditorView)bindable).OnAppearanceChanged());

    /// <summary>Identifies the <see cref="IsReadOnly"/> bindable property.</summary>
    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(NativeEditorView),
        false,
        coerceValue: static (bindable, value) =>
        {
            var editor = (NativeEditorView)bindable;
            editor.VerifyAccess();
            if (editor.StructuredSession is { } session && session.IsReadOnly != (bool)value) session.IsReadOnly = (bool)value;
            return value;
        },
        propertyChanged: static (bindable, _, _) => ((NativeEditorView)bindable).Commands.Refresh());

    /// <summary>Identifies the <see cref="IsSpellCheckEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled),
        typeof(bool?),
        typeof(NativeEditorView));

    /// <summary>Identifies the <see cref="IsTextPredictionEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsTextPredictionEnabledProperty = BindableProperty.Create(
        nameof(IsTextPredictionEnabled),
        typeof(bool?),
        typeof(NativeEditorView));

    /// <summary>Identifies the <see cref="Keyboard"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardProperty = BindableProperty.Create(
        nameof(Keyboard),
        typeof(Keyboard),
        typeof(NativeEditorView),
        Keyboard.Default,
        validateValue: static (_, value) => value is Keyboard);

    /// <summary>Identifies the <see cref="MaxLength"/> bindable property.</summary>
    public static readonly BindableProperty MaxLengthProperty = BindableProperty.Create(
        nameof(MaxLength),
        typeof(int),
        typeof(NativeEditorView),
        -1,
        validateValue: static (_, value) => value is int length && length >= -1);

    /// <summary>Identifies the <see cref="AutoSize"/> bindable property.</summary>
    public static readonly BindableProperty AutoSizeProperty = BindableProperty.Create(
        nameof(AutoSize),
        typeof(EditorAutoSizeOption),
        typeof(NativeEditorView),
        EditorAutoSizeOption.Disabled,
        validateValue: static (_, value) =>
            value is EditorAutoSizeOption option && Enum.IsDefined(option),
        propertyChanged: static (bindable, _, _) => ((NativeEditorView)bindable).InvalidateMeasure());

    /// <summary>Identifies the <see cref="AcceptsTab"/> bindable property.</summary>
    public static readonly BindableProperty AcceptsTabProperty = BindableProperty.Create(
        nameof(AcceptsTab),
        typeof(bool),
        typeof(NativeEditorView),
        false);

    /// <summary>Identifies the <see cref="ReturnCommand"/> bindable property.</summary>
    public static readonly BindableProperty ReturnCommandProperty = BindableProperty.Create(
        nameof(ReturnCommand),
        typeof(ICommand),
        typeof(NativeEditorView));

    /// <summary>Identifies the <see cref="ReturnCommandParameter"/> bindable property.</summary>
    public static readonly BindableProperty ReturnCommandParameterProperty = BindableProperty.Create(
        nameof(ReturnCommandParameter),
        typeof(object),
        typeof(NativeEditorView));

    private static readonly BindablePropertyKey CanUndoPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CanUndo),
        typeof(bool),
        typeof(NativeEditorView),
        false);

    /// <summary>Identifies the read-only <see cref="CanUndo"/> bindable property.</summary>
    public static readonly BindableProperty CanUndoProperty = CanUndoPropertyKey.BindableProperty;

    private static readonly BindablePropertyKey CanRedoPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CanRedo),
        typeof(bool),
        typeof(NativeEditorView),
        false);

    /// <summary>Identifies the read-only <see cref="CanRedo"/> bindable property.</summary>
    public static readonly BindableProperty CanRedoProperty = CanRedoPropertyKey.BindableProperty;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _synchronizingSelection;
    private bool _updatingSelectionRange;
    private bool _deferNotifications;
    private readonly List<string> _deferredProperties = [];
    private TextSelectionState _selectionBeforeNotification;
    private RichTextDocument? _attachedDocument;
    private TextSelectionState? _pendingPlatformSelection;
    private TextSelectionState? _pendingProgrammaticSelection;
    private RichTextCharacterFormat _typingCharacterFormat = RichTextCharacterFormat.Default;
    private RichTextParagraphFormat _typingParagraphFormat = RichTextParagraphFormat.Default;

    /// <summary>
    /// Initializes a rich editor.
    /// </summary>
    public NativeEditorView()
    {
        TextLayout = new RichTextLayout(this);
        KeyBindings = new EditorKeyBindingCollection(this);
        Decorations = new RichTextDecorations(this);
        Folding = new RichTextFolding(this);
        Adornments = new RichTextAdornments(this);
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

    /// <summary>Occurs after a replacement document is attached and view presentation is cleared.</summary>
    public event EventHandler<RichTextDocumentReplacedEventArgs>? DocumentChanged;

    /// <summary>Gets visible native geometry and hit testing in this editor's coordinates.</summary>
    public RichTextLayout TextLayout { get; }

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
    public TextSpan SelectedRange
    {
        get => (TextSpan)GetValue(SelectedRangeProperty);
        set => SelectionState = TextSelectionState.FromRange(value);
    }

    /// <summary>Gets or sets the anchor and active UTF-16 offsets, clamped to the document.</summary>
    public TextSelectionState SelectionState
    {
        get => (TextSelectionState)GetValue(SelectionStateProperty);
        set
        {
            VerifyAccess();
            SetValue(SelectionStateProperty, value);
        }
    }

    /// <summary>Gets the stable selection and selection-format facade.</summary>
    public RichTextSelection Selection { get; }

    /// <summary>Gets the view-owned layers for syntax colors, diagnostics, and other nonpersistent appearance.</summary>
    public RichTextDecorations Decorations { get; }

    /// <summary>Gets the explicitly controlled, view-owned collapsed source ranges.</summary>
    public RichTextFolding Folding { get; }

    /// <summary>Gets the source-anchored views displayed outside document content and undo/redo history.</summary>
    public RichTextAdornments Adornments { get; }

    internal RichTextDocumentSnapshot PresentationSnapshot => Composition.IsActive && Handler is RichEditorHandler handler &&
        handler.GetCompositionPresentation() is { } painted ? painted : Decorations.Project(InputSnapshot);

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

    /// <summary>Gets or sets the native spell-check override. Null uses the keyboard configuration.</summary>
    public bool? IsSpellCheckEnabled
    {
        get => (bool?)GetValue(IsSpellCheckEnabledProperty);
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>Gets or sets the native text-prediction override. Null uses the keyboard configuration.</summary>
    public bool? IsTextPredictionEnabled
    {
        get => (bool?)GetValue(IsTextPredictionEnabledProperty);
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
        if (IsReadOnly || Document.HasActiveComposition)
        {
            return;
        }

        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.Undo();
        }
        else
        {
            Document.Undo(this);
        }
    }

    /// <summary>Reapplies the most recently undone edit.</summary>
    public void Redo()
    {
        VerifyAccess();
        if (IsReadOnly || Document.HasActiveComposition)
        {
            return;
        }

        if (Handler is IRichEditorHandler { SupportsNativeUndo: true } handler)
        {
            handler.Redo();
        }
        else
        {
            Document.Redo(this);
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
    public void SelectAll()
    {
        if (StructuredSession is { } session) session.SelectAll();
        else SelectedRange = new TextSpan(0, Document.Length);
    }

    /// <summary>Brings a document range into view without changing the selection.</summary>
    /// <param name="range">The bounded UTF-16 range to reveal.</param>
    public void ScrollIntoView(TextSpan range)
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
        if (SourceSession is not null) { await CutSourceAsync(); return; }
        if (StructuredSession is not null) { await CutStructuredAsync(); return; }
        var document = Document;
        var snapshot = document.CurrentSnapshot;
        var selections = Selections;
        if (IsReadOnly || Document.HasActiveComposition || selections.All(item => item.Range.IsEmpty))
        {
            return;
        }

        var clipboard = CreateSelectionClipboard(snapshot, selections);
        await RichTextClipboard.SetAsync(clipboard.Payload, clipboard.Parts);
        if (IsReadOnly ||
            !ReferenceEquals(Document, document) ||
            document.Version != snapshot.Version ||
            !ReferenceEquals(Selections, selections))
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
        if (SourceSession is not null) return CopySourceAsync();
        if (StructuredSession is not null) return CopyStructuredAsync();
        var snapshot = Document.CurrentSnapshot;
        var selections = Selections;
        if (selections.All(item => item.Range.IsEmpty))
            return Task.CompletedTask;
        var clipboard = CreateSelectionClipboard(snapshot, selections);
        return RichTextClipboard.SetAsync(clipboard.Payload, clipboard.Parts);
    }

    /// <summary>Pastes a portable fragment from the system clipboard.</summary>
    /// <returns>A task that completes after paste is committed or canceled.</returns>
    public Task PasteAsync() => PasteAsync(asPlainText: false);

    internal async Task PasteAsync(bool asPlainText)
    {
        VerifyAccess();
        if (SourceSession is not null) { await PasteSourceAsync(); return; }
        if (StructuredSession is not null) { await PasteStructuredAsync(asPlainText); return; }
        var document = Document;
        var version = document.Version;
        var range = SelectedRange;
        var selections = Selections;
        if (IsReadOnly || Document.HasActiveComposition)
        {
            return;
        }

        var fragment = await RichTextClipboard.GetAsync(asPlainText);
        if (fragment is null)
        {
            return;
        }

        var args = new RichTextPastingEventArgs(fragment);
        var fragments = RichTextClipboard.GetSelectionFragments(fragment);
        Pasting?.Invoke(this, args);
        if (!args.Cancel &&
            !IsReadOnly &&
            ReferenceEquals(Document, document) &&
            document.Version == version &&
            ReferenceEquals(Selections, selections))
        {
            if (selections.Count > 1)
            {
                if (fragments?.Count == selections.Count && (ReferenceEquals(args.Fragment, fragment) ||
                    args.Fragment.IsPlainText && args.Fragment.Text == fragment.Text))
                    ReplaceSelectionFragments(args.Fragment.IsPlainText
                        ? fragments.Select(item => RichTextDocumentFragment.FromPlainText(item.Text)).ToArray() : fragments);
                else
                    ReplaceRichSelections(args.Fragment);
            }
            else if (LimitFragmentToMaxLength(args.Fragment, range) is { } accepted)
                Selection.ReplaceFragment(accepted);
        }
    }

    private RichTextDocumentFragment? LimitFragmentToMaxLength(
        RichTextDocumentFragment fragment,
        TextSpan replacedRange)
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
                : RichTextDocumentFragment.FromRange(fragment.Snapshot, new TextSpan(0, available));
    }

    internal void UpdateDocumentFromPlatform(
        RichTextDocumentSnapshot snapshot,
        int selectionStart,
        int selectionLength,
        object sourceToken,
        RichTextChangeOrigin origin = RichTextChangeOrigin.User,
        bool mergeWithPrevious = false,
        long? projectedVersion = null,
        TextSelectionState? selectionState = null,
        TextSpan? replacementHint = null)
    {
        VerifyAccess();
        if (StructuredSession is not null && Document.StructuredProjection is null)
        {
            (Handler as IRichEditorHandler)?.ApplyDocument(TextSpan.Empty, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        snapshot = Decorations.RestoreAuthoredSnapshot(snapshot, SelectedRange);
        var selection = selectionState ?? TextSelectionState.FromRange(new TextSpan(selectionStart, selectionLength));
        selection.Range.Validate(snapshot.Length, nameof(selectionLength));
        if (origin == RichTextChangeOrigin.User && TryHandleStructuredListNativeText(snapshot))
        {
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        if (StructuredInputCoordinator is not null) { ForwardStructuredInput(snapshot, selection, replacementHint); return; }
        if (Composition.IsActive)
        {
            StageComposition(snapshot, selection, sourceToken, replacementHint);
            return;
        }
        if ((SourceSession is not null || StructuredSession is not null) && MaxLength >= 0 && snapshot.Length > MaxLength && snapshot.Length > Document.Length)
        {
            SynchronizeCompositionTracking(Document.CurrentSnapshot, null);
            (Handler as RichEditorHandler)?.CompleteCompositionTracking();
            (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
            return;
        }
        var expanded = origin == RichTextChangeOrigin.User ? ExpandNativeSelectionEdit(snapshot, selection) : null;
        if (origin == RichTextChangeOrigin.User)
            expanded = PlanNativeInput(snapshot, selection, expanded, replacementHint);
        var previousSelections = _pendingSelections;
        RichTextChangeSet? corrections = null;
        if (expanded is not null)
        {
            if (ReferenceEquals(expanded.Snapshot, Document.CurrentSnapshot))
            {
                SetSelectionsFromDocument(expanded.Selections);
                SynchronizeCompositionTracking(Document.CurrentSnapshot, null);
                (Handler as RichEditorHandler)?.CompleteCompositionTracking();
                (Handler as IRichEditorHandler)?.ApplyDocument(SelectedRange, TypingCharacterFormat, TypingParagraphFormat);
                if (_rejectedInput is { } error)
                {
                    _rejectedInput = null;
                    InputRejected?.Invoke(this, new(error));
                }
                return;
            }
            snapshot = expanded.Snapshot;
            _pendingSelections = expanded.Selections;
            selection = new(expanded.Selections.Primary.Anchor, expanded.Selections.Primary.Active);
            corrections = new(Document.Version, Document.Version + 1, origin, [.. expanded.NativeCorrections], null,
                beforeSnapshot: expanded.NativeBefore, afterSnapshot: snapshot);
        }
        _pendingPlatformSelection = selection;
        SynchronizeCompositionTracking(snapshot, expanded?.NativeCorrections);
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
                projectedVersion,
                expanded?.Changes ?? (StructuredSession is not null ? RichTextDocument.CreateDelta(Document.CurrentSnapshot, snapshot, replacementHint ?? SelectedRange) : null),
                corrections);
            if (changes.IsEmpty)
            {
                if (_committingComposition)
                    (Handler as RichEditorHandler)?.CompleteCompositionTracking();
                if (expanded is not null)
                    SetSelectionsFromDocument(expanded.Selections);
                else
                    SetSelectionFromPlatform(selection);
            }
        }
        finally
        {
            _pendingPlatformSelection = null;
            _pendingSelections = previousSelections;
        }
    }

    internal void UpdateSelectionFromPlatform(int start, int length) =>
        UpdateSelectionFromPlatform(TextSelectionState.FromRange(new TextSpan(start, length)));

    internal void UpdateSelectionFromPlatform(TextSelectionState selection)
    {
        if (HasCurrentStructuredCoordinator && !Composition.IsActive && StructuredSession!.Selection is
            RichEdit.Core.RichCellSelection or RichEdit.Core.RichBlockSelection or RichEdit.Core.RichBoundarySelection) return;
        if (Composition.IsActive)
        {
            var provisional = selection.Clamp(InputSnapshot.Length);
            if (_compositionSelection != provisional)
            {
                _compositionSelection = provisional;
                OnPropertyChanged(nameof(CompositionSelection));
                CompositionChanged?.Invoke(this, new(_composition, _composition));
            }
            return;
        }
        SetSelectionFromPlatform(selection);
    }

    internal TextSelectionState InferSelectionState(TextSpan range)
    {
        var previous = SelectionState;
        if (previous.Range == range)
            return previous;
        if (range.End == previous.Anchor)
            return new TextSelectionState(range.End, range.Start);
        if (range.Start == previous.Anchor)
            return new TextSelectionState(range.Start, range.End);
        if (range.End == previous.Range.End)
            return new TextSelectionState(range.End, range.Start);

        return TextSelectionState.FromRange(range);
    }

    internal void UpdateUndoStateFromPlatform() => RefreshUndoState();

    internal void RestoreDocumentFromNativeUndo(
        RichTextDocumentSnapshot snapshot,
        TextSpan selection,
        RichTextChangeOrigin origin,
        object? sourceToken = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        selection.Validate(snapshot.Length, nameof(selection));
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = TextSelectionState.FromRange(selection);
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

    internal RichTextChangeSet EditDocument(Action<RichTextDocumentEdit> edit, RichTextEditOptions options = default) =>
        Document.Edit(edit, options, RichTextChangeOrigin.Programmatic, EditSourceToken);

    internal RichTextChangeSet EditDocument(
        Action<RichTextDocumentEdit> edit,
        TextSpan resultingSelection) => EditDocument(edit, TextSelectionState.FromRange(resultingSelection));

    internal RichTextChangeSet EditDocument(
        Action<RichTextDocumentEdit> edit,
        TextSelectionState resultingSelection,
        RichTextEditOptions options = default)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(edit);
        var previousPendingSelection = _pendingProgrammaticSelection;
        _pendingProgrammaticSelection = resultingSelection;
        RichTextChangeSet changes;
        try
        {
            changes = Document.Edit(edit, options, RichTextChangeOrigin.Programmatic, EditSourceToken);
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
        if (StructuredSession is { } session)
            session.TypingCharacterFormat = session.TypingCharacterFormat.Overlay(RichTextDocument.CharacterOverrides(_typingCharacterFormat, format));
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
            new TextSpan(link.Start, link.Length),
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
        if (_deferNotifications)
            return;

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
        ReleaseStructuredBinding(newDocument);
        DetachDocument(oldDocument);
        AttachDocument(newDocument);
        DiscardComposition();
        Decorations.Reset();
        TextLayout.Invalidate();
        Folding.Reset();
        Adornments.Clear();
        var selection = SelectionState.Clamp(newDocument.Length);
        var selectionChanged = SelectionState != selection;
        ResetSelectionsForDocument(selection);
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
        if (Handler is not IRichEditorHandler)
            Folding.NotifyChanged();

        DocumentChanged?.Invoke(this, new(oldDocument, newDocument));
    }

    private void OnSelectedRangePropertyChanged(TextSpan oldRange, TextSpan newRange)
    {
        if (!_updatingSelectionRange)
            SelectionState = TextSelectionState.FromRange(newRange);
    }

    private void OnSelectionStateChanged(TextSelectionState before, TextSelectionState after)
    {
        SynchronizeSelectionSet(after);
        _updatingSelectionRange = true;
        try
        {
            SetValue(SelectedRangeProperty, after.Range);
        }
        finally
        {
            _updatingSelectionRange = false;
        }
        if (_pendingPlatformSelection is null && _pendingProgrammaticSelection is null && !_deferNotifications)
            Document.BreakUndoGroupForSelection(after.Range);

        RefreshTypingFormats();
        if (!_synchronizingSelection && Handler is IRichEditorHandler handler)
        {
            handler.SetSelection(after);
            handler.ApplyTypingFormat(_typingCharacterFormat, _typingParagraphFormat);
        }
        if (_deferNotifications)
            return;

        SelectionChanged?.Invoke(this, new RichTextSelectionChangedEventArgs(before, after));
        RaiseSelectionFormatChanged();
    }

    private void AttachDocument(RichTextDocument document)
    {
        if (ReferenceEquals(_attachedDocument, document))
            return;

        document.AttachEditor(this);
        _attachedDocument = document;
    }

    private void DetachDocument(RichTextDocument? document)
    {
        document?.DetachEditor(this);
        if (ReferenceEquals(_attachedDocument, document))
            _attachedDocument = null;
    }

    private void ClampSelectionToDocument() => SetSelectionCore(SelectionState.Clamp(Document.Length), fromPlatform: false);

    internal TextSelectionState GetSelectionAfterEdit(IEnumerable<RichTextChange> changes, int length)
    {
        if (_pendingProgrammaticSelection is { } requested)
            requested.Range.Validate(length, nameof(requested));

        return (_pendingPlatformSelection ?? _pendingProgrammaticSelection ?? SelectionState.Map(changes, length)).Clamp(length);
    }

    internal void SynchronizeDocumentChange(RichTextChangeSet changeSet)
    {
        _deferNotifications = true;
        _selectionBeforeNotification = SelectionState;
        _selectionsBeforeNotification = Selections;
        if (_committingComposition)
            (Handler as RichEditorHandler)?.CompleteCompositionTracking();
        Decorations.MapThrough(changeSet);
        var foldingChanged = Folding.MapThrough(changeSet);
        Adornments.MapThrough(changeSet);
        var selections = SourceSession?.Selections ?? GetStructuredSelections() ?? (ReferenceEquals(changeSet.SelectionOwnerToken, EditSourceToken) && changeSet.SelectionsAfter is { } requestedSet
            ? requestedSet : GetSelectionsAfterEdit(changeSet.Changes, Document.Length));
        SetSelectionsFromDocument(selections);
        var selection = new TextSelectionState(selections.Primary.Anchor, selections.Primary.Active);
        SetSelectionCore(selection, fromPlatform: true);
        var handler = Handler as IRichEditorHandler;
        var acceptedNativeChange = handler is RichEditorHandler nativeHandler && nativeHandler.TryAcceptStructuredNativeChange(changeSet);
        if (handler is not null && !acceptedNativeChange && (changeSet.RequiresStructuredProjection || !ReferenceEquals(changeSet.SourceToken, handler.SourceToken)))
        {
            if (changeSet.RequiresStructuredProjection || changeSet.Changes.Any(static change => change.Kind == RichTextChangeKind.Reset))
                handler.ApplyDocument(selection.Range, _typingCharacterFormat, _typingParagraphFormat);
            else
                handler.ApplyChanges(changeSet, selection.Range, _typingCharacterFormat, _typingParagraphFormat);
        }
        else if (handler is not null && changeSet.NativeCorrections is { IsEmpty: false } corrections)
            handler.ApplyChanges(corrections, selection.Range, _typingCharacterFormat, _typingParagraphFormat);
        if (foldingChanged)
            handler?.ApplyFolding();

        RefreshUndoState();
        if (AutoSize == EditorAutoSizeOption.TextChanges && changeSet.IsTextChanged)
            InvalidateMeasure();
    }

    internal void ApplyDecorationChanges(RichTextDocumentSnapshot before, RichTextDocumentSnapshot after, TextSpan? dirtyRange = null)
    {
        VerifyAccess();
        var changes = RichTextDocument.CreateDelta(before, after).ToList();
        if (dirtyRange is { IsEmpty: false } range)
            changes.Add(new RichTextRangeChange(RichTextChangeKind.CharacterFormat, range, range));
        if (changes.Count == 0 || Handler is not IRichEditorHandler handler)
            return;

        var changeSet = new RichTextChangeSet(Document.Version, Document.Version, RichTextChangeOrigin.Programmatic,
            [.. changes], tag: null, beforeSnapshot: before, afterSnapshot: after);
        handler.ApplyDecorations(changeSet);
    }

    internal void PublishContentChange(RichTextChangeSet changeSet)
    {
        ContentChanged?.Invoke(this, new RichTextContentChangedEventArgs(changeSet));
        if (changeSet.IsTextChanged)
            TextChanged?.Invoke(this, new RichTextTextChangedEventArgs(changeSet));
    }

    internal void PublishSelectionChange()
    {
        _deferNotifications = false;
        foreach (var property in _deferredProperties)
            base.OnPropertyChanged(property);

        _deferredProperties.Clear();
        if (_selectionBeforeNotification != SelectionState)
            SelectionChanged?.Invoke(this, new RichTextSelectionChangedEventArgs(_selectionBeforeNotification, SelectionState));
        if (_selectionsBeforeNotification is { } previousSelections && !SameSelections(previousSelections, Selections))
            SelectionsChanged?.Invoke(this, EventArgs.Empty);

        RaiseSelectionFormatChanged();
        Commands.Refresh();
        Decorations.NotifyChanged();
        Folding.NotifyChanged();
    }

    internal void EndDocumentChange()
    {
        _deferNotifications = false;
        _selectionsBeforeNotification = null;
        _deferredProperties.Clear();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        if (_deferNotifications && propertyName is not null)
        {
            if (!_deferredProperties.Contains(propertyName))
                _deferredProperties.Add(propertyName);
        }
        else
            base.OnPropertyChanged(propertyName);
    }

    internal void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("An attached document and its editor must be mutated on the editor's owning UI thread. Use an immutable snapshot for background work.");
    }

    private void SetSelectionFromPlatform(TextSelectionState selection) => SetSelectionCore(selection, fromPlatform: true);

    private void SetSelectionCore(TextSelectionState selection, bool fromPlatform)
    {
        selection.Range.Validate(Document.Length, nameof(selection));
        if (SelectionState == selection)
        {
            RefreshTypingFormats();
            return;
        }

        _synchronizingSelection = fromPlatform;
        try
        {
            SetValue(SelectionStateProperty, selection);
        }
        finally
        {
            _synchronizingSelection = false;
        }
    }

    private void RefreshTypingFormats()
    {
        var snapshot = Document.CurrentSnapshot;
        _typingCharacterFormat = StructuredSession is { } session
            ? RichTextDocumentSnapshot.ToNative(session.ResolveTypingCharacterFormat()) : snapshot.GetCaretFormat(SelectionState.Active);
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
        if (!_deferNotifications)
            Commands.Refresh();
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
