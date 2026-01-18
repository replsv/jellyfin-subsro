using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SubsRo.Models;

/// <summary>
/// Base response with meta information.
/// </summary>
public record BaseResponse
{
    /// <summary>
    /// Gets or sets the status code.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Gets or sets the meta information.
    /// </summary>
    [JsonPropertyName("meta")]
    public Meta? Meta { get; set; }

    /// <summary>
    /// Gets or sets the error message if any.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Meta information for API responses.
/// </summary>
public record Meta
{
    /// <summary>
    /// Gets or sets the request ID.
    /// </summary>
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

/// <summary>
/// Search response from Subs.ro API.
/// </summary>
public record SearchResponse : BaseResponse
{
    /// <summary>
    /// Gets or sets the count of results.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the subtitle items.
    /// </summary>
    [JsonPropertyName("items")]
    public List<SubtitleItem> Items { get; set; } = [];
}

/// <summary>
/// Details response for a single subtitle.
/// </summary>
public record DetailsResponse : BaseResponse
{
    /// <summary>
    /// Gets or sets the subtitle item.
    /// </summary>
    [JsonPropertyName("item")]
    public SubtitleItem? Item { get; set; }
}

/// <summary>
/// Subtitle item information.
/// </summary>
public class SubtitleItem
{
    /// <summary>
    /// Gets or sets the subtitle ID.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the creation date.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the update date.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the link to the subtitle page.
    /// </summary>
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    /// <summary>
    /// Gets or sets the download link.
    /// </summary>
    [JsonPropertyName("downloadLink")]
    public string? DownloadLink { get; set; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the year.
    /// </summary>
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    /// <summary>
    /// Gets or sets the IMDb ID.
    /// </summary>
    [JsonPropertyName("imdbid")]
    public string? ImdbId { get; set; }

    /// <summary>
    /// Gets or sets the TMDb ID.
    /// </summary>
    [JsonPropertyName("tmdbid")]
    public string? TmdbId { get; set; }

    /// <summary>
    /// Gets or sets the poster URL.
    /// </summary>
    [JsonPropertyName("poster")]
    public string? Poster { get; set; }

    /// <summary>
    /// Gets or sets the translator name.
    /// </summary>
    [JsonPropertyName("translator")]
    public string? Translator { get; set; }

    /// <summary>
    /// Gets or sets the language code.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the type (movie or series).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>
/// Quota response from Subs.ro API.
/// </summary>
public record QuotaResponse : BaseResponse
{
    /// <summary>
    /// Gets or sets the quota information.
    /// </summary>
    [JsonPropertyName("quota")]
    public QuotaInfo? Quota { get; set; }
}

/// <summary>
/// Quota information.
/// </summary>
public record QuotaInfo
{
    /// <summary>
    /// Gets or sets the total quota.
    /// </summary>
    [JsonPropertyName("total_quota")]
    public int TotalQuota { get; set; }

    /// <summary>
    /// Gets or sets the used quota.
    /// </summary>
    [JsonPropertyName("used_quota")]
    public int UsedQuota { get; set; }

    /// <summary>
    /// Gets or sets the remaining quota.
    /// </summary>
    [JsonPropertyName("remaining_quota")]
    public int RemainingQuota { get; set; }

    /// <summary>
    /// Gets or sets the quota type.
    /// </summary>
    [JsonPropertyName("quota_type")]
    public string? QuotaType { get; set; }

    /// <summary>
    /// Gets or sets the IP address.
    /// </summary>
    [JsonPropertyName("ip_address")]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Gets or sets the masked API key.
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }
}
