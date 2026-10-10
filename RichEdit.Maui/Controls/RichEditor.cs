using System.ComponentModel;
using RichEdit.Core;

namespace RichEdit.Maui;

/// <summary>A native rich editor over a structured document, typed selections, reusable styles, and shared history.</summary>
/// <remarks>Persist documents and fragments with RichEdit.Core.Rtf.RtfCodec. The view owns selection, composition, and typing state.</remarks>
public sealed partial class RichEditor : ContentView
{
    private readonly NativeEditorView _view = new();
    private bool _synchronizingSelection;
    private EventHandler? _stateObserver;

    /// <summary>Identifies the shared structured document.</summary>
    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(nameof(Document), typeof(RichDocument), typeof(RichEditor),
        defaultValueCreator: static view => ((RichEditor)view).Session.Document,
        validateValue: static (_, value) => value is RichDocument,
        coerceValue: static (view, value) => { ((RichEditor)view).BindDocument((RichDocument)value); return value; },
        propertyChanged: static (view, before, after) =>
            ((RichEditor)view).DocumentChanged?.Invoke(view, new(before as RichDocument, (RichDocument)after)));

    /// <summary>Identifies this view's typed selection, including text sets, objects, cells, and complete blocks.</summary>
    public static readonly BindableProperty SelectionProperty = BindableProperty.Create(nameof(Selection), typeof(RichSelection), typeof(RichEditor),
        defaultBindingMode: BindingMode.TwoWay, defaultValueCreator: static view => ((RichEditor)view).Session.Selection,
        validateValue: static (_, value) => value is RichSelection,
        coerceValue: static (view, value) =>
        {
            var editor = (RichEditor)view;
            if (!editor._synchronizingSelection) editor.Session.Selection = (RichSelection)value;
            return editor.Session.Selection;
        });

    /// <summary>Creates an empty document and a native editing view.</summary>
    public RichEditor()
    {
        Content = _view;
        _view.BindStructuredDocument(new RichDocument());
        Subscribe(Session);
        _view.StructuredPasting += (_, args) => Pasting?.Invoke(this, args);
        _view.PropertyChanged += OnNativePropertyChanged;
        _view.KeyDown += (_, args) => KeyDown?.Invoke(this, args);
        _view.ContextMenuOpening += (_, args) => ContextMenuOpening?.Invoke(this, args);
        _view.PointerPressed += (_, args) => PointerPressed?.Invoke(this, args);
        _view.PointerMoved += (_, args) => PointerMoved?.Invoke(this, args);
        _view.PointerExited += (_, args) => PointerExited?.Invoke(this, args);
        _view.LinkInvoked += (_, args) =>
        {
            var projection = _view.Document.StructuredProjection!;
            if (projection.GetPoint(args.Range.Start) is not { } start || projection.GetPoint(args.Range.End) is not { } end) return;
            var typed = new RichHyperlinkInvokedEventArgs(new(start, end), new(args.Target, args.ToolTip));
            LinkInvoked?.Invoke(this, typed);
            args.Handled = typed.Handled;
        };
        _view.InlineObjectInvoked += (_, args) =>
        {
            if (_view.Document.StructuredProjection!.Objects.FirstOrDefault(item => item.Position == args.Image.Position) is not { } item) return;
            var typed = new RichObjectInvokedEventArgs(Document.CurrentSnapshot.Get<RichInline>(item.Object));
            ObjectInvoked?.Invoke(this, typed);
            args.Handled = typed.Handled;
        };
        TextLayout = new(this, _view);
    }

