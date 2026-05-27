namespace SasJobRunner.Models;

/// <summary>
/// Response model for the job status endpoint.
/// </summary>
/// <remarks>
/// Satisfies Requirement 5.2 — status values: Submitted, Running, Completed, Failed, Cancelled.
/// </remarks>
public class JobStatusResponse
{
    /// <summary>
    /// The current status of the job.
    /// Valid values: Submitted, Running, Completed, Failed, Cancelled.
    /// </summary>
    public string Status { get; set; } = "";
}
