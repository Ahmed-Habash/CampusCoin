using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public interface IGardenService
{
    Task<TreeHealthViewModel> EvaluateAndPersistAsync(string userId, DateOnly month, CancellationToken cancellationToken = default);
}

/// <summary>
/// Money Garden — advisory visual state from budgets, savings, cashflow, consistency, and challenges.
/// Never mutates transactions or budgets.
/// </summary>
public class GardenService(ApplicationDbContext db) : IGardenService
{
    private static readonly string[] SoftMessages =
    [
        "Your garden is breathing easier today.",
        "A few flowers need care… but nothing is lost.",
        "Something new is starting to grow.",
        "The garden remembers your good days.",
        "Steady roots. Soft light. Keep going.",
        "Even quiet days water the soil.",
        "A rare bloom is waiting for one more kind choice.",
        "The canopy is listening — and it likes this pace."
    ];

    public async Task<TreeHealthViewModel> EvaluateAndPersistAsync(string userId, DateOnly month, CancellationToken cancellationToken = default)
    {
        month = new DateOnly(month.Year, month.Month, 1);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var isPastMonth = monthEnd < today;
        var isCurrentMonth = month.Year == today.Year && month.Month == today.Month;

        var snapshot = await db.FinancialHealthTrees
            .FirstOrDefaultAsync(x => x.UserId == userId && x.Month == month, cancellationToken);

        if (snapshot is { IsMonthFinalized: true } && isPastMonth)
            return ToViewModel(snapshot, isPastMonth);

        var scorePack = await ComputeAsync(userId, month, cancellationToken);
        var state = ScoreToState(scorePack.Score);
        var growth = Math.Clamp(scorePack.Score, 10, 100);
        var unlocked = ResolveUnlocks(scorePack.Score, scorePack.ConsistencyDays, scorePack.ChallengeBoost, snapshot?.UnlockedElements);
        var soft = PickSoftMessage(state, scorePack.Score, scorePack.IsNewUser, scorePack.Improved);

        if (snapshot == null)
        {
            snapshot = new FinancialHealthTree
            {
                UserId = userId,
                Month = month,
                HealthScore = scorePack.Score,
                State = state,
                PreviousState = state,
                GrowthPercent = growth,
                IsMonthFinalized = isPastMonth,
                ConsistencyDays = scorePack.ConsistencyDays,
                BloomCount = scorePack.BloomCount,
                UnlockedElements = unlocked,
                SoftMessage = soft,
                UpdatedAt = DateTime.UtcNow
            };
            db.FinancialHealthTrees.Add(snapshot);
        }
        else
        {
            snapshot.PreviousState = snapshot.State;
            snapshot.HealthScore = scorePack.Score;
            snapshot.State = state;
            snapshot.GrowthPercent = growth;
            snapshot.IsMonthFinalized = isPastMonth || (!isCurrentMonth && month < today);
            snapshot.ConsistencyDays = scorePack.ConsistencyDays;
            snapshot.BloomCount = scorePack.BloomCount;
            snapshot.UnlockedElements = MergeUnlocks(snapshot.UnlockedElements, unlocked);
            snapshot.SoftMessage = soft;
            snapshot.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToViewModel(snapshot, isPastMonth);
    }

    private async Task<(int Score, int ConsistencyDays, int BloomCount, bool ChallengeBoost, bool IsNewUser, bool Improved)> ComputeAsync(
        string userId, DateOnly month, CancellationToken cancellationToken)
    {
        var next = month.AddMonths(1);
        var lookbackStart = month.AddDays(-14);
        if (lookbackStart < month.AddMonths(-1)) lookbackStart = month.AddDays(-13);

        var today = DateOnly.FromDateTime(DateTime.Today);

        var transactions = await db.Transactions.AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.UserId == userId && !x.IsDeleted && x.Date >= lookbackStart && x.Date < next)
            .ToListAsync(cancellationToken);

        var monthTx = transactions.Where(x => x.Date >= month && x.Date < next).ToList();
        var incomeCents = monthTx.Where(x => x.Category.Type == CategoryType.Income).Sum(x => x.AmountCents);
        var expenseCents = monthTx.Where(x => x.Category.Type == CategoryType.Expense).Sum(x => x.AmountCents);
        var balanceCents = incomeCents - expenseCents;
        var isNewUser = monthTx.Count == 0;

        var budgets = await db.Budgets.AsNoTracking()
            .Where(x => x.UserId == userId && x.Month == month)
            .ToListAsync(cancellationToken);

        double budgetScore = 55;
        if (budgets.Count > 0)
        {
            var ratios = budgets.Select(b =>
            {
                var spent = monthTx.Where(t => t.CategoryId == b.CategoryId && t.Category.Type == CategoryType.Expense)
                    .Sum(t => t.AmountCents);
                if (b.LimitCents <= 0) return 50d;
                var ratio = (double)spent / b.LimitCents;
                if (ratio <= 0.7) return 100d;
                if (ratio <= 0.9) return 82d;
                if (ratio <= 1.0) return 68d;
                if (ratio <= 1.15) return 40d;
                if (ratio <= 1.35) return 22d;
                return 8d;
            }).ToList();
            budgetScore = ratios.Average();
        }
        else if (isNewUser)
        {
            budgetScore = 52;
        }

        var goals = await db.SavingsGoals.AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsComplete)
            .ToListAsync(cancellationToken);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, cancellationToken);

        double savingsScore = 50;
        if (goals.Count > 0)
        {
            savingsScore = goals.Average(g =>
            {
                if (g.TargetCents <= 0) return 50d;
                return Math.Clamp(g.SavedCents * 100d / g.TargetCents, 0, 100);
            });
        }
        else if (user.SavingsGoalCents > 0)
        {
            savingsScore = balanceCents >= user.SavingsGoalCents
                ? 92
                : balanceCents > 0
                    ? Math.Clamp(balanceCents * 100d / user.SavingsGoalCents, 15, 85)
                    : 18;
        }
        else if (balanceCents > 0) savingsScore = 62;
        else if (balanceCents < 0) savingsScore = 28;

        double cashflowScore = balanceCents > 0 ? 80 : balanceCents == 0 ? 55 : 25;
        if (incomeCents > 0 && expenseCents > incomeCents * 1.2)
            cashflowScore = 12;

        // Consistency: days in last 14 with under-budget or net-positive days when budgets exist.
        var consistencyDays = 0;
        for (var d = 0; d < 14; d++)
        {
            var day = today.AddDays(-d);
            if (day < month) continue;
            var dayExpense = transactions
                .Where(t => t.Date == day && t.Category.Type == CategoryType.Expense)
                .Sum(t => t.AmountCents);
            var dayIncome = transactions
                .Where(t => t.Date == day && t.Category.Type == CategoryType.Income)
                .Sum(t => t.AmountCents);

            var good = false;
            if (budgets.Count > 0)
            {
                // Approximate daily budget pace: monthly limit / days-in-month
                var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
                var dailyPace = budgets.Sum(b => b.LimitCents) / (double)Math.Max(daysInMonth, 1);
                good = dayExpense <= dailyPace * 1.05;
            }
            else
            {
                good = dayExpense == 0 || dayIncome >= dayExpense;
            }

            if (good) consistencyDays++;
        }

        var consistencyScore = Math.Clamp(consistencyDays * (100d / 14d), 0, 100);

        var challenge = await db.CampusChallenges.AsNoTracking()
            .Include(c => c.Days)
            .Where(c => c.UserId == userId && (c.Status == "Active" || c.Status == "Completed"))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var challengeBoost = false;
        double challengeScore = 50;
        if (challenge != null)
        {
            var done = challenge.Days.Count(d => d.IsCompleted);
            var total = Math.Max(challenge.Days.Count, 1);
            challengeScore = done * 100d / total;
            challengeBoost = done >= 7 || string.Equals(challenge.Status, "Completed", StringComparison.OrdinalIgnoreCase);
        }

        var score = (int)Math.Round(
            budgetScore * 0.34 +
            savingsScore * 0.26 +
            cashflowScore * 0.16 +
            consistencyScore * 0.16 +
            challengeScore * 0.08);

        score = Math.Clamp(score, 0, 100);
        if (isNewUser) score = Math.Clamp(score, 42, 58);

        var previous = await db.FinancialHealthTrees.AsNoTracking()
            .Where(x => x.UserId == userId && x.Month == month)
            .Select(x => (int?)x.HealthScore)
            .FirstOrDefaultAsync(cancellationToken);
        var improved = previous is int p && score > p;

        var bloomCount = score switch
        {
            >= 88 => 6,
            >= 75 => 5,
            >= 60 => 4,
            >= 45 => 3,
            >= 28 => 2,
            _ => 1
        };

        return (score, consistencyDays, bloomCount, challengeBoost, isNewUser, improved);
    }

    private static string ResolveUnlocks(int score, int consistencyDays, bool challengeBoost, string? existing)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (existing ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part);

        set.Add("tree");
        set.Add("grass");
        if (score >= 28) set.Add("flowers");
        if (score >= 45) set.Add("bush");
        if (score >= 60 || consistencyDays >= 7) set.Add("butterfly");
        if (score >= 72) set.Add("bird");
        if (score >= 80) set.Add("glow");
        if (score >= 88 || challengeBoost) set.Add("rare");
        if (consistencyDays >= 10) set.Add("fox");

        return string.Join(',', set);
    }

    private static string MergeUnlocks(string current, string next)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in $"{current},{next}".Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(part);
        return string.Join(',', set);
    }

    private static string PickSoftMessage(TreeHealthState state, int score, bool isNewUser, bool improved)
    {
        if (isNewUser) return "A tiny garden is waking up. Add a few check-ins and watch it grow.";
        if (improved) return "Something new is starting to grow.";
        return state switch
        {
            TreeHealthState.Flowering => score >= 90
                ? "The canopy is glowing — your consistency shows."
                : SoftMessages[0],
            TreeHealthState.Healthy => SoftMessages[3],
            TreeHealthState.Steady => SoftMessages[4],
            TreeHealthState.Stressed => SoftMessages[1],
            _ => SoftMessages[5]
        };
    }

    private static TreeHealthState ScoreToState(int score) => score switch
    {
        >= 80 => TreeHealthState.Flowering,
        >= 60 => TreeHealthState.Healthy,
        >= 40 => TreeHealthState.Steady,
        >= 20 => TreeHealthState.Stressed,
        _ => TreeHealthState.Wilted
    };

    private static TreeHealthViewModel ToViewModel(FinancialHealthTree tree, bool isMonthEnd)
    {
        var unlocked = tree.UnlockedElements
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var (title, blurb) = tree.State switch
        {
            TreeHealthState.Flowering => ("Blooming", isMonthEnd
                ? "Month closed in full bloom — rare light gathered around your tree."
                : "Budgets held and savings bloom. The garden is celebrating with you."),
            TreeHealthState.Healthy => ("Lush", isMonthEnd
                ? "A solid month. Your garden stands green and calm."
                : "You're under control. Soft growth is filling the beds."),
            TreeHealthState.Steady => ("Settling in", isMonthEnd
                ? "A balanced month — rooted, with room to grow."
                : "Holding steady. Stay near your budgets to invite new blooms."),
            TreeHealthState.Stressed => ("Needs care", isMonthEnd
                ? "The month left a few petals soft. Next month can rebuild the color."
                : "A few flowers need care… but nothing is lost."),
            _ => ("Quiet soil", isMonthEnd
                ? "A tough month quieted the garden. Fresh soil starts on day one."
                : "Even quiet days water the soil. One kind choice brings color back.")
        };

        var motion = tree.State == tree.PreviousState
            ? "idle"
            : (int)tree.State > (int)tree.PreviousState ? "grow" : "wilt";

        var soft = string.IsNullOrWhiteSpace(tree.SoftMessage) ? blurb : tree.SoftMessage;

        return new TreeHealthViewModel
        {
            Month = tree.Month,
            HealthScore = tree.HealthScore,
            GrowthPercent = tree.GrowthPercent,
            State = tree.State.ToString(),
            PreviousState = tree.PreviousState.ToString(),
            Title = title,
            Blurb = blurb,
            SoftMessage = soft,
            Motion = motion,
            IsMonthFinalized = tree.IsMonthFinalized || isMonthEnd,
            UpdatedAt = tree.UpdatedAt,
            ConsistencyDays = tree.ConsistencyDays,
            BloomCount = tree.BloomCount,
            UnlockedElements = unlocked,
            HasButterfly = unlocked.Contains("butterfly", StringComparer.OrdinalIgnoreCase),
            HasBird = unlocked.Contains("bird", StringComparer.OrdinalIgnoreCase),
            HasFox = unlocked.Contains("fox", StringComparer.OrdinalIgnoreCase),
            HasGlow = unlocked.Contains("glow", StringComparer.OrdinalIgnoreCase),
            HasRare = unlocked.Contains("rare", StringComparer.OrdinalIgnoreCase),
            HasBush = unlocked.Contains("bush", StringComparer.OrdinalIgnoreCase)
        };
    }
}
