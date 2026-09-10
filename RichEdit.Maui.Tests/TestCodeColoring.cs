using System.Runtime.CompilerServices;
using CodeEdit.Maui;
using RichEdit.Maui.TestApp;

namespace RichEdit.Maui.Tests;

internal static class TestCodeColoring
{
    private static readonly ConditionalWeakTable<CodeEditor, CodeColorizer> Components = new();
    internal static CodeColorizer Coloring(this CodeEditor editor) => Components.GetValue(editor, static owner => new(owner));
}
