#if DEBUG && (ANDROID || IOS || MACCATALYST)
namespace RichEdit.Maui.TestApp;

internal sealed partial class CodeEditorTestPage
{
    private async Task RunInputTests(Func<string, string, Func<Task>, Task> test)
    {
        var defaults = _editor.KeyBindings.ToArray();
        async Task Test(string name, Func<Task> action) => await test(name, "text", async () =>
        {
            try { await action(); }
            finally
            {
                _editor.KeyBindings.Clear();
                foreach (var binding in defaults) _editor.KeyBindings.Add(binding);
                FlyoutBase.SetContextFlyout(_editor, null);
                FlyoutBase.SetContextFlyout(_textView, null);
            }
        });

        await Test("application binding overrides code default and removal restores it", () =>
        {
            _editor.SelectedRange = new(4, 0);
            _editor.KeyBindings.Add(new(EditorKey.Tab, EditorKeyModifiers.None, new Command(() => _editor.Selection.ReplaceText("!"))));
            SendNativeKey(EditorKey.Tab);
            Equal("text!", _editor.Document.Text);
            _editor.Undo();
            Equal("text", _editor.Document.Text);
            Equal(new RichTextRange(4, 0), _editor.SelectedRange);
            _editor.KeyBindings.RemoveAt(_editor.KeyBindings.Count - 1);
            SendNativeKey(EditorKey.Tab);
            Equal("text    ", _editor.Document.Text);
            return Task.CompletedTask;
        });

        await Test("handled code key event suppresses command", () =>
        {
            var invoked = 0;
            var events = 0;
            _editor.KeyBindings.Add(new(EditorKey.K, EditorKeyModifiers.Control, new Command(() => invoked++)));
            void OnKey(object? sender, EditorKeyEventArgs args)
            {
                Equal(true, ReferenceEquals(_editor, sender));
                Equal(EditorKey.K, args.Key);
                Equal(EditorKeyModifiers.Control, args.Modifiers);
                events++;
                args.Handled = true;
            }
            _editor.KeyDown += OnKey;
            try { SendNativeKey(EditorKey.K, EditorKeyModifiers.Control); }
            finally { _editor.KeyDown -= OnKey; }
            Equal(1, events);
            Equal(0, invoked);
            return Task.CompletedTask;
        });

        await Test("last binding owns gesture and rechecks command parameter", () =>
        {
            var invoked = 0;
            var enabled = false;
            var parameter = new object();
            _textView.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(() => invoked += 100)));
            _textView.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control,
                new Command<object>(value => { Equal(true, ReferenceEquals(parameter, value)); invoked++; },
                    value => enabled && ReferenceEquals(parameter, value)), parameter));
            SendNativeKey(EditorKey.S, EditorKeyModifiers.Control);
            Equal(0, invoked);
            enabled = true;
            SendNativeKey(EditorKey.S, EditorKeyModifiers.Control);
            Equal(1, invoked);
            return Task.CompletedTask;
        });

        await Test("native modifiers match exactly", () =>
        {
            var invoked = 0;
            var modifiers = EditorKeyModifiers.Control | EditorKeyModifiers.Alt;
            _editor.KeyBindings.Add(new(EditorKey.Q, modifiers, new Command(() => invoked++)));
            SendNativeKey(EditorKey.Q, modifiers | EditorKeyModifiers.Shift);
            Equal(0, invoked);
#if ANDROID
            SendNativeKey(EditorKey.Q, modifiers | EditorKeyModifiers.AltGraph);
            Equal(0, invoked);
#endif
            SendNativeKey(EditorKey.Q, modifiers);
            Equal(1, invoked);
            return Task.CompletedTask;
        });

        await Test("key binding collection rejects background mutation", async () =>
        {
            var count = _editor.KeyBindings.Count;
            var rejected = await Task.Run(() =>
            {
                try { _editor.KeyBindings.Add(new(EditorKey.S, EditorKeyModifiers.Control, new Command(static () => { }))); }
                catch (InvalidOperationException) { return true; }
                return false;
            });
            Equal(true, rejected);
            Equal(count, _editor.KeyBindings.Count);
        });

        await Test("menu customization is per opening and replaces native items", () =>
        {
            var configured = new MenuFlyout { new MenuFlyoutItem { Text = "Configured" } };
            FlyoutBase.SetContextFlyout(_textView, configured);
            var openings = 0;
            void OnMenu(object? sender, EditorContextMenuEventArgs args)
            {
                Equal(true, ReferenceEquals(_textView, sender));
                openings++;
                args.Items.Add(new MenuFlyoutItem { Text = "Dynamic" });
            }
            _textView.ContextMenuOpening += OnMenu;
            try
            {
                using var first = CreateNativeMenu();
                Equal("Configured|Dynamic", string.Join('|', first.Titles));
                using var second = CreateNativeMenu();
                Equal("Configured|Dynamic", string.Join('|', second.Titles));
                Equal(2, openings);
                Equal(1, configured.Count);
            }
            finally { _textView.ContextMenuOpening -= OnMenu; }
            return Task.CompletedTask;
        });

        await Test("code menu keeps native items and supports submenus", () =>
        {
            FlyoutBase.SetContextFlyout(_editor, new MenuFlyout { new MenuFlyoutItem { Text = "Code action" } });
            void OnMenu(object? sender, EditorContextMenuEventArgs args)
            {
                Equal(true, ReferenceEquals(_editor, sender));
                Equal(1, args.Items.Count);
                args.IncludeDefaultItems = true;
                args.Items.Add(new MenuFlyoutSeparator());
                var submenu = new MenuFlyoutSubItem { Text = "More" };
                submenu.Add(new MenuFlyoutItem { Text = "Nested" });
                args.Items.Add(submenu);
            }
            _editor.ContextMenuOpening += OnMenu;
            try
            {
                using var menu = CreateNativeMenu();
                Equal("Native|Code action|More|Nested", string.Join('|', menu.Titles));
            }
            finally { _editor.ContextMenuOpening -= OnMenu; }
            return Task.CompletedTask;
        });

        await Test("native menu action rechecks enabled state and raises Clicked", () =>
        {
            var enabled = true;
            var invoked = 0;
            var clicked = 0;
            var parameter = new object();
            var item = new MenuFlyoutItem
            {
                Text = "Action",
                Command = new Command<object>(value => { Equal(true, ReferenceEquals(parameter, value)); invoked++; }, _ => enabled),
                CommandParameter = parameter,
            };
            item.Clicked += (_, _) => clicked++;
            FlyoutBase.SetContextFlyout(_editor, new MenuFlyout { item });
            using var menu = CreateNativeMenu();
            Equal(true, menu.IsEnabled("Action"));
            enabled = false;
            menu.Invoke("Action");
            Equal(0, invoked);
            Equal(0, clicked);
            enabled = true;
            menu.Invoke("Action");
            Equal(1, invoked);
            Equal(1, clicked);
            enabled = false;
            using var disabled = CreateNativeMenu();
            Equal(false, disabled.IsEnabled("Action"));
            return Task.CompletedTask;
        });

        await Test("composition suppresses custom shortcuts", async () =>
        {
            var invoked = 0;
            _editor.KeyBindings.Add(new(EditorKey.K, EditorKeyModifiers.Control, new Command(() => invoked++)));
#if ANDROID
            var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var composing = new Java.Lang.String("int");
            connection.SetComposingText(composing, 1);
            SendNativeKey(EditorKey.K, EditorKeyModifiers.Control);
            Equal(0, invoked);
            Equal(true, Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(native.EditableText!) >= 0);
            connection.FinishComposingText();
#else
            var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
            native.SetMarkedText("int", new Foundation.NSRange(3, 0));
            SendNativeKey(EditorKey.K, EditorKeyModifiers.Control);
            Equal(0, invoked);
            Equal(true, native.MarkedTextRange is not null);
            native.UnmarkText();
#endif
            SendNativeKey(EditorKey.K, EditorKeyModifiers.Control);
            Equal(1, invoked);
            await _editor.RefreshHighlightingAsync();
        });

