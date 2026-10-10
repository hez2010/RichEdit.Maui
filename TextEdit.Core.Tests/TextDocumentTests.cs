using TextEdit.Core;
using Xunit;

namespace TextEdit.Core.Tests;

public sealed class TextDocumentTests
{
    [Fact]
    public void NativeInputAndRulesPublishOneAtomicStateAndOneUndoUnit()
    {
        var document = new TextDocument("Con");
        using var view = document.CreateSession();
        view.Selections = TextSelectionSet.Caret(3);
        using var rule = view.InputRules.Register(input => input.Replace(new(0, 4), "Console."));
        var changes = 0;
        document.Changed += (_, args) =>
        {
            changes++;
            if (args.Options.Origin == TextChangeOrigin.User)
                Assert.Equal("Console.", document.Text);
        };
        Assert.True(view.ApplyInput([new(new(3, 0), ".")], TextSelectionSet.Caret(4)).Accepted);
        Assert.Equal(1, changes);
        Assert.Equal("Console.", document.Text);
        Assert.Equal(8, view.Selections.Primary.Active);
        view.Undo();
        Assert.Equal("Con", document.Text);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void FailedTransactionsAndExpiredBuildersCannotMutateTheDocument()
    {
        var document = new TextDocument("abc\r\ndef");
        var initial = document.CurrentSnapshot;
        TextDocumentEdit? retained = null;
        Assert.Throws<ArgumentOutOfRangeException>(() => document.Edit(edit =>
        {
            retained = edit;
            edit.Insert(0, "ok");
            edit.Delete(new(100, 1));
        }));
        Assert.Same(initial, document.CurrentSnapshot);
        Assert.False(document.CanUndo);
        Assert.Throws<InvalidOperationException>(() => retained!.Insert(0, "late"));
        Assert.Throws<InvalidOperationException>(() => document.Edit(_ => document.Edit(edit => edit.Insert(0, "recursive"))));
        Assert.Same(initial, document.CurrentSnapshot);
    }

    [Fact]
    public void SharedSessionsSynchronizeBeforeObserversAndUndoRestoresTheInvokingView()
    {
        var document = new TextDocument("one two");
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        first.Selections = new((TextSelection[])[new(3, 0)]);
        second.Selections = TextSelectionSet.Caret(7);
        var observed = 0;
        document.Changed += (_, change) =>
        {
            observed++;
            if (change.Options.Origin == TextChangeOrigin.Programmatic)
            {
                Assert.Equal("X two", document.Text);
                Assert.Equal(1, first.Selections.Primary.Active);
                Assert.Equal(5, second.Selections.Primary.Active);
            }
        };
        first.Edit(edit => { edit.Replace(new(0, 3), "X"); edit.SetSelections(TextSelectionSet.Caret(1)); });
        first.Selections = TextSelectionSet.Caret(0);
        second.Undo();
        Assert.Equal("one two", document.Text);
        Assert.Equal((3, 0), (second.Selections.Primary.Anchor, second.Selections.Primary.Active));
        Assert.Equal(3, first.Selections.Primary.Active);
        Assert.Equal(2, observed);
        first.Dispose();
        second.Redo();
        Assert.Equal("X two", document.Text);
        Assert.Equal(1, second.Selections.Primary.Active);
    }

    [Fact]
    public void RevisionAndSelectionChecksRejectStaleRequestsBeforeCallingTheBuilder()
    {
        var document = new TextDocument("a");
        var other = new TextDocument("a");
        using var view = document.CreateSession();
        var initial = document.Revision;
        var selection = view.SelectionGeneration;
        view.Selections = TextSelectionSet.Caret(1);
        void Unexpected(TextDocumentEdit _) => throw new Exception("The stale builder must not run.");
        Assert.Equal(TextEditRejection.StaleSelection, view.Edit(Unexpected, expectedSelectionGeneration: selection).Rejection);
        Assert.Equal(TextEditRejection.StaleRevision, document.TryEdit(other.Revision, Unexpected).Rejection);
        Assert.Equal(TextEditRejection.StaleRevision, document.TryEdit(default, Unexpected).Rejection);
        view.IsReadOnly = true;
        Assert.Equal(TextEditRejection.ReadOnly, view.Edit(Unexpected).Rejection);
        document.Edit(edit => edit.Insert(1, "b"));
        Assert.Equal(TextEditRejection.StaleRevision, document.TryEdit(initial, Unexpected).Rejection);
    }

    [Fact]
    public void SaveCapturePairsTextWithItsStateAcrossLaterEditsAndHistory()
    {
        var document = new TextDocument("first\r\nsecond\rthird\n");
        var saved = document.CaptureSave();
        document.Edit(edit => edit.Insert(0, "prefix"));
        document.MarkSaved(saved);
        Assert.True(document.IsModified);
        Assert.Equal("first\r\nsecond\rthird\n", saved.Snapshot.ToString());
        document.Undo();
        Assert.False(document.IsModified);
        Assert.True(document.Revision.Version > saved.Snapshot.Revision.Version);
        Assert.Equal(4, document.CurrentSnapshot.LineCount);
        Assert.Throws<ArgumentException>(() => document.MarkSaved(new TextDocument().CaptureSave()));
    }

    [Fact]
    public void ObserverFailureDoesNotPreventOtherViewsOrObserversFromReceivingTheCommit()
    {
        var document = new TextDocument("abc");
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        first.Changed += (_, _) => throw new InvalidOperationException("Rendering failed");
        var notified = 0;
        second.Changed += (_, _) => notified++;
        document.Changed += (_, _) => document.Edit(edit => edit.Insert(0, "recursive"));
        document.Changed += (_, _) => notified++;
        Assert.Throws<AggregateException>(() => document.Edit(edit => edit.Insert(0, "!")));
        Assert.Equal(2, notified);
        Assert.Equal("!abc", document.Text);
        Assert.Equal(1, second.Selections.Primary.Active);
        Assert.True(document.CanUndo);
    }

    [Fact]
    public void OriginalCoordinateBatchPreservesCallerOrderAndHasExactInverseChanges()
    {
        var document = new TextDocument("abcdef");
        var change = document.Edit(edit => edit.ReplaceBatch([new(new(0, 0), "1"), new(new(0, 0), "2"), new(new(4, 2), "X")]))!;
        Assert.Equal("12abcdX", document.Text);
        Assert.Equal(4, change.Map.Map(2));
        document.Undo();
        Assert.Equal("abcdef", document.Text);
        document.Redo();
        Assert.Equal("12abcdX", document.Text);
        Assert.Throws<ArgumentException>(() => document.Edit(edit => edit.ReplaceBatch([new(new(0, 3), "a"), new(new(2, 1), "b")])));
        Assert.Equal("12abcdX", document.Text);
    }

    [Fact]
    public void RetentionEvictsHistoryWithoutInvalidatingRetainedSnapshots()
    {
        var document = new TextDocument("large") { HistoryLimit = 2 };
        var original = document.CurrentSnapshot;
        for (var i = 0; i < 4; i++)
            document.Edit(edit => edit.Insert(0, "x"));
        document.Undo();
        document.Undo();
        Assert.False(document.CanUndo);
        Assert.Equal("xxlarge", document.Text);
        Assert.Equal("large", original.ToString());
        document.ClearHistory();
        Assert.False(document.CanRedo);
        Assert.True(document.IsModified);
    }

    [Fact]
    public void ImmutableReadsCrossThreadsWhileLiveMutationIsSerialized()
    {
        var document = new TextDocument("abc");
        using var session = document.CreateSession();
        var snapshot = document.CurrentSnapshot;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                Assert.Equal("abc", snapshot.ToString());
                Assert.Throws<InvalidOperationException>(() => document.Edit(edit => edit.Insert(0, "x")));
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start();
        worker.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void TenMiBSourceEditsAvoidDocumentSizedAllocationsAndKeepLineIndexesPersistent()
    {
        var lines = string.Concat(Enumerable.Repeat(new string('a', 100) + "\r\n", 100_000));
        var document = new TextDocument(lines + new string('z', 10 * 1024 * 1024 - lines.Length));
        var original = document.CurrentSnapshot;
        using var first = document.CreateSession();
        using var second = document.CreateSession();
        second.Selections = TextSelectionSet.Caret(document.Length);
        document.Edit(edit => edit.Insert(0, "x"));
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            var position = (i % 3) switch { 0 => 0, 1 => document.CurrentSnapshot.GetLine(50_000).Span.Start, _ => document.Length };
            document.Edit(edit => edit.Insert(position, "x"));
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(bytes < 64 * 1024 * 1024, $"200 local source edits allocated {bytes:N0} bytes.");
        Assert.Equal(10 * 1024 * 1024, original.Length);
        Assert.Equal(100_001, document.CurrentSnapshot.LineCount);
        Assert.Equal("aaaa", original.GetText(new(0, 4)));
        Assert.Equal(document.Length, second.Selections.Primary.Active);
    }
}
