using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Controllers;

[Authorize]
public class CategoriesController(ApplicationDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index() => View(await db.Categories.Where(x => !x.IsArchived && (x.UserId == null || x.UserId == UserId)).OrderBy(x => x.Type).ThenBy(x => x.Name).ToListAsync());
    [HttpGet]
    public async Task<IActionResult> Edit(int? id, CategoryType type = CategoryType.Expense)
    {
        if (!id.HasValue) return View(new CategoryForm { Type = Enum.IsDefined(type) ? type : CategoryType.Expense });
        var c = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId && !x.IsArchived);
        return c == null ? NotFound() : View(new CategoryForm { Id = c.Id, Name = c.Name, Type = c.Type });
    }
    [HttpPost]
    public async Task<IActionResult> Edit(CategoryForm form)
    {
        var c = form.Id == 0 ? new Category { UserId = UserId } : await db.Categories.SingleOrDefaultAsync(x => x.Id == form.Id && x.UserId == UserId && !x.IsArchived);
        if (c == null) return NotFound();
        var name = form.Name?.Trim() ?? "";
        if (await db.Categories.AnyAsync(x => x.Id != form.Id && !x.IsArchived && (x.UserId == null || x.UserId == UserId) && x.Name.ToLower() == name.ToLower() && x.Type == form.Type)) ModelState.AddModelError("Name", "That category already exists.");
        if (c.Id != 0 && c.Type != form.Type && (await db.Transactions.AnyAsync(x => x.CategoryId == c.Id) || await db.Budgets.AnyAsync(x => x.CategoryId == c.Id))) ModelState.AddModelError("Type", "A used category cannot change type. Create a new category instead.");
        if (!ModelState.IsValid) return View(form);
        c.Name = name; c.Type = form.Type; if (c.Id == 0) db.Categories.Add(c); await db.SaveChangesAsync(); TempData["Success"] = "Category saved."; return RedirectToAction(nameof(Index));
    }
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId && !x.IsArchived); if (c == null) return NotFound();
        if (await db.Transactions.AnyAsync(x => x.CategoryId == id && !x.IsDeleted) || await db.Budgets.AnyAsync(x => x.CategoryId == id)) { TempData["Error"] = "This category is in use. Reassign its transactions and remove its budgets first."; return RedirectToAction(nameof(Index)); }
        c.IsArchived = true; await db.SaveChangesAsync(); TempData["Success"] = "Category removed."; return RedirectToAction(nameof(Index));
    }
}

