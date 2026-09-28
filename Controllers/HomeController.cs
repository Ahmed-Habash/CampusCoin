using Microsoft.AspNetCore.Mvc;
namespace CampusCoin.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
