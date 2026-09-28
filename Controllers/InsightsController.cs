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
public class InsightsController(InsightsService insights, ApplicationDbContext db, FinancialXpService xpService) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(DateOnly? month)
    {
        var selected = month ?? new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        return View(await insights.BuildAsync(UserId, selected));
    }

    [HttpPost]
    public async Task<IActionResult> CreateGoal(GoalForm form)
    {
        if (!ModelState.IsValid) { TempData["Error"] = "Add a goal name and a target amount."; return RedirectToAction(nameof(Index)); }
        db.SavingsGoals.Add(new SavingsGoal { UserId = UserId, Name = form.Name.Trim(), TargetCents = (long)(decimal.Round(form.Target, 2) * 100), Deadline = form.Deadline });
        await db.SaveChangesAsync();
        TempData["Success"] = "Savings goal added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddToGoal(int id, decimal amount)
    {
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId && !x.IsComplete);
        if (goal == null || amount <= 0) return NotFound();
        goal.SavedCents += (long)(decimal.Round(amount, 2) * 100);
        if (goal.SavedCents >= goal.TargetCents) { goal.SavedCents = goal.TargetCents; goal.IsComplete = true; }
        await db.SaveChangesAsync();
        if (goal.IsComplete) await xpService.AwardSavingsGoalAsync(UserId, goal.Id);
        TempData["Success"] = goal.IsComplete ? "Goal completed — congratulations!" : "Progress added to your goal.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteGoal(int id)
    {
        var goal = await db.SavingsGoals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (goal == null) return NotFound();
        db.SavingsGoals.Remove(goal);
        await db.SaveChangesAsync();
        TempData["Success"] = "Goal removed.";
        return RedirectToAction(nameof(Index));
    }
}
