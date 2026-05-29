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
    private readonly IConfiguration _config;

    public JobApiController(SlcHubClient hub, IConfiguration config)
    {
        _hub = hub;
        _config = config;
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

    // ── Save helper ──────────────────────────────────────────────────────────

    private sealed record SaveResult(string? FilePath, string? FileName, string? Error);

    /// <summary>
    /// Writes <paramref name="sasCode"/> to a timestamped .sas file under
    /// <c>SasOutput:NetworkPath</c> and returns the full path and file name.
    /// Returns an error message string on failure.
    /// </summary>
    private async Task<SaveResult> SaveSasFileAsync(string sasCode, CancellationToken ct)
    {
        var networkPath = _config["SasOutput:NetworkPath"];
        if (string.IsNullOrWhiteSpace(networkPath))
            return new SaveResult(null, null, "SasOutput:NetworkPath is not configured in appsettings.json.");

        try
        {
            Directory.CreateDirectory(networkPath);
            var fileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}.sas";
            var fullPath = Path.Combine(networkPath, fileName);
            await System.IO.File.WriteAllTextAsync(fullPath, sasCode, System.Text.Encoding.UTF8, ct);
            return new SaveResult(fullPath, fileName, null);
        }
        catch (Exception ex)
        {
            return new SaveResult(null, null, $"Failed to save SAS file: {ex.Message}");
        }
    }

    // ── POST /api/jobs/submit ────────────────────────────────────────────────

    /// <summary>
    /// Creates AND commits a SAS job in one step (legacy combined endpoint).
    /// Saves the SAS code to the configured network path first, then submits
    /// the saved file path to the Hub.
    /// </summary>
    [HttpPost("submit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] JobSubmitRequest request, CancellationToken ct)
    {
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
            return StatusCode(401, new ApiErrorResponse { StatusCode = 401, Message = "Session expired or not authenticated. Please log in again." });

        var activeStatus = HttpContext.Session.GetString("ActiveJobStatus");
        if (activeStatus is "Submitted" or "Running")
            return StatusCode(409, new ApiErrorResponse { StatusCode = 409, Message = "A job is already active." });

        if (Request.ContentLength.HasValue && Request.ContentLength.Value > 1_048_576)
            return StatusCode(413, new ApiErrorResponse { StatusCode = 413, Message = "The SAS program exceeds the maximum allowed size of 1 MB." });

        // Save to network path first, then submit the file path to the Hub.
        var saveResult = await SaveSasFileAsync(request.SasCode, ct);
        if (saveResult.Error is not null)
            return StatusCode(500, new ApiErrorResponse { StatusCode = 500, Message = saveResult.Error });

        try
        {
            var jobId = await _hub.CreateJobFromFileAsync(bearerToken, saveResult.FilePath!, ct);
            await _hub.CommitJobAsync(bearerToken, jobId, ct);
            HttpContext.Session.SetString("ActiveJobId", jobId);
            HttpContext.Session.SetString("ActiveJobStatus", "Submitted");
            HttpContext.Session.SetString("ActiveJobFilePath", saveResult.FilePath!);
            return Ok(new JobSubmitResponse { JobId = jobId });
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { StatusCode = ex.StatusCode, Message = string.IsNullOrWhiteSpace(ex.ErrorBody) ? $"Hub error (HTTP {ex.StatusCode})." : ex.ErrorBody });
        }
        catch (SlcHubConnectivityException ex)
        {
            return StatusCode(503, new ApiErrorResponse { StatusCode = 503, Message = ex.Message });
        }
    }

    // ── POST /api/jobs/create ────────────────────────────────────────────────

    /// <summary>
    /// Step 1: Saves the SAS code to the configured network path, then creates
    /// a job on the Hub referencing that file. The job is not yet scheduled.
    /// Returns the job ID and the saved file path.
    /// </summary>
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] JobSubmitRequest request, CancellationToken ct)
    {
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
            return StatusCode(401, new ApiErrorResponse { StatusCode = 401, Message = "Session expired or not authenticated. Please log in again." });

        var activeStatus = HttpContext.Session.GetString("ActiveJobStatus");
        if (activeStatus is "Submitted" or "Running" or "Created")
            return StatusCode(409, new ApiErrorResponse { StatusCode = 409, Message = "A job is already active. Cancel or wait for it to finish before creating a new one." });

        if (Request.ContentLength.HasValue && Request.ContentLength.Value > 1_048_576)
            return StatusCode(413, new ApiErrorResponse { StatusCode = 413, Message = "The SAS program exceeds the maximum allowed size of 1 MB." });

        // Save to network path first, then create the job referencing that file.
        var saveResult = await SaveSasFileAsync(request.SasCode, ct);
        if (saveResult.Error is not null)
            return StatusCode(500, new ApiErrorResponse { StatusCode = 500, Message = saveResult.Error });

        try
        {
            var jobId = await _hub.CreateJobFromFileAsync(bearerToken, saveResult.FilePath!, ct);
            HttpContext.Session.SetString("ActiveJobId", jobId);
            HttpContext.Session.SetString("ActiveJobStatus", "Created");
            HttpContext.Session.SetString("ActiveJobFilePath", saveResult.FilePath!);
            return Ok(new { jobId, filePath = saveResult.FilePath, fileName = saveResult.FileName });
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { StatusCode = ex.StatusCode, Message = string.IsNullOrWhiteSpace(ex.ErrorBody) ? $"Hub error (HTTP {ex.StatusCode})." : ex.ErrorBody });
        }
        catch (SlcHubConnectivityException ex)
        {
            return StatusCode(503, new ApiErrorResponse { StatusCode = 503, Message = ex.Message });
        }
    }

    // ── POST /api/jobs/{jobId}/commit ────────────────────────────────────────

    /// <summary>
    /// Step 2: Commits a previously created job, making it eligible for scheduling.
    /// Transitions session status from Created → Submitted and starts polling.
    /// </summary>
    [HttpPost("{jobId}/commit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Commit(string jobId, CancellationToken ct)
    {
        var bearerToken = GetBearerToken();
        if (bearerToken is null)
            return StatusCode(401, new ApiErrorResponse { StatusCode = 401, Message = "Session expired or not authenticated. Please log in again." });

        try
        {
            await _hub.CommitJobAsync(bearerToken, jobId, ct);
            HttpContext.Session.SetString("ActiveJobStatus", "Submitted");
            return Ok();
        }
        catch (SlcHubException ex)
        {
            return StatusCode(ex.StatusCode, new ApiErrorResponse { StatusCode = ex.StatusCode, Message = string.IsNullOrWhiteSpace(ex.ErrorBody) ? $"Hub error (HTTP {ex.StatusCode})." : ex.ErrorBody });
        }
        catch (SlcHubConnectivityException ex)
        {
            return StatusCode(503, new ApiErrorResponse { StatusCode = 503, Message = ex.Message });
        }
    }

    // ── DELETE /api/jobs/{jobId}/cancel ─────────────────────────────────────

    /// <summary>
    /// Cancels an active job on the Altair SLC Hub.
    /// On success, clears the active job from Session.
    /// </summary>
    /// <remarks>
    /// Satisfies Requirements 4.2, 7.5, 7.6.
    /// </remarks>
    [HttpDelete("{jobId}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string jobId, CancellationToken ct)
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
            await _hub.CancelJobAsync(bearerToken, jobId, ct);

            // Clear active job from Session on successful cancellation (Req 4.2).
            HttpContext.Session.Remove("ActiveJobId");
            HttpContext.Session.Remove("ActiveJobStatus");
            HttpContext.Session.Remove("ActiveJobFilePath");

            return Ok();
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
            // 30-second timeout → 503 (Req 4.6 — continue polling on timeout)
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
