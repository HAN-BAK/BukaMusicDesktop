using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BukaMusicDesktop.Views;

public sealed partial class LogsPage : Page
{
    public LogsPage()
    {
        InitializeComponent();
        LogList.ItemsSource = LogBus.Entries;
        LogBus.Entries.CollectionChanged += (_, _) => UpdateCount();
        UpdateCount();
    }

    private void UpdateCount() => CountText.Text = $"共 {LogBus.Entries.Count} 条";

    private void OnClear(object sender, RoutedEventArgs e)
    {
        LogBus.Clear();
        UpdateCount();
    }
}
