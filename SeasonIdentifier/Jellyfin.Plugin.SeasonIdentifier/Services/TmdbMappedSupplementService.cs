using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.SeasonIdentifier.Configuration;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SeasonIdentifier.Services;

/// <summary>
/// Supplies mapped-title artwork and a small metadata fallback directly from TMDb.
/// Jellyfin's normal episode image provider cannot be used for title mappings because it
/// deliberately reads the real parent Series provider id, which must remain the local series.
/// </summary>
public sealed class TmdbMappedSupplementService
{
    // Jellyfin's built-in TMDb provider uses this public application key as its default.
    // Keeping the same key means mapped lookups behave like Jellyfin's own TMDb provider.
    private const string TmdbApiKey = "4219e299c89411838049ab0dab19ebd5";
    private const string ApiBase = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p/original";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly IProviderManager _providerManager;
    private readonly ILogger<TmdbMappedSupplementService> _logger;

    public TmdbMappedSupplementService(
        IProviderManager providerManager,
        ILogger<TmdbMappedSupplementService> logger)
    {
        _providerManager = providerManager;
        _logger = logger;
    }

    public async Task<bool> ApplySeasonImagesAsync(
        Season target,
        SeasonMapping mapping,
        CancellationToken cancellationToken)
    {
        var changed = false;
        var posterSaved = false;

        if (TryGetTmdbId(mapping, out var tmdbId))
        {
            using var document = await GetJsonAsync(
                $"{ApiBase}/tv/{tmdbId}?api_key={TmdbApiKey}{BuildLanguageParameter(target.GetPreferredMetadataLanguage())}",
                cancellationToken).ConfigureAwait(false);

            if (document is not null)
            {
                var root = document.RootElement;

                if (TryGetPath(root, "poster_path", out var posterPath))
                {
                    posterSaved = await SaveImageAsync(
                        target,
                        ImageBase + posterPath,
                        ImageType.Primary,
                        null,
                        cancellationToken).ConfigureAwait(false);
                    changed |= posterSaved;
                }

                if (TryGetPath(root, "backdrop_path", out var backdropPath))
                {
                    changed |= await SaveImageAsync(
                        target,
                        ImageBase + backdropPath,
                        ImageType.Backdrop,
                        0,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // The native Identify result already contains a representative title poster. Keep it as
        // a provider-agnostic fallback when TMDb is unavailable or the selected provider is not TMDb.
        if (!posterSaved && !string.IsNullOrWhiteSpace(mapping.ImageUrl))
        {
            changed |= await SaveImageAsync(
                target,
                mapping.ImageUrl,
                ImageType.Primary,
                null,
                cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    public async Task<TmdbEpisodeSupplement?> GetEpisodeSupplementAsync(
        SeasonMapping mapping,
        int externalSeason,
        int externalEpisode,
        Episode localEpisode,
        CancellationToken cancellationToken)
    {
        if (!TryGetTmdbId(mapping, out var tmdbId))
        {
            return null;
        }

        using var document = await GetJsonAsync(
            $"{ApiBase}/tv/{tmdbId}/season/{externalSeason}/episode/{externalEpisode}?api_key={TmdbApiKey}{BuildLanguageParameter(localEpisode.GetPreferredMetadataLanguage())}",
            cancellationToken).ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var supplement = new TmdbEpisodeSupplement
        {
            Name = GetString(root, "name"),
            Overview = GetString(root, "overview"),
            StillUrl = TryGetPath(root, "still_path", out var stillPath) ? ImageBase + stillPath : null
        };

        var airDate = GetString(root, "air_date");
        if (!string.IsNullOrWhiteSpace(airDate)
            && DateTime.TryParseExact(
                airDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            supplement.PremiereDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Local).ToUniversalTime();
            supplement.ProductionYear = parsedDate.Year;
        }

        if (root.TryGetProperty("vote_average", out var vote)
            && vote.ValueKind == JsonValueKind.Number
            && vote.TryGetSingle(out var rating))
        {
            supplement.CommunityRating = rating;
        }

        return supplement;
    }

    public Task<bool> ApplyEpisodeImageAsync(
        Episode target,
        TmdbEpisodeSupplement supplement,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(supplement.StillUrl))
        {
            return Task.FromResult(false);
        }

        return SaveImageAsync(
            target,
            supplement.StillUrl,
            ImageType.Primary,
            null,
            cancellationToken);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Jellyfin-SeasonIdentifier/0.4");
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Mapped TMDb lookup returned {StatusCode} for {Url}.",
                    (int)response.StatusCode,
                    url);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mapped TMDb supplemental lookup failed.");
            return null;
        }
    }

    private async Task<bool> SaveImageAsync(
        MediaBrowser.Controller.Entities.BaseItem target,
        string url,
        ImageType type,
        int? index,
        CancellationToken cancellationToken)
    {
        try
        {
            await _providerManager.SaveImage(target, url, type, index, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not save mapped {ImageType} image for {ItemName} from {Url}.",
                type,
                target.Name,
                url);
            return false;
        }
    }

    private static bool TryGetTmdbId(SeasonMapping mapping, out int tmdbId)
    {
        var entry = mapping.ProviderIds.FirstOrDefault(x =>
            string.Equals(x.Key, "Tmdb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Key, "TheMovieDb", StringComparison.OrdinalIgnoreCase));

        return entry is not null
            && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmdbId)
            && tmdbId > 0;
    }

    private static string BuildLanguageParameter(string? language)
        => string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : "&language=" + Uri.EscapeDataString(language);

    private static bool TryGetPath(JsonElement root, string property, out string path)
    {
        path = string.Empty;
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        path = value.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static string? GetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}

public sealed class TmdbEpisodeSupplement
{
    public string? Name { get; set; }

    public string? Overview { get; set; }

    public string? StillUrl { get; set; }

    public DateTime? PremiereDate { get; set; }

    public int? ProductionYear { get; set; }

    public float? CommunityRating { get; set; }
}
