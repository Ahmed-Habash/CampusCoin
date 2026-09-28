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
public class RecurringTransactionsController(ApplicationDbContext db, RecurringTransactionService recurring) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> Index()
    {
        await recurring.ApplyDueAsync(UserId);
        return View(await db.RecurringTransactions.AsNoTracking().Include(x => x.Category)
            .Where(x => x.UserId == UserId).OrderByDescending(x => x.IsActive).ThenBy(x => x.NextRunDate).ToListAsync());
    }

    private async Task Choices()
    {
        var rows = await db.Categories
            .AsNoTracking()
            .Where(x => !x.IsArchived && (x.UserId == null || x.UserId == UserId))
            .OrderBy(x => x.Type).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Type, x.Name })
            .ToListAsync();
        ViewBag.Categories = new SelectList(
            rows.Select(x => new
            {
                x.Id,
                Label = $"{(x.Type == CategoryType.Income ? "Income" : "Expense")} · {x.Name}"
            }),
            "Id",
            "Label");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        var form = new RecurringTransactionForm();
        if (id.HasValue)
        {
            var item = await db.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
            if (item == null) return NotFound();
            form = new RecurringTransactionForm { Id = item.Id, Description = item.Description, Amount = item.AmountCents / 100m, CategoryId = item.CategoryId, Frequency = item.Frequency, StartDate = item.StartDate, IsActive = item.IsActive };
        }
        await Choices();
        return View(form);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(RecurringTransactionForm form)
    {
        var item = form.Id == 0 ? new RecurringTransaction { UserId = UserId } : await db.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == form.Id && x.UserId == UserId);
        if (item == null) return NotFound();
        if (!await db.Categories.AnyAsync(x => x.Id == form.CategoryId && !x.IsArchived && (x.UserId == null || x.UserId == UserId))) ModelState.AddModelError("CategoryId", "Choose an available category.");
        if (form.Amount is null) ModelState.AddModelError("Amount", "Enter an amount.");
        else if (form.Amount != decimal.Round(form.Amount.Value, 2)) ModelState.AddModelError("Amount", "Use at most two decimal places.");
        if (form.StartDate.Year < 2000) ModelState.AddModelError("StartDate", "Choose a date from 2000 onwards.");
        if (!ModelState.IsValid) { await Choices(); return View(form); }
        item.Description = form.Description.Trim(); item.AmountCents = (long)(form.Amount!.Value * 100); item.CategoryId = form.CategoryId; item.Frequency = form.Frequency; item.IsActive = form.IsActive;
        if (item.Id == 0) { item.StartDate = form.StartDate; item.NextRunDate = form.StartDate; db.RecurringTransactions.Add(item); }
        else item.StartDate = form.StartDate;
        await db.SaveChangesAsync();
        TempData["Success"] = "Recurring transaction saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var item = await db.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (item == null) return NotFound();
        item.IsActive = !item.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = item.IsActive ? "Recurring transaction resumed." : "Recurring transaction paused.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await db.RecurringTransactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (item == null) return NotFound();
        db.RecurringTransactions.Remove(item);
        await db.SaveChangesAsync();
        TempData["Success"] = "Recurring transaction removed.";
        return RedirectToAction(nameof(Index));
    }
}
