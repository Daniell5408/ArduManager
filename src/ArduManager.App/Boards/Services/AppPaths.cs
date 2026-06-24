namespace ArduboardsManager.App.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ArduManager",
        "boards");

    public static string LocalAppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ArduManager",
        "boards");

    public static string SettingsFile { get; } = Path.Combine(AppDataDirectory, "settings.json");

    public static string CacheDirectory { get; } = Path.Combine(LocalAppDataDirectory, "cache");

    public static string LogsDirectory { get; } = Path.Combine(AppDataDirectory, "logs");

    public static string CurrentLogFile { get; } = Path.Combine(LogsDirectory, "app.log");

    public static string LastRunLogFile { get; } = Path.Combine(LogsDirectory, "last-run.log");
}
