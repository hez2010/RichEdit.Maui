using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextInputRulesTests
{
    [Fact]
    public void CompletionAndCommitCharacterProduceOneCandidate()
    {
        var rules = new TextInputRules();
        using var registration = rules.Register(context => context.Replace(new(0, 4), "Console."));
        var result = rules.Plan(TextSnapshot.FromText("Con"), TextSnapshot.FromText("Con."),
            TextSelectionSet.Caret(3), TextSelectionSet.Caret(4), [new(new(3, 0), ".")]);
        Assert.Equal("Console.", result.Snapshot.ToString());
        Assert.Equal(8, result.Selections.Primary.Active);
    }

    [Fact]
    public void PairedDelimitersCanSetFinalSelectionsAndBuildersExpire()
    {
        var rules = new TextInputRules();
        TextInputContext? retained = null;
        using var registration = rules.Register(context =>
        {
            retained = context;
            context.Replace(new(1, 0), ")");
            context.SetSelections(context.Selections);
        });
        var selection = TextSelectionSet.Caret(1);
        var result = rules.Plan(TextSnapshot.Empty, TextSnapshot.FromText("("), TextSelectionSet.Caret(0), selection,
            [new(new(0, 0), "(")]);
        Assert.Equal("()", result.Snapshot.ToString());
        Assert.Same(selection, result.Selections);
        Assert.Throws<InvalidOperationException>(() => retained!.Replace(new(0, 0), "late"));
    }

    [Fact]
    public void RuleOrderIsStableAndConflictingPlansRejectWithoutMutatingInput()
    {
        var rules = new TextInputRules();
        var order = new List<int>();
        using var first = rules.Register(context => { order.Add(1); context.Replace(new(0, 1), "one"); }, 2);
        using var second = rules.Register(context => { order.Add(2); context.Replace(new(0, 1), "two"); }, 1);
        using var third = rules.Register(_ => order.Add(3), 2);
        var before = TextSnapshot.Empty;
        var input = TextSnapshot.FromText("x");
        Assert.Throws<InvalidOperationException>(() => rules.Plan(before, input, TextSelectionSet.Caret(0), TextSelectionSet.Caret(1), [new(new(0, 0), "x")]));
        Assert.Equal(new[] { 2, 1, 3 }, order);
        Assert.Equal("x", input.ToString());
        Assert.Equal("", before.ToString());
    }

    [Fact]
    public void LinkedMirrorsUseOriginalCandidateCoordinates()
    {
        var rules = new TextInputRules();
        using var rule = rules.Register(context => context.Replace(new(4, 1), "foo"));
        var input = TextSnapshot.FromText("foo x");
        var result = rules.Plan(TextSnapshot.FromText("x x"), input, TextSelectionSet.Caret(1), TextSelectionSet.Caret(3),
            [new(new(0, 1), "foo")], isCompositionCommit: true);
        Assert.Equal("foo foo", result.Snapshot.ToString());
        Assert.Equal(3, result.Selections.Primary.Active);
    }
}
