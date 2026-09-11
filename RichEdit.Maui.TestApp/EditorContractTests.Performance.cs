using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RichEdit.Maui.TestApp;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> PerformanceCases()
    {
        yield return new("performance dense presentation on 2000 source lines", async editor =>
        {
#if IOS || MACCATALYST
            using var smartSpacing = new PerformanceInputScope(((RichEditorHandler)editor.Handler!).PlatformView);
#endif
#if WINDOWS
            var progressPath = Path.Combine(AppContext.BaseDirectory, "editor-primitives-performance-progress.txt");
#else
            var progressPath = Path.Combine(FileSystem.CacheDirectory, "editor-primitives-performance-progress.txt");
#endif
            File.WriteAllText(progressPath, DateTimeOffset.UtcNow.ToString("O") + " start\n");
            void Progress(string phase) => File.AppendAllText(progressPath, DateTimeOffset.UtcNow.ToString("O") + " " + phase + "\n");
#if ANDROID
            Progress("native editable: " + (((RichEditorHandler)editor.Handler!).PlatformView.EditableText as Java.Lang.Object)?.Class?.Name);
#endif
            const string line = "public string Value = \"text\"; // sample\n";
            var source = string.Concat(Enumerable.Repeat(line, 2000));
            editor.Document = RichTextDocument.FromPlainText(source);
            editor.IsSpellCheckEnabled = false;
            editor.IsTextPredictionEnabled = false;
            editor.SelectedRange = new(0, 0);
            editor.ScrollIntoView(new(0, 0));
            await Task.Delay(200);
            var handler = (RichEditorHandler)editor.Handler!;
            var baseline = await MeasureEditing();
            Progress("baseline complete " + JsonSerializer.Serialize(baseline, PerformanceJsonContext.Default.EditingMeasurementArray));
            var insertedPrefix = editor.Document.Length - source.Length;
            source = editor.Document.Text;
            // Keep the same native input context across variants. Replacing a UITextView's
            // document immediately after typing can invoke platform smart-text behavior.
            editor.SelectedRange = new(0, 0);
            await Task.Delay(100);
            var publicationSource = editor.Document.CurrentSnapshot;
            using var colors = editor.Decorations.CreateLayer();
            var decorations = Enumerable.Range(0, 2000).SelectMany(index => new[]
            {
                new RichTextDecoration(new(index * line.Length + insertedPrefix, 6), new() { ForegroundColor = Colors.Blue }),
                new RichTextDecoration(new(index * line.Length + insertedPrefix + 22, 6), new() { ForegroundColor = Colors.Maroon }),
            }).ToArray();
            var timer = Stopwatch.StartNew();
            var allocation = GC.GetTotalAllocatedBytes(precise: true);
            var queryCount = GeometryQueries(handler);
            colors.TrySet(editor.Document.Revision, decorations);
            Equal(source.Length, editor.Document.Length, "color publication preserves source");
            Progress("decorations complete");
            var widgets = new List<RichTextAdornment>();
            for (var index = 0; index < 2000; index++)
                widgets.Add(editor.Adornments.Add(index * line.Length + insertedPrefix + 22,
                    new Label { Text = "value:", FontSize = 11, WidthRequest = 38, HeightRequest = 18 },
                    new() { Placement = RichTextAdornmentPlacement.Inline }));
            Progress("widgets submitted");
            var submitted = timer.Elapsed.TotalMilliseconds;
            // Dispatcher checkpoints measure scheduling delay while native presentation settles.
            var longestDelay = 0d;
            for (var pass = 0; pass < 400; pass++)
            {
                var queue = Stopwatch.StartNew();
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                editor.Dispatcher.Dispatch(() => { longestDelay = Math.Max(longestDelay, queue.Elapsed.TotalMilliseconds); ready.SetResult(); });
                await ready.Task;
                await Task.Delay(20);
                if (pass % 10 == 0) Progress($"batch checkpoint {pass}: source {editor.Document.Length}, native {NativeText(editor).Length}");
                if (NativeText(editor).Length == source.Length + widgets.Count) break;
            }
            Equal(source.Length + widgets.Count, NativeText(editor).Length, "all 2000 native reservations are published");
            var initial = new PresentationMeasurement(submitted, timer.Elapsed.TotalMilliseconds, longestDelay,
                GC.GetTotalAllocatedBytes(precise: true) - allocation, GeometryQueries(handler) - queryCount,
                widgets.Count(static item => item.View.Handler?.PlatformView is not null));
            Equal(true, initial.RealizedViews < 128, $"viewport realization: {initial.RealizedViews} of 2000");
            Equal(source, editor.Document.Text);
            Equal(true, ReferenceEquals(publicationSource, editor.Document.CurrentSnapshot), "presentation preserves source and history");
            var dense = await MeasureEditing();
            Progress("dense edits complete");
            var report = JsonSerializer.Serialize(new PerformanceReport(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, typeof(RichEditor).Assembly.ManifestModule.ModuleVersionId,
                2000, 4000, 2000, baseline, initial, dense),
                new PerformanceJsonContext(new JsonSerializerOptions { WriteIndented = true }).PerformanceReport);
#if WINDOWS
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "editor-primitives-performance.json"), report);
#else
            File.WriteAllText(Path.Combine(FileSystem.CacheDirectory, "editor-primitives-performance.json"), report);
#endif
            foreach (var widget in widgets) widget.Dispose();
            colors.Clear();
            await Task.Delay(150);
            Equal(editor.Document.Text, NativeText(editor));

            async Task<EditingMeasurement[]> MeasureEditing()
            {
                var rounds = new List<EditingMeasurement>();
                for (var index = 0; index < 5; index++)
                {
                    editor.SelectedRange = new(0, 0);
                    var before = editor.Document.Text;
                    var allocated = GC.GetTotalAllocatedBytes(precise: true);
                    var queries = GeometryQueries(handler);
                    var edit = Stopwatch.StartNew();
                    NativeReplace(editor, "x");
                    var synchronous = edit.Elapsed.TotalMilliseconds;
                    var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    editor.Dispatcher.Dispatch(queued.SetResult);
                    await queued.Task;
                    var delay = edit.Elapsed.TotalMilliseconds;
                    await Task.Delay(30);
                    Equal("x" + before, editor.Document.Text, "dense native editing preserves source");
                    rounds.Add(new(synchronous, delay - synchronous, edit.Elapsed.TotalMilliseconds,
                        GeometryQueries(handler) - queries, GC.GetTotalAllocatedBytes(precise: true) - allocated));
                }
                return rounds.ToArray();
            }
        });
    }

    private sealed record EditingMeasurement(double EditMs, double QueueDelayMs, double CompletionMs, long NativeGeometryQueries, long AllocatedBytes);
    private sealed record PresentationMeasurement(double SubmitMs, double CompletionMs, double LongestQueueDelayMs, long AllocatedBytes,
        long NativeGeometryQueries, int RealizedViews);
    private sealed record PerformanceReport(string Runtime, string OS, Guid Library, int Lines, int Decorations, int Adornments,
        EditingMeasurement[] Baseline, PresentationMeasurement Initial, EditingMeasurement[] Dense);

    [JsonSerializable(typeof(PerformanceReport))]
    private partial class PerformanceJsonContext : JsonSerializerContext;

    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(RichEditorHandler))]
    private static long GeometryQueries(RichEditorHandler handler) => (long)typeof(RichEditorHandler)
        .GetProperty("NativeGeometryQueryCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(handler)!;

#if IOS || MACCATALYST
    private sealed class PerformanceInputScope : IDisposable
    {
        private readonly UIKit.UITextView _view;
        private readonly UIKit.UITextSmartInsertDeleteType _previous;
        internal PerformanceInputScope(UIKit.UITextView view)
        {
            _view = view;
            _previous = view.SmartInsertDeleteType;
            // Use identical C# source in both variants, matching the source editor's input
            // configuration. The separate native-input cases keep smart transformations on.
            view.SmartInsertDeleteType = UIKit.UITextSmartInsertDeleteType.No;
        }
        public void Dispose() => _view.SmartInsertDeleteType = _previous;
    }
#endif
}
