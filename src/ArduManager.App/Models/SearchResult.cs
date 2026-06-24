using System.Collections.ObjectModel;
using ArduManager.Infrastructure;

namespace ArduManager.Models;

public sealed class SearchResult : ObservableObject
{
    private static readonly GithubTag LatestPlaceholder = new() { Name = "latest" };
    private ObservableCollection<GithubTag> _tags = new() { LatestPlaceholder };
    private GithubTag? _selectedTag = LatestPlaceholder;
    private bool _isLoadingTags;
    private bool _isInstalled;
    private bool _tagsLoaded;
    private InstalledLibrary? _installedLibrary;
    private string? _status;

    public required GithubRepository Repository { get; init; }
    public string Title => Repository.FullName;
    public string Url => Repository.Url;

    public string InstalledBadge
    {
        get
        {
            if (InstalledLibrary is null)
                return string.Empty;

            return string.IsNullOrWhiteSpace(InstalledLibrary.Version)
                ? " (установлена)"
                : $" (установлена v{InstalledLibrary.Version})";
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    public ObservableCollection<GithubTag> Tags
    {
        get => _tags;
        set => SetProperty(ref _tags, value);
    }

    public GithubTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (SetProperty(ref _selectedTag, value))
                OnPropertyChanged(nameof(SelectedVersionText));
        }
    }

    public string SelectedVersionText => SelectedTag?.DisplayName ?? "latest";

    public bool IsLoadingTags
    {
        get => _isLoadingTags;
        set => SetProperty(ref _isLoadingTags, value);
    }

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
                OnPropertyChanged(nameof(InstallButtonText));
        }
    }

    public bool TagsLoaded
    {
        get => _tagsLoaded;
        set => SetProperty(ref _tagsLoaded, value);
    }

    public InstalledLibrary? InstalledLibrary
    {
        get => _installedLibrary;
        set
        {
            if (SetProperty(ref _installedLibrary, value))
                OnPropertyChanged(nameof(InstalledBadge));
        }
    }

    public string InstallButtonText => IsInstalled ? "Изменить" : "Установить";

    public string? Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public void RefreshInstallState(InstalledLibrary? installed)
    {
        InstalledLibrary = installed;
        IsInstalled = installed is not null;
        OnPropertyChanged(nameof(InstallButtonText));
        OnPropertyChanged(nameof(InstalledBadge));
    }
}
