using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Controllers;

[Authorize]
public class ProfileController(ApplicationDbContext db, AdminWorkspaceService workspace) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await db.Users.SingleAsync(x => x.Id == UserId);
        ViewBag.IsAdminProfile = workspace.ShowAdminUi;
        return View(new ProfileForm
        {
            FullName = user.FullName,
            AcademicYear = user.AcademicYear,
            Allowance = user.AllowanceCents <= 0 ? null : user.AllowanceCents / 100m,
            SavingsGoal = user.SavingsGoalCents <= 0 ? null : user.SavingsGoalCents / 100m,
            Email = user.Email ?? ""
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(ProfileForm form)
    {
        ViewBag.IsAdminProfile = workspace.ShowAdminUi;
        if (!ModelState.IsValid) return View(form);

        var user = await db.Users.SingleAsync(x => x.Id == UserId);
        user.FullName = form.FullName.Trim();

        if (workspace.ShowAdminUi)
        {
            // Admins keep money fields as-is; title is optional (e.g. Staff).
            user.AcademicYear = string.IsNullOrWhiteSpace(form.AcademicYear) ? null : form.AcademicYear.Trim();
        }
        else
        {
            user.AcademicYear = string.IsNullOrWhiteSpace(form.AcademicYear) ? null : form.AcademicYear.Trim();
            user.AllowanceCents = (long)(decimal.Round(form.Allowance.GetValueOrDefault(), 2) * 100);
            user.SavingsGoalCents = (long)(decimal.Round(form.SavingsGoal.GetValueOrDefault(), 2) * 100);
        }

        await db.SaveChangesAsync();
        TempData["Success"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Index));
    }
}
