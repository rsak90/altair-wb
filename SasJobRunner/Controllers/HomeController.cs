using Microsoft.AspNetCore.Mvc;

namespace SasJobRunner.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        var token = HttpContext.Session.GetString("BearerToken");
        if (string.IsNullOrEmpty(token))
        {
            return Redirect("/account/login");
        }

        return View();
    }
}
