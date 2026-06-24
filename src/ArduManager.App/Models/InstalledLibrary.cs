using ArduManager.Infrastructure;

namespace ArduManager.Models;

public sealed class InstalledLibrary : ObservableObject
{
    private string? _latestVersion;
    private bool _isChecking;
    private bool _hasUpdate;
    private string? _status;
    private string? _updateRepositoryFullName;

    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? PropertiesVersion { get; init; }
    public string? InstalledRef { get; init; }
    public string? Maintainer { get; init; }
    public string? Url { get; init; }
    public required string LocalPath { get; init; }
    public string? RepositoryFullName { get; init; }

    public string? UpdateRepositoryFullName
    {
        get => _updateRepositoryFullName;
        set
        {
            if (SetProperty(ref _updateRepositoryFullName, value))
                NotifyGitHubLinkChanged();
        }
    }

    public string? LatestVersion
    {
        get => _latestVersion;
        set
        {
            if (SetProperty(ref _latestVersion, value))
                OnPropertyChanged(nameof(VersionBadge));
        }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(VersionBadge));
                OnPropertyChanged(nameof(HasVisibleStatus));
            }
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        set
        {
            if (SetProperty(ref _isChecking, value))
                OnPropertyChanged(nameof(VersionBadge));
        }
    }

    public string? Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(VersionBadge));
                OnPropertyChanged(nameof(HasVisibleStatus));
            }
        }
    }

    public string VersionBadge
    {
        get
        {
            var version = string.IsNullOrWhiteSpace(Version) ? string.Empty : $" v{Version}";

            if (IsChecking)
                return string.IsNullOrWhiteSpace(version) ? " (проверка...)" : version + " (проверка...)";

            if (HasUpdate)
                return string.IsNullOrWhiteSpace(version) ? " (есть обновление)" : version + " (есть обновление)";

            if (IsActualStatus(Status))
                return string.IsNullOrWhiteSpace(version) ? " (актуальная)" : version + " (актуальная)";

            return version;
        }
    }

    public bool HasVisibleStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Status))
                return false;

            if (IsActualStatus(Status))
                return false;

            if (Status.StartsWith("Есть обновление", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }

    private static bool IsActualStatus(string? status)
    {
        return !string.IsNullOrWhiteSpace(status)
            && status.StartsWith("Актуально", StringComparison.OrdinalIgnoreCase);
    }

    public string? GitHubUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(UpdateRepositoryFullName))
                return BuildGitHubUrl(UpdateRepositoryFullName);

            if (!string.IsNullOrWhiteSpace(RepositoryFullName))
                return BuildGitHubUrl(RepositoryFullName);

            if (!string.IsNullOrWhiteSpace(Url) && Url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
                return Url;

            return null;
        }
    }

    public bool HasGitHubUrl => !string.IsNullOrWhiteSpace(GitHubUrl);

    public string GitHubLinkText
    {
        get
        {
            var url = GitHubUrl;
            if (string.IsNullOrWhiteSpace(url))
                return string.Empty;

            return url
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .TrimEnd('/');
        }
    }

    private static string? BuildGitHubUrl(string fullName)
    {
        var parts = fullName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? $"https://github.com/{parts[0]}/{parts[1]}" : null;
    }

    private void NotifyGitHubLinkChanged()
    {
        OnPropertyChanged(nameof(GitHubUrl));
        OnPropertyChanged(nameof(HasGitHubUrl));
        OnPropertyChanged(nameof(GitHubLinkText));
    }

}
