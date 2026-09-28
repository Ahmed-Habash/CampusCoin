using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public interface IFutureYouService
{
    Task<FutureYouPageViewModel> BuildPageAsync(string userId, CancellationToken ct = default);
    Task<FutureYouSimulateResult> SimulateAsync(string userId, FutureYouSimulateRequest request, CancellationToken ct = default);
    Task<FutureYouDashboardCardViewModel> BuildDashboardCardAsync(string userId, CancellationToken ct = default);
    Task<FutureYouMessageViewModel?> BuildExpenseMessageAsync(string userId, decimal amount, string? categoryName, CancellationToken ct = default);
    Task SetMessagesEnabledAsync(string userId, bool enabled, CancellationToken ct = default);
    Task SetParallelLivesHiddenAsync(string userId, bool hidden, CancellationToken ct = default);
    Task<ParallelLivesViewModel> BuildParallelLivesAsync(string userId, bool regenerate, CancellationToken ct = default);
    Task TrackUsageAsync(string userId, string eventType, CancellationToken ct = default);
    Task<FutureYouAdminStatsViewModel> GetAdminStatsAsync(CancellationToken ct = default);
}

public class FutureYouService(ApplicationDbContext db, CurrencyService currency, CampusChallengeService challenges) : IFutureYouService
{
    private static readonly string[] PrimaryKeys = ["Food", "Entertainment"];
    private static readonly string[] KnownMajor =
    [
        "Food", "Entertainment", "Transport", "Subscriptions", "Hostel/Rent", "Academics", "Miscellaneous"
    ];

