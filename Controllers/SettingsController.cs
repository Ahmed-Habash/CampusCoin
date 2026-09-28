using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Controllers;

[Authorize]
public class SettingsController(ApplicationDbContext db, IDataProtectionProvider protection) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IDataProtector Protector => protection.CreateProtector("CampusCoin.UserAiKeys.v1");

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId);
        var isAdmin = User.IsInRole("Administrator");
        var pendingAdmin = !isAdmin
            && await db.AdminApplications.AsNoTracking().AnyAsync(a => a.UserId == UserId && a.Status == "Pending");
        return View(new SettingsViewModel
        {
            AiProvider = settings?.AiProvider ?? "Auto",
            CurrencyCode = settings?.CurrencyCode ?? "Auto",
            AdminCurrencyMode = string.Equals(settings?.AdminCurrencyMode, "PerUser", StringComparison.OrdinalIgnoreCase) ? "PerUser" : "Fixed",
            HasApiKey = !string.IsNullOrWhiteSpace(settings?.EncryptedApiKey),
            HasPendingAdminRequest = pendingAdmin,
            IsAdministrator = isAdmin,
            FutureYouMessagesEnabled = settings?.FutureYouMessagesEnabled ?? true,
            XpProgressEnabled = settings?.XpProgressEnabled ?? true
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(SettingsViewModel form)
    {
        form.IsAdministrator = User.IsInRole("Administrator");
        if (!ModelState.IsValid) return View(form);
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == UserId) ?? new UserSetting { UserId = UserId };
        settings.AiProvider = form.AiProvider;
        settings.CurrencyCode = form.CurrencyCode;
        if (Request.Form.ContainsKey(nameof(SettingsViewModel.FutureYouMessagesEnabled)))
            settings.FutureYouMessagesEnabled = form.FutureYouMessagesEnabled;
        if (Request.Form.ContainsKey(nameof(SettingsViewModel.XpProgressEnabled)))
            settings.XpProgressEnabled = form.XpProgressEnabled;
        if (form.IsAdministrator)
            settings.AdminCurrencyMode = form.AdminCurrencyMode == "PerUser" ? "PerUser" : "Fixed";
        if (form.RemoveApiKey) settings.EncryptedApiKey = null;
        else if (!string.IsNullOrWhiteSpace(form.ApiKey)) settings.EncryptedApiKey = Protector.Protect(form.ApiKey.Trim());
        settings.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(settings).State == EntityState.Detached) db.UserSettings.Add(settings);
        await db.SaveChangesAsync();
        TempData["Success"] = "Settings saved. Your preferences are ready.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCurrency(string currencyCode, string? adminCurrencyMode)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Auto", "USD", "QAR", "EUR", "GBP", "AED", "SAR", "PKR", "INR", "CAD", "AUD" };
        if (string.IsNullOrWhiteSpace(currencyCode) || !allowed.Contains(currencyCode))
            return BadRequest(new { ok = false, error = "Choose a valid currency." });

        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == UserId) ?? new UserSetting { UserId = UserId };
        settings.CurrencyCode = currencyCode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "Auto" : currencyCode.ToUpperInvariant();
        if (User.IsInRole("Administrator") && !string.IsNullOrWhiteSpace(adminCurrencyMode))
            settings.AdminCurrencyMode = adminCurrencyMode == "PerUser" ? "PerUser" : "Fixed";
        settings.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(settings).State == EntityState.Detached) db.UserSettings.Add(settings);
        await db.SaveChangesAsync();
        return Json(new { ok = true, currencyCode = settings.CurrencyCode, adminCurrencyMode = settings.AdminCurrencyMode });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveXpPreference(bool xpProgressEnabled)
    {
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == UserId) ?? new UserSetting { UserId = UserId };
        settings.XpProgressEnabled = xpProgressEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(settings).State == EntityState.Detached) db.UserSettings.Add(settings);
        await db.SaveChangesAsync();
        return Json(new { ok = true, xpProgressEnabled = settings.XpProgressEnabled });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFutureYouPreference(bool futureYouMessagesEnabled)
    {
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == UserId) ?? new UserSetting { UserId = UserId };
        settings.FutureYouMessagesEnabled = futureYouMessagesEnabled;
        settings.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(settings).State == EntityState.Detached) db.UserSettings.Add(settings);
        await db.SaveChangesAsync();
        return Json(new { ok = true, futureYouMessagesEnabled = settings.FutureYouMessagesEnabled });
    }

    [HttpGet]
    public IActionResult ApplyForAdmin() => RedirectToAction(nameof(Index));

    [HttpPost]
    public async Task<IActionResult> ApplyForAdmin(string reason)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var isAdmin = User.IsInRole("Administrator");
        if (isAdmin)
        {
            TempData["Error"] = "You are already an Administrator.";
            return RedirectToAction(nameof(Index));
        }

        var existingPending = await db.AdminApplications.AnyAsync(a => a.UserId == userId && a.Status == "Pending");
        if (existingPending)
            return RedirectToAction(nameof(Index));

        db.AdminApplications.Add(new AdminApplication
        {
            UserId = userId,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Requested admin access for campus management." : reason.Trim(),
            Status = "Pending",
            AppliedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        // Status is shown on the Settings card — not as a flash toast.
        return RedirectToAction(nameof(Index));
    }
}
