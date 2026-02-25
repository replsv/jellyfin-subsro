using Jellyfin.Plugin.SubsRo.Extensions;
using Jellyfin.Plugin.SubsRo.Models;
using Jellyfin.Plugin.SubsRo.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
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
    private readonly ILibraryManager? _libraryManager;

    public SubsRoDownloader(
        ILogger<SubsRoDownloader> logger,
        SubsRoApiV1 apiV1,
        ILibraryManager? libraryManager = null
    )
    {
        Instance = this;
        _logger = logger;
        _apiV1 = apiV1;
        _libraryManager = libraryManager;
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
            "Subs.ro: Search called for: {Name}, ContentType: {ContentType}, Language: {Language}, SeriesName: {SeriesName}",
            request.Name,
            request.ContentType,
            request.Language,
            request.SeriesName
        );

        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Subs.ro: API key not configured - cannot search for subtitles");
            return Array.Empty<RemoteSubtitleInfo>();
        }

        _logger.LogInformation("Subs.ro: API key is configured, proceeding with search");

        var normalizedLanguage = NormalizeLanguage(request.Language);
        var results = new List<RemoteSubtitleInfo>();

        try
        {
            var requestImdbId = request.GetProviderId(MetadataProvider.Imdb);
            var requestTmdbId = request.GetProviderId(MetadataProvider.Tmdb);

            string? seriesImdbId = null;
            string? seriesTmdbId = null;
            string? canonicalSeriesTitle = null;

            if (request.ContentType == VideoContentType.Episode)
            {
                ResolveSeriesFromEpisodeRequest(request, out seriesImdbId, out seriesTmdbId, out canonicalSeriesTitle);
            }

            _logger.LogInformation(
                "Subs.ro: Request IDs - IMDb: {ReqImdb}, TMDb: {ReqTmdb}; Resolved series - Title: {SeriesTitle}, IMDb: {SeriesImdb}, TMDb: {SeriesTmdb}",
                requestImdbId,
                requestTmdbId,
                canonicalSeriesTitle,
                seriesImdbId,
                seriesTmdbId
            );

            var effectiveImdbId = request.ContentType == VideoContentType.Episode
                ? (seriesImdbId ?? requestImdbId)
                : requestImdbId;

            var effectiveTmdbId = request.ContentType == VideoContentType.Episode
                ? (seriesTmdbId ?? requestTmdbId)
                : requestTmdbId;

            if (!string.IsNullOrEmpty(effectiveImdbId))
            {
                _logger.LogDebug("Searching by IMDb ID: {ImdbId}", effectiveImdbId);

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync("imdbid", effectiveImdbId, normalizedLanguage, apiKey, cancellationToken)
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

                _logger.LogInformation("Subs.ro: IMDb search returned 0 items");
            }
            else
            {
                _logger.LogInformation("Subs.ro: No IMDb id available for search");
            }

            if (!string.IsNullOrEmpty(effectiveTmdbId))
            {
                _logger.LogInformation("Subs.ro: Searching by TMDb ID: {TmdbId}", effectiveTmdbId);

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync("tmdbid", effectiveTmdbId, normalizedLanguage, apiKey, cancellationToken)
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

                _logger.LogInformation("Subs.ro: TMDb search returned 0 items");
            }
            else
            {
                _logger.LogInformation("Subs.ro: No TMDb id available for search");
            }

            var titleQuery =
                request.ContentType == VideoContentType.Episode
                    ? (canonicalSeriesTitle ?? request.SeriesName ?? request.Name)
                    : request.Name;

            if (!string.IsNullOrWhiteSpace(titleQuery))
            {
                _logger.LogInformation(
                    "Subs.ro: Searching by title: {TitleQuery} (request.Name: {OriginalName}, request.SeriesName: {SeriesName}, canonicalSeriesTitle: {CanonicalSeriesTitle})",
                    titleQuery,
                    request.Name,
                    request.SeriesName,
                    canonicalSeriesTitle
                );

                var searchResponse = await _apiV1
                    .SearchSubtitlesAsync(
                        "title",
                        titleQuery,
                        normalizedLanguage,
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
            else
            {
                _logger.LogWarning("Subs.ro: Title query is empty (cannot search by title)");
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

    private void ResolveSeriesFromEpisodeRequest(
        SubtitleSearchRequest request,
        out string? seriesImdbId,
        out string? seriesTmdbId,
        out string? canonicalSeriesTitle
    )
    {
        seriesImdbId = null;
        seriesTmdbId = null;
        canonicalSeriesTitle = null;

        if (_libraryManager == null)
        {
            _logger.LogInformation("Subs.ro: ILibraryManager not available; cannot resolve series from episode");
            return;
        }

        var item = TryResolveItemByRequestId(request);
        if (item == null)
        {
            item = TryResolveItemByRequestPath(request);
        }

        if (item == null)
        {
            _logger.LogInformation(
                "Subs.ro: Could not resolve episode item from request (neither id nor path). Request keys: {Keys}",
                string.Join(", ", GetPublicPropertyNames(request))
            );
            return;
        }

        if (item is not Episode episode)
        {
            _logger.LogInformation("Subs.ro: Resolved item is not an Episode ({Type}); cannot resolve series", item.GetType().Name);
            return;
        }

        Series? series = null;

        try { series = episode.Series; } catch { }

        if (series == null && episode.SeriesId != Guid.Empty)
        {
            series = _libraryManager.GetItemById(episode.SeriesId) as Series;
        }

        if (series == null && episode.SeasonId != Guid.Empty)
        {
            var season = _libraryManager.GetItemById(episode.SeasonId) as Season;
            if (season != null && season.SeriesId != Guid.Empty)
            {
                series = _libraryManager.GetItemById(season.SeriesId) as Series;
            }
        }

        if (series == null)
        {
            _logger.LogInformation("Subs.ro: Could not resolve Series from resolved Episode item {Id}", episode.Id);
            return;
        }

        canonicalSeriesTitle = series.Name;
        seriesImdbId = series.GetProviderId(MetadataProvider.Imdb);
        seriesTmdbId = series.GetProviderId(MetadataProvider.Tmdb);

        _logger.LogInformation(
            "Subs.ro: Resolved series from episode - Title: {Title}, IMDb: {Imdb}, TMDb: {Tmdb}",
            canonicalSeriesTitle,
            seriesImdbId,
            seriesTmdbId
        );
    }

    private BaseItem? TryResolveItemByRequestId(SubtitleSearchRequest request)
    {
        Guid? id = TryGetRequestItemGuid(request);
        if (id == null || id == Guid.Empty)
        {
            _logger.LogInformation("Subs.ro: Could not resolve request item id; will try path lookup");
            return null;
        }

        try
        {
            return _libraryManager!.GetItemById(id.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subs.ro: Failed to load item by id {ItemId}; will try path lookup", id);
            return null;
        }
    }

    private BaseItem? TryResolveItemByRequestPath(SubtitleSearchRequest request)
    {
        var path = TryGetRequestPath(request);
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogInformation("Subs.ro: No request path available for lookup");
            return null;
        }

        try
        {
            var lm = _libraryManager!;
            var t = lm.GetType();
            var nullableBool = typeof(bool?);

            var methods = t.GetMethods()
                .Where(m => m.Name == "FindByPath")
                .ToArray();

            foreach (var m in methods)
            {
                var ps = m.GetParameters();

                if (ps.Length == 2 &&
                    ps[0].ParameterType == typeof(string) &&
                    ps[1].ParameterType == nullableBool)
                {
                    var result = m.Invoke(lm, new object?[] { path, null }) as BaseItem;
                    if (result != null)
                    {
                        _logger.LogInformation("Subs.ro: Resolved item by path via FindByPath(path, null): {Path}", path);
                        return result;
                    }
                }
            }

            _logger.LogInformation(
                "Subs.ro: ILibraryManager.FindByPath not found / did not resolve item for path: {Path}",
                path
            );
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subs.ro: Exception resolving item by path: {Path}", path);
            return null;
        }
    }

    private static Guid? TryGetRequestItemGuid(SubtitleSearchRequest request)
    {
        var t = request.GetType();

        var candidates = new[]
        {
            "ItemId",
            "InternalId",
            "Id",
            "EpisodeId",
            "MediaItemId",
            "ItemGuid"
        };

        foreach (var name in candidates)
        {
            var p = t.GetProperty(name);
            if (p == null) continue;

            var value = p.GetValue(request);
            if (value is Guid g) return g;

            if (value is string s && Guid.TryParse(s, out var gs)) return gs;
        }

        foreach (var p in t.GetProperties())
        {
            if (!p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) continue;

            var value = p.GetValue(request);
            if (value is Guid g) return g;
            if (value is string s && Guid.TryParse(s, out var gs)) return gs;
        }

        return null;
    }

    private static string? TryGetRequestPath(SubtitleSearchRequest request)
    {
        var t = request.GetType();

        var candidates = new[]
        {
            "MediaPath",
            "Path",
            "ItemPath",
            "FilePath",
        };

        foreach (var name in candidates)
        {
            var p = t.GetProperty(name);
            if (p == null) continue;

            var value = p.GetValue(request);
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetPublicPropertyNames(object o)
        => o.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n);

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
