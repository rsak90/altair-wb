using Microsoft.AspNetCore.Mvc;

namespace SasJobRunner.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// GET / — renders the Output (Configure Output) screen.
    /// Redirects to login with session-expired message if no Bearer Token in Session.
    /// </summary>
    public IActionResult Index()
    {
        var token = HttpContext.Session.GetString("BearerToken");
        if (string.IsNullOrEmpty(token))
            return Redirect("/account/login?expired=true");

        return View();
    }

    /// <summary>
    /// GET /Home/BatchRun — placeholder for the Batch Run screen.
    /// </summary>
    public IActionResult BatchRun()
    {
        var token = HttpContext.Session.GetString("BearerToken");
        if (string.IsNullOrEmpty(token))
            return Redirect("/account/login?expired=true");

        ViewData["Title"] = "Batch Run";
        return View();
    }
}
