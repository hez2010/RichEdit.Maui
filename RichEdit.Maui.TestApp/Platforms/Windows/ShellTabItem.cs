using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RichEdit.Maui.TestApp.WinUI;

// MAUI 10's internal tab model lacks NativeAOT binding support. Keep the native
// template's property and selection behavior without ICustomPropertyProvider.
public sealed partial class ShellTabItem : NavigationViewItem
{
    private const string ModelType = "Microsoft.Maui.Platform.NavigationViewItemViewModel, Microsoft.Maui";
    private INotifyPropertyChanged? _model;
    private bool _updatingSelection;

    public ShellTabItem()
    {
        DataContextChanged += (_, _) => UpdateModel();
        Loaded += (_, _) => UpdateModel();
        Unloaded += (_, _) => DisconnectModel();
        RegisterPropertyChangedCallback(IsSelectedProperty, (_, _) =>
        {
            if (!_updatingSelection && DataContext is INotifyPropertyChanged model)
                SetIsSelected(model, IsSelected);
        });
    }

    private void UpdateModel()
    {
        DisconnectModel();
        if (DataContext is not INotifyPropertyChanged model)
            return;

        if (IsLoaded)
        {
            _model = model;
            _model.PropertyChanged += OnModelPropertyChanged;
        }

        UpdateProperties(model);
    }

    private void DisconnectModel()
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelPropertyChanged;
        _model = null;
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_model is not null)
            UpdateProperties(_model);
    }

    private void UpdateProperties(object model)
    {
        Content = GetContent(model);
        Background = GetBackground(model);
        IsEnabled = GetIsEnabled(model);
        Icon = GetIcon(model);
        MenuItemsSource = GetMenuItemsSource(model);
        _updatingSelection = true;
        try
        {
            IsSelected = GetIsSelected(model);
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Content")]
    private static extern object? GetContent([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Background")]
    private static extern Microsoft.UI.Xaml.Media.Brush? GetBackground([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsEnabled")]
    private static extern bool GetIsEnabled([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Icon")]
    private static extern IconElement? GetIcon([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_MenuItemsSource")]
    [return: UnsafeAccessorType("System.Collections.ObjectModel.ObservableCollection`1[[" + ModelType + "]], System.ObjectModel")]
    private static extern object? GetMenuItemsSource([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_IsSelected")]
    private static extern bool GetIsSelected([UnsafeAccessorType(ModelType)] object model);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_IsSelected")]
    private static extern void SetIsSelected([UnsafeAccessorType(ModelType)] object model, bool value);
}