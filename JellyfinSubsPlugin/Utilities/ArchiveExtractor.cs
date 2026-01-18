using Jellyfin.Plugin.SubsRo.Models;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Zip;

namespace Jellyfin.Plugin.SubsRo.Utilities;

internal static class ArchiveExtractor
{
    private static readonly string[] SubtitleExtensions = [".srt", ".sub", ".ass", ".ssa", ".vtt"];

    public static async Task<SubtitleFile?> ExtractFromZipAsync(
        Stream archiveStream,
        int? episodeNumber,
        string? mediaFileName,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        using var archive = ZipArchive.Open(archiveStream);
        var subtitleFiles = ExtractSubtitleEntries(
            archive.Entries.Where(e => !e.IsDirectory),
            e => e.Key ?? string.Empty,
            e => e.OpenEntryStream()
        );
        return await ProcessSubtitleFilesAsync(
            subtitleFiles,
            episodeNumber,
            mediaFileName,
            logger,
            cancellationToken
        );
    }

    public static async Task<SubtitleFile?> ExtractFromRarAsync(
        Stream archiveStream,
        int? episodeNumber,
        string? mediaFileName,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        using var archive = RarArchive.Open(archiveStream);
        var subtitleFiles = ExtractSubtitleEntries(
            archive.Entries.Where(e => !e.IsDirectory),
            e => e.Key ?? string.Empty,
            e => e.OpenEntryStream()
        );
        return await ProcessSubtitleFilesAsync(
            subtitleFiles,
            episodeNumber,
            mediaFileName,
            logger,
            cancellationToken
        );
    }

    private static List<(string entryKey, Func<Stream> openStream)> ExtractSubtitleEntries<T>(
        IEnumerable<T> entries,
        Func<T, string> getKey,
        Func<T, Stream> openStream
    )
    {
        var subtitleFiles = new List<(string entryKey, Func<Stream> openStream)>();

        foreach (var entry in entries)
        {
            var entryKey = getKey(entry);
            var extension = Path.GetExtension(entryKey).ToLowerInvariant();
            if (SubtitleExtensions.Contains(extension))
            {
                subtitleFiles.Add((entryKey, () => openStream(entry)));
            }
        }

        return subtitleFiles;
    }

    private static async Task<SubtitleFile?> ProcessSubtitleFilesAsync(
        List<(string entryKey, Func<Stream> openStream)> subtitleFiles,
        int? episodeNumber,
        string? mediaFileName,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        if (subtitleFiles.Count == 0)
        {
            return null;
        }

        if (subtitleFiles.Count == 1)
        {
            logger.LogDebug("Found single subtitle file: {FileName}", subtitleFiles[0].entryKey);
            return await ReadSubtitleFileAsync(
                subtitleFiles[0].entryKey,
                subtitleFiles[0].openStream,
                cancellationToken
            );
        }

        var fileNames = subtitleFiles.Select(f => f.entryKey).ToList();
        string? bestMatch = null;

        if (episodeNumber.HasValue)
        {
            bestMatch = SubtitleMatchingHelper.FindBestEpisodeMatch(
                fileNames,
                episodeNumber.Value,
                logger
            );
            if (bestMatch != null)
            {
                logger.LogInformation(
                    "Found matching subtitle for Episode {Episode}: {FileName}",
                    episodeNumber,
                    bestMatch
                );
            }
            else
            {
                logger.LogWarning(
                    "No matching subtitle found for Episode {Episode}",
                    episodeNumber
                );
                return null;
            }
        }
        else if (!string.IsNullOrEmpty(mediaFileName))
        {
            bestMatch = SubtitleMatchingHelper.FindBestMovieMatch(fileNames, mediaFileName, logger);
            if (bestMatch != null)
            {
                logger.LogInformation(
                    "Found matching subtitle for movie '{MediaFile}': {FileName}",
                    mediaFileName,
                    bestMatch
                );
            }
        }

        bestMatch ??= fileNames[0];
        logger.LogDebug("Using subtitle file: {FileName}", bestMatch);

        var matchedFile = subtitleFiles.First(f => f.entryKey == bestMatch);
        return await ReadSubtitleFileAsync(bestMatch, matchedFile.openStream, cancellationToken);
    }

    private static async Task<SubtitleFile> ReadSubtitleFileAsync(
        string fileName,
        Func<Stream> openStream,
        CancellationToken cancellationToken
    )
    {
        using var entryStream = openStream();
        using var memoryStream = new MemoryStream();
        await entryStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);

        return new SubtitleFile { FileName = fileName, Content = memoryStream.ToArray() };
    }
}
