using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class CampusChallengeService(ApplicationDbContext db, CurrencyService currency)
{
    public async Task<CampusChallengeViewModel> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        var challenge = await db.CampusChallenges.AsNoTracking()
            .Include(c => c.Days)
            .Where(c => c.UserId == userId && (c.Status == "Active" || c.Status == "Paused"))
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (challenge == null)
        {
            var completed = await db.CampusChallenges.AsNoTracking()
                .Include(c => c.Days)
                .Where(c => c.UserId == userId && c.Status == "Completed")
                .OrderByDescending(c => c.CompletedAt)
                .FirstOrDefaultAsync(ct);
            if (completed != null)
                return Map(completed);
            return new CampusChallengeViewModel();
        }

        return Map(challenge);
    }

    public async Task<CampusChallengeViewModel> StartAsync(string userId, CancellationToken ct = default)
    {
        var existing = await db.CampusChallenges
            .Include(c => c.Days)
            .FirstOrDefaultAsync(c => c.UserId == userId && (c.Status == "Active" || c.Status == "Paused"), ct);
        if (existing != null)
            return Map(existing);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var goals = await BuildMicroGoalsAsync(userId, ct);
        var challenge = new CampusChallenge
        {
            UserId = userId,
            Title = "30-Day Campus Challenge",
            Status = "Active",
            StartDate = today,
            StreakDays = 0,
            CreatedAt = DateTime.UtcNow
        };
        for (var i = 1; i <= 30; i++)
        {
            challenge.Days.Add(new CampusChallengeDay
            {
                DayNumber = i,
                Date = today.AddDays(i - 1),
                MicroGoal = goals[(i - 1) % goals.Count]
            });
        }

        db.CampusChallenges.Add(challenge);
        db.FutureYouUsageEvents.Add(new FutureYouUsageEvent
        {
            UserId = userId,
            EventType = "StartedChallenge",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Map(challenge);
    }

    public async Task<CampusChallengeViewModel?> SetStatusAsync(string userId, int challengeId, string status, CancellationToken ct = default)
    {
        var challenge = await db.CampusChallenges.Include(c => c.Days)
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId, ct);
        if (challenge == null) return null;

        status = status.Trim();
        if (status is not ("Active" or "Paused" or "Completed"))
            return Map(challenge);

        challenge.Status = status;
        if (status == "Completed")
        {
            challenge.CompletedAt = DateTime.UtcNow;
            db.FutureYouUsageEvents.Add(new FutureYouUsageEvent
            {
                UserId = userId,
                EventType = "CompletedChallenge",
                Timestamp = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return Map(challenge);
    }

    public async Task<CampusChallengeViewModel?> ToggleDayAsync(string userId, int challengeId, int dayNumber, CancellationToken ct = default)
    {
        var challenge = await db.CampusChallenges.Include(c => c.Days)
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.UserId == userId, ct);
        if (challenge == null) return null;
        if (challenge.Status == "Paused") return Map(challenge);

        var day = challenge.Days.FirstOrDefault(d => d.DayNumber == dayNumber);
        if (day == null) return Map(challenge);

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (day.Date > today) return Map(challenge);

        day.IsCompleted = !day.IsCompleted;
        day.CompletedAt = day.IsCompleted ? DateTime.UtcNow : null;
        challenge.StreakDays = ComputeStreak(challenge.Days.OrderBy(d => d.DayNumber).ToList());

        var completedCount = challenge.Days.Count(d => d.IsCompleted);
        if (completedCount >= 30)
        {
            challenge.Status = "Completed";
            challenge.CompletedAt = DateTime.UtcNow;
            db.FutureYouUsageEvents.Add(new FutureYouUsageEvent
            {
                UserId = userId,
                EventType = "CompletedChallenge",
                Timestamp = DateTime.UtcNow
            });
        }
        else if (challenge.Status == "Completed")
        {
            challenge.Status = "Active";
            challenge.CompletedAt = null;
        }

        await db.SaveChangesAsync(ct);
        return Map(challenge);
    }

    private async Task<List<string>> BuildMicroGoalsAsync(string userId, CancellationToken ct)
    {
        var templates = await db.ChallengeTemplates.AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.MicroGoalTemplate)
            .ToListAsync(ct);

        var from = DateOnly.FromDateTime(DateTime.Today).AddDays(-30);
        var cats = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Date >= from && t.Category.Type == CategoryType.Expense)
            .GroupBy(t => t.Category.Name)
            .Select(g => new { Name = g.Key, Total = g.Sum(x => x.AmountCents) })
            .OrderByDescending(x => x.Total)
            .Take(4)
            .ToListAsync(ct);

        var goals = new List<string>();
        if (cats.Any(c => c.Name.Contains("Food", StringComparison.OrdinalIgnoreCase)))
        {
            goals.Add("No food delivery for the next 3 days");
            goals.Add("Cook at home 4 times this week");
            goals.Add("Pack one campus lunch instead of buying out");
        }
        if (cats.Any(c => c.Name.Contains("Entertainment", StringComparison.OrdinalIgnoreCase)))
            goals.Add("Choose one free campus event instead of a paid outing");
        if (cats.Any(c => c.Name.Contains("Transport", StringComparison.OrdinalIgnoreCase)))
            goals.Add("Walk or share a ride once today");

        var saveAmt = currency.Format(5);
        goals.Add($"Save an extra {saveAmt} today");
        goals.Add("Log every expense before midnight");
        goals.Add("Skip one impulse snack purchase");
        goals.Add("Check your budget before a non-essential buy");
        goals.Add("Move leftover coffee money to your goal (in the simulator)");

        foreach (var t in templates)
            if (!string.IsNullOrWhiteSpace(t)) goals.Add(t.Trim());

        if (goals.Count == 0)
        {
            goals.Add("Log every expense before midnight");
            goals.Add($"Save an extra {saveAmt} today");
            goals.Add("Skip one impulse snack purchase");
        }

        while (goals.Count < 30)
            goals.Add(goals[goals.Count % goals.Count]);

        return goals;
    }

    private static int ComputeStreak(List<CampusChallengeDay> days)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var streak = 0;
        foreach (var day in days.Where(d => d.Date <= today).OrderByDescending(d => d.DayNumber))
        {
            if (!day.IsCompleted) break;
            streak++;
        }
        return streak;
    }

    private static CampusChallengeViewModel Map(CampusChallenge challenge)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var days = challenge.Days.OrderBy(d => d.DayNumber).ToList();
        var completed = days.Count(d => d.IsCompleted);
        var total = Math.Max(1, days.Count);
        var todayDay = days.FirstOrDefault(d => d.Date == today)
            ?? days.LastOrDefault(d => d.Date <= today)
            ?? days.FirstOrDefault();

        return new CampusChallengeViewModel
        {
            HasChallenge = true,
            ChallengeId = challenge.Id,
            Title = challenge.Title,
            Status = challenge.Status,
            DayNumber = todayDay?.DayNumber ?? 1,
            CompletedDays = completed,
            TotalDays = total,
            StreakDays = challenge.StreakDays,
            CompletionPercent = (int)Math.Round(completed * 100.0 / total),
            TodayMicroGoal = todayDay?.MicroGoal,
            TodayCompleted = todayDay?.IsCompleted ?? false,
            Days = days.Select(d => new CampusChallengeDayViewModel
            {
                DayNumber = d.DayNumber,
                Date = d.Date,
                MicroGoal = d.MicroGoal,
                IsCompleted = d.IsCompleted,
                IsToday = d.Date == today,
                IsFuture = d.Date > today
            }).ToList()
        };
    }
}
