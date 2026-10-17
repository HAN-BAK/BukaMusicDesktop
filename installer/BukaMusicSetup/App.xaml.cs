using System.Windows;

namespace BukaMusicSetup;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        string[] args = e.Args;
        string? dir = ValueOf(args, "--dir");
        if (args.Contains("--uninstall"))
        {
            Installer.Uninstall(dir);
            Shutdown();
            return;
        }
        if (args.Contains("--silent"))
        {
            try
            {
                // 不能在 UI 线程上同步等 await：HttpClient 的续体会回到 Dispatcher，
                // 阻塞 UI 线程会直接死锁。放到线程池里跑。
                string target = dir ?? Installer.DefaultDir();
                Task.Run(() => Installer.InstallAsync(target, null, CancellationToken.None))
                    .GetAwaiter().GetResult();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新安装失败：\n" + ex.Message, "BukaMusic");
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow(dir ?? Installer.DefaultDir());
        MainWindow = window;
        window.Show();
    }

    private static string? ValueOf(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return null;
    }
}
