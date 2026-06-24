using System.Text.RegularExpressions;

namespace ArduboardsManager.App.Services;

public sealed class VersionTextComparer : IComparer<string>
{
    public static VersionTextComparer Instance { get; } = new();

    private VersionTextComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var xp = SplitVersion(x);
        var yp = SplitVersion(y);
        var count = Math.Max(xp.Count, yp.Count);

        for (var i = 0; i < count; i++)
        {
            var xs = i < xp.Count ? xp[i] : "0";
            var ys = i < yp.Count ? yp[i] : "0";

            var xIsNumber = long.TryParse(xs, out var xn);
            var yIsNumber = long.TryParse(ys, out var yn);

            int result;
            if (xIsNumber && yIsNumber)
                result = xn.CompareTo(yn);
            else
                result = string.Compare(xs, ys, StringComparison.OrdinalIgnoreCase);

            if (result != 0)
                return result;
        }

        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGreaterThan(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        if (string.IsNullOrWhiteSpace(right)) return true;
        return Instance.Compare(left, right) > 0;
    }

    private static List<string> SplitVersion(string version)
    {
        return Regex.Matches(version, @"\d+|[A-Za-z]+")
            .Select(x => x.Value)
            .ToList();
    }
}
