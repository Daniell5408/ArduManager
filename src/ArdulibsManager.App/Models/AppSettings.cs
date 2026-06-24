namespace ArdulibsManager.Models;

public sealed class AppSettings
{
    public const string DefaultRepositoryListUrl = "https://raw.githubusercontent.com/arduino/library-registry/refs/heads/main/repositories.txt";

    public string LibrariesPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Arduino",
        "libraries");

    public string RepositoryListUrl { get; set; } = DefaultRepositoryListUrl;
    public int CacheTtlHours { get; set; } = 24;
    public string? GitHubToken { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = true;
}
