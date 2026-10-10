using TextEdit.Core;

namespace RichEdit.Maui.Tests;

internal static partial class EditorContractTests
{
    private static IEnumerable<Case> InputRuleCases()
    {
        yield return new("input rules completion commit characters publish one state across shared views", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("Con");
            editor.SelectedRange = new(3, 0);
            using var peer = new SharedEditor(editor);
            var observed = new List<string>();
            void Changed(object? sender, RichTextDocumentChangedEventArgs args)
            {
                observed.Add(editor.Document.Text);
                Equal("Console.", peer.Editor.Document.Text);
                Equal("Console.", NativeText(peer.Editor));
            }
            using var rule = editor.InputRules.Register(context =>
            {
                Equal("Con", context.Before.ToString());
                Equal("Con.", context.Input.ToString());
                context.Replace(new(0, 4), "Console.");
            });
            editor.Document.Changed += Changed;
            try
            {
                NativeMultiInsert(editor, ".");
                await Verify(editor);
                Equal("Console.", editor.Document.Text);
                Equal(1, observed.Count);
                Equal(new TextSpan(8, 0), editor.SelectedRange);
            }
            finally { editor.Document.Changed -= Changed; }
            editor.Undo();
            await Verify(editor);
            Equal("Con", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });

        yield return new("input rules paired delimiters preserve all caret identities and one undo", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a b");
            editor.Selections = new((TextSelection[])[new(1, 1), new(3, 3)]);
            var identities = editor.Selections.Select(item => item.Id).ToArray();
            using var rule = editor.InputRules.Register(context =>
            {
                var result = new List<TextSelection>();
                var shift = 0;
                foreach (var selection in context.Selections)
                {
                    context.Replace(new(selection.Active, 0), ")");
                    result.Add(selection with { Anchor = selection.Active + shift, Active = selection.Active + shift });
                    shift++;
                }
                context.SetSelections(new(result, context.Selections.PrimaryIndex));
            });
            NativeMultiInsert(editor, "(");
            await Verify(editor);
            Equal("a() b()", editor.Document.Text);
            Equal("2,6", string.Join(",", editor.Selections.Select(item => item.Active)));
            Equal(true, identities.SequenceEqual(editor.Selections.Select(item => item.Id)));
            editor.Undo();
            await Verify(editor);
            Equal("a b", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });

        yield return new("input rules linked fields update before observers and restore together", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("x x");
            editor.SelectedRange = new(0, 1);
            using var rule = editor.InputRules.Register(context =>
            {
                context.Replace(new(4, 1), "foo");
                context.SetSelections(new((TextSelection[])[context.Selections.Primary with { Anchor = 3, Active = 3 }]));
            });
            NativeMultiInsert(editor, "foo");
            await Verify(editor);
            Equal("foo foo", editor.Document.Text);
            Equal(new TextSpan(3, 0), editor.SelectedRange);
            editor.Undo();
            await Verify(editor);
            Equal("x x", editor.Document.Text);
            Equal(new TextSpan(0, 1), editor.SelectedRange);
            Equal(false, editor.CanUndo);
        });

        yield return new("input rules conflicts and recursive edits reject the entire native operation", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("a");
            editor.SelectedRange = new(1, 0);
            var failures = 0;
            void Rejected(object? sender, RichTextInputRejectedEventArgs args) => failures++;
            editor.InputRejected += Rejected;
            try
            {
                using (var one = editor.InputRules.Register(context => context.Replace(new(0, 1), "one")))
                using (var two = editor.InputRules.Register(context => context.Replace(new(0, 1), "two")))
                {
                    NativeMultiInsert(editor, "x");
                    await Verify(editor);
                    Equal("a", editor.Document.Text);
                    Equal(1, failures);
                    Equal(false, editor.CanUndo);
                }
                using (var recursive = editor.InputRules.Register(_ => editor.Document.Edit(edit => edit.InsertText(0, "bad"))))
                {
                    NativeMultiInsert(editor, "x");
                    await Verify(editor);
                    Equal("a", editor.Document.Text);
                    Equal(2, failures);
                    Equal(false, editor.CanUndo);
                }
                using (var selection = editor.InputRules.Register(_ => editor.SelectedRange = new(0, 0)))
                {
                    NativeMultiInsert(editor, "x");
                    await Verify(editor);
                    Equal("a", editor.Document.Text);
                    Equal(3, failures);
                }
            }
            finally { editor.InputRejected -= Rejected; }
        });

#if ANDROID || IOS || MACCATALYST
        yield return new("input rules composition invokes rules once after native marked text completes", async editor =>
        {
            editor.Document = RichTextDocument.FromPlainText("x x");
            editor.SelectedRange = new(0, 1);
            editor.Focus();
            await Task.Delay(100);
            var invocations = 0;
            using var rule = editor.InputRules.Register(context =>
            {
                invocations++;
                Equal(true, context.IsCompositionCommit);
                context.Replace(new(3, 1), "仮名");
            });
            var native = ((RichEditorHandler)editor.Handler!).PlatformView;
#if ANDROID
            using var info = new Android.Views.InputMethods.EditorInfo();
            using var connection = native.OnCreateInputConnection(info)!;
            using var value = new Java.Lang.String("仮名");
            connection.SetComposingText(value, 1);
            Equal(0, invocations);
            connection.FinishComposingText();
#else
            native.SetMarkedText("仮名", new Foundation.NSRange(2, 0));
            Equal(0, invocations);
            native.UnmarkText();
#endif
            await Verify(editor);
            Equal(1, invocations);
            Equal("仮名 仮名", editor.Document.Text);
            editor.Undo();
            await Verify(editor);
            Equal("x x", editor.Document.Text);
            Equal(false, editor.CanUndo);
        });
#endif
    }
}
