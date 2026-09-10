using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private partial void ConnectInputObservation()
    {
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(PlatformView) as SourceTextPeer)?.Connect();
        PlatformView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnObservedPointerMoved), true);
        PlatformView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnObservedPointerPressed), true);
        PlatformView.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnObservedPointerExited), true);
    }
    private partial void DisconnectInputObservation()
    {
        (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_adornmentView) as SourceTextPeer)?.Disconnect();
        _adornmentView.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnObservedPointerMoved));
        _adornmentView.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnObservedPointerPressed));
        _adornmentView.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnObservedPointerExited));
    }
    private partial RichTextCompositionState GetCompositionStateCore() => new(_isComposing, null);
    private partial void ReconcileCompositionSource() => ReadNativeDocumentChange();
    private void OnObservedPointerMoved(object sender, PointerRoutedEventArgs args) => ObservePointer(args, false);
    private void OnObservedPointerPressed(object sender, PointerRoutedEventArgs args) => ObservePointer(args, true);
    private void OnObservedPointerExited(object sender, PointerRoutedEventArgs args) => VirtualView.ObservePointerExit();
    private void ObservePointer(PointerRoutedEventArgs args, bool pressed)
    {
        var point = args.GetCurrentPoint(PlatformView);
        var properties = point.Properties;
        var buttons = RichTextPointerButtons.None;
        if (properties.IsLeftButtonPressed) buttons |= RichTextPointerButtons.Primary;
        if (properties.IsRightButtonPressed) buttons |= RichTextPointerButtons.Secondary;
        if (properties.IsMiddleButtonPressed) buttons |= RichTextPointerButtons.Middle;
        var kind = point.PointerDeviceType switch
        {
            PointerDeviceType.Touch => RichTextPointerDeviceKind.Touch,
            PointerDeviceType.Pen => RichTextPointerDeviceKind.Pen,
            _ => RichTextPointerDeviceKind.Mouse,
        };
        VirtualView.ObservePointer(new(new(point.Position.X, point.Position.Y), kind, buttons, GetEditorModifiers()), pressed);
    }
}
