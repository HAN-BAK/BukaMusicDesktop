using System;
using System.IO;
using Microsoft.UI.Xaml;
using BukaMusicDesktop.Core;

namespace BukaMusicDesktop;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] argv = Environment.GetCommandLineArgs();
        for (int i = 0; i < argv.Length - 1; i++)
        {
            if (argv[i] == "--lyrics-probe")
            {
                string outDir = argv[i + 1];
                string host = i + 2 < argv.Length ? argv[i + 2] : "192.168.0.127";
                _ = RunProbeAsync(outDir, host);
                return;
            }
            if (argv[i] == "--shader-test")
            {
                _ = RunShaderTestAsync(argv[i + 1]);
                return;
            }
            if (argv[i] == "--lyrics-seq")
            {
                // --lyrics-seq <outDir> <host> <startMs> <endMs> [width] [height] [stepMs]
                string outDir = argv[i + 1];
                string host = i + 2 < argv.Length ? argv[i + 2] : "192.168.0.124";
                long start = i + 3 < argv.Length && long.TryParse(argv[i + 3], out long a) ? a : 10_000L;
                long end = i + 4 < argv.Length && long.TryParse(argv[i + 4], out long b) ? b : 40_000L;
                int w = i + 5 < argv.Length && int.TryParse(argv[i + 5], out int c) ? c : 1351;
                int h = i + 6 < argv.Length && int.TryParse(argv[i + 6], out int d) ? d : 972;
                int step = i + 7 < argv.Length && int.TryParse(argv[i + 7], out int e) ? e : 1;
                _ = RunSequenceAsync(outDir, host, start, end, step, w, h);
                return;
            }
            if (argv[i] == "--upload-test")
            {
                // --upload-test <host> <file>  (temporary upload smoke test)
                _ = RunUploadTestAsync(argv[i + 1], argv[i + 2]);
                return;
            }
            if (argv[i] == "--shot")
            {
                // --shot <page> <host> <out.png> [elementName]
                string page = argv[i + 1];
                string host = i + 2 < argv.Length ? argv[i + 2] : "192.168.0.122";
                string outPath = i + 3 < argv.Length ? argv[i + 3] : "shot.png";
                string? element = i + 4 < argv.Length ? argv[i + 4] : null;
                _startupTag = page;
                _startupHost = host;
                _shotPath = outPath;
                _shotElement = element;
                break;
            }
        }
        // Debug shortcut: open the console on one device's lyric page directly.
        for (int i = 0; i < argv.Length - 1; i++)
        {
            // --lang can be combined with any other switch, so it never breaks.
            if (argv[i] == "--lang")
            {
                BukaMusicDesktop.MainWindow.StartupLanguage = argv[i + 1];
            }
            if (argv[i] == "--lyrics")
            {
                _startupHost = argv[i + 1];
                break;
            }
            if (argv[i] == "--navtest")
            {
                // Opens a page by "clicking" the side bar shortly after startup.
                _startupNavTest = i + 1 < argv.Length ? argv[i + 1] : null;
            }
            if (argv[i] == "--page")
            {
                _startupTag = argv[i + 1];
                _startupHost = i + 2 < argv.Length ? argv[i + 2] : null;
                break;
            }
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Activate();
        if (_startupHost != null)
        {
            Views.ShellPage.StartupNavTest = _startupNavTest;
            window.OpenPage(_startupTag ?? "lyrics", new BukaClient(new DeviceInfo
            {
                Ip = _startupHost,
                Port = 8080,
            }));
            if (_shotPath != null)
            {
                _ = CaptureAfterDelayAsync(window, _shotPath, _shotElement);
            }
        }
    }

    private static string? _shotPath;
    private static string? _shotElement;

    /// <summary>Waits for the page to load its data, then renders it to a PNG.</summary>
    private static async System.Threading.Tasks.Task CaptureAfterDelayAsync(
            MainWindow window, string path, string? elementName)
    {
        await System.Threading.Tasks.Task.Delay(4500);
        try
        {
            UIElement? target = null;
            if (!string.IsNullOrEmpty(elementName))
            {
                target = FindByName(window.Content as DependencyObject, elementName);
            }
            target ??= (UIElement?)window.Content;
            if (target is FrameworkElement element)
            {
                // Bring it on screen first: content below the fold is clipped.
                element.StartBringIntoView();
                await System.Threading.Tasks.Task.Delay(700);
            }
            if (target != null) await Core.ShotProbe.SaveAsync(target, path);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
        }
        Current.Exit();
    }

    /// <summary>Depth-first search for a named element anywhere in the tree.</summary>
    private static UIElement? FindByName(DependencyObject? node, string name)
    {
        if (node == null) return null;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement element && element.Name == name) return element;
            UIElement? found = FindByName(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static string? _startupHost;
    private static string? _startupTag;
    private static string? _startupNavTest;

    private static async System.Threading.Tasks.Task RunProbeAsync(string outDir, string host)
    {
        try
        {
            await LyricsProbe.RunAsync(outDir, host);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString()); } catch { }
        }
        Current.Exit();
    }

    private static async System.Threading.Tasks.Task RunShaderTestAsync(string outDir)
    {
        try
        {
            await LyricsProbe.RunShaderTestAsync(outDir);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString()); } catch { }
        }
        Current.Exit();
    }

    private static async System.Threading.Tasks.Task RunSequenceAsync(
            string outDir, string host, long start, long end, int step, int width, int height)
    {
        try
        {
            await LyricsProbe.RunSequenceAsync(outDir, host, start, end,
                    width, height, Math.Max(1, step));
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(outDir, "error.txt"), ex.ToString()); } catch { }
        }
        Current.Exit();
    }

    private static async System.Threading.Tasks.Task RunUploadTestAsync(string host, string file)
    {
        // Debug switch: runs the console's own upload path without the UI, so a
        // client-side regression (bad Content-Length, chunked body, ...) can be
        // reproduced and checked in one command.
        string log = Path.Combine(Path.GetTempPath(), "buka_upload_test.txt");
        try
        {
            var client = new BukaClient(new DeviceInfo { Ip = host, Port = 8080 });
            var progress = new Progress<double>(_ => { });
            UploadOutcome outcome = await client.UploadAsync(file, progress);
            string line = $"{outcome.Ok}|{outcome.Message}";
            File.WriteAllText(log, line);
            LogBus.Info($"上传自检：{line}");
        }
        catch (Exception ex)
        {
            LogBus.Error("上传自检失败：" + ex.Message);
            try { File.WriteAllText(log, "EX|" + ex.Message); } catch { }
        }
        Current.Exit();
    }
}
