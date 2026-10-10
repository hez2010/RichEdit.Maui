using System.Runtime.InteropServices;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class WindowsClipboardAccessTests
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [Fact]
    public Task CopyWaitsForTransientClipboardOwnershipWithoutBlockingTheUi() => WindowsTestHost.RunClipboardAsync(async () =>
    {
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var locked = false;
        var owner = Task.Run(() =>
        {
            locked = OpenClipboard(0);
            ready.Set();
            try { release.Wait(TimeSpan.FromSeconds(5)); }
            finally { if (locked) CloseClipboard(); }
        });
        ready.Wait(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(locked);
            var copy = RichTextClipboard.SetAsync(RichTextDocumentFragment.FromPlainText("clipboard retry"));
            Assert.False(copy.IsCompleted);
            await Task.Delay(25);
            release.Set();
            await owner;
            await copy;
            Assert.Equal("clipboard retry", (await RichTextClipboard.GetAsync())!.Text);
        }
        finally { release.Set(); await owner; }
    });
}
