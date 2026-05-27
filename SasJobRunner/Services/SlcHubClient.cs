using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SasJobRunner.Services;

/// <summary>
/// Typed HttpClient wrapper for the Altair SLC Hub API.
/// </summary>
public class SlcHubClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _namespace;
    private readonly string _executionProfileId;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SlcHubClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        var baseUrl = configuration["SlcHub:BaseUrl"]
            ?? throw new InvalidOperationException("SlcHub:BaseUrl is not configured.");
        _namespace = configuration["SlcHub:Namespace"]
            ?? throw new InvalidOperationException("SlcHub:Namespace is not configured.");
        _executionProfileId = configuration["SlcHub:ExecutionProfileId"]
            ?? throw new InvalidOperationException("SlcHub:ExecutionProfileId is not configured.");

        _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/';
    }

    /// <summary>Builds an absolute URL by appending <paramref name="relativePath"/> to the base URL.</summary>
    private string Url(string relativePath) => _baseUrl + relativePath;

    /// <summary>
    /// Authenticates against the Altair SLC Hub and returns the Bearer Token.
    /// </summary>
    /// <param name="username">The user's username.</param>
    /// <param name="password">The user's password.</param>
    /// <param name="ct">Caller-supplied cancellation token.</param>
    /// <returns>The Bearer Token string on success.</returns>
    /// <exception cref="SlcHubException">Thrown when the Hub returns a 4xx or 5xx response.</exception>
    /// <exception cref="SlcHubConnectivityException">Thrown on timeout or network-level failure.</exception>
    public async Task<string> LoginAsync(string username, string password, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var requestBody = new { username, password };

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Url("auth/login"))
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our timeout fired — the caller did not cancel.
            throw new SlcHubConnectivityException(
                "The login request timed out after 30 seconds.", ex);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            // Either our timeout or the caller cancelled — treat both as connectivity failure
            // so the controller can show a user-friendly message instead of a raw exception.
            throw new SlcHubConnectivityException(
                "The login request was cancelled or timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub. Check your network connection.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException((int)response.StatusCode, errorBody);
        }

        // Deserialize { "token": "..." }
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(
            _jsonOptions, ct);

        if (loginResponse?.Token is null or "")
        {
            throw new SlcHubConnectivityException(
                "The SLC Hub returned a success response but did not include a token.");
        }

        return loginResponse.Token;
    }

    /// <summary>
    /// Submits a SAS program to the Altair SLC Hub and returns the Job ID.
    /// Follows the two-step protocol: POST /jobs to create, then POST /jobs/{id}/commit to schedule.
    /// </summary>
    public async Task<string> SubmitJobAsync(string bearerToken, string sasCode, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // ── Step 1: Create the job ───────────────────────────────────────────
        var createBody = new
        {
            @namespace = _namespace,
            executionProfileId = _executionProfileId,
            task = new
            {
                type = "slc",
                programSource = new
                {
                    type = "inline",
                    code = sasCode
                }
            }
        };

        HttpResponseMessage createResponse;
        try
        {
            var createRequest = new HttpRequestMessage(HttpMethod.Post, Url("jobs"))
            {
                Content = JsonContent.Create(createBody, options: _jsonOptions)
            };
            createRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            createResponse = await _http.SendAsync(createRequest, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new SlcHubConnectivityException(
                "The job submission request timed out after 30 seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub. Check your network connection.", ex);
        }

        if (!createResponse.IsSuccessStatusCode)
        {
            var errorBody = await createResponse.Content.ReadAsStringAsync(ct);
            throw new SlcHubException((int)createResponse.StatusCode, errorBody);
        }

        var created = await createResponse.Content.ReadFromJsonAsync<JobDto>(_jsonOptions, ct);
        if (created?.Id is null or "")
            throw new SlcHubConnectivityException(
                "The SLC Hub created the job but did not return a job ID.");

        var jobId = created.Id;

        // ── Step 2: Commit the job (makes it eligible for scheduling) ────────
        try
        {
            var commitRequest = new HttpRequestMessage(HttpMethod.Post, Url($"jobs/{jobId}/commit"));
            commitRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            var commitResponse = await _http.SendAsync(commitRequest, cts.Token);

            if (!commitResponse.IsSuccessStatusCode)
            {
                var errorBody = await commitResponse.Content.ReadAsStringAsync(ct);
                throw new SlcHubException((int)commitResponse.StatusCode, errorBody);
            }
        }
        catch (SlcHubException) { throw; }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new SlcHubConnectivityException(
                "The job commit request timed out after 30 seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub during job commit. Check your network connection.", ex);
        }

        return jobId;
    }

    /// <summary>
    /// Requests cancellation of an active job on the Altair SLC Hub.
    /// Uses POST /jobs/{jobId}/cancel per the API spec.
    /// </summary>
    public async Task CancelJobAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Url($"jobs/{jobId}/cancel"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new SlcHubConnectivityException(
                "The cancel request timed out after 30 seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub. Check your network connection.", ex);
        }

        // 202 Accepted is the success response per the API spec.
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException((int)response.StatusCode, errorBody);
        }
    }

    /// <summary>
    /// Retrieves the current status of a job and maps the Hub's state values to
    /// the internal states used by the UI: Submitted, Running, Completed, Failed, Cancelled.
    /// </summary>
    public async Task<string> GetJobStatusAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Url($"jobs/{jobId}/status"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new SlcHubConnectivityException(
                "The status poll request timed out after 10 seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub. Check your network connection.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException((int)response.StatusCode, errorBody);
        }

        var statusResponse = await response.Content.ReadFromJsonAsync<JobStatusDto>(_jsonOptions, ct);

        if (statusResponse?.State is null or "")
            throw new SlcHubConnectivityException(
                "The SLC Hub returned a success response but did not include a job state.");

        // Map Hub state values → internal UI state values
        return statusResponse.State switch
        {
            "Creating" or "Pending"  => "Submitted",
            "Executing"              => "Running",
            "CompletedSuccess"       => "Completed",
            "CompletedError"         => "Failed",
            "Failed"                 => "Failed",
            "Cancelled"              => "Cancelled",
            _                        => statusResponse.State   // pass through unknowns
        };
    }

    /// <summary>
    /// Retrieves the stdout program log for a job.
    /// The Hub returns plain text at GET /jobs/{jobId}/logs/stdout.
    /// </summary>
    public async Task<string> GetProgramLogAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Url($"jobs/{jobId}/logs/stdout"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            throw new SlcHubConnectivityException(
                "The log fetch request timed out after 10 seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SlcHubConnectivityException(
                "Unable to reach the SLC Hub. Check your network connection.", ex);
        }

        // 404 while the job is still starting up means no log yet — return empty.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return "";

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new SlcHubException((int)response.StatusCode, errorBody);
        }

        // Response is plain text, not JSON.
        return await response.Content.ReadAsStringAsync(ct);
    }

    // DTO for the Hub's login response body.
    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    // DTO for the Hub's job create/commit response body.
    // The full job object is returned; we only need the id.
    private sealed class JobDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }

    // DTO for GET /jobs/{jobId}/status response.
    // The Hub returns { "state": "...", ... }
    private sealed class JobStatusDto
    {
        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
