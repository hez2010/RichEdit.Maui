#if IOS || MACCATALYST
using System.Runtime.Versioning;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace RichEdit.Maui.Platforms.Apple
{
    public partial class RichTextView
    {
        private readonly HashSet<int> _handledKeys = [];
        private static readonly Selector KeyBindingAction = new("richEditPerformKeyBinding:");
        private UIKeyCommand[] _bindingCommands = [];
        private long _keyBindingVersion = -1;
        internal EditorKeyBindingCollection? EditorKeyBindings { get; set; }

        internal Func<EditorKey, EditorKeyModifiers, bool>? KeyDownRequested { get; set; }

        internal void ResetKeyInput()
        {
            KeyDownRequested = null;
            EditorKeyBindings = null;
            _handledKeys.Clear();
            foreach (var command in _bindingCommands) command.Dispose();
            _bindingCommands = [];
            _keyBindingVersion = -1;
        }

        /// <inheritdoc />
        public override UIKeyCommand[] KeyCommands
        {
            get
            {
                if (MarkedTextRange is not null || EditorKeyBindings is not { } bindings) return base.KeyCommands;
                if (_keyBindingVersion != bindings.Version)
                {
                    foreach (var command in _bindingCommands) command.Dispose();
                    var commands = new List<UIKeyCommand>();
                    var gestures = new HashSet<(EditorKey, EditorKeyModifiers)>();
                    for (var index = bindings.Count - 1; index >= 0; index--)
                    {
                        var binding = bindings[index];
                        if (!gestures.Add((binding.Key, binding.Modifiers)) ||
                            (binding.Modifiers & EditorKeyModifiers.AltGraph) != 0 || GetKeyCommandInput(binding.Key) is not { } input) continue;
                        UIKeyModifierFlags flags = 0;
                        if ((binding.Modifiers & EditorKeyModifiers.Shift) != 0) flags |= UIKeyModifierFlags.Shift;
                        if ((binding.Modifiers & EditorKeyModifiers.Control) != 0) flags |= UIKeyModifierFlags.Control;
                        if ((binding.Modifiers & EditorKeyModifiers.Alt) != 0) flags |= UIKeyModifierFlags.Alternate;
                        if ((binding.Modifiers & EditorKeyModifiers.Meta) != 0) flags |= UIKeyModifierFlags.Command;
                        if (binding.Key is >= EditorKey.NumPad0 and <= EditorKey.Divide) flags |= UIKeyModifierFlags.NumericPad;
                        using var gesture = NSNumber.FromInt32((int)binding.Key | ((int)binding.Modifiers << 16));
                        var command = UIKeyCommand.Create(string.Empty, null, KeyBindingAction, input, flags, gesture);
                        command.WantsPriorityOverSystemBehavior = true;
                        commands.Add(command);
                    }
                    _bindingCommands = [.. commands];
                    _keyBindingVersion = bindings.Version;
                }
                return [.. _bindingCommands, .. base.KeyCommands ?? []];
            }
        }

        /// <inheritdoc />
        public override bool CanPerform(Selector action, NSObject? withSender)
        {
            if (action.Handle != KeyBindingAction.Handle) return base.CanPerform(action, withSender);
            return MarkedTextRange is null && withSender is UIKeyCommand { PropertyList: NSNumber gesture } &&
                EditorKeyBindings?.Find((EditorKey)(gesture.Int32Value & 0xFFFF), (EditorKeyModifiers)(gesture.Int32Value >> 16)) is { } binding &&
                binding.Command.CanExecute(binding.CommandParameter);
        }

        [Export("richEditPerformKeyBinding:")]
        private void PerformKeyBinding(UIKeyCommand command)
        {
            if (MarkedTextRange is not null || command.PropertyList is not NSNumber gesture) return;
            KeyDownRequested?.Invoke((EditorKey)(gesture.Int32Value & 0xFFFF), (EditorKeyModifiers)(gesture.Int32Value >> 16));
        }

        private static string? GetKeyCommandInput(EditorKey key)
        {
            if (key is >= EditorKey.A and <= EditorKey.Z) return ((char)('a' + key - EditorKey.A)).ToString();
            if (key is >= EditorKey.D0 and <= EditorKey.D9) return ((char)('0' + key - EditorKey.D0)).ToString();
            if (key is >= EditorKey.NumPad0 and <= EditorKey.NumPad9) return ((char)('0' + key - EditorKey.NumPad0)).ToString();
            return key switch
            {
                EditorKey.Tab => "\t",
                EditorKey.Enter => "\r",
                EditorKey.Backspace => "\b",
                EditorKey.Delete => UIKeyCommand.Delete,
                EditorKey.Escape => UIKeyCommand.Escape,
                EditorKey.Left => UIKeyCommand.LeftArrow,
                EditorKey.Up => UIKeyCommand.UpArrow,
                EditorKey.Right => UIKeyCommand.RightArrow,
                EditorKey.Down => UIKeyCommand.DownArrow,
                EditorKey.Home => UIKeyCommand.Home,
                EditorKey.End => UIKeyCommand.End,
                EditorKey.PageUp => UIKeyCommand.PageUp,
                EditorKey.PageDown => UIKeyCommand.PageDown,
                EditorKey.F1 => UIKeyCommand.F1,
                EditorKey.F2 => UIKeyCommand.F2,
                EditorKey.F3 => UIKeyCommand.F3,
                EditorKey.F4 => UIKeyCommand.F4,
                EditorKey.F5 => UIKeyCommand.F5,
                EditorKey.F6 => UIKeyCommand.F6,
                EditorKey.F7 => UIKeyCommand.F7,
                EditorKey.F8 => UIKeyCommand.F8,
                EditorKey.F9 => UIKeyCommand.F9,
                EditorKey.F10 => UIKeyCommand.F10,
                EditorKey.F11 => UIKeyCommand.F11,
                EditorKey.F12 => UIKeyCommand.F12,
                EditorKey.Space => " ",
                EditorKey.Semicolon => ";",
                EditorKey.Plus => "=",
                EditorKey.Add => "+",
                EditorKey.Minus or EditorKey.Subtract => "-",
                EditorKey.Comma => ",",
                EditorKey.Period or EditorKey.Decimal => ".",
                EditorKey.Slash or EditorKey.Divide => "/",
                EditorKey.Multiply => "*",
                EditorKey.Backquote => "`",
                EditorKey.OpenBracket => "[",
                EditorKey.CloseBracket => "]",
                EditorKey.Backslash => "\\",
                EditorKey.Quote => "\"",
                _ => null,
            };
        }

        /// <inheritdoc />
        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt) =>
            ForwardPresses(presses, press => !HandleKeyDown(press), remaining => base.PressesBegan(remaining, evt));

        /// <inheritdoc />
        public override void PressesChanged(NSSet<UIPress> presses, UIPressesEvent evt) =>
            ForwardPresses(presses, press => press.Key is not { } key || !_handledKeys.Contains((int)key.KeyCode),
                remaining => base.PressesChanged(remaining, evt));

        /// <inheritdoc />
        public override void PressesEnded(NSSet<UIPress> presses, UIPressesEvent evt) =>
            ForwardPresses(presses, press => press.Key is not { } key || !_handledKeys.Remove((int)key.KeyCode),
                remaining => base.PressesEnded(remaining, evt));

        /// <inheritdoc />
        public override void PressesCancelled(NSSet<UIPress> presses, UIPressesEvent evt) =>
            ForwardPresses(presses, press => press.Key is not { } key || !_handledKeys.Remove((int)key.KeyCode),
                remaining => base.PressesCancelled(remaining, evt));

        private bool HandleKeyDown(UIPress press)
        {
            if (press.Key is not { } key) return false;
            var code = (int)key.KeyCode;
            if (_handledKeys.Contains(code)) return true;
            if (MarkedTextRange is not null || KeyDownRequested is not { } requested) return false;
            var modifiers = EditorKeyModifiers.None;
            if ((key.ModifierFlags & UIKeyModifierFlags.Shift) != 0) modifiers |= EditorKeyModifiers.Shift;
            if ((key.ModifierFlags & UIKeyModifierFlags.Control) != 0) modifiers |= EditorKeyModifiers.Control;
            if ((key.ModifierFlags & UIKeyModifierFlags.Alternate) != 0) modifiers |= EditorKeyModifiers.Alt;
            if ((key.ModifierFlags & UIKeyModifierFlags.Command) != 0) modifiers |= EditorKeyModifiers.Meta;
            if (!requested(GetEditorKey(key), modifiers))
            {
                ProjectionHandler?.PrepareNativeSourceKey(GetEditorKey(key), modifiers);
                if (MoveSourceVertically(GetEditorKey(key), modifiers)) { _handledKeys.Add(code); return true; }
                return false;
            }
            _handledKeys.Add(code);
            return true;
        }

        private static void ForwardPresses(NSSet<UIPress> presses, Func<UIPress, bool> include, Action<NSSet<UIPress>> forward)
        {
            var remaining = presses.Where(include).ToArray();
            if (remaining.Length == (int)presses.Count) forward(presses);
            else if (remaining.Length != 0)
            {
                using var unhandled = new NSSet<UIPress>(remaining);
                forward(unhandled);
            }
        }

        private static EditorKey GetEditorKey(UIKey key)
        {
            // UIKey uses USB HID usages. Resolve letter keys through the active layout.
            var code = (int)key.KeyCode;
            if (code is >= 4 and <= 29)
            {
                var text = key.CharactersIgnoringModifiers;
                if (text.Length == 1 && char.ToUpperInvariant(text[0]) is >= 'A' and <= 'Z' and var letter)
                    return EditorKey.A + (letter - 'A');
                return EditorKey.A + (code - 4);
            }
            if (code is >= 30 and <= 38) return EditorKey.D1 + (code - 30);
            if (code is >= 58 and <= 69) return EditorKey.F1 + (code - 58);
            if (code is >= 104 and <= 115) return EditorKey.F13 + (code - 104);
            if (code is >= 89 and <= 97) return EditorKey.NumPad1 + (code - 89);
            return code switch
            {
                39 => EditorKey.D0,
                40 or 88 => EditorKey.Enter,
                41 => EditorKey.Escape,
                42 => EditorKey.Backspace,
                43 => EditorKey.Tab,
                44 => EditorKey.Space,
                45 => EditorKey.Minus,
                46 => EditorKey.Plus,
                47 => EditorKey.OpenBracket,
                48 => EditorKey.CloseBracket,
                49 or 100 => EditorKey.Backslash,
                51 => EditorKey.Semicolon,
                52 => EditorKey.Quote,
                53 => EditorKey.Backquote,
                54 => EditorKey.Comma,
                55 => EditorKey.Period,
                56 => EditorKey.Slash,
                73 => EditorKey.Insert,
                74 => EditorKey.Home,
                75 => EditorKey.PageUp,
                76 => EditorKey.Delete,
                77 => EditorKey.End,
                78 => EditorKey.PageDown,
                79 => EditorKey.Right,
                80 => EditorKey.Left,
                81 => EditorKey.Down,
                82 => EditorKey.Up,
                84 => EditorKey.Divide,
                85 => EditorKey.Multiply,
                86 => EditorKey.Subtract,
                87 => EditorKey.Add,
                98 => EditorKey.NumPad0,
                99 => EditorKey.Decimal,
                _ => EditorKey.None,
            };
        }
    }
}

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        [SupportedOSPlatform("ios16.0")]
        [SupportedOSPlatform("maccatalyst16.0")]
        private UIMenu CreateAppleContextMenu(UIMenuElement[] suggestedActions)
        {
            var menu = VirtualView.CreateContextMenu();
            var elements = new List<UIMenuElement>();
            if (menu.IncludeDefaultItems) elements.AddRange(suggestedActions);
            elements.AddRange(CreateAppleMenuItems(menu.Items));
            return UIMenu.Create([.. elements]);
        }

        private static UIMenuElement[] CreateAppleMenuItems(IEnumerable<IMenuElement> items, bool parentEnabled = true)
        {
            var result = new List<UIMenuElement>();
            var group = new List<UIMenuElement>();
            foreach (var item in items)
            {
                if (item is IMenuFlyoutSeparator)
                {
                    FlushGroup();
                    continue;
                }
                var enabled = parentEnabled && EditorMenu.CanExecute(item);
                if (item is IMenuFlyoutSubItem submenu)
                    group.Add(UIMenu.Create(item.Text, CreateAppleMenuItems(submenu, enabled)));
                else
                {
                    var action = UIAction.Create(item.Text, null, null, _ =>
                    {
                        if (parentEnabled) EditorMenu.Execute(item);
                    });
                    action.Attributes = enabled ? 0 : UIMenuElementAttributes.Disabled;
                    group.Add(action);
                }
            }
            FlushGroup();
            return [.. result];

            void FlushGroup()
            {
                if (group.Count == 0) return;
                result.Add(UIMenu.Create(string.Empty, null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, [.. group]));
                group.Clear();
            }
        }
    }
}
#endif
