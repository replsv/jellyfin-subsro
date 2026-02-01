using System.Text.Json;
using Jellyfin.Plugin.SubsRo.Converters;
using Jellyfin.Plugin.SubsRo.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// API client for Subs.ro.
/// </summary>
public sealed class SubsRoApiV1
{
    private const string _apiVersion = "v1.0";
    private const string _baseUrl = $"https://api.subs.ro/{_apiVersion}";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SubsRoApiV1> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoApiV1"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public SubsRoApiV1(IHttpClientFactory httpClientFactory, ILogger<SubsRoApiV1> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Search for subtitles.
    /// </summary>
    /// <param name="searchField">The search field (imdbid, tmdbid, title, release).</param>
    /// <param name="value">The search value.</param>
    /// <param name="language">Optional language filter.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The search response.</returns>
    public async Task<SearchResponse?> SearchSubtitlesAsync(
        string searchField,
        string value,
        string? language,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        var url = $"{_baseUrl}/search/{searchField}/{Uri.EscapeDataString(value)}";
        if (!string.IsNullOrEmpty(language))
        {
            url += $"?language={language}";
        }

        return await SendRequestAsync<SearchResponse>(url, apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Get subtitle details by ID.
    /// </summary>
    /// <param name="id">The subtitle ID.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The details response.</returns>
    public async Task<DetailsResponse?> GetSubtitleDetailsAsync(
        int id,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        var url = $"{_baseUrl}/subtitle/{id}";
        return await SendRequestAsync<DetailsResponse>(url, apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Download a subtitle archive.
    /// </summary>
    /// <param name="id">The subtitle ID.</param>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subtitle archive stream.</returns>
    public async Task<Stream?> DownloadSubtitleAsync(
        int id,
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        var url = $"{_baseUrl}/subtitle/{id}/download";

        using var httpClient = _httpClientFactory.CreateClient("SubsRo");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        AddApiKeyHeader(request, apiKey);

        try
        {
            var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            // Check for rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "Rate limited by Subs.ro API when downloading subtitle {Id}. Status code: 429.",
                    id
                );

                // Check for Retry-After header
                if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                {
                    var retryAfter = retryAfterValues.FirstOrDefault();
                    _logger.LogWarning("Retry-After: {RetryAfter}", retryAfter);
                }

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to download subtitle {Id}: {StatusCode}",
                    id,
                    response.StatusCode
                );
                return null;
            }

            var memoryStream = new MemoryStream();
            await response
                .Content.CopyToAsync(memoryStream, cancellationToken)
                .ConfigureAwait(false);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading subtitle {Id}", id);
            return null;
        }
    }

    /// <summary>
    /// Get API quota information.
    /// </summary>
    /// <param name="apiKey">The API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The quota response.</returns>
    public async Task<QuotaResponse?> GetQuotaAsync(
        string apiKey,
        CancellationToken cancellationToken
    )
    {
        var url = $"{_baseUrl}/quota";
        return await SendRequestAsync<QuotaResponse>(url, apiKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T?> SendRequestAsync<T>(
        string url,
        string apiKey,
        CancellationToken cancellationToken
    )
        where T : BaseResponse
    {
        using var httpClient = _httpClientFactory.CreateClient("SubsRo");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // _logger.LogInformation("Sending request to Subs.ro API: {Url}", url);

        AddApiKeyHeader(request, apiKey);

        try
        {
            var response = await httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var content = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            // rate limiting (429 Too Many Requests)
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(
                    "Rate limited by Subs.ro API. Status code: 429. Please wait before making more requests."
                );

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "API request failed: {StatusCode} - {Content}",
                    response.StatusCode,
                    content
                );
                return null;
            }

            // _logger.LogInformation("Response content: {Content}", content);

            var result = JsonSerializer.Deserialize<T>(
                content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new CustomDateTimeConverter() },
                }
            );

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending request to {Url}", url);
            return null;
        }
    }

    private void AddApiKeyHeader(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("X-Subs-Api-Key", apiKey);
        }
    }
}
