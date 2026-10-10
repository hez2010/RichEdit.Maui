namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private int _compositionOperationDepth;
    private bool _reconcilingInputOperation;

    private bool HasTransientInputSelection => _compositionOperationDepth > 0 && !_reconcilingInputOperation;

    internal void CompleteCompositionTracking()
    {
        _inputPresentationSource = null;
        _inputPresentation = null;
        _inputPresentationMap = null;
        CompleteCompositionTrackingCore();
    }

    partial void CompleteCompositionTrackingCore();

    bool IRichEditorHandler.TryCompleteComposition() => TryCompleteCompositionCore();

    private partial bool TryCompleteCompositionCore();

    private void BeginCompositionOperation(bool startsComposition)
    {
        _compositionOperationDepth++;
        if (startsComposition || VirtualView.Composition.IsActive)
            VirtualView.SetCompositionState(new(true, null));
    }

    private void EndCompositionOperation()
    {
        if (--_compositionOperationDepth != 0)
            return;

        _compositionOperationDepth = 1;
        _reconcilingInputOperation = true;
        try
        {
            ReconcileCompositionSource();
        }
        finally
        {
            _compositionOperationDepth = 0;
            _reconcilingInputOperation = false;
        }

        UpdateCompositionState();
    }

    private void UpdateCompositionState()
    {
        if (_applyingDocument || _compositionOperationDepth != 0)
            return;

        var state = GetCompositionStateCore();
        if (state.Range is { } nativeRange)
        {
            var start = NativeProjection.ToSource(nativeRange.Start);
            state = new(state.IsActive, new(start, NativeProjection.ToSource(nativeRange.End) - start));
        }
        if (state.Range is { } range && range.End > VirtualView.InputSnapshot.Length)
            state = new(state.IsActive, null);

        VirtualView.SetCompositionState(state);
        if (!state.IsActive)
            QueueAdornmentLayout();
    }

    private partial RichTextCompositionState GetCompositionStateCore();

    private partial void ReconcileCompositionSource();

    private partial void ConnectInputObservation();

    private partial void DisconnectInputObservation();
}
