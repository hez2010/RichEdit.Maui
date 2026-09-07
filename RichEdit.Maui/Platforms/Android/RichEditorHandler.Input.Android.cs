#if ANDROID
using Android.Views;
using NativeMenu = Android.Views.IMenu;
using NativeMenuItem = Android.Views.IMenuItem;
using NativeActionMode = Android.Views.ActionMode;

namespace RichEdit.Maui.Platforms.Android
{
    public partial class RichEditText
    {
        internal Func<EditorKey, EditorKeyModifiers, bool>? KeyDownRequested { get; set; }

        private (long Time, long DownTime, int Device, Keycode Key, int Repeat)? _shortcutEvent;
        private bool _shortcutHandled;

        internal void ResetKeyInput()
        {
            KeyDownRequested = null;
            _shortcutEvent = null;
            _shortcutHandled = false;
        }

        /// <inheritdoc />
        public override bool OnKeyShortcut(Keycode keyCode, KeyEvent? e) =>
            HandleEditorKey(keyCode, e, shortcut: true) || base.OnKeyShortcut(keyCode, e);

        private bool HandleEditorKey(Keycode keyCode, KeyEvent? e, bool shortcut = false)
        {
            if (e is null) return false;
            var identity = (e.EventTime, e.DownTime, e.DeviceId, keyCode, e.RepeatCount);
            if (!shortcut && _shortcutEvent == identity)
            {
                _shortcutEvent = null;
                return _shortcutHandled;
            }
            var composing = EditableText is { } text && global::Android.Views.InputMethods.BaseInputConnection.GetComposingSpanStart(text) >= 0;
            var handled = !composing && KeyDownRequested?.Invoke(GetEditorKey(keyCode, e), GetEditorModifiers(e)) == true;
            _shortcutEvent = shortcut ? identity : null;
            _shortcutHandled = handled;
            return handled;
        }

        private static EditorKeyModifiers GetEditorModifiers(KeyEvent key)
        {
            var modifiers = EditorKeyModifiers.None;
            if (key.IsShiftPressed) modifiers |= EditorKeyModifiers.Shift;
            if (key.IsCtrlPressed) modifiers |= EditorKeyModifiers.Control;
            if (key.IsAltPressed) modifiers |= EditorKeyModifiers.Alt;
            if (key.IsMetaPressed) modifiers |= EditorKeyModifiers.Meta;
            if (key.IsCtrlPressed && (key.MetaState & MetaKeyStates.AltRightOn) != 0)
                modifiers |= EditorKeyModifiers.AltGraph;
            return modifiers;
        }

