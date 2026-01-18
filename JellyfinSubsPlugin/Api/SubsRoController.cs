using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Jellyfin.Plugin.SubsRo.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubsRo.Api;

/// <summary>
/// Controller for Subs.ro API proxy endpoints.
/// </summary>
[ApiController]
[Route("Plugins/SubsRo")]
[Produces(MediaTypeNames.Application.Json)]
public class SubsRoController : ControllerBase
{
    private readonly ILogger<SubsRoController> _logger;
    private readonly SubsRoApiV1 _apiV1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="apiV1">The API client.</param>
    public SubsRoController(ILogger<SubsRoController> logger, SubsRoApiV1 apiV1)
    {
        _logger = logger;
        _apiV1 = apiV1;
    }

    /// <summary>
    /// Validates an API key by checking quota.
    /// </summary>
    /// <param name="apiKey">The API key to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The quota information if valid.</returns>
    [HttpGet("ValidateApiKey")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<QuotaResponse>> ValidateApiKey(
        [FromQuery, Required] string apiKey,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation("Validating API key");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("API key is empty");
            return BadRequest(new { error = "API key is required" });
        }

        try
        {
            var quotaResponse = await _apiV1
                .GetQuotaAsync(apiKey, cancellationToken)
                .ConfigureAwait(false);

            if (quotaResponse == null)
            {
                _logger.LogWarning("API key validation failed - invalid response");
                return Unauthorized(new { error = "Invalid API key" });
            }

            _logger.LogInformation("API key validated successfully");
            return Ok(quotaResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API key");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { error = "Failed to validate API key" }
            );
        }
    }
}
