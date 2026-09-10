namespace RichEdit.Maui.TestApp;

internal static class StudioTheme
{
    internal static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;
    internal static Color Get(string key) => (Color)Application.Current!.Resources[(IsDark ? "Dark" : "Light") + key];

    internal static void Toggle()
    {
        if (Application.Current is { } app) app.UserAppTheme = IsDark ? AppTheme.Light : AppTheme.Dark;
    }
}
