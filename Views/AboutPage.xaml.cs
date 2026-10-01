using System;
using BukaMusicDesktop.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BukaMusicDesktop.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private async void OnOpenProject(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/HAN-BAK/BukaMusic"));
        }
        catch (Exception ex)
        {
            LogBus.Warn("无法打开浏览器：" + ex.Message);
        }
    }

    private async void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BukaMusicDesktop");
            System.IO.Directory.CreateDirectory(path);
            await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception ex)
        {
            LogBus.Warn("无法打开目录：" + ex.Message);
        }
    }
}
