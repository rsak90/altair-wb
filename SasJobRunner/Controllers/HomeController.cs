using Microsoft.AspNetCore.Mvc;

namespace SasJobRunner.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// GET / — renders the Configure Output screen.
    /// Redirects to login with session-expired message if no Bearer Token in Session (Req 1.6, 1.8).
    /// </summary>
    public IActionResult Index()
    {
        var token = HttpContext.Session.GetString("BearerToken");
        if (string.IsNullOrEmpty(token))
        {
            return Redirect("/account/login?expired=true");
        }

        return View();
    }
}
