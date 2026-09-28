using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public interface ISundayLetterService
{
    Task<SundayLetterCardViewModel> GetCardAsync(string userId, CancellationToken ct = default);
    Task<SundayLetterCardViewModel?> OpenAsync(string userId, int letterId, CancellationToken ct = default);
    Task<SundayLetterCardViewModel?> SaveAsync(string userId, int letterId, CancellationToken ct = default);
}

/// <summary>
/// Weekly sealed Sunday letter — warm narrative from real spending habits.
/// Never mutates transactions or budgets.
/// </summary>
public class SundayLetterService(ApplicationDbContext db) : ISundayLetterService
{
    public async Task<SundayLetterCardViewModel> GetCardAsync(string userId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = WeekStartMonday(today);
        var unlock = weekStart.AddDays(6);

        var letter = await EnsureLetterAsync(userId, weekStart, unlock, ct);
        var saved = await db.SundayLetters.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsSaved)
            .OrderByDescending(x => x.UnlockDate)
            .Take(12)
            .ToListAsync(ct);

        var archive = saved.Select(x => new SundayLetterArchiveItem
        {
            Id = x.Id,
            Title = x.Title,
            UnlockDate = x.UnlockDate,
            WeekLabel = x.WeekStart.ToString("MMM d") + " – " + x.UnlockDate.ToString("MMM d"),
            Preview = x.Body.Length > 110 ? x.Body.Substring(0, 110).Trim() + "…" : x.Body,
            Body = x.Body,
            Tone = x.Tone,
            IsOpened = x.IsOpened
        }).ToList();

