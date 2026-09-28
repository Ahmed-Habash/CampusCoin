using Microsoft.AspNetCore.Mvc;
namespace CampusCoin.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View();
    public IActionResult Error() => View();
}
