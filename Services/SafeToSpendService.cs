using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class SafeToSpendService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<SafeToSpendViewModel> CalculateAsync(string userId, DateOnly? month = null, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var selected = month ?? today;
        selected = new DateOnly(selected.Year, selected.Month, 1);
        var next = selected.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(selected.Year, selected.Month);

        // Days left including today when viewing the current month; full month length for past/future views.
        int daysLeft;
        if (selected.Year == today.Year && selected.Month == today.Month)
            daysLeft = Math.Max(1, daysInMonth - today.Day + 1);
        else if (selected > new DateOnly(today.Year, today.Month, 1))
            daysLeft = daysInMonth;
        else
            daysLeft = 1; // past month — show residual as a single-day figure

        var transactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= selected && t.Date < next)
            .ToListAsync(cancellationToken);

        var income = transactions.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents) / 100m;
        var expenses = transactions.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
        var balance = income - expenses;

        var budgets = await db.Budgets.AsNoTracking()
            .Where(b => b.UserId == userId && b.Month == selected)
            .ToListAsync(cancellationToken);

        decimal remainingBudget;
        string source;
        if (budgets.Count > 0)
        {
            remainingBudget = budgets.Sum(b =>
            {
                var spent = transactions.Where(t => t.CategoryId == b.CategoryId && t.Category.Type == CategoryType.Expense)
                    .Sum(t => t.AmountCents) / 100m;
                return Math.Max(0m, b.LimitCents / 100m - spent);
            });
            source = "remaining category budgets";
        }
        else
        {
            remainingBudget = Math.Max(0m, balance);
            source = "month balance (no budgets set)";
        }

        // Pace across remaining days, then never suggest more than cash still free this month.
        var paced = remainingBudget / daysLeft;
        var safe = Math.Max(0m, Math.Min(paced, Math.Max(0m, balance)));
        safe = decimal.Round(safe, 2, MidpointRounding.AwayFromZero);

        var monthProgress = selected.Year == today.Year && selected.Month == today.Month
            ? (int)Math.Round((today.Day - 1d) * 100d / daysInMonth)
            : selected < new DateOnly(today.Year, today.Month, 1) ? 100 : 0;

        return new SafeToSpendViewModel
        {
            Amount = safe,
            AmountFormatted = currency.Format(safe),
            DaysLeft = daysLeft,
            DaysInMonth = daysInMonth,
            RemainingBudget = remainingBudget,
            RemainingBudgetFormatted = currency.Format(remainingBudget),
            MonthBalance = balance,
            MonthBalanceFormatted = currency.Format(balance),
            MonthProgressPercent = Math.Clamp(monthProgress, 0, 100),
            SourceLabel = source,
            HasBudgets = budgets.Count > 0,
            Blurb = budgets.Count > 0
                ? $"Based on {currency.Format(remainingBudget)} left across budgets ÷ {daysLeft} day{(daysLeft == 1 ? "" : "s")} left."
                : $"No budgets yet — paced from your {currency.Format(Math.Max(0, balance))} month balance across {daysLeft} day{(daysLeft == 1 ? "" : "s")}."
        };
    }
}
