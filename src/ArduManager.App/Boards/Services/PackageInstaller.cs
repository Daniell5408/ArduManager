using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public sealed class PackageInstaller
{
    private readonly ArduinoPathService _paths;
    private readonly ArchiveExtractor _extractor = new();
    private static readonly TimeSpan ResponseHeaderTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    public PackageInstaller(ArduinoPathService paths)
    {
        _paths = paths;
    }

    public async Task InstallAsync(
        PlatformDescriptor descriptor,
        string version,
        IReadOnlyList<PackageIndexDocument> knownIndexes,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        LogService.Info($"Install requested: {descriptor.Key}@{version}");
        var platform = descriptor.GetPlatform(version);
        var dependencies = platform.ToolsDependencies ?? new List<ToolDependency>();
        var totalSteps = dependencies.Count + 1;
        var step = 0;

        foreach (var dependency in dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await InstallToolDependencyAsync(dependency, knownIndexes, step, totalSteps, progress, cancellationToken);
            step++;
        }

        await InstallPlatformArchiveAsync(descriptor, platform, step, totalSteps, progress, cancellationToken);
        LogService.Info($"Install completed: {descriptor.Key}@{version}");
        progress.Report(new InstallProgress("Готово", 100));
    }

    public Task DeletePlatformAsync(
        PlatformDescriptor descriptor,
        IReadOnlyList<PackageIndexDocument> knownIndexes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogService.Info($"Delete platform requested: {descriptor.Key}");

        var platformDir = _paths.GetPlatformVersionsDirectory(descriptor.PackageName, descriptor.Architecture);
        DeleteDirectoryForce(platformDir, cancellationToken);
        DeleteEmptyAncestors(platformDir, _paths.GetPackageDirectory(descriptor.PackageName), cancellationToken);

        foreach (var dependency in ArduinoPathService.GetToolDependencies(descriptor))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsToolDependencyUsedByOtherInstalledPlatform(dependency, descriptor, knownIndexes))
            {
                LogService.Info($"Tool kept because another installed platform uses it: {dependency.Packager}:{dependency.Name}@{dependency.Version}");
                continue;
            }

            var toolVersionDir = _paths.GetToolVersionDirectory(dependency.Packager, dependency.Name, dependency.Version);
            DeleteDirectoryForce(toolVersionDir, cancellationToken);
            DeleteEmptyAncestors(toolVersionDir, _paths.GetPackageDirectory(dependency.Packager), cancellationToken);
        }

        DeleteEmptyDirectory(_paths.GetPackageHardwareDirectory(descriptor.PackageName), cancellationToken);
        DeleteEmptyDirectory(_paths.GetPackageToolsDirectory(descriptor.PackageName), cancellationToken);
        DeleteEmptyDirectory(_paths.GetPackageDirectory(descriptor.PackageName), cancellationToken);

        return Task.CompletedTask;
    }

    private bool IsToolDependencyUsedByOtherInstalledPlatform(
        ToolDependency dependency,
        PlatformDescriptor deletingDescriptor,
        IReadOnlyList<PackageIndexDocument> knownIndexes)
    {
        foreach (var entry in knownIndexes
                     .SelectMany(document => document.Index.Packages.SelectMany(package => package.Platforms.Select(platform => new
                     {
                         Package = package,
                         Platform = platform
                     }))))
        {
            if (string.Equals(entry.Package.Name, deletingDescriptor.PackageName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Platform.Architecture, deletingDescriptor.Architecture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_paths.GetInstalledPlatformVersions(entry.Package.Name, entry.Platform.Architecture).Any())
                continue;

            var isUsed = (entry.Platform.ToolsDependencies ?? new List<ToolDependency>()).Any(x =>
                string.Equals(x.Packager, dependency.Packager, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Name, dependency.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Version, dependency.Version, StringComparison.OrdinalIgnoreCase));

            if (isUsed)
                return true;
        }

        return false;
    }

    private static void DeleteEmptyAncestors(string startDirectory, string stopDirectory, CancellationToken cancellationToken)
    {
        var current = Directory.GetParent(startDirectory)?.FullName;
        var stop = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        while (!string.IsNullOrWhiteSpace(current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!normalized.StartsWith(stop, StringComparison.OrdinalIgnoreCase))
                break;

            DeleteEmptyDirectory(normalized, cancellationToken);

            if (string.Equals(normalized, stop, StringComparison.OrdinalIgnoreCase))
                break;

            current = Directory.GetParent(normalized)?.FullName;
        }
    }

    private static void DeleteEmptyDirectory(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(directory))
            return;

        if (Directory.EnumerateFileSystemEntries(directory).Any())
            return;

        try
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }
        catch
        {
            // Best effort.
        }

        Directory.Delete(directory, recursive: false);
    }

    private static void DeleteDirectoryForce(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
                // Best effort: Directory.Delete will report the actual deletion error if it still fails.
            }
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.SetAttributes(subDirectory, FileAttributes.Normal);
            }
            catch
            {
                // Best effort.
            }
        }

        File.SetAttributes(directory, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    private async Task InstallToolDependencyAsync(
        ToolDependency dependency,
        IReadOnlyList<PackageIndexDocument> knownIndexes,
        int step,
        int totalSteps,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        var target = _paths.GetToolVersionDirectory(dependency.Packager, dependency.Name, dependency.Version);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            ReportStep(progress, step, totalSteps, 100, $"Tool уже установлен: {dependency.Name} {dependency.Version}");
            return;
        }

        var tool = knownIndexes
            .SelectMany(x => x.Index.Packages)
            .Where(x => string.Equals(x.Name, dependency.Packager, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Tools)
            .FirstOrDefault(x =>
                string.Equals(x.Name, dependency.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Version, dependency.Version, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
            throw new InvalidOperationException($"Не нашёл tool {dependency.Packager}:{dependency.Name}@{dependency.Version} в загруженных index JSON.");

        var system = HostMatcher.SelectBestSystem(tool.Systems);
        var archive = new ArchiveItem(system.Url, system.ArchiveFileName, system.Checksum, system.Size);
        var localArchive = await DownloadArchiveAsync(archive, step, totalSteps, $"Скачиваю tool {dependency.Name}", progress, cancellationToken);
        await ExtractAndInstallAsync(localArchive, target, platformArchive: false, step, totalSteps, $"Устанавливаю tool {dependency.Name}", progress, cancellationToken);
    }

    private async Task InstallPlatformArchiveAsync(
        PlatformDescriptor descriptor,
        ArduinoPlatform platform,
        int step,
        int totalSteps,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        var archive = new ArchiveItem(platform.Url, platform.ArchiveFileName, platform.Checksum, platform.Size);
        var localArchive = await DownloadArchiveAsync(archive, step, totalSteps, $"Скачиваю {platform.Name} {platform.Version}", progress, cancellationToken);
        var target = _paths.GetPlatformVersionDirectory(descriptor.PackageName, descriptor.Architecture, platform.Version);
        await ExtractAndInstallAsync(localArchive, target, platformArchive: true, step, totalSteps, $"Устанавливаю {platform.Name}", progress, cancellationToken);
    }

    private async Task<string> DownloadArchiveAsync(
        ArchiveItem item,
        int step,
        int totalSteps,
        string status,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.CacheDirectory);
        var archiveFileName = !string.IsNullOrWhiteSpace(item.ArchiveFileName)
            ? item.ArchiveFileName
            : GetFileNameFromUrl(item.Url);

        var cacheFile = Path.Combine(AppPaths.CacheDirectory, $"{ShortHash(item.Url)}_{SanitizeFileName(archiveFileName)}");

        if (File.Exists(cacheFile) && VerifySize(cacheFile, item.Size) && await VerifyChecksumAsync(cacheFile, item.Checksum, cancellationToken))
        {
            ReportStep(progress, step, totalSteps, 100, $"Из кэша: {archiveFileName}");
            return cacheFile;
        }

        var tempFile = Path.Combine(
            AppPaths.CacheDirectory,
            $"{ShortHash(item.Url)}_{Guid.NewGuid():N}_{SanitizeFileName(archiveFileName)}.download");

        try
        {
            ReportStep(progress, step, totalSteps, 0, $"{status}: подключаюсь к серверу...");

            using var response = await GetResponseWithTimeoutAsync(item.Url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            var buffer = new byte[1024 * 128];
            long downloaded = 0;
            int read;

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                while ((read = await ReadWithIdleTimeoutAsync(input, buffer, archiveFileName, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;

                    var localPercent = total is > 0
                        ? Math.Min(90, downloaded * 90.0 / total.Value)
                        : 25;

                    ReportStep(progress, step, totalSteps, localPercent, $"{status}: {archiveFileName} ({FormatBytes(downloaded)}{(total is > 0 ? " / " + FormatBytes(total.Value) : string.Empty)})");
                }

                await output.FlushAsync(cancellationToken);
            }

            if (!VerifySize(tempFile, item.Size))
                throw new InvalidOperationException($"Размер архива {archiveFileName} не совпал с size из JSON.");

            if (!await VerifyChecksumAsync(tempFile, item.Checksum, cancellationToken))
                throw new InvalidOperationException($"Checksum архива {archiveFileName} не совпал с checksum из JSON.");

            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
            File.Move(tempFile, cacheFile);
        }
        catch
        {
            TryDeleteFile(tempFile);
            throw;
        }

        ReportStep(progress, step, totalSteps, 100, $"Скачано: {archiveFileName}");
        return cacheFile;
    }


    private async Task<HttpResponseMessage> GetResponseWithTimeoutAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResponseHeaderTimeout);

        try
        {
            return await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Сервер не ответил за {FormatTimeout(ResponseHeaderTimeout)}: {url}");
        }
    }

    private static async Task<int> ReadWithIdleTimeoutAsync(Stream input, byte[] buffer, string archiveFileName, CancellationToken cancellationToken)
    {
        var readTask = input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).AsTask();
        var timeoutTask = Task.Delay(DownloadIdleTimeout, cancellationToken);
        var completed = await Task.WhenAny(readTask, timeoutTask);

        if (completed == readTask)
            return await readTask;

        if (cancellationToken.IsCancellationRequested)
            cancellationToken.ThrowIfCancellationRequested();

        throw new TimeoutException($"Загрузка {archiveFileName} остановилась: за {FormatTimeout(DownloadIdleTimeout)} не получено новых данных.");
    }

    private async Task ExtractAndInstallAsync(
        string archiveFile,
        string targetDirectory,
        bool platformArchive,
        int step,
        int totalSteps,
        string status,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ArduManager.Boards", Guid.NewGuid().ToString("N"));
        var tempInstall = targetDirectory + ".installing-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(tempRoot);
            ReportStep(progress, step, totalSteps, 0, status + ": распаковка");
            await _extractor.ExtractAsync(archiveFile, tempRoot, cancellationToken);

            var contentRoot = ArchiveExtractor.FindContentRoot(tempRoot, platformArchive);
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            CopyDirectory(contentRoot, tempInstall, cancellationToken);

            if (Directory.Exists(targetDirectory))
                Directory.Delete(targetDirectory, recursive: true);

            Directory.Move(tempInstall, targetDirectory);
            ReportStep(progress, step, totalSteps, 100, status + ": установлено");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
            TryDeleteDirectory(tempInstall);
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best effort cleanup. The UI will still show the actual install error, if any.
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void ReportStep(IProgress<InstallProgress> progress, int step, int totalSteps, double localPercent, string message)
    {
        var overall = Math.Clamp(((step * 100.0) + localPercent) / Math.Max(1, totalSteps), 0, 100);
        progress.Report(new InstallProgress(message, overall));
    }

    private static bool VerifySize(string file, string? expectedSize)
    {
        if (string.IsNullOrWhiteSpace(expectedSize))
            return true;

        return long.TryParse(expectedSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
               && new FileInfo(file).Length == size;
    }

    private static async Task<bool> VerifyChecksumAsync(string file, string? checksum, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(checksum))
            return true;

        var parts = checksum.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        using HashAlgorithm algorithm = parts[0].ToUpperInvariant() switch
        {
            "SHA-256" or "SHA256" => SHA256.Create(),
            "SHA-1" or "SHA1" => SHA1.Create(),
            "MD5" => MD5.Create(),
            _ => throw new InvalidOperationException($"Неподдерживаемый checksum algorithm: {parts[0]}")
        };

        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await algorithm.ComputeHashAsync(stream, cancellationToken));
        return string.Equals(actual, parts[1].Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortHash(string text)
    {
        var bytes = SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName;
    }

    private static string GetFileNameFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Path.GetFileName(uri.LocalPath);
        return "archive.bin";
    }

    private static string FormatTimeout(TimeSpan value)
    {
        return value.TotalSeconds >= 60
            ? $"{value.TotalMinutes:0.#} мин"
            : $"{value.TotalSeconds:0} сек";
    }

    private static string FormatBytes(long value)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double number = value;
        var unit = 0;
        while (number >= 1024 && unit < units.Length - 1)
        {
            number /= 1024;
            unit++;
        }
        return $"{number:0.##} {units[unit]}";
    }

    private sealed record ArchiveItem(string Url, string ArchiveFileName, string? Checksum, string? Size);
}
