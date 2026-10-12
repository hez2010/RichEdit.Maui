#if (DEBUG || EDITOR_TESTS) && (IOS || MACCATALYST)
using UIKit;

namespace RichEdit.Maui.Tests;

internal static class AppleTestEnvironment
{
    internal static async Task RunAsync(Func<Task> tests)
    {
        var application = UIApplication.SharedApplication;
        var previous = application.IdleTimerDisabled;
        application.IdleTimerDisabled = true;
#if MACCATALYST
        var activity = Foundation.NSProcessInfo.ProcessInfo.BeginActivity(Foundation.NSActivityOptions.UserInitiated, "Native editor validation");
#endif
        try { await tests(); }
        finally
        {
#if MACCATALYST
            Foundation.NSProcessInfo.ProcessInfo.EndActivity(activity);
            activity.Dispose();
#endif
            application.IdleTimerDisabled = previous;
        }
    }
}
#endif
