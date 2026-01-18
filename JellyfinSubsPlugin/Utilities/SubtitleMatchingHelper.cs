using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo.Utilities;

internal static class SubtitleMatchingHelper
{
    private static readonly string[] ReleaseFormats =
    [
        "BluRay",
        "BLURAY",
        "BRRip",
        "BDRip",
        "WEB-DL",
        "WEBDL",
        "WEBRip",
        "WEB",
        "HDRip",
        "HDRIP",
        "DVDRip",
        "DVDRIP",
        "SCREENER",
        "SCR",
        "TS",
        "TELESYNC",
        "CAM",
        "CAMRIP",
    ];

    public static string? FindBestEpisodeMatch(
        List<string> fileNames,
        int episodeNumber,
        ILogger? logger = null
    )
    {
        if (fileNames.Count == 0)
        {
            return null;
        }

        var filesWithEpisodes = new List<(string fileName, int extractedEpisode)>();
        foreach (var fileName in fileNames)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                fileName,
                @"[Ss](\d+)[Ee](\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            if (match.Success && match.Groups.Count >= 3)
            {
                var episodeStr = match.Groups[2].Value;
                if (int.TryParse(episodeStr, out var ep))
                {
                    filesWithEpisodes.Add((fileName, ep));
                    logger?.LogDebug(
                        "Extracted episode {Episode} from file: {FileName}",
                        ep,
                        fileName
                    );
                }
            }
        }

        var exactMatch = filesWithEpisodes.FirstOrDefault(f => f.extractedEpisode == episodeNumber);
        if (exactMatch.fileName != null)
        {
            logger?.LogDebug(
                "Found exact episode match for E{Episode}: {FileName}",
                episodeNumber,
                exactMatch.fileName
            );
            return exactMatch.fileName;
        }

        var episodePatterns = new[]
        {
            $"E{episodeNumber:D2}",
            $"e{episodeNumber:D2}",
            $"E{episodeNumber}",
            $"e{episodeNumber}",
            $".{episodeNumber:D2}.",
            $" {episodeNumber:D2} ",
        };

        foreach (var pattern in episodePatterns)
        {
            var patternMatches = fileNames
                .Where(f => f.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (patternMatches.Count == 1)
            {
                logger?.LogDebug(
                    "Found pattern match for episode {Episode}: {FileName}",
                    episodeNumber,
                    patternMatches[0]
                );
                return patternMatches[0];
            }
            if (patternMatches.Count > 1)
            {
                // Multiple matches, use Levenshtein to find the best one
                var bestMatch = FindClosestMatch(patternMatches, $"Episode{episodeNumber:D2}");
                logger?.LogDebug(
                    "Multiple pattern matches for episode {Episode}, using closest: {FileName}",
                    episodeNumber,
                    bestMatch
                );
                return bestMatch;
            }
        }

        // Last resort: use Levenshtein distance on all files
        logger?.LogWarning(
            "No clear episode match found for episode {Episode}, using fuzzy matching",
            episodeNumber
        );
        return FindClosestMatch(fileNames, $"E{episodeNumber:D2}");
    }

    /// <summary>
    /// Finds the best matching subtitle file for a movie.
    /// Uses filename matching and prioritizes by release format (HDRip, DVDRip, etc.).
    /// </summary>
    /// <param name="fileNames">List of subtitle filenames in the archive.</param>
    /// <param name="mediaFileName">The media filename to match.</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    /// <returns>The best matching filename, or first file if none match.</returns>
    public static string? FindBestMovieMatch(
        List<string> fileNames,
        string mediaFileName,
        ILogger? logger = null
    )
    {
        if (fileNames.Count == 0)
        {
            return null;
        }

        // Extract format from media filename
        var mediaFormat = ExtractReleaseFormat(mediaFileName);

        // First, try to find files with matching format
        if (!string.IsNullOrEmpty(mediaFormat))
        {
            var formatMatches = fileNames
                .Where(f => f.Contains(mediaFormat, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (formatMatches.Count == 1)
            {
                logger?.LogDebug(
                    "Found subtitle with matching format '{Format}': {FileName}",
                    mediaFormat,
                    formatMatches[0]
                );
                return formatMatches[0];
            }

            if (formatMatches.Count > 1)
            {
                // Multiple format matches, use Levenshtein to find the closest filename
                var bestMatch = FindClosestMatch(formatMatches, mediaFileName);
                logger?.LogDebug(
                    "Multiple format matches, using closest match: {FileName}",
                    bestMatch
                );
                return bestMatch;
            }
        }

        // No format match, use Levenshtein distance on all filenames
        var closestMatch = FindClosestMatch(fileNames, mediaFileName);

        // Check if the closest match is reasonably close (distance < 50% of filename length)
        var distance = LevenshteinDistance(closestMatch ?? string.Empty, mediaFileName);
        var threshold = mediaFileName.Length / 2;

        if (distance <= threshold)
        {
            logger?.LogDebug(
                "Found close filename match (distance: {Distance}): {FileName}",
                distance,
                closestMatch
            );
            return closestMatch;
        }

        // No close match found, fall back to format priority
        logger?.LogDebug("No close filename match found, using format priority");
        foreach (var format in ReleaseFormats)
        {
            var match = fileNames.FirstOrDefault(f =>
                f.Contains(format, StringComparison.OrdinalIgnoreCase)
            );
            if (match != null)
            {
                logger?.LogDebug(
                    "Using subtitle with format '{Format}': {FileName}",
                    format,
                    match
                );
                return match;
            }
        }

        // Absolute fallback: return the first file
        logger?.LogDebug(
            "No format match found, using first available subtitle: {FileName}",
            fileNames[0]
        );
        return fileNames[0];
    }

    /// <summary>
    /// Extracts the release format from a filename.
    /// </summary>
    /// <param name="fileName">The filename to analyze.</param>
    /// <returns>The detected release format, or null if none found.</returns>
    public static string? ExtractReleaseFormat(string fileName)
    {
        foreach (var format in ReleaseFormats)
        {
            if (fileName.Contains(format, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the closest matching string using Levenshtein distance.
    /// </summary>
    /// <param name="candidates">List of candidate strings.</param>
    /// <param name="target">The target string to match against.</param>
    /// <returns>The closest matching string, or null if no candidates.</returns>
    public static string? FindClosestMatch(List<string> candidates, string target)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestMatch = candidates[0];
        var bestDistance = LevenshteinDistance(candidates[0], target);

        foreach (var candidate in candidates.Skip(1))
        {
            var distance = LevenshteinDistance(candidate, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = candidate;
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// Calculates the Levenshtein distance between two strings.
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="target">The target string.</param>
    /// <returns>The Levenshtein distance (number of single-character edits needed).</returns>
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (var i = 0; i <= sourceLength; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j <= targetLength; j++)
        {
            distance[0, j] = j;
        }

        // Calculate distances
        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = target[j - 1] == source[i - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost
                );
            }
        }

        return distance[sourceLength, targetLength];
    }
}
