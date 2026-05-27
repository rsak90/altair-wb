using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Models;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers.Api;

/// <summary>
/// Internal API controller that proxies authentication requests to the Altair SLC Hub.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthApiController : ControllerBase
{
    private readonly SlcHubClient _hubClient;

    public AuthApiController(SlcHubClient hubClient)
    {
        _hubClient = hubClient;
    }

    /// <summary>
    /// Authenticates the user against the Altair SLC Hub.
    /// On success, stores the Bearer Token in the server-side Session and returns 200.
    /// </summary>
    /// <remarks>
    /// Satisfies Requirements 1.2, 1.3, 1.4, 1.5
    /// </remarks>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        string token;
        try
        {
            token = await _hubClient.LoginAsync(request.Username, request.Password, ct);
        }
        catch (SlcHubException ex)
        {
            // Hub returned a 4xx/5xx — surface the Hub's error message to the caller.
            return BadRequest(new ApiErrorResponse
            {
                StatusCode = ex.StatusCode,
                Message = string.IsNullOrWhiteSpace(ex.ErrorBody)
                    ? $"Authentication failed (HTTP {ex.StatusCode})."
                    : ex.ErrorBody
            });
        }
        catch (SlcHubConnectivityException ex)
        {
            // Network failure or timeout — return a generic connectivity error.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Message = ex.Message
            });
        }

        // Store the token server-side; the browser never sees it.
        HttpContext.Session.SetString("BearerToken", token);

        return Ok(new { success = true });
    }

    /// <summary>Request body for <see cref="Login"/>.</summary>
    public sealed class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }
}
