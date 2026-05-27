namespace SasJobRunner.Services;

/// <summary>
/// DelegatingHandler that logs the outgoing request body and incoming response body
/// for every Hub call. Helps diagnose 400 Bad Request errors by showing exactly
/// what JSON was sent and what the Hub replied with.
/// Output goes to the dotnet run console.
/// </summary>
public class DiagnosticLoggingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // ── Log outgoing request ─────────────────────────────────────────────
        Console.WriteLine($"\n[HUB >>>] {request.Method} {request.RequestUri}");
        if (request.Content is not null)
        {
            var reqBody = await request.Content.ReadAsStringAsync(ct);
            Console.WriteLine($"[HUB >>>] Body: {reqBody}");
            // Re-set the content so HttpClient can still send it
            request.Content = new StringContent(reqBody,
                System.Text.Encoding.UTF8,
                request.Content.Headers.ContentType?.MediaType ?? "application/json");
        }

        // ── Send and log response ────────────────────────────────────────────
        var response = await base.SendAsync(request, ct);

        Console.WriteLine($"[HUB <<<] {(int)response.StatusCode} {response.StatusCode}");
        var respBody = await response.Content.ReadAsStringAsync(ct);
        Console.WriteLine($"[HUB <<<] Body: {respBody}");

        // Re-set the response content so callers can still read it
        response.Content = new StringContent(respBody,
            System.Text.Encoding.UTF8,
            response.Content.Headers.ContentType?.MediaType ?? "application/json");

        return response;
    }
}