#if ANDROID
        await Test("Android shortcut and keydown dispatch a gesture once", () =>
        {
            var native = (RichEdit.Maui.Platforms.Android.RichEditText)_textView.Handler!.PlatformView!;
            var invoked = 0;
            _editor.KeyBindings.Add(new(EditorKey.K, EditorKeyModifiers.Control, new Command(() => invoked++)));
            using var key = new Android.Views.KeyEvent(0, 0, Android.Views.KeyEventActions.Down, Android.Views.Keycode.K, 0, Android.Views.MetaKeyStates.CtrlOn);
            Equal(true, native.OnKeyShortcut(Android.Views.Keycode.K, key));
            Equal(true, native.OnKeyDown(Android.Views.Keycode.K, key));
            Equal(1, invoked);
            return Task.CompletedTask;
        });

        await Test("Android command exceptions reach native key callback caller", () =>
        {
            var native = (RichEdit.Maui.Platforms.Android.RichEditText)_textView.Handler!.PlatformView!;
            var failure = new InvalidOperationException("Application command failure");
            _editor.KeyBindings.Add(new(EditorKey.F1, EditorKeyModifiers.None, new Command(() => throw failure)));
            using var key = new Android.Views.KeyEvent(Android.Views.KeyEventActions.Down, Android.Views.Keycode.F1);
            Exception? actual = null;
            try { native.OnKeyDown(Android.Views.Keycode.F1, key); }
            catch (InvalidOperationException exception) { actual = exception; }
            Equal(true, ReferenceEquals(failure, actual));
            return Task.CompletedTask;
        });
