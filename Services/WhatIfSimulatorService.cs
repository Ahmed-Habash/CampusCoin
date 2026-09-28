using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class WhatIfSimulatorService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<WhatIfResult> SimulateAsync(
        string userId,
        string scenario,
        decimal amount,
        int? categoryId,
        int days,
        decimal percent,
        DateOnly? month,
        CancellationToken cancellationToken = default)
    {
        var selectedMonth = month ?? DateOnly.FromDateTime(DateTime.Today);
        selectedMonth = new DateOnly(selectedMonth.Year, selectedMonth.Month, 1);
        var next = selectedMonth.AddMonths(1);
        var daysInMonth = DateTime.DaysInMonth(selectedMonth.Year, selectedMonth.Month);
        days = Math.Clamp(days <= 0 ? 14 : days, 1, daysInMonth);
        percent = Math.Clamp(percent, 1, 100);
        amount = Math.Max(0, decimal.Round(amount, 2));

        var transactions = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= selectedMonth && t.Date < next)
            .ToListAsync(cancellationToken);

        var income = transactions.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents) / 100m;
        var expenses = transactions.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
        var beforeBalance = income - expenses;

        var budgets = await db.Budgets.AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.UserId == userId && b.Month == selectedMonth)
            .ToListAsync(cancellationToken);

        string? categoryName = null;
        decimal categorySpent = 0;
        decimal projectedDelta = 0; // positive = better balance
        string label;
        string explanation;

        scenario = (scenario ?? "extra_income").Trim().ToLowerInvariant();
        switch (scenario)
        {
            case "stop_category":
            {
                if (categoryId is not int catId)
                    return Error("Choose a category to pause.", beforeBalance, income, expenses);
                var cat = await ResolveCategory(userId, catId, cancellationToken);
                if (cat == null) return Error("Choose a valid expense category.", beforeBalance, income, expenses);
                categoryName = cat.Name;
                categorySpent = transactions.Where(t => t.CategoryId == catId && t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
                var daily = categorySpent / Math.Max(1, daysInMonth);
                projectedDelta = decimal.Round(daily * days, 2);
                label = $"Stop {categoryName} for {days} days";
                explanation = $"Based on this month’s {categoryName} pace (~{currency.Format(daily)}/day), pausing for {days} days could free about {currency.Format(projectedDelta)}.";
                break;
            }
            case "reduce_category":
            {
                if (categoryId is not int catId)
                    return Error("Choose a category to reduce.", beforeBalance, income, expenses);
                var cat = await ResolveCategory(userId, catId, cancellationToken);
                if (cat == null) return Error("Choose a valid expense category.", beforeBalance, income, expenses);
                categoryName = cat.Name;
                categorySpent = transactions.Where(t => t.CategoryId == catId && t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
                projectedDelta = decimal.Round(categorySpent * (percent / 100m), 2);
                label = $"Reduce {categoryName} by {percent:0}%";
                explanation = $"Cutting {percent:0}% of {categoryName} spending this month (currently {currency.Format(categorySpent)}) could save {currency.Format(projectedDelta)}.";
                break;
            }
            case "extra_income":
            default:
            {
                if (amount <= 0) return Error("Enter an extra income amount.", beforeBalance, income, expenses);
                projectedDelta = amount;
                label = $"Extra income of {currency.Format(amount)}";
                explanation = $"Adding {currency.Format(amount)} of income this month increases your projected balance by the same amount.";
                scenario = "extra_income";
                break;
            }
        }

        var afterBalance = beforeBalance + projectedDelta;
        var afterExpenses = scenario == "extra_income" ? expenses : Math.Max(0, expenses - projectedDelta);
        var afterIncome = scenario == "extra_income" ? income + projectedDelta : income;

        string? budgetImpact = null;
        if (categoryId is int bid && budgets.FirstOrDefault(b => b.CategoryId == bid) is { } budget)
        {
            var spent = transactions.Where(t => t.CategoryId == bid).Sum(t => t.AmountCents) / 100m;
            var beforePct = budget.LimitCents > 0 ? spent / (budget.LimitCents / 100m) * 100m : 0;
            var afterSpent = Math.Max(0, spent - (scenario == "extra_income" ? 0 : projectedDelta));
            var afterPct = budget.LimitCents > 0 ? afterSpent / (budget.LimitCents / 100m) * 100m : 0;
            budgetImpact = $"{budget.Category.Name} budget: {beforePct:0}% → {afterPct:0}% used";
        }

        return new WhatIfResult
        {
            Scenario = scenario,
            Label = label,
            Explanation = explanation,
            CategoryName = categoryName,
            BeforeBalance = beforeBalance,
            AfterBalance = afterBalance,
            BeforeBalanceFormatted = currency.Format(beforeBalance),
            AfterBalanceFormatted = currency.Format(afterBalance),
            DeltaFormatted = currency.Format(projectedDelta),
            BeforeIncomeFormatted = currency.Format(income),
            AfterIncomeFormatted = currency.Format(afterIncome),
            BeforeExpensesFormatted = currency.Format(expenses),
            AfterExpensesFormatted = currency.Format(afterExpenses),
            BudgetImpact = budgetImpact,
            Ok = true
        };
    }

    private async Task<Category?> ResolveCategory(string userId, int categoryId, CancellationToken cancellationToken) =>
        await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.Type == CategoryType.Expense && (c.UserId == null || c.UserId == userId), cancellationToken);

    private WhatIfResult Error(string message, decimal beforeBalance, decimal income, decimal expenses) => new()
    {
        Ok = false,
        Label = "Couldn’t run scenario",
        Explanation = message,
        BeforeBalance = beforeBalance,
        AfterBalance = beforeBalance,
        BeforeBalanceFormatted = currency.Format(beforeBalance),
        AfterBalanceFormatted = currency.Format(beforeBalance),
        DeltaFormatted = currency.Format(0m),
        BeforeIncomeFormatted = currency.Format(income),
        AfterIncomeFormatted = currency.Format(income),
        BeforeExpensesFormatted = currency.Format(expenses),
        AfterExpensesFormatted = currency.Format(expenses)
    };
}
