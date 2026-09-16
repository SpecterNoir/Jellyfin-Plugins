using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SeasonIdentifier.Web;

/// <summary>
/// Patches Jellyfin Web's canIdentify eligibility expression so Season items use the native Identify flow.
/// The matcher intentionally does not depend on minified variable names or on the exact order of item types.
/// </summary>
public static class SeasonIdentifyTransformation
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex StringFirstSeriesPattern = new(
        "[\\\"']Series[\\\"']\\s*===\\s*(?<v>[A-Za-z_$][\\w$]*)",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex VariableFirstSeriesPattern = new(
        "(?<v>[A-Za-z_$][\\w$]*)\\s*===\\s*[\\\"']Series[\\\"']",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly string[] CanIdentifyMarkers =
    [
        "Movie",
        "Trailer",
        "BoxSet",
        "Person",
        "Book",
        "MusicAlbum",
        "MusicArtist",
        "MusicVideo",
        "IsAdministrator"
    ];

    /// <summary>
    /// Determines whether a JavaScript bundle contains Jellyfin's canIdentify eligibility expression.
    /// </summary>
    public static bool CanPatch(string contents)
        => TryFindPatch(contents, out _, out _, out _);

    /// <summary>
    /// Adds Season beside Series in Jellyfin's native Identify eligibility expression.
    /// </summary>
    public static string Patch(string contents)
    {
        if (!TryFindPatch(contents, out var insertAt, out var variable, out var stringFirst))
        {
            return contents;
        }

        var addition = stringFirst
            ? $"||\"Season\"==={variable}"
            : $"||{variable}===\"Season\"";

        return contents.Insert(insertAt, addition);
    }

    private static bool TryFindPatch(
        string contents,
        out int insertAt,
        out string variable,
        out bool stringFirst)
    {
        insertAt = -1;
        variable = string.Empty;
        stringFirst = false;

        if (string.IsNullOrWhiteSpace(contents))
        {
            return false;
        }

        if (TryFindCandidate(contents, StringFirstSeriesPattern, true, out insertAt, out variable))
        {
            stringFirst = true;
            return true;
        }

        if (TryFindCandidate(contents, VariableFirstSeriesPattern, false, out insertAt, out variable))
        {
            stringFirst = false;
            return true;
        }

        return false;
    }

    private static bool TryFindCandidate(
        string contents,
        Regex pattern,
        bool stringFirst,
        out int insertAt,
        out string variable)
    {
        insertAt = -1;
        variable = string.Empty;

        foreach (Match match in pattern.Matches(contents))
        {
            var candidateVariable = match.Groups["v"].Value;
            if (string.IsNullOrWhiteSpace(candidateVariable))
            {
                continue;
            }

            // canIdentify contains a distinctive cluster of item type literals plus the administrator check.
            // Looking at a bounded window prevents an unrelated "Series" comparison elsewhere in the bundle
            // from being patched.
            var windowStart = Math.Max(0, match.Index - 900);
            var windowEnd = Math.Min(contents.Length, match.Index + match.Length + 1400);
            var window = contents.AsSpan(windowStart, windowEnd - windowStart);

            var looksLikeCanIdentify = true;
            foreach (var marker in CanIdentifyMarkers)
            {
                if (!window.Contains(marker, StringComparison.Ordinal))
                {
                    looksLikeCanIdentify = false;
                    break;
                }
            }

            if (!looksLikeCanIdentify)
            {
                continue;
            }

            var seasonStringFirst = $"\"Season\"==={candidateVariable}";
            var seasonVariableFirst = $"{candidateVariable}===\"Season\"";
            if (window.Contains(seasonStringFirst, StringComparison.Ordinal)
                || window.Contains(seasonVariableFirst, StringComparison.Ordinal)
                || window.Contains($"'Season'==={candidateVariable}", StringComparison.Ordinal)
                || window.Contains($"{candidateVariable}==='Season'", StringComparison.Ordinal))
            {
                return false;
            }

            insertAt = match.Index + match.Length;
            variable = candidateVariable;
            _ = stringFirst;
            return true;
        }

        return false;
    }
}
