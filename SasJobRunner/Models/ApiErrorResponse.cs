namespace SasJobRunner.Models;

/// <summary>
/// Structured JSON error response returned by internal API controllers
/// for any error condition (Hub errors, network failures, auth failures, etc.).
/// </summary>
/// <remarks>
/// Satisfies Requirements 7.3, 7.4, 7.5 — every error path returns a body
/// that deserializes to this type with a non-zero <see cref="StatusCode"/>
/// and a non-empty <see cref="Message"/>.
/// </remarks>
public class ApiErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = "";
}
