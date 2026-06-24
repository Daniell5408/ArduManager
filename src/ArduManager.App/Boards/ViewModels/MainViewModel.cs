using System.Collections.ObjectModel;
using System.Windows;
using ArduboardsManager.App.Models;
using ArduboardsManager.App.Services;

namespace ArduboardsManager.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();
    private readonly PackageIndexService _indexService = new();
    private readonly ArduinoPathService _pathService = new();
    private readonly PackageInstaller _installer;
    private readonly StandardPlatformLinksService _standardPlatformLinksService = new();
    private const string ArduinoOfficialIndexUrl = "https://downloads.arduino.cc/packages/package_index.json";

    private readonly List<PackageIndexDocument> _knownIndexes = new();
    private readonly List<PackageIndexDocument> _dependencyIndexes = new();

    public event Action<string>? StatusChanged;
    public event Action<bool>? BusyChanged;
    public event Action<string>? LogMessage;

    private string _newPackageUrl = string.Empty;
    private StandardPlatformLink? _selectedStandardPlatform;
    private string _footerText = "Готово";
    private bool _isLoading;

    public MainViewModel()
    {
        _installer = new PackageInstaller(_pathService);
        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(NewPackageUrl));
        ReloadCommand = new AsyncRelayCommand(ReloadAsync, () => !IsLoading);

        StandardPlatforms.Add(new StandardPlatformLink { Name = "Популярные", Url = string.Empty });
        foreach (var link in _standardPlatformLinksService.Load())
            StandardPlatforms.Add(link);

        _selectedStandardPlatform = StandardPlatforms.FirstOrDefault();
    }

    public ObservableCollection<PlatformCardViewModel> Platforms { get; } = new();

    public ObservableCollection<StandardPlatformLink> StandardPlatforms { get; } = new();

    public AsyncRelayCommand AddUrlCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }

    public string DataDirectory => $"Arduino15: {_pathService.DataDirectory}";

    public string NewPackageUrl
    {
        get => _newPackageUrl;
        set
        {
            if (SetProperty(ref _newPackageUrl, value))
                AddUrlCommand.RaiseCanExecuteChanged();
        }
    }

    public StandardPlatformLink? SelectedStandardPlatform
    {
        get => _selectedStandardPlatform;
        set
        {
            if (!SetProperty(ref _selectedStandardPlatform, value))
                return;

            if (value is null || string.IsNullOrWhiteSpace(value.Url))
                return;

            _ = AddStandardPlatformAsync(value);
        }
    }

    public string FooterText
    {
        get => _footerText;
        set
        {
            if (SetProperty(ref _footerText, value))
                StatusChanged?.Invoke(value);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                AddUrlCommand.RaiseCanExecuteChanged();
                ReloadCommand.RaiseCanExecuteChanged();
                BusyChanged?.Invoke(value);
            }
        }
    }

    private void ReportStatus(string message, bool writeLog = false)
    {
        FooterText = message;
        if (writeLog)
            Log(message);
    }

    private void Log(string message)
    {
        LogService.Info(message);
        LogMessage?.Invoke(message);
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync();
    }

    private Task AddUrlAsync()
    {
        return AddUrlFromInputAsync(NewPackageUrl);
    }

    private async Task AddStandardPlatformAsync(StandardPlatformLink link)
    {
        if (IsLoading)
        {
            ResetStandardPlatformSelection();
            return;
        }

        NewPackageUrl = link.Url;
        await AddUrlFromInputAsync(link.Url);
        ResetStandardPlatformSelection();
    }

    private async Task AddUrlFromInputAsync(string rawUrl)
    {
        var url = rawUrl.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return;

        IsLoading = true;
        ReportStatus("Проверяю package index...");

        try
        {
            // Важно: сначала пробуем скачать и распарсить JSON. Только после успеха запоминаем ссылку.
            await _indexService.DownloadAsync(url);
            await _settingsService.AddUrlAsync(url);
            Log($"Добавлена ссылка платформ: {url}");
            NewPackageUrl = string.Empty;
            await ReloadInternalAsync();
        }
        catch (Exception ex)
        {
            ReportStatus("Не удалось добавить ссылку.", writeLog: true);
            MessageBox.Show(ex.Message, "Ошибка добавления package index", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetStandardPlatformSelection()
    {
        SelectedStandardPlatform = StandardPlatforms.FirstOrDefault();
    }

    private async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            await ReloadInternalAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadInternalAsync()
    {
        Platforms.Clear();
        _knownIndexes.Clear();
        _dependencyIndexes.Clear();

        var settings = await _settingsService.LoadAsync();
        if (settings.PackageUrls.Count == 0)
        {
            ReportStatus("Добавь ссылку на package_*_index.json.");
            return;
        }

        var errors = new List<string>();
        ReportStatus($"Обновляю {settings.PackageUrls.Count} index JSON...");

        foreach (var url in settings.PackageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var document = await _indexService.DownloadAsync(url);
                _knownIndexes.Add(document);
            }
            catch (Exception ex)
            {
                errors.Add($"{url}: {ex.Message}");
            }
        }

        BuildCards();

        var status = errors.Count == 0
            ? $"Загружено index JSON: {_knownIndexes.Count}. Платформ: {Platforms.Count}."
            : $"Загружено index JSON: {_knownIndexes.Count}. Ошибки: {errors.Count}. Первая: {errors[0]}";
        ReportStatus(status, writeLog: errors.Count > 0);
    }

    private void BuildCards()
    {
        var entries = _knownIndexes
            .SelectMany(document => document.Index.Packages.SelectMany(package => package.Platforms.Select(platform => new
            {
                document.SourceUrl,
                Package = package,
                Platform = platform
            })))
            .Where(x => !string.IsNullOrWhiteSpace(x.Package.Name)
                        && !string.IsNullOrWhiteSpace(x.Platform.Architecture)
                        && !string.IsNullOrWhiteSpace(x.Platform.Version));

        var groups = entries.GroupBy(x => new
        {
            x.SourceUrl,
            PackageName = x.Package.Name,
            x.Platform.Architecture
        });

        foreach (var group in groups)
        {
            var versions = group
                .GroupBy(x => x.Platform.Version)
                .ToDictionary(x => x.Key, x => x.First().Platform);

            if (versions.Count == 0)
                continue;

            var sortedVersions = versions.Keys
                .OrderByDescending(x => x, VersionTextComparer.Instance)
                .ToList();

            var latestVersion = sortedVersions[0];
            var latestPlatform = versions[latestVersion];

            var descriptor = new PlatformDescriptor
            {
                SourceUrl = group.Key.SourceUrl,
                PackageName = group.Key.PackageName,
                Architecture = group.Key.Architecture,
                DisplayName = string.IsNullOrWhiteSpace(latestPlatform.Name)
                    ? $"{group.Key.PackageName}:{group.Key.Architecture}"
                    : latestPlatform.Name,
                GitHubUrl = ResolveGitHubUrl(
                    group.Select(x => x.Package.WebsiteUrl)
                        .Concat(group.Select(x => x.Platform.Url))
                        .Concat(new[] { group.Key.SourceUrl })),
                PlatformsByVersion = versions
            };

            var installed = _pathService.GetInstalledPlatformVersion(descriptor.PackageName, descriptor.Architecture);
            Platforms.Add(new PlatformCardViewModel(
                descriptor,
                sortedVersions,
                installed,
                latestVersion,
                InstallCardAsync,
                RemoveCardAsync));
        }
    }


    private static string? ResolveGitHubUrl(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeGitHubUrl(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return null;
    }

    private static string? NormalizeGitHubUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (host == "github.com" || host == "www.github.com")
        {
            if (segments.Length >= 2)
                return $"https://github.com/{segments[0]}/{segments[1]}";

            return uri.GetLeftPart(UriPartial.Authority);
        }

        if (host == "raw.githubusercontent.com" && segments.Length >= 2)
            return $"https://github.com/{segments[0]}/{segments[1]}";

        return null;
    }

    private async Task EnsureDependencyIndexesLoadedAsync(ArduinoPlatform platform)
    {
        var dependencies = platform.ToolsDependencies ?? new List<ToolDependency>();
        if (dependencies.Count == 0)
            return;

        var allIndexes = _knownIndexes.Concat(_dependencyIndexes).ToList();
        var missing = dependencies
            .Where(dependency => !ContainsTool(allIndexes, dependency))
            .ToList();

        if (missing.Count == 0)
            return;

        var needsArduinoIndex = missing.Any(dependency =>
            string.Equals(dependency.Packager, "arduino", StringComparison.OrdinalIgnoreCase));

        if (needsArduinoIndex && !HasIndexLoaded(ArduinoOfficialIndexUrl))
        {
            try
            {
                ReportStatus("Загружаю Arduino index для зависимостей...");
                Log($"Загружаю dependency index: {ArduinoOfficialIndexUrl}");
                var arduinoIndex = await _indexService.DownloadAsync(ArduinoOfficialIndexUrl);
                _dependencyIndexes.Add(arduinoIndex);
                allIndexes = _knownIndexes.Concat(_dependencyIndexes).ToList();
                missing = dependencies
                    .Where(dependency => !ContainsTool(allIndexes, dependency))
                    .ToList();
            }
            catch (Exception ex)
            {
                var missingText = FormatMissingTools(missing);
                throw new InvalidOperationException(
                    "Платформе нужны инструменты из Arduino package index, но его не удалось загрузить. " +
                    "Добавь Arduino official в список платформ или используй зеркало package_index.json, где есть эти tools.\n\n" +
                    $"Не найдены: {missingText}\n\n" +
                    $"Ошибка загрузки Arduino index: {ex.Message}", ex);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Платформе нужны tools, которых нет в загруженных package index JSON. " +
                "Добавь package index, где они описаны, или его зеркало.\n\n" +
                $"Не найдены: {FormatMissingTools(missing)}");
        }
    }

    private bool HasIndexLoaded(string sourceUrl)
    {
        return _knownIndexes.Concat(_dependencyIndexes).Any(document =>
            string.Equals(document.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsTool(IEnumerable<PackageIndexDocument> indexes, ToolDependency dependency)
    {
        return indexes
            .SelectMany(document => document.Index.Packages)
            .Where(package => string.Equals(package.Name, dependency.Packager, StringComparison.OrdinalIgnoreCase))
            .SelectMany(package => package.Tools)
            .Any(tool =>
                string.Equals(tool.Name, dependency.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tool.Version, dependency.Version, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatMissingTools(IEnumerable<ToolDependency> dependencies)
    {
        return string.Join(", ", dependencies
            .Select(dependency => $"{dependency.Packager}:{dependency.Name}@{dependency.Version}")
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task InstallCardAsync(PlatformCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(card.SelectedVersion))
            return;

        IsLoading = true;
        card.IsBusy = true;
        card.ProgressValue = 0;
        card.Status = "Старт установки...";
        ReportStatus($"Начата установка платформы {card.Descriptor.DisplayName}...");
        Log($"Установка платформы: {card.Descriptor.Key}@{card.SelectedVersion}");

        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                card.ProgressValue = p.Percent;
                card.Status = p.Message;
            });

            var platform = card.Descriptor.GetPlatform(card.SelectedVersion);
            await EnsureDependencyIndexesLoadedAsync(platform);
            var indexesForInstall = _knownIndexes.Concat(_dependencyIndexes).ToList();

            await _installer.InstallAsync(card.Descriptor, card.SelectedVersion, indexesForInstall, progress);

            var latest = card.Versions.FirstOrDefault() ?? card.SelectedVersion;
            var installed = _pathService.GetInstalledPlatformVersion(card.Descriptor.PackageName, card.Descriptor.Architecture);
            card.RefreshInstalledState(installed, latest);
            card.ProgressValue = 100;
            card.Status = "Установка завершена. Перезапусти Arduino IDE.";
            ReportStatus($"Установлена платформа {card.Descriptor.DisplayName}.");
            Log($"Платформа установлена: {card.Descriptor.Key}@{card.SelectedVersion}");
        }
        catch (Exception ex)
        {
            LogService.Error($"Install failed: {card.Descriptor.Key}@{card.SelectedVersion}", ex);
            card.Status = "Ошибка: " + ex.Message;
            ReportStatus("Ошибка установки платформы: " + ex.Message, writeLog: true);
            MessageBox.Show(ex.Message, "Ошибка установки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            card.IsBusy = false;
            IsLoading = false;
        }
    }

    private async Task RemoveCardAsync(PlatformCardViewModel card)
    {
        var hasInstalledVersion = !string.IsNullOrWhiteSpace(card.InstalledVersion);
        var hasLocalFiles = _pathService.HasAnyLocalFilesForDescriptor(card.Descriptor);
        var shouldDeleteLocalFiles = hasInstalledVersion || hasLocalFiles;
        var platformName = card.Descriptor.DisplayName;
        var message = shouldDeleteLocalFiles
            ? $"Удалить файлы платформы {platformName}?"
            : $"Удалить поддержку платформы {platformName}?\n\n" +
              "Если платформа содержит несколько плат, они тоже будут удалены из списка.";

        var result = MessageBox.Show(message, "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        card.IsBusy = true;
        card.Status = shouldDeleteLocalFiles ? "Удаляю локальные файлы платформы..." : "Удаляю ссылку и карточку...";
        ReportStatus(shouldDeleteLocalFiles ? $"Начато удаление платформы {card.Descriptor.DisplayName}..." : $"Удаляется поддержка платформы {card.Descriptor.DisplayName}...");
        Log(shouldDeleteLocalFiles
            ? $"Удаление файлов платформы: {card.Descriptor.Key}"
            : $"Удаление поддержки платформы: {card.Descriptor.Key}");

        try
        {
            if (shouldDeleteLocalFiles)
            {
                await _installer.DeletePlatformAsync(card.Descriptor, _knownIndexes);
                var latest = card.Versions.FirstOrDefault() ?? card.SelectedVersion ?? string.Empty;
                card.RefreshInstalledState(null, latest);
                card.ProgressValue = 0;
                card.Status = string.Empty;
                ReportStatus($"Удалена платформа {card.Descriptor.DisplayName}.");
                Log($"Файлы платформы удалены: {card.Descriptor.Key}");
            }
            else
            {
                await _settingsService.RemoveUrlAsync(card.Descriptor.SourceUrl);
                Log($"Поддержка платформы удалена: {card.Descriptor.Key}");
                await ReloadInternalAsync();
            }
        }
        catch (Exception ex)
        {
            card.Status = "Ошибка: " + ex.Message;
            ReportStatus("Ошибка удаления платформы: " + ex.Message, writeLog: true);
            MessageBox.Show(ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            card.IsBusy = false;
            IsLoading = false;
        }
    }
}
