#if DEBUG && (IOS || MACCATALYST)
using UIKit;

namespace RichEdit.Maui.Tests;

internal static class AppleTestEnvironment
{
    internal static async Task RunAsync(Func<Task> tests)
    {
        var application = UIApplication.SharedApplication;
        var previous = application.IdleTimerDisabled;
        application.IdleTimerDisabled = true;
        try { await tests(); }
        finally { application.IdleTimerDisabled = previous; }
    }
}
#endif
