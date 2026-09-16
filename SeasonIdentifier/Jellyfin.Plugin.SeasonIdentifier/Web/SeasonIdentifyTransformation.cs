using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.SeasonIdentifier.Web;

/// <summary>
/// Minimal patch for jellyfin-web's itemHelper.canIdentify function.
/// </summary>
public static class SeasonIdentifyTransformation
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex StringFirstPattern = new(
        "(?<prefix>[\"']Movie[\"']\\s*===\\s*(?<v>[A-Za-z_$][\\w$]*)\\s*\\|\\|\\s*[\"']Trailer[\"']\\s*===\\s*\\k<v>\\s*\\|\\|\\s*[\"']Series[\"']\\s*===\\s*\\k<v>\\s*\\|\\|)(?!\\s*[\"']Season[\"']\\s*===\\s*\\k<v>)",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex VariableFirstPattern = new(
        "(?<prefix>(?<v>[A-Za-z_$][\\w$]*)\\s*===\\s*[\"']Movie[\"']\\s*\\|\\|\\s*\\k<v>\\s*===\\s*[\"']Trailer[\"']\\s*\\|\\|\\s*\\k<v>\\s*===\\s*[\"']Series[\"']\\s*\\|\\|)(?!\\s*\\k<v>\\s*===\\s*[\"']Season[\"'])",
        RegexOptions.Compiled,
        RegexTimeout);

    /// <summary>
    /// Payload received from File Transformation.
    /// </summary>
    public sealed class PatchRequestPayload
    {
        public string Contents { get; set; } = string.Empty;
    }

    /// <summary>
    /// Determines whether this JavaScript bundle contains Jellyfin's current canIdentify condition.
    /// </summary>
    public static bool CanPatch(string contents)
    {
        if (string.IsNullOrEmpty(contents)
            || !contents.Contains("MusicVideo", StringComparison.Ordinal)
            || !contents.Contains("MusicArtist", StringComparison.Ordinal))
        {
            return false;
        }

        return StringFirstPattern.IsMatch(contents) || VariableFirstPattern.IsMatch(contents);
    }

    /// <summary>
    /// Adds Season to Jellyfin's existing Identify eligibility check and changes nothing else.
    /// </summary>
    public static string Patch(PatchRequestPayload payload)
    {
        var contents = payload?.Contents ?? string.Empty;

        var stringFirstMatch = StringFirstPattern.Match(contents);
        if (stringFirstMatch.Success)
        {
            var variable = stringFirstMatch.Groups["v"].Value;
            return StringFirstPattern.Replace(
                contents,
                match => match.Groups["prefix"].Value + "\"Season\"===" + variable + "||",
                1);
        }

        var variableFirstMatch = VariableFirstPattern.Match(contents);
        if (variableFirstMatch.Success)
        {
            var variable = variableFirstMatch.Groups["v"].Value;
            return VariableFirstPattern.Replace(
                contents,
                match => match.Groups["prefix"].Value + variable + "===\"Season\"||",
                1);
        }

        return contents;
    }
}
