namespace SasJobRunner.Models;

/// <summary>
/// Response model for the program log endpoint.
/// </summary>
/// <remarks>
/// Satisfies Requirement 6.2.
/// </remarks>
public class JobLogResponse
{
    /// <summary>
    /// The full program log text for the job.
    /// </summary>
    public string Log { get; set; } = "";
}
