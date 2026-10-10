using RichEdit.Core.Rtf;
using Xunit;

namespace RichEdit.Core.Tests;

public class RtfProjectionWriterTests
{
    [Fact]
    public async Task NativeTableProjectionUsesMergedCellAndPreferredWidthControls()
    {
        var document = new RichDocument();
        RichNodeId table = default;
        document.Edit(edit =>
        {
            table = edit.InsertTable(edit.Body, 1, 2, [new(1440), new(1440)]);
            var initial = edit.Snapshot;
            var paragraph = initial.Get<RichTableCell>(initial.GetTableGrid(table)[0, 0]).Blocks[0];
            edit.ReplaceText(new(edit.Body, paragraph, 0), 0, "alpha😀");
            edit.SetCharacterFormat(new(edit.Body, paragraph, 0), 5, new() { Bold = true, Foreground = new RichColor(255, 0, 0) });
            edit.MergeCells(new(table, 0, 0, 1, 2));
            edit.DeleteBlock(new(2));
        });
        var tokens = await Tokens(document.CurrentSnapshot);
        Assert.Equal(4, tokens.Count(token => token.Word == "trowd"));
        Assert.Equal(4, tokens.Count(token => token.Word == "cell"));
        Assert.Equal(2, tokens.Count(token => token.Word == "clmgf"));
        Assert.Equal(2, tokens.Count(token => token.Word == "clmrg"));
        Assert.Contains(tokens, token => token.Word == "trwWidth" && token.Number == 2880);
        Assert.Contains(tokens, token => token.Word == "clwWidth" && token.Number == 1440);
        Assert.Contains(tokens, token => token.Word == "b" && token.Number == 1);
        Assert.Contains(tokens, token => token.Word == "u" && token.Number < 0);
    }

    [Fact]
    public async Task NativeNestedTablesUseSeparateCellAndRowTerminators()
    {
        var snapshot = RichDocumentSnapshot.Build(edit =>
        {
            var outer = edit.InsertTable(edit.Body, 1, 1, [new(4000)]);
            var cell = edit.Snapshot.GetTableGrid(outer)[0, 0];
            edit.InsertTable(cell, 1, 2, [new(1500), new(1500)]);
            edit.DeleteBlock(new(2));
        });
        var tokens = await Tokens(snapshot);
        Assert.Equal(4, tokens.Count(token => token.Word == "nestcell"));
        Assert.Equal(2, tokens.Count(token => token.Word == "nestrow"));
        Assert.Single(tokens, token => token.Word == "row");
        Assert.Contains(tokens, token => token.Word == "itap" && token.Number == 2);
    }

    private static async Task<List<RtfToken>> Tokens(RichDocumentSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        await new RtfProjectionWriter(snapshot, stream).WriteAsync(TestContext.Current.CancellationToken);
        stream.Position = 0;
        var reader = new RtfTokenReader(stream);
        var tokens = new List<RtfToken>();
        while (true)
        {
            var token = await reader.ReadAsync(TestContext.Current.CancellationToken);
            if (token.Kind == RtfTokenKind.End) return tokens;
            tokens.Add(token);
        }
    }
}
