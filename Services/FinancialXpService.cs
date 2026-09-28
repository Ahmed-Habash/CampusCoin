using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class FinancialXpService(ApplicationDbContext db)
{
    private static readonly string[] Titles =
    [
        "Budget Rookie",
        "Smart Spender",
        "Habit Builder",
        "Budget Pro",
        "Money Master",
        "Campus Saver",
        "Wealth Wizard",
        "Legend of Ledger"
    ];

    public async Task<XpProgressViewModel> SyncAndGetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await db.UserXpProfiles.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile == null)
        {
            profile = new UserXpProfile { UserId = userId, Level = 1, Title = Titles[0], TotalXp = 0, UpdatedAt = DateTime.UtcNow };
            db.UserXpProfiles.Add(profile);
            await db.SaveChangesAsync(cancellationToken);
        }

        var previousLevel = profile.Level;
        await AwardFromActivityAsync(userId, cancellationToken);

        // Reload after awards
        profile = await db.UserXpProfiles.SingleAsync(x => x.UserId == userId, cancellationToken);
        var leveledUp = profile.Level > previousLevel;
        return ToViewModel(profile, leveledUp);
    }

    public async Task<XpProgressViewModel> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await db.UserXpProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile == null)
            return new XpProgressViewModel();
        return ToViewModel(profile, false);
    }

    public async Task AwardTransactionAsync(string userId, int transactionId, CancellationToken cancellationToken = default)
    {
        await EnsureProfileAsync(userId, cancellationToken);
        await TryAwardAsync(userId, $"tx:{transactionId}", 10, "Logged a transaction", cancellationToken);
    }

    public async Task AwardSavingsGoalAsync(string userId, int goalId, CancellationToken cancellationToken = default)
    {
        await EnsureProfileAsync(userId, cancellationToken);
        await TryAwardAsync(userId, $"goal:{goalId}", 50, "Hit a savings goal", cancellationToken);
    }

    private async Task AwardFromActivityAsync(string userId, CancellationToken cancellationToken)
    {
        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var next = month.AddMonths(1);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var yesterday = today.AddDays(-1);

        // Recent transactions (cap scan for performance)
        var recentTx = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted)
            .OrderByDescending(t => t.Id)
            .Take(40)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in recentTx)
            await TryAwardAsync(userId, $"tx:{id}", 10, "Logged a transaction", cancellationToken);

        // Under-budget categories this month
        var budgets = await db.Budgets.AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.UserId == userId && b.Month == month)
            .ToListAsync(cancellationToken);
        if (budgets.Count > 0)
        {
            var spentByCat = await db.Transactions.AsNoTracking()
                .Include(t => t.Category)
                .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= month && t.Date < next && t.Category.Type == CategoryType.Expense)
                .GroupBy(t => t.CategoryId)
                .Select(g => new { CategoryId = g.Key, Spent = g.Sum(x => x.AmountCents) })
                .ToListAsync(cancellationToken);
            var spentMap = spentByCat.ToDictionary(x => x.CategoryId, x => x.Spent);
            foreach (var b in budgets)
            {
                var spent = spentMap.GetValueOrDefault(b.CategoryId);
                if (spent < b.LimitCents)
                    await TryAwardAsync(userId, $"budget:{month:yyyy-MM}:{b.CategoryId}", 25, $"Under budget in {b.Category.Name}", cancellationToken);
            }
        }

        // Day without unnecessary spending = yesterday had no expense transactions
        var hadExpenseYesterday = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .AnyAsync(t => t.UserId == userId && !t.IsDeleted && t.Date == yesterday && t.Category.Type == CategoryType.Expense, cancellationToken);
        if (!hadExpenseYesterday && yesterday >= month)
            await TryAwardAsync(userId, $"frugal:{yesterday:yyyy-MM-dd}", 15, "A day without unnecessary spending", cancellationToken);

        // Completed savings goals
        var goals = await db.SavingsGoals.AsNoTracking()
            .Where(g => g.UserId == userId && g.IsComplete)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in goals)
            await TryAwardAsync(userId, $"goal:{id}", 50, "Hit a savings goal", cancellationToken);
    }

    private async Task EnsureProfileAsync(string userId, CancellationToken cancellationToken)
    {
        if (await db.UserXpProfiles.AnyAsync(x => x.UserId == userId, cancellationToken)) return;
        db.UserXpProfiles.Add(new UserXpProfile { UserId = userId, Level = 1, Title = Titles[0], UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task TryAwardAsync(string userId, string eventKey, int xp, string reason, CancellationToken cancellationToken)
    {
        var exists = await db.UserXpEvents.AnyAsync(e => e.UserId == userId && e.EventKey == eventKey, cancellationToken);
        if (exists) return;

        db.UserXpEvents.Add(new UserXpEvent
        {
            UserId = userId,
            EventKey = eventKey,
            XpAwarded = xp,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        });

        var profile = await db.UserXpProfiles.SingleAsync(x => x.UserId == userId, cancellationToken);
        profile.TotalXp += xp;
        ApplyLevel(profile);
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static int XpRequiredToReachLevel(int level)
    {
        if (level <= 1) return 0;
        var total = 0;
        for (var l = 2; l <= level; l++)
            total += 50 + (l - 1) * 50; // 100, 150, 200, 250...
        return total;
    }

    public static void ApplyLevel(UserXpProfile profile)
    {
        var level = 1;
        while (XpRequiredToReachLevel(level + 1) <= profile.TotalXp && level < 50)
            level++;
        profile.Level = level;
        profile.Title = Titles[Math.Min(level - 1, Titles.Length - 1)];
    }

    private static XpProgressViewModel ToViewModel(UserXpProfile profile, bool leveledUp)
    {
        var currentFloor = XpRequiredToReachLevel(profile.Level);
        var nextFloor = XpRequiredToReachLevel(profile.Level + 1);
        var span = Math.Max(1, nextFloor - currentFloor);
        var into = Math.Clamp(profile.TotalXp - currentFloor, 0, span);
        return new XpProgressViewModel
        {
            Level = profile.Level,
            Title = profile.Title,
            TotalXp = profile.TotalXp,
            XpIntoLevel = into,
            XpForNextLevel = span,
            ProgressPercent = (int)Math.Round(into * 100d / span),
            JustLeveledUp = leveledUp
        };
    }
}
