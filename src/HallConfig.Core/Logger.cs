using System;
using System.IO;

namespace HallConfig.Core;

public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogFilePath;
    private static readonly object _lock = new object();

    public static string LogDirectory => LogDir;

    static Logger()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        LogDir = Path.Combine(appData, "HallConfig", "logs");

        try
        {
            if (!Directory.Exists(LogDir))
            {
                Directory.CreateDirectory(LogDir);
            }

            // Cleanup old logs (> 14 days)
            CleanupOldLogs(14);
        }
        catch
        {
            // Ignore directory creation or cleanup errors to prevent crashing during static init
        }

        string filename = $"hallconfig-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        LogFilePath = Path.Combine(LogDir, filename);

        // Touch the file to ensure it's created
        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogFilePath, $"=== HallConfig Log Session Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
        }
        catch { }
    }

    private static void CleanupOldLogs(int daysToKeep)
    {
        var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
        var files = Directory.GetFiles(LogDir, "hallconfig-*.log");
        foreach (var file in files)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoffDate)
                {
                    fileInfo.Delete();
                }
            }
            catch { }
        }
    }

    public static void Info(string category, string message)
    {
        WriteLog("INFO ", category, message);
    }

    public static void Warn(string category, string message)
    {
        WriteLog("WARN ", category, message);
    }

    public static void Error(string category, string message, Exception? ex = null)
    {
        if (ex != null)
        {
            message = $"{message}: {ex.GetType().FullName} - {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        }
        WriteLog("ERROR", category, message);
    }

    private static void WriteLog(string level, string category, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        // Ensure category is nicely bracketed, remove brackets if already passed with brackets
        category = category.Trim('[', ']');
        string logLine = $"{timestamp} [{level}] [{category}] {message}{Environment.NewLine}";

        try
        {
            lock (_lock)
            {
                File.AppendAllText(LogFilePath, logLine);
            }
        }
        catch
        {
            // Silent fail for logging errors to prevent cascading crashes
        }
    }
}
