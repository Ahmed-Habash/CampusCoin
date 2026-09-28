using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class InsightsService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<InsightsViewModel> BuildAsync(string userId, DateOnly month)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var end = month.AddMonths(1);
        var pulseEnd = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        var fetchEnd = end > pulseEnd ? end : pulseEnd;
        var records = await db.Transactions.AsNoTracking().Include(x => x.Category)
            .Where(x => x.UserId == userId && !x.IsDeleted && x.Date >= month.AddMonths(-6) && x.Date < fetchEnd)
            .ToListAsync();
        var current = records.Where(x => x.Date >= month && x.Date < end).ToList();
        var expenses = current.Where(x => x.Category.Type == CategoryType.Expense).Sum(x => x.AmountCents) / 100m;
        var income = current.Where(x => x.Category.Type == CategoryType.Income).Sum(x => x.AmountCents) / 100m;
        var elapsed = month == new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1) ? DateTime.Today.Day : DateTime.DaysInMonth(month.Year, month.Month);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        var forecast = elapsed > 0 ? expenses / elapsed * days : 0;
        var historical = Enumerable.Range(1, 3).Select(i => records.Where(x => x.Date >= month.AddMonths(-i) && x.Date < month.AddMonths(1 - i) && x.Category.Type == CategoryType.Expense).Sum(x => x.AmountCents) / 100m).Where(x => x > 0).ToList();
        var average = historical.Count == 0 ? expenses : historical.Average();
        var budgets = await db.Budgets.AsNoTracking().Include(x => x.Category).Where(x => x.UserId == userId && x.Month == month).ToListAsync();
        var alerts = new List<InsightAlert>();
        foreach (var budget in budgets)
        {
            var spent = current.Where(x => x.CategoryId == budget.CategoryId).Sum(x => x.AmountCents) / 100m;
            var percent = budget.LimitCents == 0 ? 0 : spent / (budget.LimitCents / 100m) * 100;
            if (percent >= 100) alerts.Add(new InsightAlert($"{budget.Category.Name} budget exceeded", $"You have used {percent:0}% of your {budget.Category.Name.ToLower()} budget.", "danger"));
            else if (percent >= 80) alerts.Add(new InsightAlert($"{budget.Category.Name} is getting close", $"You have used {percent:0}% of this month's limit.", "warning"));
        }
        if (forecast > average * 1.15m && average > 0) alerts.Add(new InsightAlert("Your month is trending higher", $"At the current pace, spending may reach {currency.Format(forecast)}, around 15% above your recent average.", "info"));
        if (alerts.Count == 0) alerts.Add(new InsightAlert("You are in a good rhythm", "No urgent budget alerts. Keep checking in once or twice a week.", "success"));
        var goals = await db.SavingsGoals.AsNoTracking().Where(x => x.UserId == userId && !x.IsComplete).OrderBy(x => x.Deadline).Select(x => new GoalProgress(x.Id, x.Name, x.TargetCents / 100m, x.SavedCents / 100m, x.Deadline)).ToListAsync();
        var activeDates = records.Select(x => x.Date).Distinct().OrderByDescending(x => x).ToList();
        var streak = 0;
        var cursor = activeDates.FirstOrDefault();
        while (cursor != default && activeDates.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        var badges = new List<string>();
        if (records.Count > 0) badges.Add("First check-in");
        if (streak >= 3) badges.Add("Three-day rhythm");
        if (goals.Count > 0) badges.Add("Goal setter");
        if (budgets.Count > 0 && alerts.All(x => x.Tone != "danger")) badges.Add("Budget guardian");

        // Always last 14 calendar days (independent of the month picker).
        var heatStart = DateOnly.FromDateTime(DateTime.Today.AddDays(-13));
        var heat = Enumerable.Range(0, 14).Select(i => heatStart.AddDays(i)).ToArray();
        var values = heat.Select(day =>
            records.Where(x => x.Date == day && x.Category.Type == CategoryType.Expense)
                .Sum(x => x.AmountCents) / 100m).ToArray();
        var pulseTotal = values.Sum();
        var pulsePeak = values.DefaultIfEmpty(0).Max();
        var pulsePeakLabel = pulsePeak > 0
            ? heat[Array.IndexOf(values, pulsePeak)].ToString("dd MMM")
            : "";

        return new InsightsViewModel
        {
            Month = month,
            ForecastExpenses = forecast,
            AverageExpenses = average,
            ForecastBalance = income - forecast,
            SavingsRate = income <= 0 ? 0 : Math.Max(0, Math.Round((income - expenses) / income * 100, 1)),
            Alerts = alerts,
            Goals = goals,
            CheckInStreak = streak,
            Badges = badges,
            HeatmapLabels = heat.Select(x => x.ToString("dd MMM")).ToArray(),
            HeatmapValues = values,
            PulseTotal = pulseTotal,
            PulsePeakLabel = pulsePeakLabel,
            HasPulseData = pulseTotal > 0
        };
    }
}