    internal NativeEditorView NativeView => _view;
    /// <summary>Gets or sets the document shared by one or more views on the same dispatcher.</summary>
    public RichDocument Document { get => Session.Document; set { ArgumentNullException.ThrowIfNull(value); SetValue(DocumentProperty, value); } }
    /// <summary>Gets this control's owned editing session. It remains attached across native handler replacement.</summary>
    public RichEditorSession Session => _view.StructuredSession!;
    /// <summary>Gets or sets the view's typed selection. Multiple carets use a RichTextSelectionSet.</summary>
    public RichSelection Selection { get => Session.Selection; set { ArgumentNullException.ThrowIfNull(value); SetValue(SelectionProperty, value); } }
    /// <summary>Gets or sets explicit character overrides for newly typed text.</summary>
    public RichCharacterStyle TypingCharacterFormat { get => Session.TypingCharacterFormat; set => Session.TypingCharacterFormat = value; }
    /// <summary>Gets the provisional rich composition, or null when input is committed.</summary>
    public RichComposition? Composition => Session.Composition;
    /// <summary>Gets whether the document differs from its acknowledged save capture.</summary>
    public bool IsModified => Document.IsModified;
    /// <summary>Gets whether document undo is available.</summary>
    public bool CanUndo => Document.CanUndo;
    /// <summary>Gets whether document redo is available.</summary>
    public bool CanRedo => Document.CanRedo;
    /// <summary>Gets history and native clipboard commands.</summary>
    public RichEditorCommands Commands => _view.Commands;
    /// <summary>Gets hardware-key command bindings.</summary>
    public EditorKeyBindingCollection KeyBindings => _view.KeyBindings;
    /// <summary>Gets native geometry using rich text addresses and snapshot-bound captures.</summary>
    public RichEditorLayout TextLayout { get; }

    /// <summary>Occurs after authored document content and every view have synchronized.</summary>
    public event EventHandler<RichDocumentChangedEventArgs>? ContentChanged;
    /// <summary>Occurs when the control attaches a different document.</summary>
    public event EventHandler<RichDocumentReplacedEventArgs>? DocumentChanged;
    /// <summary>Occurs after the typed selection changes.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Occurs when provisional native input starts, updates, commits, or cancels.</summary>
    public event EventHandler? CompositionChanged;
    /// <summary>Allows inspection or replacement of rich clipboard content and RTF import diagnostics before paste.</summary>
    public event EventHandler<RichPastingEventArgs>? Pasting;
    /// <summary>Occurs before built-in hardware-key handling outside composition.</summary>
    public event EventHandler<EditorKeyEventArgs>? KeyDown;
    /// <summary>Allows application commands in the native context menu.</summary>
    public event EventHandler<EditorContextMenuEventArgs>? ContextMenuOpening;
    /// <summary>Observes a pointer press in editor coordinates.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerPressed;
    /// <summary>Observes pointer movement in editor coordinates.</summary>
    public event EventHandler<RichTextPointerEventArgs>? PointerMoved;
    /// <summary>Occurs when a hovering pointer leaves the editor.</summary>
    public event EventHandler? PointerExited;
    /// <summary>Allows the application to handle an authored hyperlink activation.</summary>
    public event EventHandler<RichHyperlinkInvokedEventArgs>? LinkInvoked;
    /// <summary>Allows the application to handle an authored inline-object activation.</summary>
    public event EventHandler<RichObjectInvokedEventArgs>? ObjectInvoked;

