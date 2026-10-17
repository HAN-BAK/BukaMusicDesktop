using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace BukaMusicSetup;

public partial class MainWindow : Window
{
    private readonly string _defaultDir;
    private string? _installedDir;
    private CancellationTokenSource? _cts;

    public MainWindow(string initialDir)
    {
        InitializeComponent();
        _defaultDir = initialDir;
        PathBox.Text = initialDir;
    }

    /// <summary>无边框窗口：按住空白处拖动。</summary>
    private void OnDrag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择安装位置",
            InitialDirectory = Directory.Exists(PathBox.Text) ? PathBox.Text : _defaultDir,
        };
        if (dialog.ShowDialog(this) == true) PathBox.Text = dialog.FolderName;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        string dir = PathBox.Text.Trim();
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "请先选择安装位置。", "BukaMusic");
            return;
        }
        ShowPage(Page.Progress);
        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string Text)>(value =>
        {
            Progress.Value = Math.Clamp(value.Percent, 0, 100);
            StatusText.Text = value.Text;
        });
        try
        {
            await Installer.InstallAsync(dir, progress, _cts.Token);
            _installedDir = dir;
            DoneText.Text = "BukaMusic 控制台已经安装到：\n" + dir;
            ShowPage(Page.Done);
        }
        catch (OperationCanceledException)
        {
            ShowPage(Page.Path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "安装失败：\n" + ex.Message, "BukaMusic",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowPage(Page.Path);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            Close();
            return;
        }
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_cts != null) _cts.Cancel();
        Close();
    }

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        if (RunBox.IsChecked == true && _installedDir != null)
        {
            string exe = Path.Combine(_installedDir, "BukaMusicDesktop.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = _installedDir,
                });
            }
        }
        Close();
    }

    private enum Page { Path, Progress, Done }

    private void ShowPage(Page page)
    {
        PathPage.Visibility = page == Page.Path ? Visibility.Visible : Visibility.Collapsed;
        ProgressPage.Visibility = page == Page.Progress ? Visibility.Visible : Visibility.Collapsed;
        DonePage.Visibility = page == Page.Done ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = page == Page.Path ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = page == Page.Done ? Visibility.Collapsed : Visibility.Visible;
        FinishButton.Visibility = page == Page.Done ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.IsEnabled = page != Page.Progress;
    }
}