        private static EditorKey GetEditorKey(Keycode code, KeyEvent key)
        {
            if (code is >= Keycode.A and <= Keycode.Z)
            {
                var letter = char.ToUpperInvariant((char)key.GetUnicodeChar(MetaKeyStates.None));
                if (letter is >= 'A' and <= 'Z') return EditorKey.A + (letter - 'A');
                return EditorKey.A + (code - Keycode.A);
            }
            if (code is >= Keycode.Num0 and <= Keycode.Num9) return EditorKey.D0 + (code - Keycode.Num0);
            if (code is >= Keycode.F1 and <= Keycode.F12) return EditorKey.F1 + (code - Keycode.F1);
            if (code is >= Keycode.Numpad0 and <= Keycode.Numpad9) return EditorKey.NumPad0 + (code - Keycode.Numpad0);
            return code switch
            {
                Keycode.Del => EditorKey.Backspace,
                Keycode.ForwardDel => EditorKey.Delete,
                Keycode.Tab => EditorKey.Tab,
                Keycode.Enter or Keycode.NumpadEnter => EditorKey.Enter,
                Keycode.Escape => EditorKey.Escape,
                Keycode.Space => EditorKey.Space,
                Keycode.PageUp => EditorKey.PageUp,
                Keycode.PageDown => EditorKey.PageDown,
                Keycode.MoveHome => EditorKey.Home,
                Keycode.MoveEnd => EditorKey.End,
                Keycode.DpadLeft => EditorKey.Left,
                Keycode.DpadUp => EditorKey.Up,
                Keycode.DpadRight => EditorKey.Right,
                Keycode.DpadDown => EditorKey.Down,
                Keycode.Insert => EditorKey.Insert,
                Keycode.NumpadMultiply => EditorKey.Multiply,
                Keycode.NumpadAdd => EditorKey.Add,
                Keycode.NumpadSubtract => EditorKey.Subtract,
                Keycode.NumpadDot => EditorKey.Decimal,
                Keycode.NumpadDivide => EditorKey.Divide,
                Keycode.Semicolon => EditorKey.Semicolon,
                Keycode.Plus or Keycode.Equals => EditorKey.Plus,
                Keycode.Comma => EditorKey.Comma,
                Keycode.Minus => EditorKey.Minus,
                Keycode.Period => EditorKey.Period,
                Keycode.Slash => EditorKey.Slash,
                Keycode.Grave => EditorKey.Backquote,
                Keycode.LeftBracket => EditorKey.OpenBracket,
                Keycode.Backslash => EditorKey.Backslash,
                Keycode.RightBracket => EditorKey.CloseBracket,
                Keycode.Apostrophe => EditorKey.Quote,
                _ => EditorKey.None,
            };
        }
    }
}

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        private EditorActionModeCallback? _contextMenuCallback;

        private sealed class EditorActionModeCallback(RichEditorHandler handler) : Java.Lang.Object, NativeActionMode.ICallback
        {
            private readonly WeakReference<RichEditorHandler> _handler = new(handler);
            private readonly Dictionary<int, IMenuElement> _items = [];
            private NativeActionMode? _mode;
            private bool _includeDefaultItems;

            internal void Finish() => _mode?.Finish();

            public bool OnCreateActionMode(NativeActionMode? mode, NativeMenu? menu)
            {
                if (menu is null || !_handler.TryGetTarget(out var target) || target.VirtualView is null) return false;
                _mode = mode;
                _items.Clear();
                var request = target.VirtualView.CreateContextMenu();
                _includeDefaultItems = request.IncludeDefaultItems;
                if (!_includeDefaultItems) menu.Clear();
                AddItems(menu, request.Items);
                return menu.HasVisibleItems;
            }

            public bool OnPrepareActionMode(NativeActionMode? mode, NativeMenu? menu)
            {
                if (menu is null) return false;
                if (!_includeDefaultItems)
                {
                    for (var index = menu.Size() - 1; index >= 0; index--)
                    {
                        var item = menu.GetItem(index);
                        if (item is not null && !_items.ContainsKey(item.ItemId)) menu.RemoveItem(item.ItemId);
                    }
                }
                foreach (var (id, item) in _items) menu.FindItem(id)?.SetEnabled(EditorMenu.CanExecute(item));
                return true;
            }

            public bool OnActionItemClicked(NativeActionMode? mode, NativeMenuItem? item)
            {
                if (item is null || !_items.TryGetValue(item.ItemId, out var command) || command is IMenuFlyoutSubItem) return false;
                EditorMenu.Execute(command);
                mode?.Finish();
                return true;
            }

            public void OnDestroyActionMode(NativeActionMode? mode)
            {
                _mode = null;
                _items.Clear();
            }

            private void AddItems(NativeMenu menu, IEnumerable<IMenuElement> items)
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(28)) menu.SetGroupDividerEnabled(true);
                var group = global::Android.Views.View.GenerateViewId();
                foreach (var item in items)
                {
                    if (item is IMenuFlyoutSeparator)
                    {
                        group = global::Android.Views.View.GenerateViewId();
                        continue;
                    }
                    var id = global::Android.Views.View.GenerateViewId();
                    _items.Add(id, item);
                    if (item is IMenuFlyoutSubItem submenu)
                    {
                        var nativeSubmenu = menu.AddSubMenu(group, id, 0, item.Text)!;
                        nativeSubmenu.Item?.SetEnabled(EditorMenu.CanExecute(item));
                        AddItems(nativeSubmenu, submenu);
                    }
                    else menu.Add(group, id, 0, item.Text)?.SetEnabled(EditorMenu.CanExecute(item));
                }
            }
        }
    }
}
#endif
