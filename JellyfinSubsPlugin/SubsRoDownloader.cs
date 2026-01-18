using Jellyfin.Plugin.SubsRo.Extensions;
using Jellyfin.Plugin.SubsRo.Models;
using Jellyfin.Plugin.SubsRo.Utilities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

public sealed class SubsRoDownloader : ISubtitleProvider
{
    private readonly ILogger<SubsRoDownloader> _logger;
    private readonly SubsRoApiV1 _apiV1;

    public SubsRoDownloader(ILogger<SubsRoDownloader> logger, SubsRoApiV1 apiV1)
    {
        Instance = this;
        _logger = logger;
        _apiV1 = apiV1;
        _logger.LogInformation("SubsRoDownloader initialized");
    }

    public static SubsRoDownloader? Instance { get; private set; }

    public string Name => "Subs.ro";

    public IEnumerable<VideoContentType> SupportedMediaTypes =>
        [VideoContentType.Movie, VideoContentType.Episode];

    /// <inheritdoc />
    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subs.ro: Getting subtitles for ID: {Id}", id);

        // Format: "subsro-{subtitleId}-{language}" (old movies, backward compat)
        //     or: "subsro-{subtitleId}-{language}-S##E##" (series)
        //     or: "subsro-{subtitleId}-{language}-{encodedFileName}" (movies)
        var parts = id.Split('-', 4); // Limit to 4 parts to handle filenames with dashes
        if (parts.Length < 3 || parts[0] != "subsro" || !int.TryParse(parts[1], out var subtitleId))
        {
            throw new ArgumentException($"Invalid subtitle ID format: {id}", nameof(id));
        }

        var language = parts[2];

        int? seasonNumber = null;
        int? episodeNumber = null;
        string? mediaFileName = null;

        if (parts.Length >= 4)
        {
            var extraInfo = parts[3];
            var decodedInfo = Uri.UnescapeDataString(extraInfo);
            var seMatch = System.Text.RegularExpressions.Regex.Match(
                decodedInfo,
                @"[Ss](\d+)[Ee](\d+)"
            );

            // episode or movie
            if (seMatch.Success)
            {
                seasonNumber = int.Parse(seMatch.Groups[1].Value);
                episodeNumber = int.Parse(seMatch.Groups[2].Value);

                if (!System.Text.RegularExpressions.Regex.IsMatch(extraInfo, @"^S\d+E\d+$"))
                {
                    mediaFileName = decodedInfo;
                    _logger.LogInformation(
                        "Subs.ro: Extracting subtitle for Season {Season}, Episode {Episode} from media filename: {FileName}",
                        seasonNumber,
                        episodeNumber,
                        mediaFileName
                    );
                }
                else
                {
                    _logger.LogInformation(
                        "Subs.ro: Extracting subtitle for Season {Season}, Episode {Episode}",
                        seasonNumber,
                        episodeNumber
                    );
                }
            }
            else
            {
                mediaFileName = decodedInfo;
                _logger.LogInformation(
                    "Subs.ro: Extracting subtitle for movie: {FileName}",
                    mediaFileName
                );
            }
        }

