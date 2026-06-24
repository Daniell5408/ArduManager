using System.Collections.ObjectModel;
using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.ViewModels;

public sealed class PlatformCardViewModel : ObservableObject
{
    private string? _selectedVersion;
    private string? _installedVersion;
    private string _latestVersion;
    private bool _hasUpdate;
    private bool _isBusy;
    private double _progressValue;
    private string _status = string.Empty;

    public PlatformCardViewModel(
        PlatformDescriptor descriptor,
        IReadOnlyList<string> versions,
        string? installedVersion,
        string latestVersion,
        Func<PlatformCardViewModel, Task> install,
        Func<PlatformCardViewModel, Task> remove)
    {
        Descriptor = descriptor;
        Versions = new ObservableCollection<string>(versions);
        _installedVersion = installedVersion;
        _latestVersion = latestVersion;
        _hasUpdate = Services.VersionTextComparer.IsGreaterThan(latestVersion, installedVersion);
        _selectedVersion = latestVersion;

        InstallCommand = new AsyncRelayCommand(() => install(this), CanRunMainAction);
        RemoveCommand = new AsyncRelayCommand(() => remove(this), CanRunMainAction);
        Status = string.Empty;
    }

    public PlatformDescriptor Descriptor { get; }
    public ObservableCollection<string> Versions { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }

    public string Title => Descriptor.DisplayName;
    public string Subtitle => Descriptor.Key;
    public string? GitHubUrl => Descriptor.GitHubUrl;
    public bool HasGitHubUrl => !string.IsNullOrWhiteSpace(GitHubUrl);
    public string GitHubLinkText => FormatGitHubLinkText(GitHubUrl);

    public string VersionBadge
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstalledVersion))
                return "(не установлена)";

            return HasUpdate
                ? $"v{InstalledVersion} (есть обновление)"
                : $"v{InstalledVersion}";
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);

    public string? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
                InstallCommand.RaiseCanExecuteChanged();
        }
    }

    public string? InstalledVersion
    {
        get => _installedVersion;
        private set
        {
            if (SetProperty(ref _installedVersion, value))
            {
                OnPropertyChanged(nameof(CurrentVersionText));
                OnPropertyChanged(nameof(VersionBadge));
                InstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LatestVersion
    {
        get => _latestVersion;
        private set
        {
            if (SetProperty(ref _latestVersion, value))
            {
                OnPropertyChanged(nameof(CurrentVersionText));
                OnPropertyChanged(nameof(VersionBadge));
            }
        }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(CurrentVersionText));
                OnPropertyChanged(nameof(VersionBadge));
            }
        }
    }

    public string CurrentVersionText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstalledVersion))
                return "не установлена";

            return HasUpdate
                ? $"текущая: {InstalledVersion} (есть обновление: {LatestVersion})"
                : $"текущая: {InstalledVersion}";
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                InstallCommand.RaiseCanExecuteChanged();
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public void RefreshInstalledState(string? installedVersion, string latestVersion)
    {
        InstalledVersion = installedVersion;
        LatestVersion = latestVersion;
        HasUpdate = Services.VersionTextComparer.IsGreaterThan(latestVersion, installedVersion);
    }


    private static string FormatGitHubLinkText(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var text = uri.Host + uri.AbsolutePath.TrimEnd('/');
        return text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
    }

    private bool CanRunMainAction()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(SelectedVersion);
    }
}