    public async Task TrackUsageAsync(string userId, string eventType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(eventType)) return;
        db.FutureYouUsageEvents.Add(new FutureYouUsageEvent
        {
            UserId = userId,
            EventType = eventType.Trim(),
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetMessagesEnabledAsync(string userId, bool enabled, CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(userId, ct);
        settings.FutureYouMessagesEnabled = enabled;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetParallelLivesHiddenAsync(string userId, bool hidden, CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(userId, ct);
        settings.ParallelLivesHidden = hidden;
        settings.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FutureYouPageViewModel> BuildPageAsync(string userId, CancellationToken ct = default)
    {
        var snapshot = await LoadSnapshotAsync(userId, ct);
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var simulate = BuildSimulation(snapshot, null);
        var page = new FutureYouPageViewModel
        {
            FirstName = snapshot.FirstName,
            HasTransactionHistory = snapshot.HasTransactions,
            HasIncome = snapshot.MonthlyIncome > 0,
            MessagesEnabled = settings?.FutureYouMessagesEnabled ?? true,
            ParallelLivesHidden = settings?.ParallelLivesHidden ?? false,
            GoalName = snapshot.GoalName,
            GoalTarget = snapshot.GoalTarget,
            GoalSaved = snapshot.GoalSaved,
            GoalRemaining = snapshot.GoalRemaining,
            GoalReached = snapshot.GoalReached,
            UsingTemporaryGoal = snapshot.UsingTemporaryGoal,
            CurrencyCode = currency.Code,
            MonthlyIncome = snapshot.MonthlyIncome,
            MonthlyExpenses = snapshot.MonthlyExpenses,
            CurrentMonthlySavings = snapshot.CurrentMonthlySavings,
            Sliders = snapshot.Sliders,
            ExtraMonthlySavings = 0,
            SuggestedExtraSavings = snapshot.SuggestedExtraSavings,
            CurrentYou = simulate.CurrentYou,
            SmarterYou = simulate.SmarterYou,
            YourScenario = simulate.YourScenario,
            FutureMessage = settings?.FutureYouMessagesEnabled == false
                ? null
                : BuildMessage(snapshot, simulate.YourScenario, null),
            Tips = BuildTips(snapshot),
            ParallelLives = settings?.ParallelLivesHidden == true
                ? new ParallelLivesViewModel { Hidden = true }
                : BuildParallelLives(snapshot, simulate),
            Challenge = await challenges.GetForUserAsync(userId, ct)
        };
        await TrackUsageAsync(userId, "OpenedSimulator", ct);
        return page;
    }

    public async Task<FutureYouSimulateResult> SimulateAsync(string userId, FutureYouSimulateRequest request, CancellationToken ct = default)
    {
        var snapshot = await LoadSnapshotAsync(userId, ct);
        if (request.TemporaryGoalTarget is decimal temp && temp > 0)
        {
            snapshot.GoalTarget = decimal.Round(temp, 2);
            snapshot.GoalName = string.IsNullOrWhiteSpace(request.TemporaryGoalName) ? "Simulator goal" : request.TemporaryGoalName.Trim();
            snapshot.GoalRemaining = Math.Max(0, snapshot.GoalTarget - snapshot.GoalSaved);
            snapshot.GoalReached = snapshot.GoalRemaining <= 0;
            snapshot.UsingTemporaryGoal = true;
        }

        var result = BuildSimulation(snapshot, request);
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (settings?.FutureYouMessagesEnabled != false)
            result.FutureMessage = BuildMessage(snapshot, result.YourScenario, request.PurchaseAmount);
        return result;
    }

    public async Task<FutureYouDashboardCardViewModel> BuildDashboardCardAsync(string userId, CancellationToken ct = default)
    {
        var snapshot = await LoadSnapshotAsync(userId, ct);
        var simulate = BuildSimulation(snapshot, null);
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        return new FutureYouDashboardCardViewModel
        {
            GoalName = snapshot.GoalName,
            GoalRemaining = snapshot.GoalRemaining,
            GoalRemainingFormatted = currency.Format(snapshot.GoalRemaining),
            MonthsLabel = simulate.YourScenario.MonthsToGoalLabel,
            CoachingMessage = simulate.YourScenario.CoachingMessage,
            FutureMessage = settings?.FutureYouMessagesEnabled == false
                ? null
                : BuildMessage(snapshot, simulate.YourScenario, 8m),
            Tips = BuildTips(snapshot).Take(2).ToList(),
            ParallelLives = settings?.ParallelLivesHidden == true
                ? new ParallelLivesViewModel { Hidden = true }
                : BuildParallelLives(snapshot, simulate),
            Challenge = await challenges.GetForUserAsync(userId, ct)
        };
    }

    public async Task<FutureYouMessageViewModel?> BuildExpenseMessageAsync(string userId, decimal amount, string? categoryName, CancellationToken ct = default)
    {
        var settings = await db.UserSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (settings?.FutureYouMessagesEnabled == false) return null;
        var snapshot = await LoadSnapshotAsync(userId, ct);
        var simulate = BuildSimulation(snapshot, null);
        return BuildMessage(snapshot, simulate.YourScenario, amount, categoryName);
    }

    public async Task<ParallelLivesViewModel> BuildParallelLivesAsync(string userId, bool regenerate, CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(userId, ct);
        if (settings.ParallelLivesHidden)
            return new ParallelLivesViewModel { Hidden = true };

        var snapshot = await LoadSnapshotAsync(userId, ct);
        var simulate = BuildSimulation(snapshot, null);
        var stories = BuildParallelLives(snapshot, simulate, regenerate ? Random.Shared.Next() : 0);
        if (regenerate) await TrackUsageAsync(userId, "RegeneratedStories", ct);
        return stories;
    }

    public async Task<FutureYouAdminStatsViewModel> GetAdminStatsAsync(CancellationToken ct = default)
    {
        var opens = await db.FutureYouUsageEvents.AsNoTracking()
            .Where(e => e.EventType == "OpenedSimulator").ToListAsync(ct);
        var started = await db.FutureYouUsageEvents.AsNoTracking()
            .CountAsync(e => e.EventType == "StartedChallenge", ct);
        var completed = await db.FutureYouUsageEvents.AsNoTracking()
            .CountAsync(e => e.EventType == "CompletedChallenge", ct);

        var challengeStats = await db.CampusChallenges.AsNoTracking()
            .Select(c => new
            {
                c.Status,
                Completed = c.Days.Count(d => d.IsCompleted),
                Total = c.Days.Count
            }).ToListAsync(ct);

        decimal avg = 0;
        if (challengeStats.Count > 0)
        {
            avg = challengeStats.Average(c => c.Total == 0 ? 0 : (decimal)c.Completed / c.Total * 100m);
            avg = decimal.Round(avg, 1);
        }

        var templates = await db.ChallengeTemplates.AsNoTracking()
            .OrderBy(t => t.Title)
            .Select(t => new ChallengeTemplateItem
            {
                Id = t.Id,
                Title = t.Title,
                PatternKey = t.PatternKey,
                MicroGoalTemplate = t.MicroGoalTemplate,
                IsActive = t.IsActive
            }).ToListAsync(ct);

        return new FutureYouAdminStatsViewModel
        {
            DistinctSimulatorUsers = opens.Select(o => o.UserId).Distinct().Count(),
            SimulatorOpens = opens.Count,
            ChallengesStarted = started,
            ChallengesCompleted = completed,
            AverageChallengeCompletionPercent = avg,
            Templates = templates
        };
    }

    private async Task<UserSetting> EnsureSettingsAsync(string userId, CancellationToken ct)
    {
        var settings = await db.UserSettings.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (settings != null) return settings;
        settings = new UserSetting { UserId = userId, UpdatedAt = DateTime.UtcNow };
        db.UserSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private sealed class Snapshot
    {
        public string FirstName { get; set; } = "there";
        public bool HasTransactions { get; set; }
        public decimal MonthlyIncome { get; set; }
        public decimal MonthlyExpenses { get; set; }
        public decimal CurrentMonthlySavings { get; set; }
        public string GoalName { get; set; } = "your savings goal";
        public decimal GoalTarget { get; set; }
        public decimal GoalSaved { get; set; }
        public decimal GoalRemaining { get; set; }
        public bool GoalReached { get; set; }
        public bool UsingTemporaryGoal { get; set; }
        public decimal SuggestedExtraSavings { get; set; }
        public List<FutureYouCategorySlider> Sliders { get; set; } = [];
        public Dictionary<string, decimal> CategoryMonthly { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string TopCategory { get; set; } = "Food";
    }

    private async Task<Snapshot> LoadSnapshotAsync(string userId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var from = today.AddDays(-30);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        var first = (user.FullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

        var txs = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= from && t.Date <= today)
            .ToListAsync(ct);

        var income = txs.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents) / 100m;
        var expenses = txs.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
        var byCat = txs.Where(t => t.Category.Type == CategoryType.Expense)
            .GroupBy(t => t.Category.Name)
            .ToDictionary(g => g.Key, g => decimal.Round(g.Sum(x => x.AmountCents) / 100m, 2), StringComparer.OrdinalIgnoreCase);

        // Scale 30-day window toward a monthly estimate.
        var scale = 30.4m / 30m;
        income = decimal.Round(income * scale, 2);
        expenses = decimal.Round(expenses * scale, 2);
        foreach (var key in byCat.Keys.ToList())
            byCat[key] = decimal.Round(byCat[key] * scale, 2);

        if (income <= 0 && user.AllowanceCents > 0)
            income = user.AllowanceCents / 100m;

        var goal = await db.SavingsGoals.AsNoTracking()
            .Where(g => g.UserId == userId && !g.IsComplete)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

        decimal goalTarget;
        decimal goalSaved;
        string goalName;
        bool tempGoal = false;
        if (goal != null)
        {
            goalTarget = goal.TargetCents / 100m;
            goalSaved = goal.SavedCents / 100m;
            goalName = goal.Name;
        }
        else if (user.SavingsGoalCents > 0)
        {
            goalTarget = user.SavingsGoalCents / 100m;
            goalSaved = 0;
            goalName = "Monthly savings target";
        }
        else
        {
            goalTarget = 500m;
            goalSaved = 0;
            goalName = "Laptop fund (sandbox)";
            tempGoal = true;
        }

        var remaining = Math.Max(0, goalTarget - goalSaved);
        var savings = decimal.Round(income - expenses, 2);
        var sliders = BuildSliders(byCat, expenses);
        var top = byCat.OrderByDescending(x => x.Value).Select(x => x.Key).FirstOrDefault() ?? "Food";

        return new Snapshot
        {
            FirstName = first.Length == 1 ? first.ToUpperInvariant() : char.ToUpperInvariant(first[0]) + first[1..],
            HasTransactions = txs.Count > 0,
            MonthlyIncome = income,
            MonthlyExpenses = expenses,
            CurrentMonthlySavings = savings,
            GoalName = goalName,
            GoalTarget = goalTarget,
            GoalSaved = goalSaved,
            GoalRemaining = remaining,
            GoalReached = remaining <= 0,
            UsingTemporaryGoal = tempGoal,
            SuggestedExtraSavings = Math.Clamp(decimal.Round(Math.Max(10m, income * 0.05m), 0), 10m, 100m),
            Sliders = sliders,
            CategoryMonthly = byCat,
            TopCategory = top
        };
    }

    private static List<FutureYouCategorySlider> BuildSliders(Dictionary<string, decimal> byCat, decimal totalExpenses)
    {
        var list = new List<FutureYouCategorySlider>();
        void Add(string key, string label, bool primary)
        {
            byCat.TryGetValue(key, out var current);
            if (!primary && current <= 0) return;
            if (primary && current <= 0)
                current = key.Equals("Food", StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(40m, decimal.Round(totalExpenses * 0.28m, 0))
                    : Math.Max(20m, decimal.Round(totalExpenses * 0.12m, 0));
            var smarter = decimal.Round(current * 0.85m, 2);
            var max = Math.Max(current * 1.6m, current + 40m);
            list.Add(new FutureYouCategorySlider
            {
                Key = key,
                Label = label,
                Current = current,
                Smarter = smarter,
                Scenario = current,
                Max = Math.Max(50m, decimal.Round(max, 0)),
                IsPrimary = primary
            });
        }

        Add("Food", "Monthly Food spending", true);
        Add("Entertainment", "Entertainment", true);
        foreach (var major in KnownMajor)
        {
            if (PrimaryKeys.Contains(major, StringComparer.OrdinalIgnoreCase)) continue;
            Add(major, major, false);
        }

        foreach (var pair in byCat.OrderByDescending(x => x.Value))
        {
            if (list.Any(s => s.Key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))) continue;
            if (pair.Value < 15m) continue;
            if (list.Count >= 6) break;
            Add(pair.Key, pair.Key, false);
        }

        return list;
    }

    private FutureYouSimulateResult BuildSimulation(Snapshot snapshot, FutureYouSimulateRequest? request)
    {
        var currentMap = snapshot.Sliders.ToDictionary(s => s.Key, s => s.Current, StringComparer.OrdinalIgnoreCase);
        var smarterMap = snapshot.Sliders.ToDictionary(s => s.Key, s => s.Smarter, StringComparer.OrdinalIgnoreCase);
        var scenarioMap = snapshot.Sliders.ToDictionary(s => s.Key, s => s.Scenario, StringComparer.OrdinalIgnoreCase);
        decimal extra = 0;

        if (request != null)
        {
            foreach (var (key, value) in request.Categories)
            {
                if (!scenarioMap.ContainsKey(key)) continue;
                scenarioMap[key] = Math.Max(0, decimal.Round(value, 2));
            }
            extra = Math.Max(0, decimal.Round(request.ExtraMonthlySavings, 2));
        }

        var otherCurrent = OtherExpenses(snapshot, currentMap);
        var currentSavings = SavingsFrom(snapshot.MonthlyIncome, currentMap, otherCurrent, 0);
        var smarterExtra = snapshot.SuggestedExtraSavings;
        var smarterSavings = SavingsFrom(snapshot.MonthlyIncome, smarterMap, otherCurrent * 0.97m, smarterExtra);
        var scenarioSavings = SavingsFrom(snapshot.MonthlyIncome, scenarioMap, otherCurrent, extra);

        var horizon = SharedChartHorizon(snapshot, currentSavings, smarterSavings, scenarioSavings);
        var current = Project("current", "Current You", currentSavings, snapshot, currentSavings, horizon);
        var smarter = Project("smarter", "Smarter You", smarterSavings, snapshot, currentSavings, horizon);
        var scenario = Project("scenario", "Your Scenario", scenarioSavings, snapshot, currentSavings, horizon);

        FutureYouDecisionImpact? impact = null;
        if (request?.PurchaseAmount is decimal purchase && purchase > 0)
            impact = BuildDecisionImpact(purchase, scenarioSavings, snapshot.GoalName);

        return new FutureYouSimulateResult
        {
            Ok = true,
            CurrentYou = current,
            SmarterYou = smarter,
            YourScenario = scenario,
            DecisionImpact = impact,
            GoalTarget = snapshot.GoalTarget,
            GoalTargetFormatted = currency.Format(snapshot.GoalTarget),
            GoalName = snapshot.GoalName,
            GoalRemaining = snapshot.GoalRemaining,
            GoalRemainingFormatted = currency.Format(snapshot.GoalRemaining),
            UsingTemporaryGoal = snapshot.UsingTemporaryGoal
        };
    }

    private static int SharedChartHorizon(Snapshot snapshot, params decimal[] monthlyRates)
    {
        if (snapshot.GoalReached || snapshot.GoalRemaining <= 0)
            return 12;

        var monthsNeeded = monthlyRates
            .Where(r => r > 0)
            .Select(r => (int)Math.Ceiling((double)(snapshot.GoalRemaining / r)))
            .DefaultIfEmpty(12)
            .Max();

        // Enough runway to see slopes diverge, even when the goal is hit quickly.
        return Math.Clamp(Math.Max(12, monthsNeeded + 3), 12, 24);
    }

    private static decimal OtherExpenses(Snapshot snapshot, Dictionary<string, decimal> mapped)
    {
        var mappedTotal = mapped.Values.Sum();
        return Math.Max(0, snapshot.MonthlyExpenses - mappedTotal);
    }

    private static decimal SavingsFrom(decimal income, Dictionary<string, decimal> cats, decimal other, decimal extra)
    {
        var expenses = cats.Values.Sum() + Math.Max(0, other);
        return decimal.Round(income - expenses + Math.Max(0, extra), 2);
    }

    private FutureYouProjection Project(
        string key,
        string title,
        decimal monthlySavings,
        Snapshot snapshot,
        decimal baselineSavings,
        int sharedHorizon)
    {
        var labels = new List<string>();
        var values = new List<decimal>();
        var start = DateOnly.FromDateTime(DateTime.Today);
        int? months = null;
        DateOnly? goalDate = null;

        if (snapshot.GoalReached)
        {
            months = 0;
            goalDate = start;
        }
        else if (monthlySavings > 0 && snapshot.GoalRemaining > 0)
        {
            var need = snapshot.GoalRemaining;
            var m = (int)Math.Ceiling((double)(need / monthlySavings));
            months = Math.Clamp(m, 1, 120);
            goalDate = start.AddMonths(months.Value);
        }

        // Start at "Now" so slopes are visible; do not clamp to the goal
        // (a flat goal line is drawn separately on the chart).
        decimal running = snapshot.GoalSaved;
        labels.Add("Now");
        values.Add(decimal.Round(running, 2));

        for (var i = 1; i <= sharedHorizon; i++)
        {
            running += monthlySavings;
            var plotted = Math.Max(0m, running);
            labels.Add(start.AddMonths(i).ToString("MMM yy"));
            values.Add(decimal.Round(plotted, 2));
        }

        var extraVs = decimal.Round(monthlySavings - baselineSavings, 2);
        var (tone, message) = Coaching(monthlySavings, months, snapshot, title, extraVs);

        return new FutureYouProjection
        {
            PathKey = key,
            Title = title,
            MonthlySavings = monthlySavings,
            MonthlySavingsFormatted = currency.Format(monthlySavings),
            MonthsToGoal = months,
            MonthsToGoalLabel = FormatMonths(months, monthlySavings, snapshot),
            GoalDate = goalDate,
            GoalDateLabel = goalDate?.ToString("MMM yyyy") ?? "Not yet on track",
            ExtraVsCurrent = extraVs,
            ExtraVsCurrentFormatted = currency.Format(extraVs),
            CoachingTone = tone,
            CoachingMessage = message,
            Accumulation = values,
            MonthLabels = labels
        };
    }

    private static string FormatMonths(int? months, decimal monthlySavings, Snapshot snapshot)
    {
        if (snapshot.GoalReached) return "Goal already reached";
        if (monthlySavings <= 0) return "Need positive monthly savings";
        if (months == null) return "Not enough data yet";
        if (months == 1) return "About 1 month";
        if (months >= 120) return "100+ months (try a smaller goal or higher savings)";
        return $"About {months} months";
    }

    private static (string Tone, string Message) Coaching(decimal monthlySavings, int? months, Snapshot snapshot, string path, decimal extraVs)
    {
        if (!snapshot.HasTransactions && snapshot.MonthlyIncome <= 0)
            return ("neutral", "Add a few transactions so Future You can learn your real campus rhythm. Estimates stay sandbox-only.");
        if (snapshot.GoalReached)
            return ("positive", $"Nice — {snapshot.GoalName} looks funded. Keep the habits that got you here.");
        if (monthlySavings <= 0)
            return ("caution", "Right now spending matches or exceeds income. Tiny cuts in food or entertainment can flip this to progress.");
        if (months is <= 6)
            return ("positive", $"{path}: you’re on a fast track to {snapshot.GoalName}. Small consistency beats big guilt.");
        if (extraVs > 0)
            return ("positive", $"This path frees about {extraVs:0.##} more per month than Current You — estimates only.");
        if (months is > 24)
            return ("caution", "The finish line is far but reachable. Try a Smarter You nudge or a temporary mini-goal.");
        return ("neutral", "Steady progress. Adjust the sliders to explore — nothing writes to your real budgets.");
    }

    private FutureYouDecisionImpact BuildDecisionImpact(decimal purchase, decimal monthlySavings, string goalName)
    {
        purchase = decimal.Round(Math.Max(0, purchase), 2);
        var delayDays = 0;
        string message;
        if (monthlySavings <= 0)
        {
            message = $"With no monthly surplus yet, {currency.Format(purchase)} mainly adds pressure — no goal delay math until savings turn positive.";
        }
        else
        {
            delayDays = (int)Math.Ceiling((double)(purchase / monthlySavings * 30m));
            delayDays = Math.Clamp(delayDays, 0, 3650);
            message = delayDays <= 0
                ? $"{currency.Format(purchase)} is small vs your projected savings pace — {goalName} barely notices."
                : $"Skipping {currency.Format(purchase)} could bring {goalName} about {delayDays} day{(delayDays == 1 ? "" : "s")} closer. Your call.";
        }

        return new FutureYouDecisionImpact
        {
            PurchaseAmount = purchase,
            PurchaseAmountFormatted = currency.Format(purchase),
            DelayDays = delayDays,
            Message = message
        };
    }

    private FutureYouMessageViewModel BuildMessage(Snapshot snapshot, FutureYouProjection scenario, decimal? purchase, string? categoryName = null)
    {
        var futureMonth = scenario.GoalDate?.ToString("MMMM yyyy")
            ?? DateOnly.FromDateTime(DateTime.Today).AddMonths(Math.Max(6, scenario.MonthsToGoal ?? 12)).ToString("MMMM yyyy");
        var amount = purchase is > 0 ? purchase.Value : 8m;
        var impact = BuildDecisionImpact(amount, scenario.MonthlySavings, snapshot.GoalName);
        var catBit = string.IsNullOrWhiteSpace(categoryName) ? "this" : categoryName.Trim().ToLowerInvariant();

        string body;
        if (snapshot.GoalReached)
            body = $"Hey {snapshot.FirstName}… we already made it to {snapshot.GoalName}. Protect the calm you built — even small {catBit} spends are optional now.";
        else if (impact.DelayDays > 0)
            body = $"Hey… if you skip this {currency.Format(amount)} {catBit} today, we’ll reach {snapshot.GoalName} about {impact.DelayDays} day{(impact.DelayDays == 1 ? "" : "s")} earlier. I’ve already waited long enough. Your call.";
        else
            body = $"Hey {snapshot.FirstName}… {snapshot.GoalName} is still ahead. A mindful {catBit} choice today keeps Future You cheering, not judging.";

        return new FutureYouMessageViewModel
        {
            FromLabel = $"Message from Future You ({futureMonth})",
            Body = body,
            Enabled = true
        };
    }

    private List<AlternativeTipViewModel> BuildTips(Snapshot snapshot)
    {
        var tips = new List<AlternativeTipViewModel>();
        void TipFor(string key, string habit, decimal unitHint)
        {
            if (!snapshot.CategoryMonthly.TryGetValue(key, out var monthly) || monthly < 12m) return;
            var weekly = decimal.Round(monthly / 4.3m, 2);
            var save = decimal.Round(Math.Min(weekly * 0.4m, unitHint * 2), 2);
            if (save < 3m) return;
            tips.Add(new AlternativeTipViewModel
            {
                Title = $"You usually spend about {currency.Format(weekly)}/week on {key.ToLowerInvariant()}.",
                Body = $"Making a cheaper {habit} swap a couple times this week could potentially save about {currency.Format(save)}.",
                CategoryKey = key,
                SuggestedWeeklySave = save,
                SuggestedWeeklySaveFormatted = currency.Format(save),
                SimulatorExtraMonthly = decimal.Round(save * 4m, 2),
                SimulatorCategoryCutPercent = 10
            });
        }

        TipFor("Food", "home-cooked / campus meal", 4.5m);
        TipFor("Entertainment", "free campus event", 6m);
        TipFor("Transport", "walk / shared ride", 3.5m);
        TipFor("Subscriptions", "shared plan pause", 5m);

        if (tips.Count == 0 && snapshot.HasTransactions)
        {
            tips.Add(new AlternativeTipViewModel
            {
                Title = "Small swaps still count.",
                Body = "Try parking an extra bit of cash toward your goal inside the simulator — it never touches real budgets unless you choose to later.",
                CategoryKey = "General",
                SuggestedWeeklySave = 5,
                SuggestedWeeklySaveFormatted = currency.Format(5),
                SimulatorExtraMonthly = 20
            });
        }

        return tips.Take(3).ToList();
    }

    private ParallelLivesViewModel BuildParallelLives(Snapshot snapshot, FutureYouSimulateResult simulate, int salt = 0)
    {
        var top = snapshot.TopCategory.ToLowerInvariant();
        var monthsA = simulate.CurrentYou.MonthsToGoalLabel.ToLowerInvariant();
        var monthsB = simulate.SmarterYou.MonthsToGoalLabel.ToLowerInvariant();
        var stress = snapshot.CurrentMonthlySavings < 0 ? "money stress hums under lectures" : "finances feel mostly manageable";
        var variant = Math.Abs(salt) % 2;

        string lifeA = variant == 0
            ? $"You keep spending on {top} the way you do now. Some weeks feel fine; others, {stress}. Sleep gets lighter near rent day. You still make campus events, but a last-minute trip with friends makes you hesitate. {snapshot.GoalName} stays on the wishlist for {monthsA}. You are not failing — you are busy surviving the semester."
            : $"Life stays familiar: same {top} rhythm, same late-night orders when projects pile up. Your calendar is full, yet energy dips after surprise expenses. Friends invite you out and you do a quiet mental math check first. Progress toward {snapshot.GoalName} crawls along ({monthsA}). It is honest, human, and a little heavier than it needs to be.";

        string lifeB = variant == 0
            ? $"You trim {top} just enough to notice — not enough to feel punished. Mornings feel clearer; you say yes to one spontaneous plan without the spiral. An opportunity (workshop, short course, better laptop) stops sounding impossible. {snapshot.GoalName} moves closer ({monthsB}). Future You still has exams and laundry — just fewer money knots in the chest."
            : $"With a gentler {top} habit and a tiny automatic save, evenings unwind easier. You sleep through more alarms because rent week is less dramatic. A campus door opens — interview outfit, conference fee, quieter housing deposit — and you can walk through it. {snapshot.GoalName} lands around {monthsB}. Same student life, softer edges.";

        return new ParallelLivesViewModel { LifeA = lifeA, LifeB = lifeB, Hidden = false };
    }
}
