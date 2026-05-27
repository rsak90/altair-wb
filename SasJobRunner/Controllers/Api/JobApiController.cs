using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Models;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers.Api;

/// <summary>
/// Internal API controller that proxies SAS job operations to the Altair SLC Hub.
/// All endpoints require a valid Bearer Token in the server-side Session.
/// </summary>
/// <remarks>
/// Satisfies Requirements 7.1, 7.5 (session auth helper, 401 on missing token).
/// </remarks>
[ApiController]
[Route("api/jobs")]
public class JobApiController : ControllerBase
{
    private readonly SlcHubClient _hub;

    public JobApiController(SlcHubClient hub)
    {
        _hub = hub;
    }

    // ── Auth helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the Bearer Token from the server-side Session.
    /// Returns <c>null</c> if the token is absent or empty; callers must return 401.
    /// </summary>
    private string? GetBearerToken()
    {
        var token = HttpContext.Session.GetString("BearerToken");
        return string.IsNullOrEmpty(token) ? null : token;
    }

    // ── POST /api/jobs/submit ────────────────────────────────────────────────

    /// <summary>
    /// Submits a SAS program to the Altair SLC Hub.
    /// </summary>
    /// <remarks>
    /// Satisfies Requirements 3.3, 3.6, 7.5, 7.6, 7.7.
    /// </remarks>
    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] JobSubmitRequest request, CancellationToken ct)
    {
        // Req 7.5 — 401 if no Bearer Token in Session
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
        {
            return StatusCode(401, new ApiErrorResponse
            {
                StatusCode = 401,
                Message = "Session expired or not authenticated. Please log in again."
            });
        }

        // Req 3.6 — 409 if a job is already Submitted or Running
        var activeStatus = HttpContext.Session.GetString("ActiveJobStatus");
        if (activeStatus is "Submitted" or "Running")
        {
            return StatusCode(409, new ApiErrorResponse
            {
                StatusCode = 409,
                Message = "A job is already active. Wait for it to complete or cancel it before submitting a new one."
            });
        }

        // Req 7.7 — 413 if request body exceeds 1 MB
        if (Request.ContentLength.HasValue && Request.ContentLength.Value > 1_048_576)
        {
            return StatusCode(413, new ApiErrorResponse
            {
                StatusCode = 413,
                Message = "The SAS program exceeds the maximum allowed size of 1 MB."
            });
        }

        // Req 3.3 — Submit job to Hub; store Job ID and status in Session on success
        try
        {
            var jobId = await _hub.SubmitJobAsync(bearerToken, request.SasCode, ct);

            HttpContext.Session.SetString("ActiveJobId", jobId);
            HttpContext.Session.SetString("ActiveJobStatus", "Submitted");

            return Ok(new JobSubmitResponse { JobId = jobId });
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse
            {
                StatusCode = ex.StatusCode,
                Message = string.IsNullOrWhiteSpace(ex.ErrorBody)
                    ? $"The SLC Hub returned an error (HTTP {ex.StatusCode})."
                    : ex.ErrorBody
            });
        }
        catch (SlcHubConnectivityException ex)
        {
            return StatusCode(503, new ApiErrorResponse
            {
                StatusCode = 503,
                Message = ex.Message
            });
        }
    }

    // ── GET /api/jobs/{jobId}/status ─────────────────────────────────────────

    /// <summary>
    /// Returns the current status of a job from the Altair SLC Hub.
    /// Also updates the Session's ActiveJobStatus to reflect the latest value.
    /// </summary>
    /// <remarks>
    /// Satisfies Requirements 5.2, 7.5.
    /// </remarks>
    [HttpGet("{jobId}/status")]
    public async Task<IActionResult> GetStatus(string jobId, CancellationToken ct)
    {
        // Req 7.5 — 401 if no Bearer Token in Session
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
        {
            return StatusCode(401, new ApiErrorResponse
            {
                StatusCode = 401,
                Message = "Session expired or not authenticated. Please log in again."
            });
        }

        try
        {
            var status = await _hub.GetJobStatusAsync(bearerToken, jobId, ct);

            // Keep Session in sync with the latest known status.
            HttpContext.Session.SetString("ActiveJobStatus", status);

            return Ok(new JobStatusResponse { Status = status });
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse
            {
                StatusCode = ex.StatusCode,
                Message = string.IsNullOrWhiteSpace(ex.ErrorBody)
                    ? $"The SLC Hub returned an error (HTTP {ex.StatusCode})."
                    : ex.ErrorBody
            });
        }
        catch (SlcHubConnectivityException ex)
        {
            // 10-second timeout → 504 Gateway Timeout (Req 5.4)
            return StatusCode(504, new ApiErrorResponse
            {
                StatusCode = 504,
                Message = ex.Message
            });
        }
    }

    // ── GET /api/jobs/{jobId}/log ────────────────────────────────────────────

    /// <summary>
    /// Returns the program log for a job from the Altair SLC Hub.
    /// </summary>
    /// <remarks>
    /// Satisfies Requirements 6.2, 7.5.
    /// </remarks>
    [HttpGet("{jobId}/log")]
    public async Task<IActionResult> GetLog(string jobId, CancellationToken ct)
    {
        // Req 7.5 — 401 if no Bearer Token in Session
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
        {
            return StatusCode(401, new ApiErrorResponse
            {
                StatusCode = 401,
                Message = "Session expired or not authenticated. Please log in again."
            });
        }

        try
        {
            var log = await _hub.GetProgramLogAsync(bearerToken, jobId, ct);
            return Ok(new JobLogResponse { Log = log });
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse
            {
                StatusCode = ex.StatusCode,
                Message = string.IsNullOrWhiteSpace(ex.ErrorBody)
                    ? $"The SLC Hub returned an error (HTTP {ex.StatusCode})."
                    : ex.ErrorBody
            });
        }
        catch (SlcHubConnectivityException ex)
        {
            // 10-second timeout → 504 Gateway Timeout (Req 6.2)
            return StatusCode(504, new ApiErrorResponse
            {
                StatusCode = 504,
                Message = ex.Message
            });
        }
    }
}