        return MapCard(letter, today, archive);
    }

    public async Task<SundayLetterCardViewModel?> OpenAsync(string userId, int letterId, CancellationToken ct = default)
    {
        var letter = await db.SundayLetters.FirstOrDefaultAsync(x => x.Id == letterId && x.UserId == userId, ct);
        if (letter == null) return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today < letter.UnlockDate)
            return await GetCardAsync(userId, ct);

        // Refresh body on open so Sunday includes the full week.
        await RefreshBodyAsync(letter, ct);

        if (!letter.IsOpened)
        {
            letter.IsOpened = true;
            letter.OpenedAt = DateTime.UtcNow;
        }
        letter.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetCardAsync(userId, ct);
    }

    public async Task<SundayLetterCardViewModel?> SaveAsync(string userId, int letterId, CancellationToken ct = default)
    {
        var letter = await db.SundayLetters.FirstOrDefaultAsync(x => x.Id == letterId && x.UserId == userId, ct);
        if (letter == null) return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today < letter.UnlockDate || !letter.IsOpened)
            return await GetCardAsync(userId, ct);

        letter.IsSaved = true;
        letter.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetCardAsync(userId, ct);
    }

    private async Task<SundayLetter> EnsureLetterAsync(string userId, DateOnly weekStart, DateOnly unlock, CancellationToken ct)
    {
        var letter = await db.SundayLetters
            .FirstOrDefaultAsync(x => x.UserId == userId && x.WeekStart == weekStart, ct);

        if (letter == null)
        {
            letter = new SundayLetter
            {
                UserId = userId,
                WeekStart = weekStart,
                UnlockDate = unlock,
                CreatedAt = DateTime.UtcNow
            };
            await ComposeAsync(letter, ct);
            db.SundayLetters.Add(letter);
            await db.SaveChangesAsync(ct);
            return letter;
        }

        // Keep sealed letters fresh until opened; opened letters stay as read.
        if (!letter.IsOpened)
        {
            await ComposeAsync(letter, ct);
            letter.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return letter;
    }

    private async Task RefreshBodyAsync(SundayLetter letter, CancellationToken ct)
    {
        await ComposeAsync(letter, ct);
        letter.UpdatedAt = DateTime.UtcNow;
    }

    private async Task ComposeAsync(SundayLetter letter, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var endExclusive = letter.UnlockDate.AddDays(1);
        var through = today < letter.UnlockDate ? today.AddDays(1) : endExclusive;

        var txs = await db.Transactions.AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == letter.UserId && !t.IsDeleted && t.Date >= letter.WeekStart && t.Date < through)
            .ToListAsync(ct);

        var month = new DateOnly(letter.WeekStart.Year, letter.WeekStart.Month, 1);
        var budgets = await db.Budgets.AsNoTracking()
            .Where(b => b.UserId == letter.UserId && b.Month == month)
            .ToListAsync(ct);

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == letter.UserId, ct);
        var firstName = (user.FullName ?? "friend").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "friend";

        var daysCovered = Math.Max(1, (through.DayNumber - letter.WeekStart.DayNumber));
        daysCovered = Math.Min(daysCovered, 7);

        var goodDays = 0;
        var heavyDays = 0;
        for (var i = 0; i < daysCovered; i++)
        {
            var day = letter.WeekStart.AddDays(i);
            var dayExpense = txs.Where(t => t.Date == day && t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents);
            var dayIncome = txs.Where(t => t.Date == day && t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents);
            var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            var dailyPace = budgets.Count > 0
                ? budgets.Sum(b => b.LimitCents) / (double)Math.Max(daysInMonth, 1)
                : 0;

            var good = budgets.Count > 0
                ? dayExpense <= dailyPace * 1.08
                : dayExpense == 0 || dayIncome >= dayExpense;

            if (good) goodDays++;
            else if (dayExpense > 0) heavyDays++;
        }

        var weekExpense = txs.Where(t => t.Category.Type == CategoryType.Expense).Sum(t => t.AmountCents) / 100m;
        var weekIncome = txs.Where(t => t.Category.Type == CategoryType.Income).Sum(t => t.AmountCents) / 100m;
        var topCategory = txs.Where(t => t.Category.Type == CategoryType.Expense)
            .GroupBy(t => t.Category.Name)
            .OrderByDescending(g => g.Sum(x => x.AmountCents))
            .Select(g => g.Key)
            .FirstOrDefault();

        var tone = goodDays >= 5 ? "hopeful" : goodDays >= 3 ? "steady" : "recovery";
        letter.Tone = tone;
        letter.GoodDays = goodDays;
        letter.Title = tone switch
        {
            "hopeful" => "A quiet win, sealed for you",
            "recovery" => "A soft letter for a hard week",
            _ => "Your Sunday letter"
        };

        var lines = new List<string>();
        if (txs.Count == 0)
        {
            lines.Add($"Hey {firstName} — this week was quiet in the ledger.");
            lines.Add("That’s okay. A sealed letter still arrives.");
            lines.Add("Next week, one small check-in can give this ritual more to celebrate.");
        }
        else
        {
            lines.Add($"This week you protected your goal on {goodDays} out of {daysCovered} days.");
            if (heavyDays > 0)
                lines.Add(heavyDays == 1
                    ? "There was one heavy evening, but you recovered."
                    : $"There were a few heavier days — and you still came back.");
            else
                lines.Add("No dramatic crashes. Just steady, human choices.");

            if (!string.IsNullOrWhiteSpace(topCategory) && weekExpense > 0)
                lines.Add($"{topCategory} asked for the most attention this week.");

            if (weekIncome > weekExpense)
                lines.Add("You’re quietly getting stronger.");
            else if (weekIncome > 0 && weekExpense > weekIncome)
                lines.Add("Spending ran ahead of income — noticing that is already a kind of strength.");
            else
                lines.Add("You’re building awareness. That compounds.");

            lines.Add(tone == "hopeful"
                ? "Next week can be even lighter."
                : tone == "recovery"
                    ? "Next week doesn’t need to be perfect — just a little kinder."
                    : "Keep the ritual. Sunday will meet you again.");
        }

        letter.Body = string.Join("\n\n", lines);
    }

    private static SundayLetterCardViewModel MapCard(SundayLetter letter, DateOnly today, List<SundayLetterArchiveItem> archive)
    {
        var unlocked = today >= letter.UnlockDate;
        var daysUntil = unlocked ? 0 : letter.UnlockDate.DayNumber - today.DayNumber;

        return new SundayLetterCardViewModel
        {
            LetterId = letter.Id,
            WeekStart = letter.WeekStart,
            UnlockDate = letter.UnlockDate,
            WeekLabel = $"{letter.WeekStart:MMM d} – {letter.UnlockDate:MMM d}",
            IsUnlocked = unlocked,
            IsOpened = letter.IsOpened && unlocked,
            IsSaved = letter.IsSaved,
            DaysUntilUnlock = Math.Max(0, daysUntil),
            UnlockLabel = unlocked
                ? (letter.IsOpened ? "Opened" : "Ready to open")
                : daysUntil == 1 ? "Unlocks tomorrow"
                : $"Unlocks in {daysUntil} days",
            Title = letter.Title,
            Body = unlocked && letter.IsOpened ? letter.Body : "",
            SealedHint = unlocked
                ? "Your sealed letter is waiting. Open it when you’re ready."
                : "Campus Coin is writing your week. The seal breaks on Sunday.",
            Tone = letter.Tone,
            GoodDays = letter.GoodDays,
            LetterBox = archive
        };
    }

    private static DateOnly WeekStartMonday(DateOnly day)
    {
        var diff = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return day.AddDays(-diff);
    }
}
