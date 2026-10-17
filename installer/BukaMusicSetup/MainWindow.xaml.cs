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
        VersionText.Text = "v" + (System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");
        SourceInitialized += (_, _) => TryEnableMica();
    }

    /// <summary>Windows 11 的 Mica 背景；老系统上保持浅灰底色。</summary>
    private void TryEnableMica()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            // 只开深色标题栏；窗口本身用与应用一致的深色底。
            // （无边框窗口开 Mica 在部分机器上会变成纯黑，所以不依赖它。）
            int dark = 1;   // 深色标题栏 / 深色 Mica，与应用内配色一致
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
                                                    ref int value, int size);

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
