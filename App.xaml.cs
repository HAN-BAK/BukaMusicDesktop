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
        }
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
}
