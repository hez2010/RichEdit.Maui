namespace RichEdit.Maui;

public sealed partial class RichEditor
{
    private static BindableProperty Proxy(string name, BindableProperty property) => BindableProperty.Create(name, property.ReturnType, typeof(RichEditor),
        property.DefaultValue, coerceValue: (view, value) =>
        {
            var editor = (RichEditor)view;
            editor._view.VerifyAccess();
            editor._view.SetValue(property, value);
            return editor._view.GetValue(property);
        });

    /// <summary>Identifies the view's read-only state.</summary>
    public static readonly BindableProperty IsReadOnlyProperty = Proxy(nameof(IsReadOnly), NativeEditorView.IsReadOnlyProperty);
    /// <summary>Identifies the native placeholder.</summary>
    public static readonly BindableProperty PlaceholderProperty = Proxy(nameof(Placeholder), NativeEditorView.PlaceholderProperty);
    /// <summary>Identifies the native placeholder color.</summary>
    public static readonly BindableProperty PlaceholderColorProperty = Proxy(nameof(PlaceholderColor), NativeEditorView.PlaceholderColorProperty);
    /// <summary>Identifies the fallback text color.</summary>
    public static readonly BindableProperty TextColorProperty = Proxy(nameof(TextColor), NativeEditorView.TextColorProperty);
    /// <summary>Identifies the fallback font family.</summary>
    public static readonly BindableProperty FontFamilyProperty = Proxy(nameof(FontFamily), NativeEditorView.FontFamilyProperty);
    /// <summary>Identifies the fallback font size.</summary>
    public static readonly BindableProperty FontSizeProperty = Proxy(nameof(FontSize), NativeEditorView.FontSizeProperty);
    /// <summary>Identifies native spell-check configuration.</summary>
    public static readonly BindableProperty IsSpellCheckEnabledProperty = Proxy(nameof(IsSpellCheckEnabled), NativeEditorView.IsSpellCheckEnabledProperty);
    /// <summary>Identifies native text-prediction configuration.</summary>
    public static readonly BindableProperty IsTextPredictionEnabledProperty = Proxy(nameof(IsTextPredictionEnabled), NativeEditorView.IsTextPredictionEnabledProperty);
    /// <summary>Identifies native keyboard configuration.</summary>
    public static readonly BindableProperty KeyboardProperty = Proxy(nameof(Keyboard), NativeEditorView.KeyboardProperty);
    /// <summary>Identifies the maximum projected text length for user input.</summary>
    public static readonly BindableProperty MaxLengthProperty = Proxy(nameof(MaxLength), NativeEditorView.MaxLengthProperty);
    /// <summary>Identifies native automatic height sizing.</summary>
    public static readonly BindableProperty AutoSizeProperty = Proxy(nameof(AutoSize), NativeEditorView.AutoSizeProperty);
    /// <summary>Identifies whether Tab inserts text.</summary>
    public static readonly BindableProperty AcceptsTabProperty = Proxy(nameof(AcceptsTab), NativeEditorView.AcceptsTabProperty);

    /// <summary>Gets or sets whether this view accepts authored edits. Other views remain independently editable.</summary>
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    /// <summary>Gets or sets placeholder text.</summary>
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    /// <summary>Gets or sets the placeholder color, or null for the native default.</summary>
    public Color? PlaceholderColor { get => (Color?)GetValue(PlaceholderColorProperty); set => SetValue(PlaceholderColorProperty, value); }
    /// <summary>Gets or sets a view fallback for unresolved authored text colors.</summary>
    public Color? TextColor { get => (Color?)GetValue(TextColorProperty); set => SetValue(TextColorProperty, value); }
    /// <summary>Gets or sets a view fallback for unresolved authored fonts.</summary>
    public string? FontFamily { get => (string?)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    /// <summary>Gets or sets a view fallback for unresolved authored font sizes.</summary>
    public double? FontSize { get => (double?)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    /// <summary>Gets or sets native spell checking, or null for the platform default.</summary>
    public bool? IsSpellCheckEnabled { get => (bool?)GetValue(IsSpellCheckEnabledProperty); set => SetValue(IsSpellCheckEnabledProperty, value); }
    /// <summary>Gets or sets native prediction, or null for the platform default.</summary>
    public bool? IsTextPredictionEnabled { get => (bool?)GetValue(IsTextPredictionEnabledProperty); set => SetValue(IsTextPredictionEnabledProperty, value); }
    /// <summary>Gets or sets the native keyboard.</summary>
    public Keyboard Keyboard { get => (Keyboard)GetValue(KeyboardProperty); set => SetValue(KeyboardProperty, value); }
    /// <summary>Gets or sets the maximum UTF-16 length in the explicit text projection; -1 allows unlimited input.</summary>
    public int MaxLength { get => (int)GetValue(MaxLengthProperty); set => SetValue(MaxLengthProperty, value); }
    /// <summary>Gets or sets native automatic height sizing.</summary>
    public EditorAutoSizeOption AutoSize { get => (EditorAutoSizeOption)GetValue(AutoSizeProperty); set => SetValue(AutoSizeProperty, value); }
    /// <summary>Gets or sets whether the native Tab key inserts a tab.</summary>
    public bool AcceptsTab { get => (bool)GetValue(AcceptsTabProperty); set => SetValue(AcceptsTabProperty, value); }
}
