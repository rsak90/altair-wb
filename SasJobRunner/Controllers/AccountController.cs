using Microsoft.AspNetCore.Mvc;
using SasJobRunner.Models;
using SasJobRunner.Services;

namespace SasJobRunner.Controllers;

/// <summary>
/// MVC controller that handles the login/logout UI flows.
/// </summary>
/// <remarks>
/// Satisfies Requirements 1.1, 1.3, 1.4, 1.5
/// </remarks>
public class AccountController : Controller
{
    private readonly SlcHubClient _hubClient;

    public AccountController(SlcHubClient hubClient)
    {
        _hubClient = hubClient;
    }

    /// <summary>
    /// GET /account/login — renders the login form.
    /// </summary>
    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }

    /// <summary>
    /// POST /account/login — authenticates against the Altair SLC Hub.
    /// On success, stores the Bearer Token in Session and redirects to the home page.
    /// On failure, re-renders the login view with an error message.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string token;
        try
        {
            token = await _hubClient.LoginAsync(model.Username, model.Password, ct);
        }
        catch (SlcHubException ex)
        {
            // Hub returned a 4xx/5xx — surface the Hub's error body to the user.
            model.ErrorMessage = string.IsNullOrWhiteSpace(ex.ErrorBody)
                ? $"Authentication failed (HTTP {ex.StatusCode})."
                : ex.ErrorBody;
            return View(model);
        }
        catch (SlcHubConnectivityException ex)
        {
            // Network failure or timeout — show a generic connectivity message.
            model.ErrorMessage = ex.Message;
            return View(model);
        }

        // Store the token server-side; the browser never sees it.
        HttpContext.Session.SetString("BearerToken", token);

        return Redirect("/");
    }

    /// <summary>
    /// POST /account/logout — clears the Session and redirects to the login page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login));
    }
}
