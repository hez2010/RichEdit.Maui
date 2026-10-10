using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial bool TryCompleteCompositionCore()
    {
        if (_isComposing)
            return false;
        UpdateCompositionState();
        return !VirtualView.Composition.IsActive;
    }

    private partial void ConnectInputObservation()
    {
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(PlatformView) as SourceTextPeer)?.Connect();
        PlatformView.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnObservedPointerExited), true);
    }

    private partial void DisconnectInputObservation()
    {
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_adornmentView) as SourceTextPeer)?.Disconnect();
        _adornmentView.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnObservedPointerExited));
    }

    private partial RichTextCompositionState GetCompositionStateCore() => new(_isComposing, null);

    private partial void ReconcileCompositionSource() => ReadNativeDocumentChange();

    private void OnObservedPointerExited(object sender, PointerRoutedEventArgs args) => VirtualView.ObservePointerExit();

    private bool ObservePointer(PointerRoutedEventArgs args, bool pressed)
    {
        if (!_adornmentsConnected) return false;
        var point = args.GetCurrentPoint(PlatformView);
        var properties = point.Properties;
        var buttons = RichTextPointerButtons.None;
        if (properties.IsLeftButtonPressed)
            buttons |= RichTextPointerButtons.Primary;
        if (properties.IsRightButtonPressed)
            buttons |= RichTextPointerButtons.Secondary;
        if (properties.IsMiddleButtonPressed)
            buttons |= RichTextPointerButtons.Middle;

        var kind = point.PointerDeviceType switch
        {
            PointerDeviceType.Touch => RichTextPointerDeviceKind.Touch,
            PointerDeviceType.Pen => RichTextPointerDeviceKind.Pen,
            _ => RichTextPointerDeviceKind.Mouse,
        };
        return VirtualView.ObservePointer(new(new(point.Position.X, point.Position.Y), kind, buttons, GetEditorModifiers()), pressed);
    }

    private sealed partial class SourceRichEditBox
    {
        protected override void OnPointerPressed(PointerRoutedEventArgs args)
        {
            if (handler.ObservePointer(args, true))
            {
                args.Handled = true;
                Focus(FocusState.Pointer);
                CapturePointer(args.Pointer);
            }
            else base.OnPointerPressed(args);
        }
        protected override void OnPointerMoved(PointerRoutedEventArgs args)
        {
            if (handler.ObservePointer(args, false)) args.Handled = true;
            else base.OnPointerMoved(args);
        }
        protected override void OnPointerReleased(PointerRoutedEventArgs args)
        {
            if (handler.ObservePointer(args, false)) { args.Handled = true; ReleasePointerCapture(args.Pointer); }
            else base.OnPointerReleased(args);
        }
    }
}
