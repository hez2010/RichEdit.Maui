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
            _structureHost = new(this);
            Content = _structureHost;
            _view.Handler = null;
        }
        else if (!needsHost && _structureHost is not null)
        {
            var previous = _structureHost;
            _structureHost = null;
            Content = _view;
            previous.Handler?.DisconnectHandler();
        }
    }
}
