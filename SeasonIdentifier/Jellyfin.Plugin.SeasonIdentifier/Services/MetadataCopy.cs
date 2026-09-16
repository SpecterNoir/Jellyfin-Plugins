using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.SeasonIdentifier.Services;

/// <summary>
/// Copies mapped remote metadata while preserving the local hierarchy and numbering.
/// </summary>
internal static class MetadataCopy
{
    public static void ApplySeriesToSeason(Series source, Season target)
    {
        target.Name = source.Name;
        target.OriginalTitle = source.OriginalTitle;
        target.Overview = source.Overview;
        target.Tagline = source.Tagline;
        target.PremiereDate = source.PremiereDate;
        target.EndDate = source.EndDate;
        target.ProductionYear = source.ProductionYear;
        target.CommunityRating = source.CommunityRating;
        target.CriticRating = source.CriticRating;
        target.OfficialRating = source.OfficialRating;
        target.OriginalLanguage = source.OriginalLanguage;
        target.Genres = source.Genres;
        target.Studios = source.Studios;
        target.Tags = source.Tags;
        target.ProductionLocations = source.ProductionLocations;
    }

    public static void ApplyEpisode(Episode source, Episode target)
    {
        var localIndex = target.IndexNumber;
        var localIndexEnd = target.IndexNumberEnd;
        var localParentIndex = target.ParentIndexNumber;

        target.Name = source.Name;
        target.OriginalTitle = source.OriginalTitle;
        target.Overview = source.Overview;
        target.Tagline = source.Tagline;
        target.PremiereDate = source.PremiereDate;
        target.EndDate = source.EndDate;
        target.ProductionYear = source.ProductionYear;
        target.CommunityRating = source.CommunityRating;
        target.CriticRating = source.CriticRating;
        target.OfficialRating = source.OfficialRating;
        target.OriginalLanguage = source.OriginalLanguage;
        target.ProviderIds = new Dictionary<string, string>(source.ProviderIds, StringComparer.OrdinalIgnoreCase);

        if (source.Genres.Length > 0)
        {
            target.Genres = source.Genres;
        }

        if (source.Studios.Length > 0)
        {
            target.Studios = source.Studios;
        }

        if (source.Tags.Length > 0)
        {
            target.Tags = source.Tags;
        }

        if (source.ProductionLocations.Length > 0)
        {
            target.ProductionLocations = source.ProductionLocations;
        }

        if (source.RemoteTrailers.Count > 0)
        {
            target.RemoteTrailers = source.RemoteTrailers;
        }

        // The metadata source is allowed to change the episode's descriptive identity, but never
        // its position inside the user's local Jellyfin structure.
        target.IndexNumber = localIndex;
        target.IndexNumberEnd = localIndexEnd;
        target.ParentIndexNumber = localParentIndex;
    }
}
