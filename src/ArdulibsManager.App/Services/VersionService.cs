using System.Text.RegularExpressions;
using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public static class VersionService
{
    private static readonly Regex DottedVersionRegex = new(
        @"(?<!\d)(\d+(?:\.\d+)+)(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrereleaseMarkerRegex = new(
        @"(^|[-_.+])(?:alpha|beta|rc\d*|pre|preview|dev|nightly|snapshot)([-_.+]|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<GithubTag> SortTags(IEnumerable<GithubTag> tags)
        => tags.Select(x => new { Tag = x, Version = ParseVersionOrNull(x.Name) })
               .OrderByDescending(x => x.Version is not null)
               .ThenByDescending(x => x.Version)
               .ThenByDescending(x => x.Tag.Name, StringComparer.OrdinalIgnoreCase)
               .Select(x => x.Tag)
               .ToList();

    public static bool IsNewer(string? candidate, string? current)
    {
        var a = ParseVersionOrNull(candidate);
        var b = ParseVersionOrNull(current);

        // Для автообновлений сравниваем только значения, из которых удалось выделить
        // числовую версию вида 1.2 / 1.2.3 / v1.2.3 / release-1.2.3.
        // Теги без dotted numeric version, например TM74HC595_Gyver, игнорируются.
        if (a is null || b is null) return false;

        return a.CompareTo(b) > 0;
    }

    public static bool IsSameVersion(string? a, string? b)
    {
        var va = ParseVersionOrNull(a);
        var vb = ParseVersionOrNull(b);
        if (va is not null && vb is not null) return va.CompareTo(vb) == 0;
        return NormalizeText(a).Equals(NormalizeText(b), StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeVersion(string? value) => ParseVersionOrNull(value) is not null;

    public static string? ExtractNormalizedVersion(string? value)
        => ParseVersionOrNull(value)?.ToString();

    private static string NormalizeText(string? value)
        => (value ?? string.Empty).Trim().TrimStart('v', 'V');

    private static ParsedDottedVersion? ParseVersionOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // По умолчанию не считаем alpha/beta/rc/dev/nightly latest-версией.
        // Это проще и безопаснее для Arduino-библиотек, где prerelease-теги часто не нужны.
        if (PrereleaseMarkerRegex.IsMatch(value))
            return null;

        var match = DottedVersionRegex.Match(value);
        if (!match.Success) return null;

        var parts = match.Groups[1].Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var number) ? number : -1)
            .ToArray();

        if (parts.Length < 2 || parts.Any(x => x < 0)) return null;

        return new ParsedDottedVersion(parts);
    }

    private sealed class ParsedDottedVersion : IComparable<ParsedDottedVersion>
    {
        public ParsedDottedVersion(IReadOnlyList<int> parts)
        {
            Parts = parts;
        }

        public IReadOnlyList<int> Parts { get; }

        public int CompareTo(ParsedDottedVersion? other)
        {
            if (other is null) return 1;

            var length = Math.Max(Parts.Count, other.Parts.Count);
            for (var i = 0; i < length; i++)
            {
                var left = i < Parts.Count ? Parts[i] : 0;
                var right = i < other.Parts.Count ? other.Parts[i] : 0;
                var result = left.CompareTo(right);
                if (result != 0) return result;
            }

            return 0;
        }

        public override string ToString() => string.Join('.', Parts);
    }
}
