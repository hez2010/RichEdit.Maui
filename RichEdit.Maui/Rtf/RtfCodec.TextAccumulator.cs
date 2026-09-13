using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed class TextAccumulator
    {
        private readonly StringBuilder _text = new();
        private readonly List<RichTextRun> _runs = [];

        public string Text => _text.ToString();

        public int Length => _text.Length;

        public char? LastCharacter => _text.Length == 0 ? null : _text[^1];

        public IReadOnlyList<RichTextRun> Runs => _runs;

        public void Append(char character, RichTextCharacterFormat format)
        {
            var start = _text.Length;
            _text.Append(character);
            if (_runs.Count > 0)
            {
                var previous = _runs[^1];
                if (previous.End == start && previous.Format == format)
                {
                    _runs[^1] = previous with { Length = previous.Length + 1 };
                    return;
                }
            }

            _runs.Add(new RichTextRun(start, 1, format));
        }

        public void Append(TextAccumulator fragment)
        {
            var offset = _text.Length;
            _text.Append(fragment._text);
            foreach (var run in fragment._runs)
            {
                var adjusted = run with { Start = run.Start + offset };
                if (_runs.Count > 0 &&
                    _runs[^1].End == adjusted.Start &&
                    _runs[^1].Format == adjusted.Format)
                {
                    _runs[^1] = _runs[^1] with
                    {
                        Length = _runs[^1].Length + adjusted.Length,
                    };
                }
                else
                {
                    _runs.Add(adjusted);
                }
            }
        }
    }
}
