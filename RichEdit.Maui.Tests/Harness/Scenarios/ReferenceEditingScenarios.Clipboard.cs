#if WINDOWS || ((DEBUG || EDITOR_TESTS) && (ANDROID || IOS || MACCATALYST))
using System.Text;
using RichEdit.Core;
using static RichEdit.Maui.Tests.EditingScenarioContext;

namespace RichEdit.Maui.Tests;

internal static partial class ReferenceEditingScenarios
{
    private static IEnumerable<EditingScenario> ClipboardCases()
    {
        foreach (var external in new[] { false, true })
            yield return new($"rich/clipboard/table-before-table-{(external ? "rtf" : "cached")}",
                ["clipboard", "tables", "styles", "native text", "undo", "redo", "typing"], async context =>
            {
                await context.OpenReference();
                var document = context.Rich.Document;
                var before = document.CurrentSnapshot;
                var blocks = before.Get<RichStory>(before.Body).Blocks;
                var table = blocks.First(id => before.Get<RichBlock>(id) is RichTable);
                var paragraph = blocks[blocks.IndexOf(table) - 1];
                await context.Select(new(before.Body, paragraph, before.GetParagraphLength(paragraph)));
                var original = await context.SaveRich();
                var nativeOriginal = context.RichNativeTexts();
                var source = RichDocumentSnapshot.Build(edit =>
                {
                    var copiedTable = edit.InsertTable(edit.Body, 1, 8, (RichLength[])[new(1494), new(1494), new(1494), new(1494), new(1494)]);
                    var grid = edit.Snapshot.GetTableGrid(copiedTable);
                    for (var row = 0; row < 8; row++)
                        for (var column = 0; column < 5; column++)
                        {
                            var cellParagraph = edit.Snapshot.Get<RichTableCell>(grid[row, column]).Blocks[0];
                            edit.ReplaceText(new(edit.Body, cellParagraph, 0), 0, $"Pasted {row}:{column}");
                        }
                    edit.ApplyCharacterFormat(new RichCellSelection(copiedTable, 0, 0, 8, 5), new() { Bold = true, Italic = true });
                    edit.InsertParagraph(edit.Body, 2, "");
                });
                if (external)
                {
                    using var stream = new MemoryStream();
                    await RichEdit.Core.Rtf.RtfCodec.WriteAsync(source, stream);
                    await SetExternalClipboard(Encoding.ASCII.GetString(stream.ToArray()), source.ProjectText().Text.ToString());
                }
                else await RichTextClipboard.SetStructuredAsync(new(source));
                var errors = new List<Exception>();
                void Failed(object? sender, RichTextCommandFailedEventArgs args) => errors.Add(args.Exception);
                context.Rich.Commands.Failed += Failed;
                try
                {
                    await PasteNatively(context);
                    await Settle(500);
                    Check(errors.Count == 0, string.Join("\n", errors));
                    AssertNativeTextMatchesDocument(context);
                    Equal(before.Nodes.Values.OfType<RichTable>().Count() + 1, document.CurrentSnapshot.Nodes.Values.OfType<RichTable>().Count(), "both tables retained");
                    var nativePasted = context.RichNativeTexts();
                    Check(nativePasted.Any(text => text.Contains("Pasted 0:0", StringComparison.Ordinal)), "pasted native cell text");
                    Check(nativePasted.Any(text => text.Contains("1st column", StringComparison.Ordinal)), "original native cell text");
                    await context.Select(context.Find("Pasted 0:0"), 6);
                    context.AssertNativeCharacterStyle(new() { Bold = true, Italic = true });
                    var pasted = await context.SaveRich();
                    context.Rich.Undo(); await Settle(500);
                    var restored = await context.SaveRich();
                    Check(original.AsSpan().SequenceEqual(restored), "undo restores document and styles");
                    Check(nativeOriginal.SequenceEqual(context.RichNativeTexts()), "undo restores every native text surface");
                    AssertNativeTextMatchesDocument(context);
                    context.Rich.Redo(); await Settle(500);
                    restored = await context.SaveRich();
                    Check(pasted.AsSpan().SequenceEqual(restored), "redo restores document and styles");
                    Check(nativePasted.SequenceEqual(context.RichNativeTexts()), "redo restores every native text surface");
                    AssertNativeTextMatchesDocument(context);
                    var textBeforeTyping = context.Text;
                    var insertion = context.Find("Pasted 0:0", 3);
                    var insertionOffset = document.CurrentSnapshot.ProjectText().GetOffset(insertion);
                    await context.Select(insertion);
                    context.Type("X"); await Settle();
                    Equal(textBeforeTyping.Insert(insertionOffset, "X"), context.Text, "native typing after paste uses the correct address");
                    AssertNativeTextMatchesDocument(context);
                    context.Rich.Undo(); await Settle();
                    restored = await context.SaveRich();
                    Check(pasted.AsSpan().SequenceEqual(restored), "typing undo restores pasted content");
                    AssertNativeTextMatchesDocument(context);
                    Check(errors.Count == 0, string.Join("\n", errors));
                    context.Observe("paste-undo-redo-typing-complete");
                }
                finally { context.Rich.Commands.Failed -= Failed; }
            });

        yield return new("rich/clipboard/rejected-rtf", ["clipboard", "failure reporting", "state preservation", "recovery"], async context =>
        {
            await context.OpenReference();
            await context.Select(context.Find("Simple table"));
            var before = context.Rich.Document.CurrentSnapshot;
            var selection = context.Rich.Selection;
            var nativeBefore = context.RichNativeTexts();
            var errors = new List<Exception>();
            void Failed(object? sender, RichTextCommandFailedEventArgs args) => errors.Add(args.Exception);
            context.Rich.Commands.Failed += Failed;
            try
            {
                await SetExternalClipboard(@"{\rtf1\expnd2147483647 invalid}", "Recovered");
                await PasteNatively(context); await Settle();
                Check(errors.Count == 1 && errors[0] is OverflowException, "native paste reports the parser failure once");
                Check(ReferenceEquals(before, context.Rich.Document.CurrentSnapshot), "failed paste leaves committed state unchanged");
                Equal(selection, context.Rich.Selection, "failed paste leaves selection unchanged");
                Check(!context.Rich.CanUndo, "failed paste creates no undo unit");
                Check(nativeBefore.SequenceEqual(context.RichNativeTexts()), "failed paste leaves native text unchanged");
                await context.Rich.PastePlainTextAsync(); await Settle();
                Check(context.Text.Contains("Recovered", StringComparison.Ordinal), "explicit plain-text recovery works");
                context.Rich.Undo(); await Settle();
                Check(nativeBefore.SequenceEqual(context.RichNativeTexts()), "recovery can be undone");
            }
            finally { context.Rich.Commands.Failed -= Failed; }
        });
    }

