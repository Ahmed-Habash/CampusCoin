using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class AffordabilityService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<List<AffordCategoryOption>> GetExpenseCategoriesAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await db.Categories.AsNoTracking()
            .Where(c => !c.IsArchived && c.Type == CategoryType.Expense && (c.UserId == null || c.UserId == userId))
            .OrderBy(c => c.Name)
            .Select(c => new AffordCategoryOption(c.Id, c.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<AffordabilityResult> EvaluateAsync(
        string userId,
        decimal amount,
        int? categoryId,
        DateOnly? month,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            return Fail("Enter an amount greater than zero.", amount);

        var selectedMonth = month ?? DateOnly.FromDateTime(DateTime.Today);
        selectedMonth = new DateOnly(selectedMonth.Year, selectedMonth.Month, 1);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthEnd = selectedMonth.AddMonths(1).AddDays(-1);
        var nextMonth = selectedMonth.AddMonths(1);
        var amountCents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

        var transactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= selectedMonth && t.Date < nextMonth)
            .ToListAsync(cancellationToken);

        var incomeCents = transactions.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents);
        var expenseCents = transactions.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents);
        var balanceCents = incomeCents - expenseCents;

        var recurrings = await db.RecurringTransactions.AsNoTracking()
            .Include(r => r.Category)
            .Where(r => r.UserId == userId && r.IsActive)
            .ToListAsync(cancellationToken);

        var upcomingCents = recurrings.Sum(r => SumRemainingThisMonth(r, today, selectedMonth, monthEnd));
        var availableCents = balanceCents - upcomingCents;

        string? categoryName = null;
        long? budgetLimit = null;
        long categorySpent = 0;
        long remainingBudget = 0;
        bool hasBudget = false;
        bool wouldExceedBudget = false;
        decimal? percentOfRemaining = null;

        if (categoryId is int catId)
        {
            var category = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == catId && c.Type == CategoryType.Expense && (c.UserId == null || c.UserId == userId), cancellationToken);
            if (category == null)
                return Fail("Choose a valid expense category.", amount);

            categoryName = category.Name;
            categorySpent = transactions.Where(t => t.CategoryId == catId && t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents);
            var budget = await db.Budgets.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Month == selectedMonth && b.CategoryId == catId, cancellationToken);
            if (budget != null)
            {
                hasBudget = true;
                budgetLimit = budget.LimitCents;
                remainingBudget = budget.LimitCents - categorySpent;
                wouldExceedBudget = amountCents > remainingBudget;
                if (remainingBudget > 0)
                    percentOfRemaining = Math.Round(amountCents * 100m / remainingBudget, 0);
                else if (amountCents > 0)
                    percentOfRemaining = 100;
            }
        }

        var wouldExceedBalance = amountCents > availableCents;
        var money = (long cents) => currency.Format(cents);
        var moneyDec = (decimal value) => currency.Format(value);

        var result = new AffordabilityResult
        {
            Amount = amount,
            AmountFormatted = moneyDec(amount),
            MonthBalance = balanceCents / 100m,
            MonthBalanceFormatted = money(balanceCents),
            UpcomingRecurring = upcomingCents / 100m,
            UpcomingRecurringFormatted = money(upcomingCents),
            AvailableAfterRecurring = availableCents / 100m,
            AvailableAfterRecurringFormatted = money(availableCents),
            CategoryId = categoryId,
            CategoryName = categoryName,
            HasBudget = hasBudget,
            BudgetLimit = budgetLimit.HasValue ? budgetLimit.Value / 100m : null,
            BudgetRemaining = hasBudget ? remainingBudget / 100m : null,
            BudgetRemainingFormatted = hasBudget ? money(remainingBudget) : null,
            PercentOfRemainingBudget = percentOfRemaining,
            WouldExceedBudget = wouldExceedBudget,
            WouldExceedBalance = wouldExceedBalance
        };

        if (wouldExceedBalance && wouldExceedBudget)
        {
            result.Verdict = "No";
            result.Headline = "Not recommended — you will go over budget";
            result.Detail = $"Spending {result.AmountFormatted} would leave your month balance short after upcoming bills ({result.UpcomingRecurringFormatted} still due) and push past your {(categoryName ?? "category")} budget.";
        }
        else if (wouldExceedBudget)
        {
            result.Verdict = "No";
            result.Headline = "Not recommended — you will go over budget";
            var overBy = money(amountCents - Math.Max(0, remainingBudget));
            result.Detail = $"{categoryName} has {result.BudgetRemainingFormatted} left. This purchase would overshoot by about {overBy}.";
        }
        else if (wouldExceedBalance)
        {
            result.Verdict = "No";
            result.Headline = "Not recommended — tight on cash this month";
            result.Detail = upcomingCents > 0
                ? $"After {result.UpcomingRecurringFormatted} in upcoming recurring expenses, you’d only have {result.AvailableAfterRecurringFormatted} free — short of {result.AmountFormatted}."
                : $"Your current month balance is {result.MonthBalanceFormatted}, which isn’t enough for {result.AmountFormatted}.";
        }
        else if (hasBudget && percentOfRemaining >= 65)
        {
            result.Verdict = "Caution";
            result.Headline = $"Yes, but it will use {percentOfRemaining:0}% of your remaining {categoryName} budget";
            result.Detail = $"You can cover it from your {result.AvailableAfterRecurringFormatted} available after recurrings, but {categoryName} would be nearly spent.";
        }
        else if (hasBudget && percentOfRemaining is > 35 and < 65)
        {
            result.Verdict = "Yes";
            result.Headline = $"Yes, but it will use {percentOfRemaining:0}% of your remaining {categoryName} budget";
            result.Detail = $"Balance stays healthy ({result.AvailableAfterRecurringFormatted} free after upcoming bills). Keep an eye on {categoryName} afterward.";
        }
        else if (!hasBudget && amountCents > availableCents * 0.5m)
        {
            result.Verdict = "Caution";
            result.Headline = "Yes, but it uses a large share of what’s left";
            result.Detail = $"You have {result.AvailableAfterRecurringFormatted} free after upcoming recurrings ({result.UpcomingRecurringFormatted}). No matching budget was set for this category.";
        }
        else
        {
            result.Verdict = "Easy";
            result.Headline = "Yes, you can easily afford it";
            result.Detail = hasBudget
                ? $"It uses about {percentOfRemaining:0}% of remaining {categoryName} budget, with {result.AvailableAfterRecurringFormatted} still free after upcoming bills."
                : upcomingCents > 0
                    ? $"Even after {result.UpcomingRecurringFormatted} in upcoming recurrings, you’d still have room for {result.AmountFormatted}."
                    : $"Your month balance of {result.MonthBalanceFormatted} covers {result.AmountFormatted} comfortably.";
        }

        return result;
    }

    private static AffordabilityResult Fail(string message, decimal amount) => new()
    {
        Verdict = "No",
        Headline = message,
        Detail = "Adjust the amount and try again.",
        Amount = amount,
        AmountFormatted = amount.ToString("0.00")
    };

    private static long SumRemainingThisMonth(RecurringTransaction recurring, DateOnly today, DateOnly monthStart, DateOnly monthEnd)
    {
        // Only count runs still due inside the viewed month.
        var cursor = recurring.NextRunDate;
        if (cursor < monthStart)
        {
            while (cursor < monthStart)
                cursor = Advance(recurring.Frequency, cursor);
        }

        // For the current month, skip anything already due today or earlier (ApplyDue may post it).
        var floor = monthStart == new DateOnly(today.Year, today.Month, 1) ? today.AddDays(1) : monthStart;
        long total = 0;
        while (cursor <= monthEnd)
        {
            if (cursor >= floor)
                total += recurring.AmountCents;
            cursor = Advance(recurring.Frequency, cursor);
        }
        return total;
    }

    private static DateOnly Advance(RecurrenceFrequency frequency, DateOnly date) =>
        frequency == RecurrenceFrequency.Weekly ? date.AddDays(7) : date.AddMonths(1);
}
