#if WINDOWS
using System.Runtime.InteropServices;
using Clipboard = Windows.ApplicationModel.DataTransfer.Clipboard;
using DataPackage = Windows.ApplicationModel.DataTransfer.DataPackage;

namespace RichEdit.Maui;

internal static class WindowsClipboardAccess
{
    // Another process can briefly own the clipboard while a copy command runs.
    // Yield between bounded retries; never block the UI thread or claim success
    // when the final publish/flush failed.
    internal static async Task SetAsync(DataPackage package)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return;
            }
            catch (COMException error) when (error.HResult == unchecked((int)0x800401D0) && attempt < 4)
            {
                await Task.Delay(10 << attempt);
            }
        }
    }
}
#endif
