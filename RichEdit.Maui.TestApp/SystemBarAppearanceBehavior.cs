namespace RichEdit.Maui.TestApp;

// XAML selects the appearance; Android exposes system-bar icons through its window API.
internal sealed partial class SystemBarAppearanceBehavior : Behavior<Page>
{
    public static readonly BindableProperty LightIconsProperty = BindableProperty.Create(
        nameof(LightIcons), typeof(bool), typeof(SystemBarAppearanceBehavior), false,
        propertyChanged: (bindable, _, _) => ((SystemBarAppearanceBehavior)bindable).Apply());
    private Page? _page;

    public bool LightIcons
    {
        get => (bool)GetValue(LightIconsProperty);
        set => SetValue(LightIconsProperty, value);
    }

    protected override void OnAttachedTo(Page page)
    {
        base.OnAttachedTo(page);
        _page = page;
        page.Loaded += OnAppearance;
        page.Appearing += OnAppearance;
    }

    protected override void OnDetachingFrom(Page page)
    {
        page.Loaded -= OnAppearance;
        page.Appearing -= OnAppearance;
        _page = null;
        base.OnDetachingFrom(page);
    }

    private void OnAppearance(object? sender, EventArgs args) => Apply();
    private void Apply()
    {
        if (_page?.Handler is null) return;
#if ANDROID
        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window is { } window &&
            AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
        {
            controller.AppearanceLightStatusBars = !LightIcons;
            controller.AppearanceLightNavigationBars = !LightIcons;
        }
#endif
    }
}
