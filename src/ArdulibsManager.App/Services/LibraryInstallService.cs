using System.IO.Compression;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class LibraryInstallService
{
    private readonly GithubService _github;
    private readonly SettingsService _settings;

    public LibraryInstallService(GithubService github, SettingsService settings)
    {
        _github = github;
        _settings = settings;
    }

    public async Task<InstalledLibrary> InstallAsync(GithubRepository repo, string tag, string librariesPath, IProgress<string>? log = null, string? targetPathOverride = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(librariesPath);
        var librariesRoot = Path.GetFullPath(librariesPath);
        var tempRoot = Path.Combine(Path.GetTempPath(), "ArdulibsManager", Guid.NewGuid().ToString("N"));
        var stagingPath = Path.Combine(librariesRoot, ".ardulibs-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var zipPath = Path.Combine(tempRoot, "repo.zip");
        var extractPath = Path.Combine(tempRoot, "extract");

        try
        {
            log?.Report($"Скачивание {repo.FullName} {tag}...");
            await _github.DownloadZipballAsync(repo, tag, zipPath, ct);

            log?.Report("Распаковка...");
            ZipFile.ExtractToDirectory(zipPath, extractPath);

            var propsPath = Directory.EnumerateFiles(extractPath, "library.properties", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(ch => ch == Path.DirectorySeparatorChar))
                .FirstOrDefault();

            if (propsPath is null)
                throw new InvalidOperationException("В архиве не найден library.properties. Это может быть не Arduino-библиотека.");

            var libRoot = Path.GetDirectoryName(propsPath)!;
            var props = LibraryProperties.Parse(await File.ReadAllTextAsync(propsPath, ct));
            var folderName = SanitizeFolderName(props.Name ?? repo.Name);
            var targetPath = !string.IsNullOrWhiteSpace(targetPathOverride)
                ? Path.GetFullPath(targetPathOverride)
                : Path.Combine(librariesRoot, folderName);

            EnsureInsideLibraries(librariesRoot, targetPath);

            log?.Report("Подготовка новой версии...");
            CopyDirectory(libRoot, stagingPath);

            var stagedProps = Path.Combine(stagingPath, "library.properties");
            if (!File.Exists(stagedProps))
                throw new InvalidOperationException("Ошибка подготовки: library.properties не найден в подготовленной папке.");

            // Некоторые репозитории ставят новый GitHub tag, но забывают обновить
            // version= в library.properties. Для Arduino это важное поле, поэтому
            // в установленной копии синхронизируем его с выбранным tag, если tag
            // похож на нормальную числовую версию. Исходный репозиторий не меняется.
            props = await SyncLibraryPropertiesVersionWithTagAsync(stagedProps, props, tag, ct);

            await WriteMetadataAsync(stagingPath, repo, tag, ct);

            log?.Report("Атомарная замена библиотеки...");
            ReplaceDirectoryAtomic(stagingPath, targetPath, librariesRoot, log);

            log?.Report("Готово.");
            return new InstalledLibrary
            {
                Name = props.Name ?? repo.Name,
                Version = tag,
                PropertiesVersion = props.Version,
                InstalledRef = tag,
                Maintainer = props.Maintainer,
                Url = props.Url ?? repo.Url,
                LocalPath = targetPath,
                RepositoryFullName = repo.FullName,
                Status = "Установлено"
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
            TryDeleteDirectory(stagingPath);
        }
    }

    public Task RemoveAsync(InstalledLibrary lib)
    {
        var librariesRoot = Path.GetFullPath(_settings.Current.LibrariesPath);
        var targetPath = Path.GetFullPath(lib.LocalPath);
        EnsureInsideLibraries(librariesRoot, targetPath);

        var propsPath = Path.Combine(targetPath, "library.properties");
        if (!File.Exists(propsPath))
            throw new InvalidOperationException("Удаление отменено: в папке библиотеки не найден library.properties.");

        if (Directory.Exists(targetPath))
        {
            NormalizeAttributes(targetPath);
            RetryIo(() => Directory.Delete(targetPath, recursive: true));
        }

        return Task.CompletedTask;
    }


    private static async Task<LibraryProperties> SyncLibraryPropertiesVersionWithTagAsync(string propsPath, LibraryProperties props, string tag, CancellationToken ct)
    {
        var tagVersion = VersionService.ExtractNormalizedVersion(tag);
        if (string.IsNullOrWhiteSpace(tagVersion))
            return props;

        if (VersionService.IsSameVersion(props.Version, tagVersion))
            return props;

        var lines = (await File.ReadAllTextAsync(propsPath, ct))
            .Replace("\r\n", "\n")
            .Split('\n')
            .ToList();
        var replaced = false;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("version=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = "version=" + tagVersion;
                replaced = true;
                break;
            }
        }

        if (!replaced)
            lines.Add("version=" + tagVersion);

        await File.WriteAllTextAsync(propsPath, string.Join(Environment.NewLine, lines), ct);
        var updatedText = await File.ReadAllTextAsync(propsPath, ct);
        return LibraryProperties.Parse(updatedText);
    }

    private static async Task WriteMetadataAsync(string libraryPath, GithubRepository repo, string tag, CancellationToken ct)
    {
        var metadata = new ManagedLibraryMetadata
        {
            RepositoryFullName = repo.FullName,
            RepositoryUrl = repo.Url,
            InstalledRef = tag,
            InstalledAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(libraryPath, ManagedLibraryMetadata.FileName), json, ct);
    }

    private static void ReplaceDirectoryAtomic(string stagingPath, string targetPath, string librariesRoot, IProgress<string>? log)
    {
        var oldPath = Path.Combine(librariesRoot, ".ardulibs-old-" + Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            NormalizeAttributes(stagingPath);

            if (!Directory.Exists(targetPath))
            {
                RetryIo(() => Directory.Move(stagingPath, targetPath));
                return;
            }

            NormalizeAttributes(targetPath);
            RetryIo(() => Directory.Move(targetPath, oldPath));
        }
        catch (Exception ex) when (IsAccessOrBusy(ex))
        {
            throw new IOException(
                "Не удалось заменить папку библиотеки. Обычно это значит, что библиотека открыта или используется другой программой. " +
                "Закрой Arduino IDE, Serial Monitor, проводник/терминал в этой папке и повтори обновление. Путь: " + targetPath,
                ex);
        }

        try
        {
            RetryIo(() => Directory.Move(stagingPath, targetPath));
        }
        catch
        {
            TryDeleteDirectory(targetPath);
            if (Directory.Exists(oldPath) && !Directory.Exists(targetPath))
                RetryIo(() => Directory.Move(oldPath, targetPath));
            throw;
        }

        try
        {
            NormalizeAttributes(oldPath);
            RetryIo(() => Directory.Delete(oldPath, recursive: true));
        }
        catch (Exception ex)
        {
            log?.Report("Новая версия установлена, но временную старую папку не удалось удалить: " + ex.Message);
        }
    }

    private static void EnsureInsideLibraries(string librariesRoot, string targetPath)
    {
        librariesRoot = Path.GetFullPath(librariesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        targetPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(librariesRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Операция отменена: путь библиотеки находится вне папки Arduino libraries.");
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }

    private static void NormalizeAttributes(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(dir, FileAttributes.Directory);

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        File.SetAttributes(path, FileAttributes.Directory);
    }

    private static void RetryIo(Action action)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && IsAccessOrBusy(ex))
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }

    private static bool IsAccessOrBusy(Exception ex)
        => ex is IOException or UnauthorizedAccessException;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                NormalizeAttributes(path);
                RetryIo(() => Directory.Delete(path, recursive: true));
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
