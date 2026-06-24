using System.Diagnostics;
using System.IO.Compression;

namespace ArduboardsManager.App.Services;

public sealed class ArchiveExtractor
{
    public async Task ExtractAsync(string archiveFile, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);

        var extension = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (extension == ".zip")
        {
            ZipFile.ExtractToDirectory(archiveFile, destinationDirectory, overwriteFiles: true);
            return;
        }

        await ExtractWithWindowsTarAsync(archiveFile, destinationDirectory, cancellationToken);
    }

    private static async Task ExtractWithWindowsTarAsync(string archiveFile, string destinationDirectory, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-xf");
        psi.ArgumentList.Add(archiveFile);
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(destinationDirectory);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить tar.exe для распаковки архива.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"tar.exe не смог распаковать архив '{archiveFile}'. Код: {process.ExitCode}. {details}");
        }
    }

    public static string FindContentRoot(string extractedDirectory, bool platformArchive)
    {
        if (platformArchive)
        {
            var platformFile = Directory.EnumerateFiles(extractedDirectory, "platform.txt", SearchOption.AllDirectories)
                .OrderBy(x => x.Length)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(platformFile))
                return Path.GetDirectoryName(platformFile)!;

            var boardsFile = Directory.EnumerateFiles(extractedDirectory, "boards.txt", SearchOption.AllDirectories)
                .OrderBy(x => x.Length)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(boardsFile))
                return Path.GetDirectoryName(boardsFile)!;
        }

        var topDirectories = Directory.EnumerateDirectories(extractedDirectory).ToList();
        var topFiles = Directory.EnumerateFiles(extractedDirectory).ToList();

        return topDirectories.Count == 1 && topFiles.Count == 0
            ? topDirectories[0]
            : extractedDirectory;
    }
}