        var apiKey = GetApiKey();

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("API key not configured");
            return new SubtitleResponse();
        }

        var archiveStream = await _apiV1
            .DownloadSubtitleAsync(subtitleId, apiKey, cancellationToken)
            .ConfigureAwait(false);

        if (archiveStream == null)
        {
            _logger.LogWarning("Failed to download subtitle archive for ID: {Id}", subtitleId);
            return new SubtitleResponse();
        }

        try
        {
            var subtitleContent = await ExtractSubtitleFromArchive(
                    archiveStream,
                    seasonNumber,
                    episodeNumber,
                    mediaFileName,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (subtitleContent == null)
            {
                _logger.LogWarning("No subtitle file found in archive for ID: {Id}", subtitleId);
                return new SubtitleResponse();
            }

            return new SubtitleResponse
            {
                Format = GetSubtitleFormat(subtitleContent.FileName),
                Language = language,
                Stream = new MemoryStream(subtitleContent.Content),
            };
        }
        finally
        {
            await archiveStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation(
            "Subs.ro: Search called for: {Name}, ContentType: {ContentType}, Language: {Language}",
            request.Name,
            request.ContentType,
            request.Language
        );

        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Subs.ro: API key not configured - cannot search for subtitles");
            return Array.Empty<RemoteSubtitleInfo>();
        }

        _logger.LogInformation("Subs.ro: API key is configured, proceeding with search");

        var language = NormalizeLanguage(request.Language);
        var results = new List<RemoteSubtitleInfo>();

        try
        {
            var imdbId = request.GetProviderId(MetadataProvider.Imdb);
            if (!string.IsNullOrEmpty(imdbId))
            {
                _logger.LogDebug("Searching by IMDb ID: {ImdbId}", imdbId);

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync("imdbid", imdbId, language, apiKey, cancellationToken)
                    .ConfigureAwait(false);

                if (searchResponse?.Items != null && searchResponse.Items.Count > 0)
                {
                    results.AddRange(
                        searchResponse.Items.ToRemoteSubtitleInfo(request, Name, _logger)
                    );
                    _logger.LogDebug("Found {Count} subtitles by IMDb ID", results.Count);
                    _logger.LogInformation(
                        "Subs.ro: Returning {Count} subtitle results",
                        results.Count
                    );
                    return results;
                }
            }

            var tmdbId = request.GetProviderId(MetadataProvider.Tmdb);
            if (!string.IsNullOrEmpty(tmdbId))
            {
                _logger.LogDebug("Searching by TMDb ID: {TmdbId}", tmdbId);

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync("tmdbid", tmdbId, language, apiKey, cancellationToken)
                    .ConfigureAwait(false);

                if (searchResponse?.Items != null && searchResponse.Items.Count > 0)
                {
                    results.AddRange(
                        searchResponse.Items.ToRemoteSubtitleInfo(request, Name, _logger)
                    );
                    _logger.LogDebug("Found {Count} subtitles by TMDb ID", results.Count);
                    _logger.LogInformation(
                        "Subs.ro: Returning {Count} subtitle results",
                        results.Count
                    );
                    return results;
                }
            }

            if (!string.IsNullOrEmpty(request.Name))
            {
                _logger.LogDebug("Searching by title: {Title}", request.Name);

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync(
                        "title",
                        request.Name,
                        language,
                        apiKey,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (searchResponse?.Items != null)
                {
                    results.AddRange(
                        searchResponse.Items.ToRemoteSubtitleInfo(request, Name, _logger)
                    );
                }
            }

            _logger.LogDebug("Found {Count} subtitles", results.Count);
            _logger.LogInformation("Subs.ro: Returning {Count} subtitle results", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subs.ro: Error searching for subtitles");
            return Array.Empty<RemoteSubtitleInfo>();
        }
    }

    /// <summary>
    /// Called when configuration changes.
    /// </summary>
    public void ConfigurationChanged()
    {
        _logger.LogInformation("Subs.ro: Configuration changed, validating API key");
        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Subs.ro: API key is empty after configuration change");
        }
        else
        {
            _logger.LogInformation(
                "Subs.ro: API key is configured ({Length} characters)",
                apiKey.Length
            );
        }
    }

    private async Task<SubtitleFile?> ExtractSubtitleFromArchive(
        Stream archiveStream,
        int? seasonNumber,
        int? episodeNumber,
        string? mediaFileName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var buffer = new byte[8];
            await archiveStream.ReadAsync(buffer, 0, 8, cancellationToken).ConfigureAwait(false);
            archiveStream.Position = 0;

            if (buffer[0] == 0x50 && buffer[1] == 0x4B)
            {
                return await ArchiveExtractor.ExtractFromZipAsync(
                    archiveStream,
                    episodeNumber,
                    mediaFileName,
                    _logger,
                    cancellationToken
                );
            }

            if (buffer[0] == 0x52 && buffer[1] == 0x61 && buffer[2] == 0x72 && buffer[3] == 0x21)
            {
                return await ArchiveExtractor.ExtractFromRarAsync(
                    archiveStream,
                    episodeNumber,
                    mediaFileName,
                    _logger,
                    cancellationToken
                );
            }

            _logger.LogWarning("Unknown archive format");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting subtitle from archive");
            return null;
        }
    }

    private string? GetApiKey()
    {
        return SubsRoPlugin.Instance?.Configuration.ApiKey;
    }

    private string? NormalizeLanguage(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        // API spec enum: [ro, en, ita, fra, ger, ung, gre, por, spa, alt]
        return language.ToLowerInvariant() switch
        {
            // Romanian
            "ro" or "rom" or "ron" => "ro",
            // English
            "en" or "eng" => "en",
            // Italian
            "it" or "ita" => "ita",
            // French
            "fr" or "fre" or "fra" => "fra",
            // German
            "de" or "ger" or "deu" => "ger",
            // Hungarian
            "hu" or "hun" or "ung" => "ung",
            // Greek
            "el" or "gre" or "ell" => "gre",
            // Portuguese
            "pt" or "por" => "por",
            // Spanish
            "es" or "spa" => "spa",
            // Other/Unknown - map to 'alt' (Other)
            _ => "alt",
        };
    }

    private string GetSubtitleFormat(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".srt" => "srt",
            ".sub" => "sub",
            ".ssa" => "ssa",
            ".ass" => "ass",
            ".vtt" => "vtt",
            _ => "srt",
        };
    }
}