    private static void AssertNativeTextMatchesDocument(EditingScenarioContext context)
    {
#if ANDROID
        var snapshot = context.Rich.Document.CurrentSnapshot;
        var surfaces = snapshot.Nodes.Values.OfType<RichParagraph>().Select(paragraph =>
        {
            var point = new RichTextPoint(snapshot.Body, paragraph.Id, 0);
            return context.Rich.NativeView.FindStructuredSurface(new RichEdit.Core.RichTextSelection(point, point))
                ?? throw new InvalidOperationException("A paragraph has no native text surface.");
        }).Distinct();
#else
        var surfaces = new[] { context.Rich.NativeView };
#endif
        foreach (var surface in surfaces)
        {
            var handler = (RichEditorHandler)surface.Handler!;
            var expected = handler.NativeProjection.StructuredDisplay?.Text ?? surface.Document.Text;
#if WINDOWS
            var range = handler.PlatformView.Document.GetRange(0, 0);
            range.SetRange(0, range.StoryLength);
            var actual = range.Text;
#else
            var actual = handler.PlatformView.Text ?? "";
#endif
            Equal(expected, actual.Replace('\r', '\n').Replace('\v', RichTextDocument.SoftLineBreakCharacter), "native text matches the document projection");
        }
    }

    private static async Task PasteNatively(EditingScenarioContext context)
    {
        var surface = context.Surface;
#if ANDROID
        Check(((RichEditorHandler)surface.Handler!).PlatformView.OnTextContextMenuItem(global::Android.Resource.Id.Paste), "native Paste handled");
#elif IOS || MACCATALYST
        ((RichEditorHandler)surface.Handler!).PlatformView.Paste((Foundation.NSObject?)null);
#else
        surface.Commands.Paste.Execute(null);
#endif
        await surface.Commands.ClipboardCompletion;
    }

    private static async Task SetExternalClipboard(string rtf, string text)
    {
#if WINDOWS
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text); package.SetRtf(rtf);
        await WindowsClipboardAccess.SetAsync(package);
#elif IOS || MACCATALYST
        using var data = Foundation.NSData.FromArray(Encoding.ASCII.GetBytes(rtf));
        using var value = new Foundation.NSString(text);
        using var item = Foundation.NSDictionary.FromObjectsAndKeys([value, data],
            [new Foundation.NSString("public.utf8-plain-text"), new Foundation.NSString("public.rtf")]);
        UIKit.UIPasteboard.General.Items = [item];
#elif ANDROID
        var context = global::Android.App.Application.Context;
        var clipboard = (global::Android.Content.ClipboardManager)context.GetSystemService(global::Android.Content.Context.ClipboardService)!;
        using var uri = await RichEdit.Maui.Platforms.Android.RtfClipboardProvider.StoreAsync(rtf);
        using var value = new Java.Lang.String(text);
        using var item = new global::Android.Content.ClipData.Item(value, null, null, uri);
        using var label = new Java.Lang.String("External RTF test");
        using var description = new global::Android.Content.ClipDescription(label, ["application/rtf", "text/plain"]);
        using var clip = new global::Android.Content.ClipData(description, item);
        clipboard.PrimaryClip = clip;
#endif
        await Task.CompletedTask;
    }
}
#endif
