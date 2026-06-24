using System.Net.Http;
using System.Text.Json;
using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public sealed class PackageIndexService
{
    private static readonly TimeSpan ResponseHeaderTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<PackageIndexDocument> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        LogService.Info($"Downloading package index: {url}");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Нужна корректная http/https-ссылка на package index JSON.");

        using var response = await GetResponseWithTimeoutAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var bytes = await ReadResponseWithIdleTimeoutAsync(response, uri.ToString(), cancellationToken);
        var index = JsonSerializer.Deserialize<PackageIndex>(bytes, JsonOptions);

        if (index?.Packages is null || index.Packages.Count == 0)
            throw new InvalidOperationException("JSON скачан, но в нём не найдены packages[].");

        LogService.Info($"Package index loaded: {url}. Packages: {index.Packages.Count}");

        return new PackageIndexDocument
        {
            SourceUrl = url,
            Index = index
        };
    }

    private async Task<HttpResponseMessage> GetResponseWithTimeoutAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResponseHeaderTimeout);

        try
        {
            return await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Сервер не ответил за {FormatTimeout(ResponseHeaderTimeout)}: {uri}");
        }
    }

    private static async Task<byte[]> ReadResponseWithIdleTimeoutAsync(HttpResponseMessage response, string url, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 64];
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new MemoryStream();

        while (true)
        {
            var readTask = input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).AsTask();
            var timeoutTask = Task.Delay(DownloadIdleTimeout, cancellationToken);
            var completed = await Task.WhenAny(readTask, timeoutTask);

            if (completed != readTask)
            {
                if (cancellationToken.IsCancellationRequested)
                    cancellationToken.ThrowIfCancellationRequested();

                throw new TimeoutException($"Загрузка index JSON остановилась: за {FormatTimeout(DownloadIdleTimeout)} не получено новых данных. URL: {url}");
            }

            var read = await readTask;
            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static string FormatTimeout(TimeSpan value)
    {
        return value.TotalSeconds >= 60
            ? $"{value.TotalMinutes:0.#} мин"
            : $"{value.TotalSeconds:0} сек";
    }
}
