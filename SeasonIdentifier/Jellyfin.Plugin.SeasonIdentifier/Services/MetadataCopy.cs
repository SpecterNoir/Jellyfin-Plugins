using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.SeasonIdentifier.Services;

/// <summary>
/// Copies selected remote metadata while preserving local numbering and hierarchy.
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
        target.Genres = source.Genres;
        target.Studios = source.Studios;
    }

    public static void ApplyEpisode(Episode source, Episode target)
    {
        var localIndex = target.IndexNumber;
        var localParentIndex = target.ParentIndexNumber;

        target.Name = source.Name;
        target.OriginalTitle = source.OriginalTitle;
        target.Overview = source.Overview;
        target.Tagline = source.Tagline;
        target.PremiereDate = source.PremiereDate;
        target.ProductionYear = source.ProductionYear;
        target.CommunityRating = source.CommunityRating;
        target.CriticRating = source.CriticRating;
        target.OfficialRating = source.OfficialRating;
        target.ProviderIds = new Dictionary<string, string>(source.ProviderIds, StringComparer.OrdinalIgnoreCase);

        target.IndexNumber = localIndex;
        target.ParentIndexNumber = localParentIndex;
    }
}
