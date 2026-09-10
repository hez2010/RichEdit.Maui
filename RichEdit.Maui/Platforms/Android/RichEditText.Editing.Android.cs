using Android.Text;
using Java.Lang;

namespace RichEdit.Maui.Platforms.Android;

public partial class RichEditText
{
    private sealed class SourceEditableFactory(RichEditText view) : EditableFactory
    {
        private readonly WeakReference<RichEditText> _view = new(view);
        public override IEditable NewEditable(ICharSequence? source) => new SourceEditable(source, _view);
    }

    private sealed class SourceEditable(ICharSequence? source, WeakReference<RichEditText> view) : SpannableStringBuilder(source)
    {
        public override IEditable? Replace(int start, int end, ICharSequence? text, int textStart, int textEnd)
        {
            var handler = view.TryGetTarget(out var target) ? target.ProjectionHandler : null;
            handler?.BeginNativeTextChange();
            try { return base.Replace(start, end, text, textStart, textEnd); }
            finally { handler?.EndNativeTextChange(); }
        }
    }
}
