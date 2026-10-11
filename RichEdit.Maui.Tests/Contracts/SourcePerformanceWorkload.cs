using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

// Measurements deliberately distinguish native input, a dispatcher checkpoint,
// and a second frame opportunity. A frame callback is not a GPU-present timestamp.
internal static partial class SourcePerformanceWorkload
{
    internal static async Task RunLarge(CodeEditor editor, CodeEditor? peer = null)
    {
        var colorizer = editor.Coloring();
        var enabled = colorizer.Enabled;
        colorizer.Enabled = false;
        var measurements = new List<LargeCase>();
#if WINDOWS
        var progressPath = Path.Combine(AppContext.BaseDirectory, "source-editor-large-progress.txt");
#else
        var progressPath = Path.Combine(FileSystem.CacheDirectory, "source-editor-large-progress.txt");
#endif
        File.WriteAllText(progressPath, "");
        void Progress(string text) => File.AppendAllText(progressPath, $"{DateTimeOffset.UtcNow:O} {text}\n");
        try
        {
            editor.WordWrap = false;
            editor.AutoIndent = false;
            if (peer is not null) { peer.WordWrap = false; peer.Coloring().Enabled = false; }
            foreach (var workload in new[]
            {
                (Name: "100000-lines-10MiB", Text: string.Concat(Enumerable.Repeat(new string('x', 105) + "\r\n", 100_000))),
                (Name: "1MiB-logical-line", Text: new string('x', 1024 * 1024) + "a\u0301 \U0001f600 אבג العربية"),
            })
            {
                var allocation = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                Progress(workload.Name + " assigning source");
                editor.Document = new(workload.Text);
                Progress("primary bound");
                if (peer is not null) peer.Document = editor.Document;
                Progress("peer bound");
                editor.Focus();
                await FrameOpportunity(TimeSpan.FromSeconds(60));
                Progress("initial layout complete");
                var load = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var frozen = editor.Document.CurrentSnapshot;
                InputMeasurements samples;
                using (editor.Document.BeginUndoGroup("Large workload input"))
                    samples = await MeasureInput(editor, 100, Progress);
                Check(peer is null || ReferenceEquals(editor.Document.CurrentSnapshot, peer.Document.CurrentSnapshot), "split views lost shared document identity");
                if (peer is not null) Check(Surface(peer).Document.CurrentSnapshot.TextBuffer.Length == editor.Document.Length, "split native cache did not synchronize");
                var history = editor.Document.RetainedHistoryByteEstimate;
                editor.Undo();
                Check(editor.Document.Length == frozen.Length && editor.Document.CurrentSnapshot.GetText(new(0, frozen.Length)) == workload.Text,
                    "large document history changed canonical source");
                measurements.Add(new(workload.Name, frozen.Length, frozen.LineCount, load, samples, history, GC.GetTotalAllocatedBytes(true) - allocation));
            }
            var report = new LargeReport(DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, peer is not null, measurements.ToArray());
            var json = JsonSerializer.Serialize(report, new ReportJsonContext(new() { WriteIndented = true }).LargeReport);
#if WINDOWS
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "source-editor-large-performance.json"), json);
#else
            File.WriteAllText(Path.Combine(FileSystem.CacheDirectory, "source-editor-large-performance.json"), json);
#endif
        }
        finally { colorizer.Enabled = enabled; }
    }

    internal static async Task RunDense(CodeEditor editor)
    {
        const int lines = 2000;
        const int samples = 100;
        var textView = Surface(editor);
        var handler = (RichEditorHandler)textView.Handler!;
#if WINDOWS
        var progressPath = Path.Combine(AppContext.BaseDirectory, "source-editor-performance-progress.txt");
#else
        var progressPath = Path.Combine(FileSystem.CacheDirectory, "source-editor-performance-progress.txt");
#endif
        File.WriteAllText(progressPath, DateTimeOffset.UtcNow.ToString("O") + " start\n");
        void Progress(string message) => File.AppendAllText(progressPath, DateTimeOffset.UtcNow.ToString("O") + " " + message + "\n");
        var colorizer = editor.Coloring();
        var wasEnabled = colorizer.Enabled;
        colorizer.Enabled = false;
        editor.WordWrap = false;
        editor.AutoIndent = false;
        editor.Document = new(string.Concat(Enumerable.Repeat("public string Value = \"text\"; // sample\n", lines)));
        editor.Focus();
        await FrameOpportunity();
        var baseline = await MeasureInput(editor, samples, Progress);
        Progress("baseline complete");
        editor.SelectedRange = TextSpan.Empty;
        editor.ScrollIntoView(TextSpan.Empty);
        await FrameOpportunity();
        var frozen = editor.Document.CurrentSnapshot;
        var created = 0;
        var recycled = 0;
        var started = Stopwatch.GetTimestamp();
        var allocation = GC.GetTotalAllocatedBytes(true);
        using var colors = editor.Decorations.CreateLayer();
        using var widgets = editor.Adornments.CreateSource(widget =>
        {
            created++;
            return new Label { Text = "value:", FontSize = 11, WidthRequest = 38, HeightRequest = 18 };
        }, (_, _) => recycled++);
        try
        {
            var positions = Enumerable.Range(0, lines).Select(line => frozen.GetLine(line).Span.Start).ToArray();
            var decorations = positions.SelectMany(start => new[]
            {
                new RichTextDecoration(new(start, 6), new() { ForegroundColor = Colors.Blue }),
                new RichTextDecoration(new(start + 22, 6), new() { ForegroundColor = Colors.Maroon }),
            });
            Check(colors.TryPublish(colors.BeginUpdate(), decorations), "fresh decorations rejected");
            Progress("decorations submitted");
            Check(widgets.TryPublish(widgets.BeginUpdate(), positions.Select((start, index) => new RichTextWidget(index.ToString(), start + 22, new(38, 18),
                Options: new() { Placement = RichTextAdornmentPlacement.Inline }, AccessibleDescription: "parameter value"))), "fresh widgets rejected");
            Progress("widgets submitted");
            Check(created == 0, "publication invoked a widget factory");
            var submitted = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var longestQueue = 0d;
            var lastProgress = Stopwatch.GetTimestamp();
            while (handler.NativeProjection.Reservations.Length != lines)
            {
                Check(Stopwatch.GetElapsedTime(started) < TimeSpan.FromMinutes(2), "native reservation publication timed out");
                var queued = Stopwatch.GetTimestamp();
                await DispatcherCheckpoint(editor);
                longestQueue = Math.Max(longestQueue, Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
                await Task.Delay(10);
                if (Stopwatch.GetElapsedTime(lastProgress).TotalSeconds >= 1)
                {
                    Progress(handler.PresentationSchedulingState + $", created {created}, recycled {recycled}");
                    lastProgress = Stopwatch.GetTimestamp();
                }
            }
            await FrameOpportunity();
            var publication = new Publication(submitted, Stopwatch.GetElapsedTime(started).TotalMilliseconds, longestQueue,
                GC.GetTotalAllocatedBytes(true) - allocation, created, recycled, editor.Adornments.Count(item => item.CreatedView is not null));
            Check(ReferenceEquals(frozen, editor.Document.CurrentSnapshot), "presentation mutated source");
            Check(publication.LiveViews < 128, "widget realization grew beyond the viewport");
            Progress("publication complete");
            var dense = await MeasureInput(editor, samples, Progress);
            var report = new Report(DateTimeOffset.UtcNow, System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription, Environment.ProcessorCount,
                typeof(CodeEditor).Assembly.ManifestModule.ModuleVersionId, typeof(NativeEditorView).Assembly.ManifestModule.ModuleVersionId,
                lines, 4000, lines, baseline, publication, dense, editor.Document.RetainedHistoryByteEstimate);
            var json = JsonSerializer.Serialize(report, new ReportJsonContext(new() { WriteIndented = true }).Report);
#if WINDOWS
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "source-editor-performance.json"), json);
#else
            File.WriteAllText(Path.Combine(FileSystem.CacheDirectory, "source-editor-performance.json"), json);
#endif
        }
        finally { colorizer.Enabled = wasEnabled; }
    }

    private static async Task<InputMeasurements> MeasureInput(CodeEditor editor, int count, Action<string> progress)
    {
        var textView = Surface(editor);
        var results = new List<InputSample>();
        for (var index = -5; index < count; index++)
        {
            var before = editor.Document.CurrentSnapshot;
            var position = index < 0 ? 0 : (index % 3) switch { 0 => 0, 1 => before.Length / 2, _ => before.Length };
            var navigation = Stopwatch.GetTimestamp();
            editor.SelectedRange = new(position, 0);
            editor.ScrollIntoView(new(position, 0));
            var navigationMs = Stopwatch.GetElapsedTime(navigation).TotalMilliseconds;
            var allocation = GC.GetTotalAllocatedBytes(true);
            var started = Stopwatch.GetTimestamp();
            EditorContractTests.NativeReplace(textView, "x");
            var synchronous = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            await DispatcherCheckpoint(editor);
            var checkpoint = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            await FrameOpportunity();
            var frame = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var allocated = GC.GetTotalAllocatedBytes(true) - allocation;
            Check(editor.Document.Length == before.Length + 1 && editor.Document.CurrentSnapshot.GetText(new(position, 1)) == "x",
                $"native input changed unexpected source text: sample {index}, position {position}, length {before.Length}->{editor.Document.Length}, " +
                $"observed {editor.Document.CurrentSnapshot.GetText(new(Math.Min(position, editor.Document.Length), Math.Min(12, editor.Document.Length - Math.Min(position, editor.Document.Length))))}; " +
                ((RichEditorHandler)textView.Handler!).PresentationSchedulingState);
            if (index >= 0) results.Add(new(position, navigationMs, synchronous, checkpoint, frame, allocated));
            if (index >= 0 && index % 10 == 0) progress($"input {index}: native {synchronous:F2} ms, checkpoint {checkpoint:F2} ms, second frame {frame:F2} ms");
        }
        return new([.. results], Percentiles(results.Select(item => item.NativeInputMs)), Percentiles(results.Select(item => item.DispatcherCheckpointMs)),
            Percentiles(results.Select(item => item.SecondFrameOpportunityMs)));
    }

    private static Distribution Percentiles(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return new(ordered[(int)Math.Ceiling(ordered.Length * 0.5) - 1], ordered[(int)Math.Ceiling(ordered.Length * 0.95) - 1],
            ordered[(int)Math.Ceiling(ordered.Length * 0.99) - 1], ordered[^1]);
    }

    private static Task DispatcherCheckpoint(CodeEditor editor)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Dispatcher.Dispatch(ready.SetResult);
        return ready.Task;
    }

    private static async Task FrameOpportunity(TimeSpan? timeout = null)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
