using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;

namespace BukaMusicDesktop.Core;

public enum LogLevel { Info, Success, Warn, Error }

public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";

    public string TimeText => Time.ToString("HH:mm:ss");

    public string LevelText => Level switch
    {
        LogLevel.Success => "OK",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERR",
        _ => "INFO",
    };

    public Brush LevelBrush => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[Level switch
    {
        LogLevel.Success => "SuccessBrush",
        LogLevel.Warn => "WarnBrush",
        LogLevel.Error => "ErrorBrush",
        _ => "TextSecondaryBrush",
    }];
}

/// <summary>Application-wide log shown on the 日志 page.</summary>
public static class LogBus
{
    private const int MaxEntries = 800;

    /// <summary>UI dispatcher, assigned once the window exists.</summary>
    public static DispatcherQueue? Dispatcher { get; set; }

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Info(string message) => Add(LogLevel.Info, message);
    public static void Success(string message) => Add(LogLevel.Success, message);
    public static void Warn(string message) => Add(LogLevel.Warn, message);
    public static void Error(string message) => Add(LogLevel.Error, message);

    public static void Clear()
    {
        Run(() => Entries.Clear());
    }

    private static void Add(LogLevel level, string message)
        => Run(() =>
        {
            Entries.Add(new LogEntry { Level = level, Message = message });
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
            WriteToFile(level, message);
        });

    private static readonly object FileLock = new();

    /// <summary>Mirrors the log to %APPDATA%\BukaMusicDesktop\log.txt for support.</summary>
    private static void WriteToFile(LogLevel level, string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BukaMusicDesktop");
            System.IO.Directory.CreateDirectory(dir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level.ToString().ToUpperInvariant()}] {message}"
                       + Environment.NewLine;
            lock (FileLock)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "log.txt"), line);
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static void Run(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher == null || dispatcher.HasThreadAccess) action();
        else dispatcher.TryEnqueue(() => action());
    }
}
