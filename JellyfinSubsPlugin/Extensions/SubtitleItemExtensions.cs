using Jellyfin.Plugin.SubsRo.Models;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo.Extensions;

internal static class SubtitleItemExtensions
{
    public static List<RemoteSubtitleInfo> ToRemoteSubtitleInfo(
        this List<SubtitleItem> items,
        SubtitleSearchRequest request,
        string providerName,
        ILogger logger
    )
    {
        var results = new List<RemoteSubtitleInfo>();

        foreach (var item in items)
        {
            if (request.ContentType == VideoContentType.Episode && item.Type == "series")
            {
                var episodeInfo = CreateEpisodeSubtitleInfo(item, request, providerName, logger);
                if (episodeInfo != null)
                {
                    results.Add(episodeInfo);
                }
            }
            else if (request.ContentType == VideoContentType.Episode && item.Type == "movie")
            {
                results.Add(CreateMovieTypeForEpisode(item, request, providerName));
            }
            else if (request.ContentType == VideoContentType.Movie)
            {
                results.Add(CreateMovieSubtitleInfo(item, request, providerName));
            }
        }

        return results;
    }

    private static RemoteSubtitleInfo? CreateEpisodeSubtitleInfo(
        SubtitleItem item,
        SubtitleSearchRequest request,
        string providerName,
        ILogger logger
    )
    {
        var requestedSeason = request.ParentIndexNumber ?? 0;

        if (!IsSeasonMatch(item.Title, requestedSeason, logger))
        {
            return null;
        }

        var episodeNumber = request.IndexNumber ?? 0;
        var displayName = BuildDisplayName(item.Title, $"E{episodeNumber:D2}", item.Translator);

        logger.LogDebug(
            "Adding series subtitle for episode: ID=subsro-{Id}-{Lang}-S{Season:D2}E{Episode:D2}, Title={Title}",
            item.Id,
            item.Language,
            requestedSeason,
            episodeNumber,
            displayName
        );

        return new RemoteSubtitleInfo
        {
            Id = $"subsro-{item.Id}-{item.Language}-S{requestedSeason:D2}E{episodeNumber:D2}",
            Name = displayName,
            Author = item.Translator,
            Comment = item.Description,
            CommunityRating = null,
            DownloadCount = null,
            Format = "srt",
            ProviderName = providerName,
            ThreeLetterISOLanguageName = ConvertToThreeLetterCode(item.Language),
            DateCreated = item.CreatedAt,
            IsHashMatch = false,
        };
    }

    private static RemoteSubtitleInfo CreateMovieTypeForEpisode(
        SubtitleItem item,
        SubtitleSearchRequest request,
        string providerName
    )
    {
        var episodeNumber = request.IndexNumber ?? 0;
        var requestedSeason = request.ParentIndexNumber ?? 0;
        var displayName = BuildDisplayName($"[Movie] {item.Title}", null, item.Translator);

        return new RemoteSubtitleInfo
        {
            Id = $"subsro-{item.Id}-{item.Language}-S{requestedSeason:D2}E{episodeNumber:D2}",
            Name = displayName,
            Author = item.Translator,
            Comment = item.Description,
            CommunityRating = null,
            DownloadCount = null,
            Format = "srt",
            ProviderName = providerName,
            ThreeLetterISOLanguageName = ConvertToThreeLetterCode(item.Language),
            DateCreated = item.CreatedAt,
            IsHashMatch = false,
        };
    }

    private static RemoteSubtitleInfo CreateMovieSubtitleInfo(
        SubtitleItem item,
        SubtitleSearchRequest request,
        string providerName
    )
    {
        var mediaFileName = !string.IsNullOrEmpty(request.MediaPath)
            ? Path.GetFileNameWithoutExtension(request.MediaPath)
            : request.Name ?? string.Empty;

        var encodedFileName = Uri.EscapeDataString(mediaFileName);
        var displayName = item.Description ?? item.Title;

        if (item.Type == "series")
        {
            var multiSeasonMatch = System.Text.RegularExpressions.Regex.Match(
                item.Title ?? string.Empty,
                @"Sezon(?:ul|ele)\s+(\d+(?:\s*-\s*\d+)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (multiSeasonMatch.Success)
            {
                displayName = $"[Series Archive] {displayName}";
            }
        }

        displayName = BuildDisplayName(displayName, null, item.Translator);

        return new RemoteSubtitleInfo
        {
            Id = $"subsro-{item.Id}-{item.Language}-{encodedFileName}",
            Name = displayName,
            Author = item.Translator,
            Comment = item.Description,
            CommunityRating = null,
            DownloadCount = null,
            Format = "srt",
            ProviderName = providerName,
            ThreeLetterISOLanguageName = ConvertToThreeLetterCode(item.Language),
            DateCreated = item.CreatedAt,
            IsHashMatch = false,
        };
    }

    private static bool IsSeasonMatch(string? title, int requestedSeason, ILogger logger)
    {
        if (string.IsNullOrEmpty(title))
        {
            return true;
        }

        var singleSeasonMatch = System.Text.RegularExpressions.Regex.Match(
            title,
            @"Sezonul\s+(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        var seasonRangeMatch = System.Text.RegularExpressions.Regex.Match(
            title,
            @"Sezon(?:ul|ele)\s+(\d+)\s*-\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (singleSeasonMatch.Success && !seasonRangeMatch.Success)
        {
            if (int.TryParse(singleSeasonMatch.Groups[1].Value, out var itemSeason))
            {
                if (requestedSeason != itemSeason)
                {
                    logger.LogInformation(
                        "Skipping subtitle '{Title}' - season mismatch (requested: S{RequestedSeason:D2}, item: S{ItemSeason:D2})",
                        title,
                        requestedSeason,
                        itemSeason
                    );
                    return false;
                }
            }
        }
        else if (seasonRangeMatch.Success)
        {
            if (
                int.TryParse(seasonRangeMatch.Groups[1].Value, out var startSeason)
                && int.TryParse(seasonRangeMatch.Groups[2].Value, out var endSeason)
            )
            {
                if (requestedSeason < startSeason || requestedSeason > endSeason)
                {
                    logger.LogInformation(
                        "Skipping subtitle '{Title}' - season out of range (requested: S{RequestedSeason:D2}, range: S{StartSeason:D2}-S{EndSeason:D2})",
                        title,
                        requestedSeason,
                        startSeason,
                        endSeason
                    );
                    return false;
                }
            }
        }

        return true;
    }

    private static string BuildDisplayName(string? baseName, string? suffix, string? translator)
    {
        var name = baseName ?? "Unknown";
        var displayName = suffix != null ? $"{name} - {suffix}" : name;

        if (!string.IsNullOrEmpty(translator))
        {
            displayName += $" [{translator}]";
        }

        return displayName;
    }

    private static string ConvertToThreeLetterCode(string? language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return "und";
        }

        return language.ToLowerInvariant() switch
        {
            "ro" => "rom",
            "en" => "eng",
            "ita" => "ita",
            "fra" => "fra",
            "ger" => "ger",
            "ung" => "hun",
            "gre" => "gre",
            "por" => "por",
            "spa" => "spa",
            "alt" => "und",
            _ => "und",
        };
    }
}