#if WINDOWS
        void Frame(object? sender, object args) { if (++frames == 2) ready.TrySetResult(); }
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Frame;
        try { await ready.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)); }
        finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Frame; }
#elif ANDROID
        using var frame = new FrameCallback(() => { if (++frames == 2) ready.TrySetResult(); return frames < 2; });
        Android.Views.Choreographer.Instance!.PostFrameCallback(frame);
        try { await ready.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5)); }
        finally { Android.Views.Choreographer.Instance!.RemoveFrameCallback(frame); }
#else
        using var link = CoreAnimation.CADisplayLink.Create(() => { if (++frames == 2) ready.TrySetResult(); });
        link.AddToRunLoop(Foundation.NSRunLoop.Main, Foundation.NSRunLoopMode.Common);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { link.Invalidate(); }
#endif
    }
#if ANDROID
    private sealed class FrameCallback(Func<bool> again) : Java.Lang.Object, Android.Views.Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos) { if (again()) Android.Views.Choreographer.Instance!.PostFrameCallback(this); }
    }
#endif

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static NativeEditorView Surface(CodeEditor editor) => ((Grid)editor.Content).Children.OfType<NativeEditorView>().Single();
    private sealed record InputSample(int Position, double NavigationMs, double NativeInputMs, double DispatcherCheckpointMs, double SecondFrameOpportunityMs, long AllocatedBytes);
    private sealed record Distribution(double P50, double P95, double P99, double Maximum);
    private sealed record InputMeasurements(InputSample[] Samples, Distribution NativeInput, Distribution DispatcherCheckpoint, Distribution SecondFrameOpportunity);
    private sealed record Publication(double SubmitMs, double CompletionMs, double LongestQueueDelayMs, long AllocatedBytes, int CreatedViews, int RecycledViews, int LiveViews);
    private sealed record Report(DateTimeOffset Timestamp, string Runtime, string OS, int LogicalProcessors, Guid CodeAssembly, Guid NativeAssembly,
        int Lines, int Decorations, int Widgets, InputMeasurements Baseline, Publication Initial, InputMeasurements Dense, long RetainedHistoryBytes);
    private sealed record LargeCase(string Name, int Length, int Lines, double LoadMs, InputMeasurements Input, long RetainedHistoryBytes, long AllocatedBytes);
    private sealed record LargeReport(DateTimeOffset Timestamp, string Runtime, string OS, bool SplitViews, LargeCase[] Workloads);
    [JsonSerializable(typeof(Report))]
    [JsonSerializable(typeof(LargeReport))]
    private partial class ReportJsonContext : JsonSerializerContext;
}
