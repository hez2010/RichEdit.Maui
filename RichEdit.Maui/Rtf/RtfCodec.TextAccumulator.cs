using System.Text;

namespace RichEdit.Maui;

internal static partial class RtfCodec
{
    private sealed class TextAccumulator
    {
        private readonly StringBuilder _text = new();
        private readonly List<(int Start, int Length, RichTextCharacterFormat Format)> _runs = [];

        public string Text => _text.ToString();

        public int Length => _text.Length;

        public char? LastCharacter => _text.Length == 0 ? null : _text[^1];

        public IEnumerable<RichTextRun> Runs => _runs.Select(static run => new RichTextRun(run.Start, run.Length, run.Format));

        public void Append(char character, RichTextCharacterFormat format)
        {
            var start = _text.Length;
            _text.Append(character);
            if (_runs.Count > 0)
            {
                var previous = _runs[^1];
                if (previous.Start + previous.Length == start && previous.Format == format)
                {
                    _runs[^1] = (previous.Start, previous.Length + 1, previous.Format);
                    return;
                }
            }

            _runs.Add((start, 1, format));
        }

        public void Append(TextAccumulator fragment)
        {
            var offset = _text.Length;
            _text.Append(fragment._text);
            foreach (var run in fragment._runs)
            {
                var adjusted = (Start: run.Start + offset, run.Length, run.Format);
                if (_runs.Count > 0 &&
                    _runs[^1].Start + _runs[^1].Length == adjusted.Start &&
                    _runs[^1].Format == adjusted.Format)
                {
                    var previous = _runs[^1];
                    _runs[^1] = (previous.Start, previous.Length + adjusted.Length, previous.Format);
                }
                else
                {
                    _runs.Add(adjusted);
                }
            }
        }
    }
}
