using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Controllers;

[Authorize]
public class BudgetsController(ApplicationDbContext db, DashboardService service) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index(DateOnly? month)
    {
        if (!ModelState.IsValid || month.HasValue && (month.Value.Year < 2000 || month.Value.Year > 2100))
            return BadRequest("Choose a valid month between 2000 and 2100.");
        return View(await service.BuildAsync(UserId, month ?? DateOnly.FromDateTime(DateTime.Today)));
    }
    private async Task Choices() => ViewBag.Categories = new SelectList(await db.Categories.Where(x => !x.IsArchived && x.Type == CategoryType.Expense && (x.UserId == null || x.UserId == UserId)).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        var f = new BudgetForm();
        if (id.HasValue) { var b = await db.Budgets.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId); if (b == null) return NotFound(); f = new BudgetForm { Id = b.Id, CategoryId = b.CategoryId, Month = b.Month, Amount = b.LimitCents / 100m }; }
        await Choices(); return View(f);
    }
    [HttpPost]
    public async Task<IActionResult> Edit(BudgetForm form)
    {
        var b = form.Id == 0 ? new Budget { UserId = UserId } : await db.Budgets.SingleOrDefaultAsync(x => x.Id == form.Id && x.UserId == UserId); if (b == null) return NotFound();
        var month = new DateOnly(form.Month.Year, form.Month.Month, 1);
        if (month.Year < 2000 || month.Year > 2100) ModelState.AddModelError("Month", "Choose a month between 2000 and 2100.");
        if (form.Amount is null) ModelState.AddModelError("Amount", "Enter an amount.");
        else if (form.Amount != decimal.Round(form.Amount.Value, 2)) ModelState.AddModelError("Amount", "Use at most two decimal places.");
        if (!await db.Categories.AnyAsync(x => x.Id == form.CategoryId && !x.IsArchived && x.Type == CategoryType.Expense && (x.UserId == null || x.UserId == UserId))) ModelState.AddModelError("CategoryId", "Choose an expense category.");
        if (await db.Budgets.AnyAsync(x => x.Id != form.Id && x.UserId == UserId && x.CategoryId == form.CategoryId && x.Month == month)) ModelState.AddModelError("CategoryId", "This category already has a budget for that month. Edit it instead.");
        if (!ModelState.IsValid) { await Choices(); return View(form); }
        b.CategoryId = form.CategoryId; b.Month = month; b.LimitCents = (long)(form.Amount!.Value * 100); if (b.Id == 0) db.Budgets.Add(b);
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { ModelState.AddModelError("", "Unable to save. A budget may already exist for this category and month."); await Choices(); return View(form); }
        TempData["Success"] = "Monthly budget saved."; return RedirectToAction(nameof(Index), new { month = month.ToString("yyyy-MM-dd") });
    }
    [HttpPost] public async Task<IActionResult> Delete(int id) { var b = await db.Budgets.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId); if (b == null) return NotFound(); db.Budgets.Remove(b); await db.SaveChangesAsync(); TempData["Success"] = "Budget removed."; return RedirectToAction(nameof(Index)); }
}
