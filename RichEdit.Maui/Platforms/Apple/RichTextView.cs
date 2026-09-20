#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple;

/// <summary>Provides the Apple native text view used by <see cref="RichEditorHandler"/>.</summary>
public partial class RichTextView : UITextView
{
    private readonly UIColor _defaultPlaceholderColor;
    private readonly UILabel _placeholderLabel;
    private readonly DocumentUndoManager _documentUndoManager = new();

    /// <inheritdoc />
    public override NSUndoManager? UndoManager => _documentUndoManager ?? base.UndoManager;

    internal void SetUndoEditor(RichEditor? editor) => _documentUndoManager.SetEditor(editor);

    internal void NotifyUndoStateChanged() => _documentUndoManager.NotifyStateChanged();

    internal Func<Task>? PasteRequested { get; set; }

    internal Func<Task>? CopyRequested { get; set; }

    internal Func<Task>? CutRequested { get; set; }

    internal Action? NativeAppearanceChanged { get; set; }

    /// <summary>Initializes the native rich-text view.</summary>
    public RichTextView()
    {
        _placeholderLabel = new UILabel
        {
            BackgroundColor = UIColor.Clear,
            Lines = 0,
            UserInteractionEnabled = false,
        };
        _defaultPlaceholderColor = _placeholderLabel.TextColor ?? UIColor.Label;

        AddSubview(_placeholderLabel);
        TextContainerInset = new UIEdgeInsets(10, 8, 10, 8);
        // Geometry and folding use TextKit 1. Its list markers are drawn by
        // this view, since NSTextList alone only renders them in TextKit 2.
        _ = LayoutManager;
    }

    /// <summary>Sets placeholder text.</summary>
    /// <param name="text">The placeholder text.</param>
    public void SetPlaceholder(string? text)
    {
        _placeholderLabel.Text = text;
        UpdatePlaceholderVisibility();
        SetNeedsLayout();
    }

    /// <summary>Sets the placeholder color.</summary>
    /// <param name="color">The color, or null for the native default.</param>
    public void SetPlaceholderColor(UIColor? color) =>
        _placeholderLabel.TextColor = color ?? _defaultPlaceholderColor;

    /// <summary>Sets the placeholder font.</summary>
    /// <param name="font">The native font.</param>
    public void SetPlaceholderFont(UIFont font) => _placeholderLabel.Font = font;

    /// <summary>Updates placeholder visibility from current content.</summary>
    public void UpdatePlaceholderVisibility() =>
        _placeholderLabel.Hidden = !string.IsNullOrEmpty(Text);

    /// <inheritdoc />
    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        var inset = TextContainerInset;
        var x = inset.Left + TextContainer.LineFragmentPadding;
        var width = Math.Max(0, Bounds.Width - x - inset.Right - TextContainer.LineFragmentPadding);
        var size = _placeholderLabel.SizeThatFits(new CGSize(width, nfloat.MaxValue));
        _placeholderLabel.Frame = new CGRect(x, inset.Top, width, size.Height);
        ProjectionHandler?.OnNativeLayoutCompleted();
        SetNeedsDisplay();
    }

    /// <inheritdoc />
    public override async void Paste(NSObject? sender)
    {
        if (PasteRequested is { } pasteRequested)
        {
            await ExecuteClipboardCommand(pasteRequested);
            return;
        }

        base.Paste(sender);
    }

    /// <inheritdoc />
    public override async void Copy(NSObject? sender)
    {
        if (CopyRequested is { } copy)
        {
            await ExecuteClipboardCommand(copy);
            return;
        }

        base.Copy(sender);
    }

    /// <inheritdoc />
    public override async void Cut(NSObject? sender)
    {
        if (CutRequested is { } cut)
        {
            await ExecuteClipboardCommand(cut);
            return;
        }

        base.Cut(sender);
    }

    private Task ExecuteClipboardCommand(Func<Task> command) => ProjectionHandler is { } handler
        ? handler.VirtualView.Commands.ExecuteClipboardAsync(command)
        : RichEditorCommands.ExecuteAsync(command);

#pragma warning disable CA1422 // Required on iOS 15-16; the callback remains valid on 17+.
    /// <inheritdoc />
    public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
    {
        base.TraitCollectionDidChange(previousTraitCollection);
        if (previousTraitCollection is not null &&
            previousTraitCollection.UserInterfaceStyle != TraitCollection.UserInterfaceStyle)
        {
            NativeAppearanceChanged?.Invoke();
        }
    }
#pragma warning restore CA1422

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ResetKeyInput();
            PasteRequested = null;
            CopyRequested = null;
            CutRequested = null;
            NativeAppearanceChanged = null;
            _placeholderLabel.Dispose();
            _documentUndoManager.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class DocumentUndoManager : NSUndoManager
    {
        private WeakReference<RichEditor>? _editor;

        public DocumentUndoManager() => DisableUndoRegistration();

        public void SetEditor(RichEditor? editor) =>
            _editor = editor is null ? null : new WeakReference<RichEditor>(editor);

        private RichEditor? Editor => _editor?.TryGetTarget(out var editor) == true ? editor : null;

        public override bool CanUndo => Editor is { IsReadOnly: false, CanUndo: true };

        public override bool CanRedo => Editor is { IsReadOnly: false, CanRedo: true };

        public override void Undo()
        {
            if (CanUndo)
                Editor?.Undo();
        }

        public override void Redo()
        {
            if (CanRedo)
                Editor?.Redo();
        }

        public void NotifyStateChanged()
        {
            // This manager exposes document history; it has no native groups.
            // Posting native undo/group notifications makes UIKit replay its
            // own text-service adjustments a second time.
            WillChangeValue("canUndo");
            WillChangeValue("canRedo");
            DidChangeValue("canRedo");
            DidChangeValue("canUndo");
        }
    }
}
#endif
