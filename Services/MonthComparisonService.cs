using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class MonthComparisonService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<MonthComparisonViewModel> CompareAsync(string userId, DateOnly? month = null, CancellationToken cancellationToken = default)
    {
        var currentMonth = month ?? DateOnly.FromDateTime(DateTime.Today);
        currentMonth = new DateOnly(currentMonth.Year, currentMonth.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);

        var current = await LoadMonthAsync(userId, currentMonth, cancellationToken);
        var previous = await LoadMonthAsync(userId, previousMonth, cancellationToken);

        return new MonthComparisonViewModel
        {
            CurrentLabel = currentMonth.ToString("MMM yyyy"),
            PreviousLabel = previousMonth.ToString("MMM yyyy"),
            CurrentIncome = current.Income,
            PreviousIncome = previous.Income,
            CurrentIncomeFormatted = currency.Format(current.Income),
            PreviousIncomeFormatted = currency.Format(previous.Income),
            IncomeChangePercent = Pct(current.Income, previous.Income),
            CurrentExpenses = current.Expenses,
            PreviousExpenses = previous.Expenses,
            CurrentExpensesFormatted = currency.Format(current.Expenses),
            PreviousExpensesFormatted = currency.Format(previous.Expenses),
            ExpenseChangePercent = Pct(current.Expenses, previous.Expenses),
            CurrentSaved = current.Income - current.Expenses,
            PreviousSaved = previous.Income - previous.Expenses,
            CurrentSavedFormatted = currency.Format(current.Income - current.Expenses),
            PreviousSavedFormatted = currency.Format(previous.Income - previous.Expenses),
            SavedChangePercent = Pct(current.Income - current.Expenses, previous.Income - previous.Expenses),
            CurrentTopCategory = current.TopCategory,
            PreviousTopCategory = previous.TopCategory,
            CurrentTopCategoryAmountFormatted = currency.Format(current.TopAmount),
            PreviousTopCategoryAmountFormatted = currency.Format(previous.TopAmount),
            TopCategoryChangePercent = current.TopCategory == previous.TopCategory && previous.TopCategory != "—"
                ? Pct(current.TopAmount, previous.TopAmount)
                : null,
            TopCategoryNote = BuildTopNote(current, previous)
        };
    }

    private async Task<MonthSnap> LoadMonthAsync(string userId, DateOnly month, CancellationToken cancellationToken)
    {
        var next = month.AddMonths(1);
        var rows = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= month && t.Date < next)
            .ToListAsync(cancellationToken);

        var income = rows.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents) / 100m;
        var expenses = rows.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
        var top = rows.Where(t => t.Category.Type == CategoryType.Expense)
            .GroupBy(t => t.Category.Name)
            .Select(g => new { Name = g.Key, Amount = g.Sum(x => x.AmountCents) / 100m })
            .OrderByDescending(x => x.Amount)
            .FirstOrDefault();

        return new MonthSnap(income, expenses, top?.Name ?? "—", top?.Amount ?? 0m);
    }

    private static decimal? Pct(decimal current, decimal previous)
    {
        if (previous == 0)
            return current == 0 ? 0 : null; // undefined % from zero baseline
        return Math.Round((current - previous) / Math.Abs(previous) * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static string BuildTopNote(MonthSnap current, MonthSnap previous)
    {
        if (current.TopCategory == "—" && previous.TopCategory == "—")
            return "Not enough spending yet to compare categories.";
        if (current.TopCategory == previous.TopCategory && current.TopCategory != "—")
            return $"{current.TopCategory} stayed your top category.";
        if (previous.TopCategory == "—")
            return $"{current.TopCategory} is leading this month.";
        if (current.TopCategory == "—")
            return $"Last month’s top category was {previous.TopCategory}.";
        return $"Shifted from {previous.TopCategory} → {current.TopCategory}.";
    }

    private sealed record MonthSnap(decimal Income, decimal Expenses, string TopCategory, decimal TopAmount);
}
