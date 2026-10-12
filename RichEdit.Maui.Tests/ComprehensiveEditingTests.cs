using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using CodeEdit.Maui;
using System.Text.Json;

namespace RichEdit.Maui.Tests;

[Collection("Native editor")]
public sealed class ComprehensiveEditingTests
{
    private static MauiApp? _app;
    private static Microsoft.UI.Xaml.Window? _window;
    private static IEnumerable<object[]> SelectCases(IReadOnlyList<EditingScenario> cases) => cases
        .Where(test => Environment.GetEnvironmentVariable("RICHEDIT_SCENARIO_FILTER") is not { Length: > 0 } filter || test.Id.Contains(filter, StringComparison.OrdinalIgnoreCase))
        .Select(test => new object[] { test.Id });
    public static IEnumerable<object[]> RichCases => SelectCases(ReferenceEditingScenarios.Cases);
    public static IEnumerable<object[]> CodeCases => SelectCases(SourceEditingScenarios.Cases);
    [Theory, MemberData(nameof(RichCases))]
    public Task ReferenceEditing(string id) => Run(ReferenceEditingScenarios.Cases.Single(test => test.Id == id));
    [Theory, MemberData(nameof(CodeCases))]
    public Task SourceEditing(string id) => Run(SourceEditingScenarios.Cases.Single(test => test.Id == id));

    private static Task Run(EditingScenario test) => WindowsTestHost.RunAsync(async () =>
    {
        var app = _app ??= MauiApp.CreateBuilder().UseMauiApp<ScenarioApplication>().UseCodeEditor().Build();
        var window = _window ??= new Microsoft.UI.Xaml.Window();
        var context = new EditingScenarioContext(async view =>
        {
            window.Content = view.ToPlatform(new MauiContext(app.Services));
            window.AppWindow.Resize(new(900, 680));
            window.AppWindow.Show(activateWindow: false);
            ((IView)view).Measure(900, 640);
            ((IView)view).Arrange(new(0, 0, 900, 640));
            await Task.Delay(100);
        });
        try { await test.Run(context); context.VerifyBehavior(); }
        finally
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "scenario-results");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, test.Id.Replace('/', '-') + ".json"), JsonSerializer.Serialize(context.Observations, new JsonSerializerOptions { WriteIndented = true }));
            context.Disconnect(); window.Content = null;
        }
    }, TimeSpan.FromSeconds(45));

    private sealed class ScenarioApplication : Application { }
}
