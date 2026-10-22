using System.Text.RegularExpressions;

namespace CatSoftwareManager.Services;

public static partial class VersionComparer
{
    [GeneratedRegex(@"(?<!\d)(\d+(?:\.\d+){0,8})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NumericVersionRegex();

    public static bool TryCompare(string? left, string? right, out int result)
    {
        result = 0;
        var leftNumbers = ParseNumbers(left);
        var rightNumbers = ParseNumbers(right);
        if (leftNumbers.Length == 0 || rightNumbers.Length == 0)
        {
            return false;
        }

        var length = Math.Max(leftNumbers.Length, rightNumbers.Length);
        for (var index = 0; index < length; index++)
        {
            var leftPart = index < leftNumbers.Length ? leftNumbers[index] : "0";
            var rightPart = index < rightNumbers.Length ? rightNumbers[index] : "0";
            var partResult = CompareNumericString(leftPart, rightPart);
            if (partResult != 0)
            {
                result = partResult;
                return true;
            }
        }

        return true;
    }

    public static bool IsNewer(string? current, string? latest) =>
        TryCompare(current, latest, out var comparison) && comparison < 0;

    public static string? PickHighest(IEnumerable<string> versions)
    {
        string? highest = null;
        foreach (var version in versions.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (highest is null || IsNewer(highest, version))
            {
                highest = version;
            }
        }

        return highest;
    }

    private static string[] ParseNumbers(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            version.Equals("未知", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var match = NumericVersionRegex().Match(version);
        return match.Success
            ? match.Groups[1].Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            : [];
    }

    private static int CompareNumericString(string left, string right)
    {
        var normalizedLeft = left.TrimStart('0');
        var normalizedRight = right.TrimStart('0');
        normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
        normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;

        var lengthResult = normalizedLeft.Length.CompareTo(normalizedRight.Length);
        return lengthResult != 0
            ? lengthResult
            : string.CompareOrdinal(normalizedLeft, normalizedRight);
    }
}
