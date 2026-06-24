using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using ArduManager.Infrastructure;
using ArduManager.Models;
using ArduManager.Services;
using Microsoft.Win32;

namespace ArduManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly RepositoryRegistryService _registry;
    private readonly GithubService _github;
    private readonly LibraryScannerService _scanner;
    private readonly LibraryInstallService _installer;
    private readonly DependencyResolverService _dependencyResolver;
    private readonly SemaphoreSlim _libraryOperationLock = new(1, 1);

    private IReadOnlyList<GithubRepository> _repositories = Array.Empty<GithubRepository>();
    private string _searchText = string.Empty;
    private string _status = "Готово";
    private bool _isBusy;
    private string _librariesPath = string.Empty;
    private string _repositoryListUrl = AppSettings.DefaultRepositoryListUrl;
    private string? _githubToken;
    private bool _checkUpdatesOnStartup = true;
    private readonly string _arduinoDataDirectory = new ArduboardsManager.App.Services.ArduinoPathService().DataDirectory;

    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<InstalledLibrary> InstalledLibraries { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync();
        }
    }

    public string LibrariesPath
    {
        get => _librariesPath;
        set => SetProperty(ref _librariesPath, value);
    }

    public string RepositoryListUrl
    {
        get => _repositoryListUrl;
        set => SetProperty(ref _repositoryListUrl, value);
    }

    public string? GitHubToken
    {
        get => _githubToken;
        set => SetProperty(ref _githubToken, value);
    }

    public bool CheckUpdatesOnStartup
    {
        get => _checkUpdatesOnStartup;
        set => SetProperty(ref _checkUpdatesOnStartup, value);
    }

    public string ArduinoDataDirectory => _arduinoDataDirectory;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public AsyncRelayCommand InitializeCommand { get; }
    public AsyncRelayCommand RefreshRegistryCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand LoadVersionsCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand UpdateAllCommand { get; }
    public RelayCommand BrowseLibrariesCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenLibraryGithubCommand { get; }
    public RelayCommand OpenArduinoDataDirectoryCommand { get; }

    public MainViewModel()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _settings = new SettingsService();
        var cache = new CacheService();
        _registry = new RepositoryRegistryService(http, cache, _settings);
        _github = new GithubService(http, _settings);
        _scanner = new LibraryScannerService();
        _installer = new LibraryInstallService(_github, _settings);
        _dependencyResolver = new DependencyResolverService(_github);

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshRegistryCommand = new AsyncRelayCommand(RefreshRegistryFromSettingsAsync);
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        RescanCommand = new AsyncRelayCommand(RefreshInstalledListAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        InstallCommand = new AsyncRelayCommand(async p => await InstallSelectedAsync(p as SearchResult));
        LoadVersionsCommand = new AsyncRelayCommand(async p => await LoadTagsForResultAsync(p as SearchResult));
        RemoveCommand = new AsyncRelayCommand(async p => await RemoveAsync(p as InstalledLibrary));
        UpdateCommand = new AsyncRelayCommand(async p => await UpdateAsync(p as InstalledLibrary));
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllWithUpdatesAsync);
        BrowseLibrariesCommand = new RelayCommand(BrowseLibraries);
        OpenFolderCommand = new RelayCommand(p => OpenFolder((p as InstalledLibrary)?.LocalPath ?? LibrariesPath));
        OpenLibraryGithubCommand = new RelayCommand(p => OpenLibraryGithub(p as InstalledLibrary));
        OpenArduinoDataDirectoryCommand = new RelayCommand(_ => OpenFolder(ArduinoDataDirectory));
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settings.LoadAsync();
            LibrariesPath = settings.LibrariesPath;
            RepositoryListUrl = string.IsNullOrWhiteSpace(settings.RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : settings.RepositoryListUrl;
            GitHubToken = settings.GitHubToken;
            CheckUpdatesOnStartup = settings.CheckUpdatesOnStartup;
            await LoadRegistryAsync(force: false);
            await ScanInstalledAsync();
            if (CheckUpdatesOnStartup)
                _ = CheckUpdatesAsync(showPopup: true);
        }
        catch (Exception ex)
        {
            Log("Ошибка запуска: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshRegistryFromSettingsAsync()
    {
        var settings = _settings.Current;
        settings.LibrariesPath = LibrariesPath;
        settings.RepositoryListUrl = string.IsNullOrWhiteSpace(RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : RepositoryListUrl.Trim();
        settings.GitHubToken = GitHubToken;
        settings.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        await _settings.SaveAsync(settings);
        await LoadRegistryAsync(force: true);
    }

    private async Task LoadRegistryAsync(bool force)
    {
        IsBusy = true;
        try
        {
            Status = "Загрузка списка репозиториев...";
            _repositories = await _registry.LoadRepositoriesAsync(force);
            Status = $"Загружено репозиториев: {_repositories.Count}";
            Log(Status);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            Status = "Ошибка загрузки registry";
            Log(ex.Message);
        }
        finally { IsBusy = false; }
    }

    private async Task SearchAsync()
    {
        var q = SearchText.Trim();
        SearchResults.Clear();
        if (q.Length < 3) return;

        var matches = _repositories
            .Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .Select(r => new SearchResult { Repository = r })
            .ToList();

        foreach (var item in matches)
        {
            item.RefreshInstallState(FindInstalled(item.Repository));
            SearchResults.Add(item);
        }

        // Важно для лимитов GitHub API: поиск работает только по локальному registry-кешу.
        // Теги/версии загружаются лениво — только когда пользователь открывает dropdown версии
        // или нажимает установку. Если dropdown не открывали, установка берёт первый GitHub tag как latest.
        await Task.CompletedTask;
    }

    private async Task LoadTagsForResultAsync(SearchResult? result)
    {
        if (result is null) return;
        if (result.TagsLoaded || result.IsLoadingTags) return;

        result.IsLoadingTags = true;
        result.Status = "Загрузка версий...";
        try
        {
            var tags = await _github.GetTagsAsync(result.Repository);
            App.Current.Dispatcher.Invoke(() =>
            {
                result.Tags.Clear();
                foreach (var tag in tags.Take(80)) result.Tags.Add(tag);
                result.SelectedTag = result.Tags.FirstOrDefault();
                result.TagsLoaded = true;
                result.Status = result.Tags.Count == 0 ? "Теги не найдены" : null;
            });
        }
        catch (Exception ex)
        {
            result.Status = "Ошибка тегов: " + ex.Message;
        }
        finally { result.IsLoadingTags = false; }
    }

    private async Task RefreshInstalledListAsync()
    {
        await ScanInstalledAsync();
        await CheckUpdatesAsync();
    }

    private async Task ScanInstalledAsync()
    {
        Status = "Сканирование установленных библиотек...";
        InstalledLibraries.Clear();
        var libs = await _scanner.ScanAsync(LibrariesPath);
        foreach (var lib in libs) InstalledLibraries.Add(lib);
        Status = $"Установлено библиотек: {InstalledLibraries.Count}";
        foreach (var sr in SearchResults) sr.RefreshInstallState(FindInstalled(sr.Repository));
        Log(Status);
    }

    private async Task CheckUpdatesAsync(bool showPopup = false)
    {
        var updated = new List<InstalledLibrary>();

        foreach (var lib in InstalledLibraries.ToList())
        {
            lib.IsChecking = true;
            try
            {
                var result = await ResolveUpdateCandidateAsync(lib);
                lib.UpdateRepositoryFullName = result.Repository?.FullName;
                lib.LatestVersion = result.LatestTag;
                lib.HasUpdate = result.HasUpdate;
                lib.Status = result.Status;
                if (lib.HasUpdate) updated.Add(lib);
            }
            catch (Exception ex)
            {
                lib.UpdateRepositoryFullName = null;
                lib.LatestVersion = null;
                lib.HasUpdate = false;
                lib.Status = "Не удалось проверить: " + ex.Message;
            }
            finally { lib.IsChecking = false; }
        }

        if (showPopup && updated.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Есть обновления для библиотек:");
            sb.AppendLine();
            foreach (var lib in updated.OrderBy(x => x.Name))
            {
                var source = string.IsNullOrWhiteSpace(lib.UpdateRepositoryFullName) ? string.Empty : $" ({lib.UpdateRepositoryFullName})";
                sb.AppendLine($"• {lib.Name} {lib.Version} → {lib.LatestVersion}{source}");
            }
            MessageBox.Show(sb.ToString(), "Доступны обновления", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private InstalledLibrary? FindInstalled(GithubRepository repo)
    {
        return InstalledLibraries.FirstOrDefault(x =>
            x.RepositoryFullName?.Equals(repo.FullName, StringComparison.OrdinalIgnoreCase) == true)
            ?? InstalledLibraries.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.RepositoryFullName) && NamesMatch(x.Name, repo.Name));
    }

    private sealed record UpdateCandidateResult(GithubRepository? Repository, string? LatestTag, bool HasUpdate, string Status);

    private static string? GetInstalledComparableVersion(InstalledLibrary lib)
    {
        // Если библиотека установлена/обновлена через ArduManager, сравниваем обновления
        // с выбранным GitHub ref из .ardulibs.json, а не только с version из library.properties.
        // Некоторые библиотеки публикуют tag 1.1.2, но забывают обновить library.properties version.
        return string.IsNullOrWhiteSpace(lib.InstalledRef) ? lib.Version : lib.InstalledRef;
    }

    private async Task<UpdateCandidateResult> ResolveUpdateCandidateAsync(InstalledLibrary lib)
    {
        if (string.IsNullOrWhiteSpace(lib.Name))
            return new UpdateCandidateResult(null, null, false, "Нет имени библиотеки");

        // Если библиотека установлена ArduManager, обновляем строго по сохранённому repo
        // из .ardulibs.json и не ищем совпадения в registry.
        if (!string.IsNullOrWhiteSpace(lib.RepositoryFullName))
        {
            var managedRepo = ResolveRepositoryByFullName(lib.RepositoryFullName);
            if (managedRepo is null)
                return new UpdateCandidateResult(null, null, false, "Некорректный repo в .ardulibs.json");

            var installedRef = lib.InstalledRef;
            var propertiesVersion = lib.PropertiesVersion;
            var hasMetadataMismatch = !string.IsNullOrWhiteSpace(installedRef)
                && !string.IsNullOrWhiteSpace(propertiesVersion)
                && !VersionService.IsSameVersion(installedRef, propertiesVersion);

            var latest = await _github.GetLatestTagNameAsync(managedRepo);
            var currentVersion = string.IsNullOrWhiteSpace(installedRef) ? lib.Version : installedRef;
            var hasUpdate = VersionService.IsNewer(latest, currentVersion);

            if (hasUpdate)
                return CandidateToResult(managedRepo, latest, true, $"Есть обновление из {managedRepo.FullName}");

            if (hasMetadataMismatch && !string.IsNullOrWhiteSpace(installedRef))
            {
                // .ardulibs.json говорит, что должен быть установлен ref X, но
                // library.properties показывает другую версию. Это считаем
                // неконсистентной установкой: кнопка «Обновить» должна не
                // просто править файл, а заново скачать repo/ref из GitHub и
                // заменить папку библиотеки.
                return new UpdateCandidateResult(
                    managedRepo,
                    installedRef,
                    true,
                    $"Требуется переустановка {installedRef} из {managedRepo.FullName}");
            }

            return CandidateToResult(managedRepo, latest, false, $"Актуально ({managedRepo.FullName})");
        }

        // Для ручных библиотек без .ardulibs.json используем fallback:
        // ищем только репозитории из registry, у которых repo name строго совпадает
        // с library.properties name после нормализации.
        var candidates = _repositories.Where(r => NamesMatch(lib.Name, r.Name)).ToList();
        if (candidates.Count == 0)
            return new UpdateCandidateResult(null, null, false, "Не найдено точное совпадение name/repo");

        var checkedCandidates = new List<(GithubRepository Repo, string? Latest, bool HasUpdate)>();
        foreach (var repo in candidates)
        {
            var currentVersion = GetInstalledComparableVersion(lib);
            var latest = await _github.GetLatestTagNameAsync(repo);
            var hasUpdate = VersionService.IsNewer(latest, currentVersion);
            checkedCandidates.Add((repo, latest, hasUpdate));
        }

        if (candidates.Count == 1)
        {
            var only = checkedCandidates[0];
            return CandidateToResult(only.Repo, only.Latest, only.HasUpdate);
        }

        // Если repo name не уникален, не берём первый попавшийся репозиторий.
        // Автоматически выбираем только ситуацию, когда ровно один кандидат реально новее локальной версии.
        var newer = checkedCandidates.Where(x => x.HasUpdate).ToList();
        if (newer.Count == 1)
            return CandidateToResult(newer[0].Repo, newer[0].Latest, true, $"Есть обновление из {newer[0].Repo.FullName}");

        if (newer.Count > 1)
            return new UpdateCandidateResult(null, null, false, "Несколько repo с таким именем имеют обновления");

        var currentLike = checkedCandidates.FirstOrDefault(x => VersionService.IsSameVersion(x.Latest, GetInstalledComparableVersion(lib)));
        if (currentLike.Repo is not null)
            return new UpdateCandidateResult(currentLike.Repo, currentLike.Latest, false, $"Актуально ({currentLike.Repo.FullName})");

        return new UpdateCandidateResult(null, null, false, "Несколько repo с таким именем, обновление не выбрано");
    }

    private static UpdateCandidateResult CandidateToResult(GithubRepository repo, string? latest, bool hasUpdate, string? overrideStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideStatus))
            return new UpdateCandidateResult(repo, latest, hasUpdate, overrideStatus);
        if (string.IsNullOrWhiteSpace(latest))
            return new UpdateCandidateResult(repo, latest, false, "Теги не найдены");
        if (!VersionService.LooksLikeVersion(latest))
            return new UpdateCandidateResult(repo, latest, false, "Теги не похожи на версии");
        return new UpdateCandidateResult(repo, latest, hasUpdate, hasUpdate ? "Есть обновление" : "Актуально");
    }

    private GithubRepository? ResolveRepositoryForInstalledLibrary(InstalledLibrary lib)
    {
        if (!string.IsNullOrWhiteSpace(lib.UpdateRepositoryFullName))
            return ResolveRepositoryByFullName(lib.UpdateRepositoryFullName);

        if (!string.IsNullOrWhiteSpace(lib.RepositoryFullName))
            return ResolveRepositoryByFullName(lib.RepositoryFullName);

        var candidates = _repositories.Where(r => NamesMatch(lib.Name, r.Name)).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private GithubRepository? ResolveRepositoryByFullName(string fullName)
    {
        var repo = _repositories.FirstOrDefault(r => r.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
        if (repo is not null) return repo;

        var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return null;

        return new GithubRepository
        {
            Owner = parts[0],
            Name = parts[1],
            Url = $"https://github.com/{parts[0]}/{parts[1]}"
        };
    }

    private static bool NamesMatch(string? libraryName, string? repositoryName)
    {
        return NormalizeName(libraryName) == NormalizeName(repositoryName);
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var chars = value
            .Trim()
            .Where(ch => ch != ' ' && ch != '_' && ch != '-')
            .Select(char.ToLowerInvariant);
        return new string(chars.ToArray());
    }

    private async Task InstallSelectedAsync(SearchResult? result)
    {
        if (result is null) return;
        if (!result.TagsLoaded)
            await LoadTagsForResultAsync(result);
        if (result.SelectedTag is null)
        {
            Log("Установка отменена: версии не найдены для " + result.Repository.FullName);
            return;
        }

        if (!await TryEnterLibraryOperationAsync("Установка уже выполняется. Дождитесь окончания текущей операции."))
            return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(Log);
            var dependencyPlan = await _dependencyResolver.BuildInstallPlanAsync(result.Repository, _repositories, InstalledLibraries.ToList());
            if (dependencyPlan.Count > 0)
            {
                var confirm = ConfirmDependencies(dependencyPlan);
                if (confirm != MessageBoxResult.Yes)
                {
                    Log("Установка отменена пользователем: зависимости не подтверждены");
                    return;
                }

                await InstallDependencyPlanAsync(dependencyPlan, progress);
            }

            await _installer.InstallAsync(result.Repository, result.SelectedTag.Name, LibrariesPath, progress, result.InstalledLibrary?.LocalPath);
            await ScanInstalledAsync();
            _ = CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка установки: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка установки", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            LeaveLibraryOperation();
        }
    }

    private MessageBoxResult ConfirmDependencies(IReadOnlyList<DependencyPlanItem> dependencyPlan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Для работы библиотеки требуются зависимости:");
        sb.AppendLine();
        foreach (var item in dependencyPlan)
            sb.AppendLine("• " + item.DisplayText);
        sb.AppendLine();
        sb.AppendLine("Установить/обновить зависимости до latest и продолжить?");

        return MessageBox.Show(sb.ToString(), "Зависимости", MessageBoxButton.YesNo, MessageBoxImage.Information);
    }

    private async Task InstallDependencyPlanAsync(IReadOnlyList<DependencyPlanItem> dependencyPlan, IProgress<string> progress)
    {
        foreach (var item in dependencyPlan)
        {
            progress.Report($"Зависимость: {item.Action} {item.Repository.FullName} {item.LatestTag}");
            await _installer.InstallAsync(
                item.Repository,
                item.LatestTag,
                LibrariesPath,
                progress,
                item.InstalledLibrary?.LocalPath);
        }
    }

    private async Task UpdateAsync(InstalledLibrary? lib)
    {
        if (lib is null) return;

        // Не полагаемся только на значения, сохранённые во время фоновой проверки.
        // Для ручных библиотек без .ardulibs.json кандидат может быть найден по fallback-правилу
        // name == repo name. Перед обновлением пересчитываем его заново и берём конкретный repo + tag.
        UpdateCandidateResult candidate;
        try
        {
            candidate = await ResolveUpdateCandidateAsync(lib);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Обновление недоступно: " + ex.Message, "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var repo = candidate.Repository;
        var tag = candidate.LatestTag;
        if (repo is null || string.IsNullOrWhiteSpace(tag))
        {
            MessageBox.Show("Обновление недоступно: " + candidate.Status, "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!candidate.HasUpdate && VersionService.IsSameVersion(tag, lib.Version))
        {
            // Если раньше metadata уже была обновлена до tag, но library.properties остался
            // со старой версией, разрешаем повторную установку того же ref как repair.
            // Это чинит репозитории, где tag новый, а version= в library.properties забыли поменять.
            if (string.IsNullOrWhiteSpace(lib.PropertiesVersion) || VersionService.IsSameVersion(lib.PropertiesVersion, lib.Version))
            {
                MessageBox.Show("Библиотека уже актуальна.", "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            tag = lib.Version;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            Log($"Не удалось определить версию для обновления {lib.Name}");
            MessageBox.Show("Не удалось определить версию для обновления.", "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!await TryEnterLibraryOperationAsync("Обновление уже выполняется. Дождитесь окончания текущей операции."))
            return;

        IsBusy = true;
        try
        {
            Log($"Обновление библиотеки: {lib.Name} {lib.Version} -> {tag} ({repo.FullName})");
            await _installer.InstallAsync(repo, tag, LibrariesPath, new Progress<string>(Log), lib.LocalPath);
            await ScanInstalledAsync();
            _ = CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка обновления: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            LeaveLibraryOperation();
        }
    }

    private async Task UpdateAllWithUpdatesAsync()
    {
        var libs = InstalledLibraries.Where(x => x.HasUpdate && ResolveRepositoryForInstalledLibrary(x) is not null).ToList();
        if (libs.Count == 0)
        {
            MessageBox.Show("Библиотек с обновлениями не найдено.", "Обновление библиотек", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Обновить библиотеки с доступными обновлениями?\nКоличество: {libs.Count}", "Обновление библиотек", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        if (!await TryEnterLibraryOperationAsync("Обновление уже выполняется. Дождитесь окончания текущей операции."))
            return;

        IsBusy = true;
        try
        {
            foreach (var lib in libs)
            {
                var repo = ResolveRepositoryForInstalledLibrary(lib);
                if (repo is null)
                {
                    Log("Пропущено: нет точного совпадения name/repo для " + lib.Name);
                    continue;
                }

                var tag = lib.LatestVersion ?? await _github.GetLatestTagNameAsync(repo);
                if (string.IsNullOrWhiteSpace(tag))
                {
                    Log("Пропущено: не найдена свежая версия для " + lib.Name);
                    continue;
                }

                Log($"Обновление библиотеки: {lib.Name} -> {tag}");
                await _installer.InstallAsync(repo, tag, LibrariesPath, new Progress<string>(Log), lib.LocalPath);
            }

            await ScanInstalledAsync();
            await CheckUpdatesAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка массового обновления: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка массового обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            LeaveLibraryOperation();
        }
    }

    private async Task RemoveAsync(InstalledLibrary? lib)
    {
        if (lib is null) return;
        var confirm = MessageBox.Show($"Удалить библиотеку '{lib.Name}'?\n{lib.LocalPath}", "Удаление", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        if (!await TryEnterLibraryOperationAsync("Операция с библиотеками уже выполняется. Дождитесь окончания текущей операции."))
            return;

        IsBusy = true;
        try
        {
            await _installer.RemoveAsync(lib);
            Log("Удалено: " + lib.Name);
            await ScanInstalledAsync();
        }
        catch (Exception ex)
        {
            Log("Ошибка удаления: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            LeaveLibraryOperation();
        }
    }

    private async Task SaveSettingsAsync()
    {
        var settings = _settings.Current;
        settings.LibrariesPath = LibrariesPath;
        settings.RepositoryListUrl = string.IsNullOrWhiteSpace(RepositoryListUrl) ? AppSettings.DefaultRepositoryListUrl : RepositoryListUrl.Trim();
        settings.GitHubToken = GitHubToken;
        settings.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        await _settings.SaveAsync(settings);
        Log("Настройки сохранены");
        await ScanInstalledAsync();
    }

    private void BrowseLibraries()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку Arduino libraries",
            InitialDirectory = Directory.Exists(LibrariesPath) ? LibrariesPath : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog() == true)
            LibrariesPath = dialog.FolderName;
    }

    private async Task<bool> TryEnterLibraryOperationAsync(string busyMessage)
    {
        if (await _libraryOperationLock.WaitAsync(0))
            return true;

        Log(busyMessage);
        MessageBox.Show(busyMessage, "Операция уже выполняется", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void LeaveLibraryOperation()
    {
        try
        {
            _libraryOperationLock.Release();
        }
        catch (SemaphoreFullException)
        {
            // ignore double release protection
        }
    }

    public void SetPlatformStatus(string message, bool? isBusy = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Status = message;

        if (isBusy.HasValue)
            IsBusy = isBusy.Value;
    }

    public void AddPlatformLog(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Log("Платформы: " + message);
    }

    public void RestoreLibraryStatus()
    {
        if (_libraryOperationLock.CurrentCount > 0)
            IsBusy = false;

        Status = InstalledLibraries.Count > 0
            ? $"Установлено библиотек: {InstalledLibraries.Count}"
            : "Готово";
    }


    private void OpenLibraryGithub(InstalledLibrary? lib)
    {
        if (lib is null)
            return;

        var repo = ResolveRepositoryForInstalledLibrary(lib);
        var url = repo?.Url;

        if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(lib.Url))
            url = lib.Url;

        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Репозиторий GitHub для этой библиотеки не найден.", "GitHub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static void OpenFolder(string path)
    {
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void Log(string message)
    {
        App.Current.Dispatcher.Invoke(() => Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}"));
    }
}
