using System.Globalization;
using System.Security.Claims;
using CampusCoin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampusCoin.Controllers;

[Authorize]
public class DashboardController(
    DashboardService service,
    RecurringTransactionService recurring,
    FinancialHealthTreeService treeService,
    AffordabilityService affordabilityService,
    FinancialXpService xpService,
    WhatIfSimulatorService whatIfService,
    SafeToSpendService safeToSpendService,
    MonthComparisonService monthComparisonService,
    ISundayLetterService sundayLetterService) : Controller
{
    public async Task<IActionResult> Index(DateOnly? month)
    {
        if (!ModelState.IsValid || month.HasValue && (month.Value.Year < 2000 || month.Value.Year > 2100))
            return BadRequest("Choose a valid month between 2000 and 2100.");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await recurring.ApplyDueAsync(userId);
        return View(await service.BuildAsync(userId, month ?? DateOnly.FromDateTime(DateTime.Today)));
    }

    [HttpPost("Dashboard/SundayLetter/Open")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenSundayLetter(int letterId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        await sundayLetterService.OpenAsync(userId, letterId, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Dashboard/SundayLetter/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSundayLetter(int letterId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        await sundayLetterService.SaveAsync(userId, letterId, cancellationToken);
        TempData["Success"] = "Saved to your Letter Box.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Dashboard/Tree")]
    public async Task<IActionResult> Tree(DateOnly? month, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var selected = month ?? DateOnly.FromDateTime(DateTime.Today);
        if (selected.Year < 2000 || selected.Year > 2100) return BadRequest("Choose a valid month between 2000 and 2100.");
        var tree = await treeService.EvaluateAndPersistAsync(userId, selected, cancellationToken);
        return Json(tree);
    }

    [HttpGet("Dashboard/Afford")]
    public async Task<IActionResult> Afford(decimal amount, int? categoryId, string? month, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (amount <= 0 || amount > 10_000_000m) return BadRequest(new { error = "Enter an amount between 0.01 and 10,000,000." });
        if (!TryParseMonth(month, out var selected))
            return BadRequest(new { error = "Choose a valid month." });
        var result = await affordabilityService.EvaluateAsync(userId, amount, categoryId, selected, cancellationToken);
        return Json(result);
    }

    [HttpGet("Dashboard/Xp")]
    public async Task<IActionResult> Xp(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        return Json(await xpService.SyncAndGetAsync(userId, cancellationToken));
    }

    [HttpGet("Dashboard/WhatIf")]
    public async Task<IActionResult> WhatIf(
        string scenario = "extra_income",
        decimal amount = 0,
        int? categoryId = null,
        int days = 14,
        decimal percent = 50,
        string? month = null,
        CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!TryParseMonth(month, out var selected))
            return BadRequest(new { error = "Choose a valid month." });
        var result = await whatIfService.SimulateAsync(userId, scenario, amount, categoryId, days, percent, selected, cancellationToken);
        return Json(result);
    }

    [HttpGet("Dashboard/SafeToSpend")]
    public async Task<IActionResult> SafeToSpend(string? month, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!TryParseMonth(month, out var selected))
            return BadRequest(new { error = "Choose a valid month." });
        return Json(await safeToSpendService.CalculateAsync(userId, selected, cancellationToken));
    }

    [HttpGet("Dashboard/MonthCompare")]
    public async Task<IActionResult> MonthCompare(string? month, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();
        if (!TryParseMonth(month, out var selected))
            return BadRequest(new { error = "Choose a valid month." });
        return Json(await monthComparisonService.CompareAsync(userId, selected, cancellationToken));
    }

    private static bool TryParseMonth(string? month, out DateOnly selected)
    {
        selected = DateOnly.FromDateTime(DateTime.Today);
        if (string.IsNullOrWhiteSpace(month)) return true;
        month = month.Trim();
        if (DateOnly.TryParse(month, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
        {
            selected = new DateOnly(full.Year, full.Month, 1);
            return selected.Year is >= 2000 and <= 2100;
        }
        if (DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromYm))
        {
            selected = fromYm;
            return selected.Year is >= 2000 and <= 2100;
        }
        return false;
    }
}
