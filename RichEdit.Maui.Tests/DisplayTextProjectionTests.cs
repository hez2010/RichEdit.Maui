using CodeEdit.Maui;

namespace RichEdit.Maui.Tests;

public sealed class DisplayTextProjectionTests
{
    [Fact]
    public Task IncrementalDisplayTextMatchesFreshProjectionAcrossEditsReservationsAndWrapping() => WindowsTestHost.RunAsync(() =>
    {
        var random = new Random(314159);
        var editor = new CodeEditor { Document = new("first\r\nsecond\rthird\nlast"), WordWrap = false };
        RichTextDisplayProjection? prior = null;
        TextSnapshot? priorSource = null, priorDisplay = null;
        for (var iteration = 0; iteration < 250; iteration++)
        {
            if (iteration % 3 == 0)
            {
                var options = new RichTextAdornmentOptions
                {
                    Placement = (RichTextAdornmentPlacement)(2 + random.Next(3)),
                    Affinity = random.Next(2) == 0 ? PositionAffinity.BeforeInsertion : PositionAffinity.AfterInsertion,
                };
                var item = editor.Adornments.Add(random.Next(editor.Document.Length + 1), new Label(), options);
                item.MeasuredSize = new(random.Next(10, 120), 20);
            }
            else if (iteration % 5 == 0 && editor.Adornments.Count > 0)
                editor.Adornments[random.Next(editor.Adornments.Count)].Dispose();
            else
            {
                var start = random.Next(editor.Document.Length + 1);
                var count = random.Next(Math.Min(5, editor.Document.Length - start) + 1);
                var text = new[] { "x", "\r\n", "\r", "\n", "😀", "" }[random.Next(6)];
                editor.Document.Edit(edit => edit.ReplaceText(new(start, count), text));
            }
            var width = iteration % 7 == 0 ? 25 : 150;
            var current = RichTextDisplayProjection.Create(editor.TextView, width);
            var source = editor.Document.CurrentSnapshot.TextBuffer;
            var display = current.ProjectText(source, prior, priorSource, priorDisplay);
            var expected = editor.Document.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var endings = new LfTextProjection(source);
            foreach (var reservation in current.Reservations.Reverse()) expected = expected.Insert(endings.ToDisplay(reservation.SourcePosition), reservation.Text);
            Assert.Equal(expected, display.ToString());
            Assert.Equal(expected, current.Project(editor.TextView.Document.CurrentSnapshot, width, display).Text);
            Assert.Same(display, current.ProjectText(source, current, source, display));
            prior = current; priorSource = source; priorDisplay = display;
        }
    });

    [Fact]
    public Task LocalSourceEditRetainsTheLargeDisplayTreeOutsideItsAffectedReservationRegion() => WindowsTestHost.RunAsync(() =>
    {
        var editor = new CodeEditor { Document = new(new string('x', 10 * 1024 * 1024)) };
        var before = editor.Document.CurrentSnapshot.TextBuffer;
        var position = before.Length / 2;
        var item = editor.Adornments.Add(position, new Label(), new() { Placement = RichTextAdornmentPlacement.Inline });
        item.MeasuredSize = new(20, 20);
        var prior = RichTextDisplayProjection.Create(editor.TextView, 200);
        var display = prior.ProjectText(before);
        editor.Document.Edit(edit => edit.InsertText(position, "!"));
        var current = RichTextDisplayProjection.Create(editor.TextView, 200);
        var after = editor.Document.CurrentSnapshot.TextBuffer;
        var result = current.ProjectText(after, prior, before, display);
        var change = display.FindDifference(result, out var visits)!.Value;
        Assert.InRange(visits, 1, 256);
        Assert.Equal(new(position, 0), change.Before);
        Assert.Equal("!", result.GetText(change.After));
        Assert.Equal(before.Length + 2, result.Length);
    });
}
