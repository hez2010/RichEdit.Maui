namespace RichEdit.Maui.Tests;

internal static class EditorTestOutput
{
    // External app files let adb retrieve Release reports without making the
    // application debuggable or enabling a debug runtime.
    internal static string Directory =>
#if ANDROID
        Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath ?? FileSystem.CacheDirectory;
#elif WINDOWS
        AppContext.BaseDirectory;
#else
        FileSystem.CacheDirectory;
#endif
}
