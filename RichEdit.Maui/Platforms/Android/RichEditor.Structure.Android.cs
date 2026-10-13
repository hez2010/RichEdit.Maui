using RichEdit.Core;

namespace RichEdit.Maui;

public sealed partial class RichEditor
{
    private RichDocumentHostView? _structureHost;
    private bool _hostUpdateQueued;
    partial void UpdateStructureHost(bool immediate)
    {
        if (!immediate)
        {
            if (_hostUpdateQueued) return;
            _hostUpdateQueued = true;
            Dispatcher.Dispatch(() => { _hostUpdateQueued = false; UpdateStructureHost(true); });
            return;
        }
        if (Composition is not null) return;
        var needsHost = Document.CurrentSnapshot.Nodes.Values.Any(node => node is RichTable or RichList);
        if (needsHost && _structureHost is null)
        {
            var focused = _view.IsFocused;
            _structureHost = new(this);
            Content = _structureHost;
            _view.Handler = null;
            if (focused) Focus();
        }
        else if (!needsHost && _structureHost is not null)
        {
            var focused = _view.HasFocusedStructuredSurface;
            var previous = _structureHost;
            _structureHost = null;
            Content = _view;
            previous.Handler?.DisconnectHandler();
            if (focused) Focus();
        }
    }
}
