using System;
using System.IO;

namespace Glint.Core;

/// <summary>
/// Minimal file logger so we can capture crashes/exceptions even when the app
/// runs headless from the tray. Writes to %TEMP%\Glint.log.
/// </summary>
public static class Log
{
    private static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Glint.log");

    private static readonly object Gate = new();

    public static string FilePath => Path;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex is not null) line += Environment.NewLine + ex;
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
