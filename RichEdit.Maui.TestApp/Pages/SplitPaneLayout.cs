namespace RichEdit.Maui.TestApp;

internal static class SplitPaneLayout
{
    internal static void Update(Grid host, View second)
    {
        var stacked = host.Width < 700;
        Grid.SetRow(second, stacked ? 1 : 0);
        Grid.SetColumn(second, stacked ? 0 : 1);
        host.ColumnDefinitions[1].Width = second.IsVisible && !stacked ? GridLength.Star : new GridLength(0);
        host.RowDefinitions[1].Height = second.IsVisible && stacked ? GridLength.Star : new GridLength(0);
        host.ColumnSpacing = second.IsVisible && !stacked ? 10 : 0;
        host.RowSpacing = second.IsVisible && stacked ? 10 : 0;
    }
}
