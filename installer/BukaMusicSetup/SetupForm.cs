using System.Diagnostics;

namespace BukaMusicSetup;

/// <summary>安装向导：选目录 → 下载（带进度）→ 建快捷方式 → 可立即启动。</summary>
internal sealed class SetupForm : Form
{
    private readonly TextBox _path;
    private readonly Button _browse;
    private readonly Button _install;
    private readonly Button _cancel;
    private readonly ProgressBar _progress;
    private readonly Label _status;
    private CancellationTokenSource? _cts;

    public SetupForm(string initialDir)
    {
        Text = "BukaMusic 控制台 安装程序";
        ClientSize = new Size(560, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        var title = new Label
        {
            Text = "BukaMusic 控制台",
            Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 20),
        };
        var subtitle = new Label
        {
            Text = "从缤纷云下载程序文件并安装到本机。",
            ForeColor = Color.FromArgb(90, 90, 90),
            AutoSize = true,
            Location = new Point(26, 62),
        };
        var pathLabel = new Label
        {
            Text = "安装目录",
            AutoSize = true,
            Location = new Point(26, 104),
        };
        _path = new TextBox
        {
            Text = initialDir,
            Location = new Point(26, 126),
            Width = 420,
        };
        _browse = new Button
        {
            Text = "浏览…",
            Location = new Point(454, 125),
            Width = 82,
        };
        _browse.Click += OnBrowse;
        _progress = new ProgressBar
        {
            Location = new Point(26, 168),
            Width = 510,
            Height = 8,
            Style = ProgressBarStyle.Continuous,
        };
        _status = new Label
        {
            Text = "准备就绪",
            Location = new Point(26, 184),
            Width = 510,
            Height = 24,
            ForeColor = Color.FromArgb(90, 90, 90),
        };
        _install = new Button
        {
            Text = "安装",
            Location = new Point(360, 214),
            Width = 86,
            Height = 32,
        };
        _install.Click += OnInstall;
        _cancel = new Button
        {
            Text = "取消",
            Location = new Point(452, 214),
            Width = 84,
            Height = 32,
        };
        _cancel.Click += (_, _) =>
        {
            if (_cts != null) _cts.Cancel();
            Close();
        };
        Controls.AddRange(new Control[]
        {
            title, subtitle, pathLabel, _path, _browse, _progress, _status, _install, _cancel,
        });
        AcceptButton = _install;
        CancelButton = _cancel;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择安装目录",
            SelectedPath = _path.Text,
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.SelectedPath;
    }

    private async void OnInstall(object? sender, EventArgs e)
    {
        string dir = _path.Text.Trim();
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "请先选择安装目录。", "BukaMusic");
            return;
        }
        _install.Enabled = false;
        _browse.Enabled = false;
        _path.Enabled = false;
        _cts = new CancellationTokenSource();
        var progress = new Progress<(int Percent, string Text)>(value =>
        {
            _progress.Value = Math.Clamp(value.Percent, 0, 100);
            _status.Text = value.Text;
        });
        try
        {
            await Program.InstallAsync(dir, progress, _cts.Token);
            _status.Text = "安装完成";
            _progress.Value = 100;
            if (MessageBox.Show(this, "安装完成，现在启动 BukaMusic 控制台吗？", "BukaMusic",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(dir, "BukaMusicDesktop.exe"))
                {
                    UseShellExecute = true,
                    WorkingDirectory = dir,
                });
            }
            Close();
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消";
        }
        catch (Exception ex)
        {
            _status.Text = "安装失败";
            MessageBox.Show(this, "安装失败：\n" + ex.Message, "BukaMusic",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _install.Enabled = true;
            _browse.Enabled = true;
            _path.Enabled = true;
        }
    }
}
