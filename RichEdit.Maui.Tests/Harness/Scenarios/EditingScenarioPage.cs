#if (DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST)
using System.Text.Json;

namespace RichEdit.Maui.Tests;

internal sealed class EditingScenarioPage(string suite, string? filter) : ContentPage
{
    private bool _started;
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
#if IOS || MACCATALYST
        await AppleTestEnvironment.RunAsync(RunAsync);
#else
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity!.Window!;
        var wasKeptOn = (window.Attributes!.Flags & Android.Views.WindowManagerFlags.KeepScreenOn) != 0;
        window.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);
        try { await RunAsync(); }
        finally { if (!wasKeptOn) window.ClearFlags(Android.Views.WindowManagerFlags.KeepScreenOn); }
#endif
    }

    private async Task RunAsync()
    {
        var tests = suite == "code" ? SourceEditingScenarios.Cases : ReferenceEditingScenarios.Cases;
        var results = new List<object>();
        var summary = new List<string> { $"Started {DateTimeOffset.UtcNow:O}; {DeviceInfo.Platform}; library {typeof(RichEditor).Assembly.ManifestModule.ModuleVersionId}" };
        var file = Path.Combine(EditorTestOutput.Directory, "scenarios-" + suite);
        void Save() { File.WriteAllLines(file + ".txt", summary); File.WriteAllText(file + ".json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
        foreach (var test in tests.Where(test => string.IsNullOrEmpty(filter) || test.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            var context = new EditingScenarioContext(async view =>
            {
                Content = view;
                for (var attempt = 0; attempt < 100 && (view.Handler is null || view.Width <= 0); attempt++) await Task.Delay(20);
                EditingScenarioContext.Check(view.Handler is not null && view.Width > 0, "scenario control attached");
            });
            summary.Add("RUN " + test.Id); Save();
            string? error = null;
            try { await test.Run(context); context.VerifyBehavior(); summary[^1] = "PASS " + test.Id; }
            catch (Exception exception) { error = exception.ToString(); summary[^1] = $"FAIL {test.Id}: {error}"; }
            finally { context.Disconnect(); Content = null; }
            results.Add(new { test.Id, test.Features, Error = error, context.Observations });
            Save();
        }
        summary.Add($"COMPLETE {results.Count} tests, {summary.Count(line => line.StartsWith("FAIL", StringComparison.Ordinal))} failures"); Save();
    }
}
#endif
