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
    /// Executes <paramref name="operation"/> and converts any
    /// <see cref="OperationCanceledException"/> into a <see cref="SlcHubConnectivityException"/>
    /// so callers always receive a typed, user-friendly error rather than a raw cancellation.
    /// </summary>
    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation, string timeoutMessage)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException ex)
        {
            throw new SlcHubConnectivityException(timeoutMessage, ex);
        }
    }

    private static async Task GuardAsync(Func<Task> operation, string timeoutMessage)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException ex)
        {
            throw new SlcHubConnectivityException(timeoutMessage, ex);
        }
    }

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
        return await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var request = new HttpRequestMessage(HttpMethod.Post, Url("auth/login"))
            {
                Content = JsonContent.Create(new { username, password }, options: _jsonOptions)
            };

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions, CancellationToken.None);
            if (result?.Token is null or "")
                throw new SlcHubConnectivityException(
                    "The SLC Hub returned a success response but did not include a token.");

            return result.Token;
        }, "The login request timed out or was cancelled.");
    }

    /// <summary>
    /// Creates a job on the Altair SLC Hub using an inline SAS code string (step 1 of 2).
    /// Returns the job ID. The job is NOT yet scheduled — call CommitJobAsync to schedule it.
    /// </summary>
    public async Task<string> CreateJobAsync(string bearerToken, string sasCode, CancellationToken ct)
    {
        return await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var body = new
            {
                @namespace = _namespace,
                executionProfileId = _executionProfileId,
                task = new
                {
                    type = "slc",
                    programSource = new { type = "inline", code = sasCode }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, Url("jobs"))
            {
                Content = JsonContent.Create(body, options: _jsonOptions)
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }

            var created = await response.Content.ReadFromJsonAsync<JobDto>(_jsonOptions, CancellationToken.None);
            if (created?.Id is null or "")
                throw new SlcHubConnectivityException(
                    "The SLC Hub created the job but did not return a job ID.");

            return created.Id;
        }, "The job creation request timed out or was cancelled.");
    }

    /// <summary>
    /// Creates a job on the Altair SLC Hub using a saved .sas file path (step 1 of 2).
    /// Sends <c>programSource.type = "path"</c> so the Hub reads the program from the file.
    /// Returns the job ID. The job is NOT yet scheduled — call CommitJobAsync to schedule it.
    /// </summary>
    public async Task<string> CreateJobFromFileAsync(string bearerToken, string filePath, CancellationToken ct)
    {
        return await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var body = new
            {
                @namespace = _namespace,
                executionProfileId = _executionProfileId,
                task = new
                {
                    type = "slc",
                    programSource = new { type = "path", path = filePath }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, Url("jobs"))
            {
                Content = JsonContent.Create(body, options: _jsonOptions)
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }

            var created = await response.Content.ReadFromJsonAsync<JobDto>(_jsonOptions, CancellationToken.None);
            if (created?.Id is null or "")
                throw new SlcHubConnectivityException(
                    "The SLC Hub created the job but did not return a job ID.");

            return created.Id;
        }, "The job creation request timed out or was cancelled.");
    }

    /// <summary>
    /// Commits a previously created job (step 2 of 2), making it eligible for scheduling.
    /// </summary>
    public async Task CommitJobAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var request = new HttpRequestMessage(HttpMethod.Post, Url($"jobs/{jobId}/commit"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub during job commit. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }
        }, "The job commit request timed out or was cancelled.");
    }

    /// <summary>
    /// Submits a SAS program to the Altair SLC Hub and returns the Job ID.
    /// Follows the two-step protocol: POST /jobs to create, then POST /jobs/{id}/commit to schedule.
    /// </summary>
    public async Task<string> SubmitJobAsync(string bearerToken, string sasCode, CancellationToken ct)
    {
        var jobId = await CreateJobAsync(bearerToken, sasCode, ct);
        await CommitJobAsync(bearerToken, jobId, ct);
        return jobId;
    }

    /// <summary>
    /// Requests cancellation of an active job on the Altair SLC Hub.
    /// Uses POST /jobs/{jobId}/cancel per the API spec.
    /// </summary>
    public async Task CancelJobAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var request = new HttpRequestMessage(HttpMethod.Post, Url($"jobs/{jobId}/cancel"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }
        }, "The cancel request timed out or was cancelled.");
    }

    /// <summary>
    /// Retrieves the current status of a job and maps the Hub's state values to
    /// the internal states used by the UI: Submitted, Running, Completed, Failed, Cancelled.
    /// </summary>
    public async Task<string> GetJobStatusAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        return await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var request = new HttpRequestMessage(HttpMethod.Get, Url($"jobs/{jobId}/status"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }

            var result = await response.Content.ReadFromJsonAsync<JobStatusDto>(_jsonOptions, CancellationToken.None);
            if (result?.State is null or "")
                throw new SlcHubConnectivityException(
                    "The SLC Hub returned a success response but did not include a job state.");

            return result.State switch
            {
                "Creating" or "Pending" => "Submitted",
                "Executing"             => "Running",
                "CompletedSuccess"      => "Completed",
                "CompletedError"        => "Failed",
                "Failed"                => "Failed",
                "Cancelled"             => "Cancelled",
                _                       => result.State
            };
        }, "The status poll request timed out or was cancelled.");
    }

    /// <summary>
    /// Retrieves the stdout program log for a job.
    /// The Hub returns plain text at GET /jobs/{jobId}/logs/stdout.
    /// </summary>
    public async Task<string> GetProgramLogAsync(string bearerToken, string jobId, CancellationToken ct)
    {
        return await GuardAsync(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var request = new HttpRequestMessage(HttpMethod.Get, Url($"jobs/{jobId}/logs/stdout"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, cts.Token); }
            catch (HttpRequestException ex)
            {
                throw new SlcHubConnectivityException(
                    "Unable to reach the SLC Hub. Check your network connection.", ex);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return "";

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                throw new SlcHubException((int)response.StatusCode, errorBody);
            }

            return await response.Content.ReadAsStringAsync(CancellationToken.None);
        }, "The log fetch request timed out or was cancelled.");
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
