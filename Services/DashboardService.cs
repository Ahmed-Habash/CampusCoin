using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Services;

public class DashboardService(
    ApplicationDbContext db,
    FinancialHealthTreeService treeService,
    AffordabilityService affordabilityService,
    FinancialXpService xpService,
    SafeToSpendService safeToSpendService,
    MonthComparisonService monthComparisonService,
    IFutureYouService futureYouService,
    ISundayLetterService sundayLetterService)
{
    public async Task<DashboardViewModel> BuildAsync(string userId, DateOnly month)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var start = month.AddMonths(-5);
        var end = month.AddMonths(1);
        var records = await db.Transactions.AsNoTracking().Include(x => x.Category).Where(x => x.UserId == userId && !x.IsDeleted && x.Date >= start && x.Date < end).ToListAsync();
        var current = records.Where(x => x.Date >= month).ToList();
        var budgets = await db.Budgets.AsNoTracking().Include(x => x.Category).Where(x => x.UserId == userId && x.Month == month).ToListAsync();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var groups = current.Where(x => x.Category.Type == CategoryType.Expense).GroupBy(x => x.Category.Name).OrderByDescending(g => g.Sum(x => x.AmountCents)).ToList();
        var model = new DashboardViewModel
        {
            Name = user.FullName.Split(' ')[0],
            Month = month,
            Income = current.Where(x => x.Category.Type == CategoryType.Income).Sum(x => x.AmountCents) / 100m,
            Expenses = current.Where(x => x.Category.Type == CategoryType.Expense).Sum(x => x.AmountCents) / 100m,
            Recent = current.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Take(5).ToList(),
            Budgets = budgets.Select(b => new BudgetProgress(b.Id, b.Category.Name, b.LimitCents / 100m, current.Where(x => x.CategoryId == b.CategoryId).Sum(x => x.AmountCents) / 100m)).ToList(),
            Labels = Enumerable.Range(0, 6).Select(i => start.AddMonths(i).ToString("MMM")).ToArray(),
            CategoryLabels = groups.Select(g => g.Key).ToArray(),
            CategoryAmounts = groups.Select(g => g.Sum(x => x.AmountCents) / 100m).ToArray()
        };
        decimal[] Trend(CategoryType type) => Enumerable.Range(0, 6).Select(i => records.Where(x => x.Date.Year == start.AddMonths(i).Year && x.Date.Month == start.AddMonths(i).Month && x.Category.Type == type).Sum(x => x.AmountCents) / 100m).ToArray();
        model.IncomeTrend = Trend(CategoryType.Income); model.ExpenseTrend = Trend(CategoryType.Expense);
        model.ActiveAnnouncements = await db.Announcements.AsNoTracking()
            .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(a => a.CreatedAt).Take(3).ToListAsync();
        var warning = model.Budgets.OrderByDescending(x => x.Percent).FirstOrDefault(x => x.Percent >= 80);
        var activeSystemTip = await db.TipTemplates.AsNoTracking().Where(t => t.IsActive).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync();
        model.Tip = warning != null ? $"You've used {warning.Percent:0}% of your {warning.Category.ToLower()} budget. Check what's left before your next purchase." : activeSystemTip != null ? $"{activeSystemTip.Title}: {activeSystemTip.Tip}" : groups.Count > 0 ? $"{groups[0].Key} is your biggest expense this month. Review this category when planning next month's budget." : "Start with one expense today. Small check-ins make a big difference.";
        model.Tree = await treeService.EvaluateAndPersistAsync(userId, month);
        model.AffordCategories = await affordabilityService.GetExpenseCategoriesAsync(userId);
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId);
        model.XpProgressEnabled = settings?.XpProgressEnabled ?? true;
        if (model.XpProgressEnabled)
            model.Xp = await xpService.SyncAndGetAsync(userId);
        model.SafeToSpend = await safeToSpendService.CalculateAsync(userId, month);
        model.MonthCompare = await monthComparisonService.CompareAsync(userId, month);
        model.FutureYou = await futureYouService.BuildDashboardCardAsync(userId);
        model.SundayLetter = await sundayLetterService.GetCardAsync(userId);
        return model;
    }
}