#endif
    }

    private void SendNativeKey(EditorKey key, EditorKeyModifiers modifiers = EditorKeyModifiers.None)
    {
#if ANDROID
        var native = (Android.Widget.EditText)_textView.Handler!.PlatformView!;
        var code = key switch
        {
            EditorKey.Tab => Android.Views.Keycode.Tab,
            EditorKey.K => Android.Views.Keycode.K,
            EditorKey.Q => Android.Views.Keycode.Q,
            EditorKey.S => Android.Views.Keycode.S,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        var flags = Android.Views.MetaKeyStates.None;
        if ((modifiers & EditorKeyModifiers.Control) != 0) flags |= Android.Views.MetaKeyStates.CtrlOn;
        if ((modifiers & EditorKeyModifiers.Alt) != 0) flags |= Android.Views.MetaKeyStates.AltOn;
        if ((modifiers & EditorKeyModifiers.Shift) != 0) flags |= Android.Views.MetaKeyStates.ShiftOn;
        if ((modifiers & EditorKeyModifiers.Meta) != 0) flags |= Android.Views.MetaKeyStates.MetaOn;
        if ((modifiers & EditorKeyModifiers.AltGraph) != 0) flags |= Android.Views.MetaKeyStates.AltRightOn;
        var time = Android.OS.SystemClock.UptimeMillis();
        using var down = new Android.Views.KeyEvent(time, time, Android.Views.KeyEventActions.Down, code, 0, flags);
        using var up = new Android.Views.KeyEvent(time, time, Android.Views.KeyEventActions.Up, code, 0, flags);
        native.DispatchKeyEvent(down);
        native.DispatchKeyEvent(up);
#else
        var native = (UIKit.UITextView)_textView.Handler!.PlatformView!;
        var input = key == EditorKey.Tab ? "\t" : key.ToString().ToLowerInvariant();
        UIKit.UIKeyModifierFlags flags = 0;
        if ((modifiers & EditorKeyModifiers.Control) != 0) flags |= UIKit.UIKeyModifierFlags.Control;
        if ((modifiers & EditorKeyModifiers.Alt) != 0) flags |= UIKit.UIKeyModifierFlags.Alternate;
        if ((modifiers & EditorKeyModifiers.Shift) != 0) flags |= UIKit.UIKeyModifierFlags.Shift;
        if ((modifiers & EditorKeyModifiers.Meta) != 0) flags |= UIKit.UIKeyModifierFlags.Command;
        var command = native.KeyCommands?.FirstOrDefault(command => command.Input == input && command.ModifierFlags == flags);
        if (command is not null && native.CanPerform(command.Action, command))
            Equal(true, UIKit.UIApplication.SharedApplication.SendAction(command.Action, native, command, null), "UIKit key action");
#endif
    }

    // Exercise the installed native menu callbacks with native suggested items.
    // Invocation uses the native action object; no editor dispatch internals are called.
    private NativeInputMenu CreateNativeMenu() => new(_textView);

    private sealed class NativeInputMenu : IDisposable
    {
#if ANDROID
        private readonly Android.Widget.PopupMenu _popup;
        private readonly Android.Views.ActionMode.ICallback _callback;
        private readonly List<Android.Views.IMenuItem> _items = [];

        internal NativeInputMenu(RichEditor editor)
        {
            var native = (Android.Widget.EditText)editor.Handler!.PlatformView!;
            _callback = native.CustomSelectionActionModeCallback!;
            _popup = new Android.Widget.PopupMenu(native.Context!, native);
            using var title = new Java.Lang.String("Native");
            _popup.Menu!.Add(title);
            Equal(true, _callback.OnCreateActionMode(null, _popup.Menu));
            _callback.OnPrepareActionMode(null, _popup.Menu);
            Read(_popup.Menu!);
        }

        private void Read(Android.Views.IMenu menu)
        {
            for (var index = 0; index < menu.Size(); index++)
            {
                var item = menu.GetItem(index)!;
                _items.Add(item);
                if (item.SubMenu is { } submenu) Read(submenu);
            }
        }

        internal IEnumerable<string> Titles => _items.Select(item => item.TitleFormatted?.ToString() ?? "");
        internal bool IsEnabled(string title) => _items.Single(item => item.TitleFormatted?.ToString() == title).IsEnabled;
        internal void Invoke(string title) => Equal(true, _callback.OnActionItemClicked(null, _items.Single(item => item.TitleFormatted?.ToString() == title)));
        public void Dispose() { _callback.OnDestroyActionMode(null); _popup.Dispose(); }
#else
        private readonly UIKit.UIMenu _menu;
        private readonly UIKit.UIAction _suggested;
        private readonly List<UIKit.UIMenuElement> _items = [];

        internal NativeInputMenu(RichEditor editor)
        {
#if IOS
            if (!OperatingSystem.IsIOSVersionAtLeast(16))
#else
            if (!OperatingSystem.IsMacCatalystVersionAtLeast(16))
#endif
                throw new PlatformNotSupportedException("Native edit menu tests require iOS or Mac Catalyst 16.");
            var native = (UIKit.UITextView)editor.Handler!.PlatformView!;
            _suggested = UIKit.UIAction.Create("Native", null, null, _ => { });
            _menu = native.GetEditMenu(native.SelectedTextRange!, [_suggested]) ?? throw new InvalidOperationException("No native edit menu.");
            Read(_menu);
        }

        private void Read(UIKit.UIMenu menu)
        {
            foreach (var item in menu.Children)
            {
                if (item.Title.Length != 0) _items.Add(item);
                if (item is UIKit.UIMenu submenu) Read(submenu);
            }
        }

        internal IEnumerable<string> Titles => _items.Select(item => item.Title);
        internal bool IsEnabled(string title) => (_items.OfType<UIKit.UIAction>().Single(item => item.Title == title).Attributes & UIKit.UIMenuElementAttributes.Disabled) == 0;
        internal void Invoke(string title)
        {
            using var control = new UIKit.UIControl();
            control.SendAction(_items.OfType<UIKit.UIAction>().Single(item => item.Title == title));
        }
        public void Dispose() { _menu.Dispose(); _suggested.Dispose(); }
#endif
    }
}
#endif
