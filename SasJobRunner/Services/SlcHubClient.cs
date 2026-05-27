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

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SlcHubClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        var baseUrl = configuration["SlcHub:BaseUrl"]
            ?? throw new InvalidOperationException("SlcHub:BaseUrl is not configured.");
        _http.BaseAddress = new Uri(baseUrl);
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
        // Link a 30-second timeout to the caller's cancellation token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var requestBody = new { username, password };

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The linked CTS timed out (not the caller's token).
            throw new SlcHubConnectivityException(
                "The login request timed out after 30 seconds.", ex);
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
    /// </summary>
    /// <param name="bearerToken">The Bearer Token obtained from <see cref="LoginAsync"/>.</param>
    /// <param name="sasCode">The SAS program source code to execute.</param>
    /// <param name="ct">Caller-supplied cancellation token.</param>
    /// <returns>The Job ID string on success.</returns>
    /// <exception cref="SlcHubException">Thrown when the Hub returns a 4xx or 5xx response.</exception>
    /// <exception cref="SlcHubConnectivityException">Thrown on timeout or network-level failure.</exception>
    public async Task<string> SubmitJobAsync(string bearerToken, string sasCode, CancellationToken ct)
    {
        // Link a 30-second timeout to the caller's cancellation token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var requestBody = new { sasCode };

        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/jobs")
            {
                Content = JsonContent.Create(requestBody, options: _jsonOptions)
            };
            // Set Authorization on the individual message, not DefaultRequestHeaders,
            // to avoid race conditions under concurrent requests.
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            response = await _http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The linked CTS timed out (not the caller's token).
            throw new SlcHubConnectivityException(
                "The job submission request timed out after 30 seconds.", ex);
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

        // Deserialize { "jobId": "..." }
        var submitResponse = await response.Content.ReadFromJsonAsync<SubmitJobResponse>(
            _jsonOptions, ct);

        if (submitResponse?.JobId is null or "")
        {
            throw new SlcHubConnectivityException(
                "The SLC Hub returned a success response but did not include a job ID.");
        }

        return submitResponse.JobId;
    }

    // DTO for the Hub's login response body.
    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    // DTO for the Hub's job submission response body.
    private sealed class SubmitJobResponse
    {
        [JsonPropertyName("jobId")]
        public string? JobId { get; set; }
    }
}
