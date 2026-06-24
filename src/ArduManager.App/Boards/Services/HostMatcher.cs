using System.Runtime.InteropServices;
using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public static class HostMatcher
{
    public static ToolSystem SelectBestSystem(IReadOnlyList<ToolSystem> systems)
    {
        if (systems.Count == 0)
            throw new InvalidOperationException("У tool нет systems[].");

        var hosts = GetPreferredHostFragments();

        foreach (var host in hosts)
        {
            var match = systems.FirstOrDefault(x => x.Host.Contains(host, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        var fallback = systems.FirstOrDefault(x => x.Host.Contains("mingw", StringComparison.OrdinalIgnoreCase))
            ?? systems.FirstOrDefault(x => x.Host.Contains("windows", StringComparison.OrdinalIgnoreCase));

        return fallback ?? throw new InvalidOperationException(
            $"Не нашёл подходящий tool archive для этой ОС. Доступные host: {string.Join(", ", systems.Select(x => x.Host))}");
    }

    private static IReadOnlyList<string> GetPreferredHostFragments()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture is Architecture.X64 or Architecture.Arm64
                ? new[] { "x86_64-mingw32", "amd64-mingw32", "i686-mingw32", "mingw32" }
                : new[] { "i686-mingw32", "x86_64-mingw32", "mingw32" };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture is Architecture.Arm64
                ? new[] { "arm64-apple-darwin", "aarch64-apple-darwin", "x86_64-apple-darwin", "apple-darwin" }
                : new[] { "x86_64-apple-darwin", "i386-apple-darwin", "apple-darwin" };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.OSArchitecture is Architecture.Arm64
                ? new[] { "aarch64-linux-gnu", "arm64-linux-gnu", "x86_64-pc-linux-gnu", "linux-gnu" }
                : new[] { "x86_64-pc-linux-gnu", "i686-pc-linux-gnu", "linux-gnu" };
        }

        return Array.Empty<string>();
    }
}
