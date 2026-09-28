using CampusCoin.Data;
using CampusCoin.Models;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Services;

public class RecurringTransactionService(ApplicationDbContext db)
{
    public async Task<int> ApplyDueAsync(string userId, DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.Today);
        var due = await db.RecurringTransactions
            .Include(x => x.Category)
            .Where(x => x.UserId == userId && x.IsActive && x.NextRunDate <= today)
            .ToListAsync();
        var created = 0;
        foreach (var recurring in due)
        {
            var next = recurring.NextRunDate;
            while (next <= today)
            {
                db.Transactions.Add(new Transaction
                {
                    UserId = userId,
                    CategoryId = recurring.CategoryId,
                    AmountCents = recurring.AmountCents,
                    Description = recurring.Description + " (recurring)",
                    Date = next
                });
                created++;
                next = recurring.Frequency == RecurrenceFrequency.Weekly ? next.AddDays(7) : next.AddMonths(1);
            }
            recurring.NextRunDate = next;
        }
        if (created > 0 || due.Count > 0) await db.SaveChangesAsync();
        return created;
    }
}
