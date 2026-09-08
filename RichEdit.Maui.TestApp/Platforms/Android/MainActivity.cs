using Android.App;
using Android.Content.PM;
using Android.OS;

namespace RichEdit.Maui.TestApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
#if DEBUG
    private bool _testsStarted;

    protected override void OnPostResume()
    {
        base.OnPostResume();
        if (!_testsStarted && Intent?.GetBooleanExtra("run-code-editor-tests", false) == true)
        {
            _testsStarted = true;
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new Tests.CodeEditorTestPage(Intent?.GetStringExtra("test-filter"));
        }
        if (!_testsStarted && Intent?.GetBooleanExtra("run-editor-tests", false) == true)
        {
            _testsStarted = true;
            var editor = new RichEditor();
            editor.Loaded += async (_, _) => await AndroidEditorTests.RunAsync(editor, Intent?.GetStringExtra("test-filter"));
            Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = new ContentPage
            {
                Content = editor,
                SafeAreaEdges = new (SafeAreaRegions.Container),
            };
        }
    }
#endif
}
