namespace SasJobRunner.Services;

/// <summary>
/// Thrown when the Altair SLC Hub returns a 4xx or 5xx HTTP response.
/// Carries the HTTP status code and the error body returned by the Hub.
/// </summary>
public class SlcHubException : Exception
{
    /// <summary>The HTTP status code returned by the Hub.</summary>
    public int StatusCode { get; }

    /// <summary>The raw error body returned by the Hub (may be empty).</summary>
    public string ErrorBody { get; }

    public SlcHubException(int statusCode, string errorBody)
        : base($"SLC Hub returned HTTP {statusCode}: {errorBody}")
    {
        StatusCode = statusCode;
        ErrorBody = errorBody;
    }
}

/// <summary>
/// Thrown when the Altair SLC Hub is unreachable due to a network-level failure
/// or a request timeout.
/// </summary>
public class SlcHubConnectivityException : Exception
{
    public SlcHubConnectivityException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
