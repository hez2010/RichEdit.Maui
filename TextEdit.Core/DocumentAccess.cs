namespace TextEdit.Core;

// Shared live-document ownership and reentrancy policy. Immutable snapshots never
// carry this dispatcher state and remain freely readable on background threads.
internal sealed class DocumentAccess
{
    private int _ownerThread;
    internal bool IsBusy { get; set; }

    internal void VerifyAccess()
    {
        var thread = Environment.CurrentManagedThreadId;
        var owner = Interlocked.CompareExchange(ref _ownerThread, thread, 0);
        if (owner != 0 && owner != thread)
            throw new InvalidOperationException("Live document mutations belong to the owning thread. Share immutable snapshots for background reads.");
    }

    internal void VerifyIdle()
    {
        VerifyAccess();
        if (IsBusy) throw new InvalidOperationException("A live document cannot be mutated from an edit callback or change notification.");
    }

    internal T Plan<T>(Func<T> action)
    {
        VerifyIdle();
        IsBusy = true;
        try { return action(); }
        finally { IsBusy = false; }
    }
}
