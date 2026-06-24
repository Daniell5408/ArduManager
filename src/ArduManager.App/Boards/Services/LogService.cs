using System.Text;

namespace ArduboardsManager.App.Services;

public static class LogService
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);

            var builder = new StringBuilder();
            builder.Append('[')
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append("] ")
                .Append(level)
                .Append("  ")
                .AppendLine(message);

            if (exception is not null)
                builder.AppendLine(exception.ToString());

            lock (Sync)
            {
                File.AppendAllText(AppPaths.CurrentLogFile, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
    }
}
