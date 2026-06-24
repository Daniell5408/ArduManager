namespace ArduboardsManager.App.Services;

public sealed class StartupCleanupService
{
    private readonly ArduinoPathService _paths;

    public StartupCleanupService(ArduinoPathService paths)
    {
        _paths = paths;
    }

    public void CleanupLeftovers()
    {
        CleanupDownloadFiles();
        CleanupTempExtractionRoot();
        CleanupInstallingDirectories();
    }

    private static void CleanupDownloadFiles()
    {
        try
        {
            if (!Directory.Exists(AppPaths.CacheDirectory))
                return;

            var deleted = 0;
            foreach (var file in Directory.EnumerateFiles(AppPaths.CacheDirectory, "*.download", SearchOption.AllDirectories))
            {
                if (TryDeleteFile(file))
                    deleted++;
            }

            if (deleted > 0)
                LogService.Info($"Startup cleanup: deleted partial downloads: {deleted}");
        }
        catch (Exception ex)
        {
            LogService.Error("Startup cleanup: partial downloads cleanup failed", ex);
        }
    }

    private static void CleanupTempExtractionRoot()
    {
        try
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ArduManager.Boards");
            if (!Directory.Exists(tempRoot))
                return;

            Directory.Delete(tempRoot, recursive: true);
            LogService.Info($"Startup cleanup: deleted temp extraction root: {tempRoot}");
        }
        catch (Exception ex)
        {
            LogService.Error("Startup cleanup: temp extraction cleanup failed", ex);
        }
    }

    private void CleanupInstallingDirectories()
    {
        try
        {
            var packagesDirectory = Path.Combine(_paths.DataDirectory, "packages");
            if (!Directory.Exists(packagesDirectory))
                return;

            var deleted = 0;
            foreach (var directory in Directory.EnumerateDirectories(packagesDirectory, "*.installing-*", SearchOption.AllDirectories))
            {
                if (TryDeleteDirectory(directory))
                    deleted++;
            }

            if (deleted > 0)
                LogService.Info($"Startup cleanup: deleted stale install directories: {deleted}");
        }
        catch (Exception ex)
        {
            LogService.Error("Startup cleanup: install directory cleanup failed", ex);
        }
    }

    private static bool TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
