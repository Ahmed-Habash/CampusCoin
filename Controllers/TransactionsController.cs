using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using CampusCoin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Controllers;

[Authorize]
public class TransactionsController(ApplicationDbContext db, RecurringTransactionService recurring, TransactionPdfService pdf, CurrencyService currency, FinancialXpService xpService) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private IQueryable<Transaction> Own => db.Transactions.Where(x => x.UserId == UserId && !x.IsDeleted);
    public async Task<IActionResult> Index(string? search, CategoryType? type, DateOnly? month)
    {
        if (!ModelState.IsValid || month.HasValue && (month.Value.Year < 2000 || month.Value.Year > 2100))
            return BadRequest("Choose a valid month between 2000 and 2100.");
        await recurring.ApplyDueAsync(UserId);
        var q = Own.Include(x => x.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Description.Contains(search));
        if (type.HasValue) q = q.Where(x => x.Category.Type == type);
        if (month.HasValue) { var start = new DateOnly(month.Value.Year, month.Value.Month, 1); var end = start.AddMonths(1); q = q.Where(x => x.Date >= start && x.Date < end); }
        ViewBag.Search = search; ViewBag.Type = type; ViewBag.Month = month?.ToString("yyyy-MM");
        return View(await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync());
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
        var form = new TransactionForm();
        if (id.HasValue) { var t = await Own.SingleOrDefaultAsync(x => x.Id == id); if (t == null) return NotFound(); form = new TransactionForm { Id = t.Id, Description = t.Description, CategoryId = t.CategoryId, Amount = t.AmountCents / 100m, Date = t.Date }; }
        await Choices(); return View(form);
    }
    [HttpPost]
    public async Task<IActionResult> Edit(TransactionForm form)
    {
        var t = form.Id == 0 ? new Transaction { UserId = UserId } : await Own.SingleOrDefaultAsync(x => x.Id == form.Id);
        if (t == null) return NotFound();
        if (!await db.Categories.AnyAsync(x => x.Id == form.CategoryId && !x.IsArchived && (x.UserId == null || x.UserId == UserId))) ModelState.AddModelError("CategoryId", "Choose an available category.");
        if (form.Amount is null) ModelState.AddModelError("Amount", "Enter an amount.");
        else if (form.Amount != decimal.Round(form.Amount.Value, 2)) ModelState.AddModelError("Amount", "Use at most two decimal places.");
        if (form.Date.Year < 2000 || form.Date > DateOnly.FromDateTime(DateTime.Today)) ModelState.AddModelError("Date", "Choose a date from 2000 through today.");
        if (!ModelState.IsValid) { await Choices(); return View(form); }
        if (t.Id != 0) Audit(t, "Edited");
        t.Description = form.Description.Trim(); t.CategoryId = form.CategoryId; t.AmountCents = (long)(form.Amount!.Value * 100); t.Date = form.Date;
        var isNew = t.Id == 0;
        if (isNew) db.Transactions.Add(t);
        await db.SaveChangesAsync();
        if (isNew) await xpService.AwardTransactionAsync(UserId, t.Id);
        TempData["Success"] = "Transaction saved. Your totals are up to date."; return RedirectToAction(nameof(Index));
    }
    private void Audit(Transaction t, string action) => db.TransactionRevisions.Add(new TransactionRevision { TransactionId = t.Id, Action = action, SnapshotJson = JsonSerializer.Serialize(new { t.Description, t.AmountCents, t.CategoryId, t.Date }) });
    [HttpPost] public async Task<IActionResult> Delete(int id) { var t = await Own.SingleOrDefaultAsync(x => x.Id == id); if (t == null) return NotFound(); Audit(t, "Deleted"); t.IsDeleted = true; await db.SaveChangesAsync(); TempData["Success"] = "Transaction removed. Its audit history is retained."; return RedirectToAction(nameof(Index)); }

    [HttpGet]
    public async Task<IActionResult> ExportCsv()
    {
        var rows = await Own.Include(x => x.Category).OrderBy(x => x.Date).ToListAsync();
        var csv = new StringBuilder("Date,Description,Amount,Type,Category\n");
        foreach (var row in rows) csv.AppendLine(string.Join(',', row.Date.ToString("yyyy-MM-dd"), Csv(row.Description), (row.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture), row.Category.Type, Csv(row.Category.Name)));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"campus-coin-{DateTime.Today:yyyy-MM-dd}.csv");
    }

    [HttpGet]
    public async Task<IActionResult> ExportPdf()
    {
        var rows = await Own.Include(x => x.Category).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToListAsync();
        return File(pdf.Create(rows, currency), "application/pdf", $"campus-coin-{DateTime.Today:yyyy-MM-dd}.pdf");
    }

    [HttpPost]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file == null || file.Length == 0) { TempData["Error"] = "Choose a CSV file to import."; return RedirectToAction(nameof(Index)); }
        if (file.Length > 2_000_000) { TempData["Error"] = "CSV files must be smaller than 2 MB."; return RedirectToAction(nameof(Index)); }
        using var reader = new StreamReader(file.OpenReadStream());
        var header = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(header)) { TempData["Error"] = "The CSV file is empty."; return RedirectToAction(nameof(Index)); }
        var columns = ParseCsv(header).Select((name, index) => new { Name = name.Trim().ToLowerInvariant(), index }).ToDictionary(x => x.Name, x => x.index);
        var required = new[] { "date", "description", "amount", "category" };
        if (required.Any(x => !columns.ContainsKey(x))) { TempData["Error"] = "CSV needs Date, Description, Amount, and Category columns."; return RedirectToAction(nameof(Index)); }
        var imported = 0; var skipped = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            var values = ParseCsv(line);
            try
            {
                var date = DateOnly.Parse(values[columns["date"]], CultureInfo.InvariantCulture);
                var description = values[columns["description"]].Trim();
                var rawAmount = decimal.Parse(values[columns["amount"]], NumberStyles.Currency | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                if (date.Year < 2000 || date > DateOnly.FromDateTime(DateTime.Today) || string.IsNullOrWhiteSpace(description) || rawAmount == 0) { skipped++; continue; }
                var type = columns.TryGetValue("type", out var typeIndex) && Enum.TryParse<CategoryType>(values[typeIndex], true, out var parsedType) ? parsedType : rawAmount < 0 ? CategoryType.Expense : CategoryType.Expense;
                var name = values[columns["category"]].Trim();
                var category = await db.Categories.FirstOrDefaultAsync(x => !x.IsArchived && x.Name == name && x.Type == type && (x.UserId == null || x.UserId == UserId));
                if (category == null) { category = new Category { UserId = UserId, Name = name.Length > 60 ? name[..60] : name, Type = type }; db.Categories.Add(category); await db.SaveChangesAsync(); }
                db.Transactions.Add(new Transaction { UserId = UserId, CategoryId = category.Id, AmountCents = (long)(Math.Abs(decimal.Round(rawAmount, 2)) * 100), Description = description.Length > 160 ? description[..160] : description, Date = date });
                imported++;
            }
            catch (FormatException) { skipped++; }
            catch (ArgumentOutOfRangeException) { skipped++; }
            catch (IndexOutOfRangeException) { skipped++; }
        }
        await db.SaveChangesAsync();
        TempData["Success"] = $"Imported {imported} transaction{(imported == 1 ? "" : "s")}. {(skipped > 0 ? $"Skipped {skipped} invalid row{(skipped == 1 ? "" : "s")}." : "")}";
        return RedirectToAction(nameof(Index));
    }

    private static string Csv(string value) => value.Contains(',') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    private static List<string> ParseCsv(string line)
    {
        var values = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++) { var ch = line[i]; if (ch == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else if (ch == '"') quoted = !quoted; else if (ch == ',' && !quoted) { values.Add(current.ToString()); current.Clear(); } else current.Append(ch); }
        values.Add(current.ToString()); return values;
    }
}
