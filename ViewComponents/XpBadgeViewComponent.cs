using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.ViewComponents;

public class XpBadgeViewComponent(FinancialXpService xpService, ApplicationDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Content(string.Empty);
        var enabled = await db.UserSettings.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (bool?)x.XpProgressEnabled)
            .FirstOrDefaultAsync() ?? true;
        if (!enabled) return Content(string.Empty);
        var xp = await xpService.GetAsync(userId);
        return View(xp);
    }
}
