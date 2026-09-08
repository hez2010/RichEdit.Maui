#if IOS || MACCATALYST
using Foundation;
using UIKit;

namespace RichEdit.Maui;

public partial class RichEditorHandler
{
    private FoldLayoutDelegate? _foldLayoutDelegate;
    private INSLayoutManagerDelegate? _previousFoldLayoutDelegate;

    private partial void ApplyFoldingCore()
    {
        if (PlatformView is null || _foldLayoutDelegate is null && VirtualView.Folding.EffectiveRanges.IsEmpty) return;
        var wasApplying = _applyingDocument;
        _applyingDocument = true;
        try
        {
            // TextKit 1's glyph delegate is available across the supported Apple OS
            // versions and keeps the complete NSTextStorage and character offsets.
            var layout = PlatformView.LayoutManager;
            if (VirtualView.Folding.EffectiveRanges.IsEmpty)
            {
                DisconnectFolding();
            }
            else if (_foldLayoutDelegate is null)
            {
                _previousFoldLayoutDelegate = layout.Delegate;
                _foldLayoutDelegate = new FoldLayoutDelegate(VirtualView.Folding);
                layout.Delegate = _foldLayoutDelegate;
            }
            var range = new NSRange(0, PlatformView.TextStorage.Length);
            layout.InvalidateGlyphs(range, 0, out _);
            layout.InvalidateLayout(range);
            PlatformView.SetNeedsLayout();
            PlatformView.SetNeedsDisplay();
        }
        finally { _applyingDocument = wasApplying; }
    }

    private void DisconnectFolding()
    {
        if (_foldLayoutDelegate is null) return;
        PlatformView.LayoutManager.Delegate = _previousFoldLayoutDelegate!;
        _previousFoldLayoutDelegate = null;
        _foldLayoutDelegate.Dispose();
        _foldLayoutDelegate = null;
    }

    private sealed class FoldLayoutDelegate(RichTextFolding folding) : NSLayoutManagerDelegate
    {
        public override unsafe nuint ShouldGenerateGlyphs(NSLayoutManager layoutManager, nint glyphBuffer, nint properties,
            nint characterIndexes, UIFont font, NSRange glyphRange)
        {
            var count = checked((int)glyphRange.Length);
            var indexes = (nuint*)characterIndexes;
            nuint[]? foldedProperties = null;
            for (var index = 0; index < count; index++)
            {
                if (folding.FindRange(checked((int)indexes[index])) is null) continue;
                foldedProperties ??= new ReadOnlySpan<nuint>((void*)properties, count).ToArray();
                foldedProperties[index] = (nuint)NSGlyphProperty.Null;
            }
            if (foldedProperties is null) return 0;
            fixed (nuint* buffer = foldedProperties)
                layoutManager.SetGlyphs(glyphBuffer, (nint)buffer, characterIndexes, font, glyphRange);
            return (nuint)glyphRange.Length;
        }

        public override NSControlCharacterAction ShouldUseAction(NSLayoutManager layoutManager, NSControlCharacterAction action,
            nuint characterIndex) => folding.FindRange(checked((int)characterIndex)) is null ? action : NSControlCharacterAction.ZeroAdvancement;
    }
}
#endif