    /// <summary>Plans a checked transaction originating in this view.</summary>
    public RichEditResult Edit(Action<RichDocumentEdit> edit, RichEditOptions options = default, DocumentRevision? expectedRevision = null,
        long? expectedSelectionGeneration = null) => Session.Edit(edit, options, expectedRevision, expectedSelectionGeneration);
    /// <summary>Replaces the typed selection or every caret using semantic paragraphs and soft breaks.</summary>
    public RichEditResult ReplaceSelection(string text) => Session.ReplaceSelection(text);
    /// <summary>Pastes rich content at the typed selection or every caret.</summary>
    public RichEditResult ReplaceSelection(RichDocumentFragment fragment) => Session.ReplaceSelection(fragment);
    /// <summary>Applies explicit character properties to selected content or view-owned typing state.</summary>
    public RichEditResult ApplyCharacterFormat(RichCharacterStyle format) => Session.ApplyCharacterFormat(format);
    /// <summary>Toggles bold using resolved selected formatting.</summary>
    public RichEditResult ToggleBold() => Session.ToggleBold();
    /// <summary>Toggles italic using resolved selected formatting.</summary>
    public RichEditResult ToggleItalic() => Session.ToggleItalic();
    /// <summary>Toggles single underlining using resolved selected formatting.</summary>
    public RichEditResult ToggleUnderline() => Session.ToggleUnderline();
    /// <summary>Selects every complete body block.</summary>
    public void SelectAll() => Session.SelectAll();
    /// <summary>Undoes a shared history unit and restores its selection in this view.</summary>
    public void Undo() => Session.Undo();
    /// <summary>Redoes a shared history unit and restores its selection in this view.</summary>
    public void Redo() => Session.Redo();
    /// <summary>Copies the typed selection as RTF with an ordinary text clipboard fallback.</summary>
    public Task CopyAsync() => _view.CopyAsync();
    /// <summary>Copies and deletes the typed selection after checking that its revision and selection remain current.</summary>
    public Task CutAsync() => _view.CutAsync();
    /// <summary>Pastes RTF or ordinary clipboard text as one checked transaction.</summary>
    public Task PasteAsync() => _view.PasteAsync();
    /// <summary>Pastes the clipboard's text using current typing attributes.</summary>
    public Task PastePlainTextAsync() => _view.PasteAsync(asPlainText: true);
    /// <summary>Requests completion of native marked input without moving focus.</summary>
    public bool TryCompleteComposition() => _view.TryCompleteComposition();
    /// <summary>Moves all text selections through native caret geometry.</summary>
    public bool MoveSelections(EditorKey direction, bool extend = false) => _view.MoveSelections(direction, extend);
    /// <summary>Adds a caret on the preceding native visual line.</summary>
    public bool AddCaretAbove() => _view.AddCaretAbove();
    /// <summary>Adds a caret on the following native visual line.</summary>
    public bool AddCaretBelow() => _view.AddCaretBelow();
    /// <summary>Focuses the native editor.</summary>
    public new bool Focus() => _view.Focus();
    /// <summary>Removes focus from the native editor.</summary>
    public new void Unfocus() => _view.Unfocus();

    private void BindDocument(RichDocument document)
    {
        var before = Session;
        if (ReferenceEquals(before.Document, document)) return;
        _view.BindStructuredDocument(document);
        Unsubscribe(before);
        Subscribe(Session);
        OnPropertyChanged(nameof(Session));
        OnSelectionChanged(this, EventArgs.Empty);
        OnStateChanged();
    }
    private void Subscribe(RichEditorSession session)
    {
        session.Changed += OnDocumentChanged;
        session.SelectionChanged += OnSelectionChanged;
        session.CompositionChanged += OnCompositionChanged;
        session.TypingFormatChanged += OnTypingFormatChanged;
        var owner = new WeakReference<RichEditor>(this);
        var document = session.Document;
        EventHandler? observer = null;
        observer = (_, _) =>
        {
            if (owner.TryGetTarget(out var editor)) editor.OnStateChanged();
            else document.StateChanged -= observer;
        };
        document.StateChanged += _stateObserver = observer;
    }
    private void Unsubscribe(RichEditorSession session)
    {
        session.Changed -= OnDocumentChanged;
        session.SelectionChanged -= OnSelectionChanged;
        session.CompositionChanged -= OnCompositionChanged;
        session.TypingFormatChanged -= OnTypingFormatChanged;
        session.Document.StateChanged -= _stateObserver;
        _stateObserver = null;
    }
    private void OnDocumentChanged(object? sender, RichDocumentChangedEventArgs args) { OnStateChanged(); ContentChanged?.Invoke(this, args); }
    private void OnSelectionChanged(object? sender, EventArgs args)
    {
        _synchronizingSelection = true;
        try { SetValue(SelectionProperty, Session.Selection); }
        finally { _synchronizingSelection = false; }
        OnPropertyChanged(nameof(TypingCharacterFormat));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void OnCompositionChanged(object? sender, EventArgs args) { OnPropertyChanged(nameof(Composition)); CompositionChanged?.Invoke(this, args); }
    private void OnTypingFormatChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(TypingCharacterFormat));
    private void OnNativePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NativeEditorView.IsReadOnly)) SetValue(IsReadOnlyProperty, _view.IsReadOnly);
        if (args.PropertyName is nameof(NativeEditorView.CanUndo) or nameof(NativeEditorView.CanRedo)) OnStateChanged();
    }
    private void OnStateChanged() { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); OnPropertyChanged(nameof(IsModified)); }
}
