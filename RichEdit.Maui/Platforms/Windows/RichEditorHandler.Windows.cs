using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private static readonly byte[] ImagePlaceholder = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private bool _applyingDocument;
    private bool _isComposing;
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
    protected override RichEditBox CreatePlatformView() => new SourceRichEditBox(this)
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
        ConnectAdornments();
        platformView.TextChanging += OnNativeDocumentChanged;
        platformView.SelectionChanged += OnNativeSelectionChanged;
        platformView.ActualThemeChanged += OnPlatformThemeChanged;
        platformView.Loaded += OnPlatformViewLoaded;
        platformView.LostFocus += OnPlatformViewLostFocus;
        platformView.PreviewKeyDown += OnPlatformKeyDown;
        platformView.Paste += OnPlatformPaste;
        platformView.CopyingToClipboard += OnPlatformCopy;
        platformView.CuttingToClipboard += OnPlatformCut;
        platformView.DragStarting += OnSourceDragStarting;
        platformView.Tapped += OnPlatformTapped;
        platformView.TextCompositionStarted += OnCompositionStarted;
        platformView.TextCompositionEnded += OnCompositionEnded;
        _contextFlyout = new TextCommandBarFlyout();
        _selectionFlyout = new TextCommandBarFlyout();
        _contextFlyout.Opening += OnTextFlyoutOpening;
        _contextFlyout.Closed += OnTextFlyoutClosed;
        _selectionFlyout.Opening += OnTextFlyoutOpening;
        _selectionFlyout.Closed += OnTextFlyoutClosed;
        platformView.ContextFlyout = _contextFlyout;
        platformView.SelectionFlyout = _selectionFlyout;
    }

    /// <inheritdoc />
    protected override void DisconnectHandler(RichEditBox platformView)
    {
        VirtualView?.Commands.Disconnect();
        DisconnectAdornments();
        platformView.TextChanging -= OnNativeDocumentChanged;
        platformView.SelectionChanged -= OnNativeSelectionChanged;
        platformView.ActualThemeChanged -= OnPlatformThemeChanged;
        platformView.Loaded -= OnPlatformViewLoaded;
        platformView.LostFocus -= OnPlatformViewLostFocus;
        platformView.PreviewKeyDown -= OnPlatformKeyDown;
        platformView.Paste -= OnPlatformPaste;
        platformView.CopyingToClipboard -= OnPlatformCopy;
        platformView.CuttingToClipboard -= OnPlatformCut;
        platformView.DragStarting -= OnSourceDragStarting;
        platformView.Tapped -= OnPlatformTapped;
        platformView.TextCompositionStarted -= OnCompositionStarted;
        platformView.TextCompositionEnded -= OnCompositionEnded;
        _isComposing = false;
        if (_contextFlyout is not null)
        {
            _contextFlyout.Opening -= OnTextFlyoutOpening;
            _contextFlyout.Closed -= OnTextFlyoutClosed;
            RestoreTextFlyout(_contextFlyout);
        }

        if (_selectionFlyout is not null)
        {
            _selectionFlyout.Opening -= OnTextFlyoutOpening;
            _selectionFlyout.Closed -= OnTextFlyoutClosed;
            RestoreTextFlyout(_selectionFlyout);
        }

        _contextFlyout = null;
        _selectionFlyout = null;
        _hasCompletedInitialLoad = false;
        _hasNativeLinks = false;
        _nativeTextSnapshot = null;

        base.DisconnectHandler(platformView);
    }

    private partial void SetNativeSelectionCore(RichTextSelectionState selection)
    {
        if (PlatformView is null)
            return;

        var range = selection.Range;
        var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        var native = PlatformView.Document.Selection;
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            native.SetRange(positions?.ToNativePosition(range.Start) ?? range.Start, positions?.ToNativePosition(range.End) ?? range.End);
            native.Options = selection.IsReversed ? native.Options | SelectionOptions.StartActive : native.Options & ~SelectionOptions.StartActive;
        }
        finally
        {
            _applyingDocument = wasApplying;
        }
    }

    private partial void ScrollIntoViewCore(RichTextRange range)
    {
        var positions = _hasNativeLinks ? GetNativeTextSnapshot() : null;
        PlatformView.Document.GetRange(positions?.ToNativePosition(range.Start) ?? range.Start,
            positions?.ToNativePosition(range.End) ?? range.End).ScrollIntoView(PointOptions.None);
    }

    private partial bool IsComposingCore() => _isComposing;

    private void OnCompositionStarted(RichEditBox sender, TextCompositionStartedEventArgs args)
    {
        _isComposing = true;
        UpdateCompositionState();
    }

    private void OnCompositionEnded(RichEditBox sender, TextCompositionEndedEventArgs args)
    {
        ReadNativeDocumentChange();
        _isComposing = false;
        UpdateCompositionState();
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

        UpdateForegroundStateResources();

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
                ApplyCurrentDocument(editor.SelectedRange.Start, editor.SelectedRange.Length);
                ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
                return;
            }

            _applyingDocument = true;
            var nativeDocument = PlatformView.Document;
            nativeDocument.BatchDisplayUpdates();
            try
            {
                ApplyCharacterFormatsIncrementally(
                    DisplayPresentationSnapshot,
                    new RichTextRange(0, DisplayPresentationSnapshot.Length));
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
        UpdateDisplayInputLimit();
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
        if (VirtualView is null || eventArgs.Handled || _isComposing)
        {
            return;
        }

        if (VirtualView.SendKeyDown(GetEditorKey(eventArgs.Key), GetEditorModifiers()))
        {
            eventArgs.Handled = true;
            return;
        }

        PrepareNativeSourceKey(GetEditorKey(eventArgs.Key), GetEditorModifiers());
        if (!NativeProjection.IsEmpty && eventArgs.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down && !IsAltKeyDown())
        {
            var selection = PlatformView.Document.Selection;
            var unit = IsControlKeyDown() ? TextRangeUnit.Paragraph : TextRangeUnit.Line;
            int moved;
            do
            {
                moved = eventArgs.Key == Windows.System.VirtualKey.Up ? selection.MoveUp(unit, 1, IsShiftKeyDown()) : selection.MoveDown(unit, 1, IsShiftKeyDown());
            } while (moved != 0 && IsDisplayReservationLine(GetNativeTextSnapshot().ToLogicalPosition(
                (selection.Options & SelectionOptions.StartActive) != 0 ? selection.StartPosition : selection.EndPosition)));

            eventArgs.Handled = true;
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
        await VirtualView.Commands.ExecuteClipboardAsync(VirtualView.PasteAsync);
    }

    private async void OnPlatformCopy(RichEditBox sender, TextControlCopyingToClipboardEventArgs args)
    {
        args.Handled = true;
        await VirtualView.Commands.ExecuteClipboardAsync(VirtualView.CopyAsync);
    }

    private async void OnPlatformCut(RichEditBox sender, TextControlCuttingToClipboardEventArgs args)
    {
        args.Handled = true;
        await VirtualView.Commands.ExecuteClipboardAsync(VirtualView.CutAsync);
    }

    private void OnSourceDragStarting(Microsoft.UI.Xaml.UIElement sender, Microsoft.UI.Xaml.DragStartingEventArgs args)
    {
        if (NativeProjection.IsEmpty || VirtualView.SelectedRange.IsEmpty)
            return;

        var fragment = RichTextDocumentFragment.FromRange(VirtualView.Document.CurrentSnapshot, VirtualView.SelectedRange);
        args.Data.SetText(fragment.Text);
        args.Data.SetRtf(fragment.RtfText);
    }

    [DynamicWindowsRuntimeCast(typeof(TextCommandBarFlyout))]
    private void OnTextFlyoutOpening(object? sender, object args)
    {
        if (sender is not TextCommandBarFlyout flyout || VirtualView is null)
        {
            return;
        }

        ConfigureTextFlyoutCommands(flyout);
        CustomizeTextFlyout(flyout);
    }

    [DynamicWindowsRuntimeCast(typeof(StandardUICommand))]
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

        if (NativeProjection.ContainsDisplayCharacter(position))
            return;

        position = Math.Clamp(NativeProjection.ToSource(position), 0, VirtualView.Document.Length);
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

    private void OnPlatformViewLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (VirtualView is null)
        {
            return;
        }

        UpdateForegroundStateResources();

        if (_hasCompletedInitialLoad)
        {
            _nativeTextSnapshot = null;
            if (string.Equals(
                GetNativeTextSnapshot().Text,
                DisplaySourceSnapshot.Text,
                StringComparison.Ordinal))
            {
                return;
            }
        }

        ApplyCurrentDocument(
            VirtualView.SelectedRange.Start,
            VirtualView.SelectedRange.Length);
        ApplyTypingFormatCore(_nativeTypingFormat, _nativeTypingParagraphFormat);
        ClearUndoHistoryCore();
        _hasCompletedInitialLoad = true;
    }

    private void UpdateForegroundStateResources()
    {
        if (PlatformView.Foreground is not { } foreground)
            return;
        // Changing the template's text-surface foreground resets TOM run colors.
        // Use the same brush in every interaction state so authored colors survive.
        // The normal Foreground still follows the view/native theme fallback.
        PlatformView.Resources["TextControlForegroundFocused"] = foreground;
        PlatformView.Resources["TextControlForegroundPointerOver"] = foreground;
        PlatformView.Resources["TextControlForegroundDisabled"] = foreground;
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
}
