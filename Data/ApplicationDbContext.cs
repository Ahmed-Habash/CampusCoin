using CampusCoin.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionRevision> TransactionRevisions => Set<TransactionRevision>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<Insight> Insights => Set<Insight>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<TipTemplate> TipTemplates => Set<TipTemplate>();
    public DbSet<FinancialHealthTree> FinancialHealthTrees => Set<FinancialHealthTree>();
    public DbSet<AdminActivityLog> AdminActivityLogs => Set<AdminActivityLog>();
    public DbSet<RoommateGroup> RoommateGroups => Set<RoommateGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<SharedExpense> SharedExpenses => Set<SharedExpense>();
    public DbSet<SharedExpenseParticipant> SharedExpenseParticipants => Set<SharedExpenseParticipant>();
    public DbSet<SettleUpLog> SettleUpLogs => Set<SettleUpLog>();
    public DbSet<GlobalSystemSetting> GlobalSystemSettings => Set<GlobalSystemSetting>();
    public DbSet<GroupChatMessage> GroupChatMessages => Set<GroupChatMessage>();
    public DbSet<GroupAbuseReport> GroupAbuseReports => Set<GroupAbuseReport>();
    public DbSet<EventInviteCode> EventInviteCodes => Set<EventInviteCode>();
    public DbSet<AdminApplication> AdminApplications => Set<AdminApplication>();
    public DbSet<UserXpProfile> UserXpProfiles => Set<UserXpProfile>();
    public DbSet<UserXpEvent> UserXpEvents => Set<UserXpEvent>();
    public DbSet<CampusChallenge> CampusChallenges => Set<CampusChallenge>();
    public DbSet<CampusChallengeDay> CampusChallengeDays => Set<CampusChallengeDay>();
    public DbSet<ChallengeTemplate> ChallengeTemplates => Set<ChallengeTemplate>();
    public DbSet<FutureYouUsageEvent> FutureYouUsageEvents => Set<FutureYouUsageEvent>();
    public DbSet<SundayLetter> SundayLetters => Set<SundayLetter>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Category>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transaction>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Budget>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Transaction>().HasIndex(x => new { x.UserId, x.Date });
        b.Entity<Budget>().HasIndex(x => new { x.UserId, x.CategoryId, x.Month }).IsUnique();
        b.Entity<RecurringTransaction>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringTransaction>().HasIndex(x => new { x.UserId, x.IsActive, x.NextRunDate });
        b.Entity<SavingsGoal>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SavingsGoal>().ToTable(t => t.HasCheckConstraint("CK_SavingsGoal_Target", "TargetCents > 0"));
        b.Entity<FinancialHealthTree>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FinancialHealthTree>().HasIndex(x => new { x.UserId, x.Month }).IsUnique();
        b.Entity<UserXpProfile>().HasOne(x => x.User).WithOne().HasForeignKey<UserXpProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserXpEvent>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserXpEvent>().HasIndex(x => new { x.UserId, x.EventKey }).IsUnique();
        b.Entity<UserSetting>().HasOne(x => x.User).WithOne().HasForeignKey<UserSetting>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Transaction>().ToTable(t => t.HasCheckConstraint("CK_Transaction_Amount", "AmountCents > 0"));
        b.Entity<Budget>().ToTable(t => t.HasCheckConstraint("CK_Budget_Limit", "LimitCents > 0"));
        b.Entity<RecurringTransaction>().ToTable(t => t.HasCheckConstraint("CK_Recurring_Amount", "AmountCents > 0"));

        b.Entity<GroupMember>().HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
        b.Entity<GroupMember>().HasOne(x => x.Group).WithMany(x => x.Members).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GroupMember>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<SharedExpense>().HasOne(x => x.Group).WithMany(x => x.SharedExpenses).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SharedExpense>().HasOne(x => x.PaidByUser).WithMany().HasForeignKey(x => x.PaidByUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<SharedExpenseParticipant>().HasOne(x => x.SharedExpense).WithMany(x => x.Participants).HasForeignKey(x => x.SharedExpenseId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SharedExpenseParticipant>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<SettleUpLog>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SettleUpLog>().HasOne(x => x.PerformedByUser).WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<GroupChatMessage>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GroupChatMessage>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<GroupAbuseReport>().HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GroupAbuseReport>().HasOne(x => x.ReportedByUser).WithMany().HasForeignKey(x => x.ReportedByUserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<CampusChallenge>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CampusChallenge>().HasIndex(x => new { x.UserId, x.Status });
        b.Entity<CampusChallengeDay>().HasOne(x => x.Challenge).WithMany(x => x.Days).HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CampusChallengeDay>().HasIndex(x => new { x.ChallengeId, x.DayNumber }).IsUnique();
        b.Entity<FutureYouUsageEvent>().HasIndex(x => new { x.EventType, x.Timestamp });
        b.Entity<SundayLetter>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SundayLetter>().HasIndex(x => new { x.UserId, x.WeekStart }).IsUnique();
    }

    public override int SaveChanges()
    {
        SyncLegacySharedExpenseColumns();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SyncLegacySharedExpenseColumns();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SyncLegacySharedExpenseColumns();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SyncLegacySharedExpenseColumns()
    {
        foreach (var entry in ChangeTracker.Entries<SharedExpense>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var e = entry.Entity;
            if (string.IsNullOrWhiteSpace(e.PayerId)) e.PayerId = e.PaidByUserId;
            if (string.IsNullOrWhiteSpace(e.PaidByUserId)) e.PaidByUserId = e.PayerId;
            if (string.IsNullOrWhiteSpace(e.Title)) e.Title = e.Description;
            if (string.IsNullOrWhiteSpace(e.Description)) e.Description = e.Title;
            if (string.IsNullOrWhiteSpace(e.Tag)) e.Tag = string.IsNullOrWhiteSpace(e.EventTag) ? "Personal" : e.EventTag;
            if (string.IsNullOrWhiteSpace(e.EventTag)) e.EventTag = e.Tag;
            e.IsHiddenFromPulse = e.HideFromPulse;
            if (e.CreatedAt == default) e.CreatedAt = e.Date == default ? DateTime.UtcNow : e.Date;
            if (e.Date == default) e.Date = e.CreatedAt;
        }

        foreach (var entry in ChangeTracker.Entries<SharedExpenseParticipant>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            var e = entry.Entity;
            if (e.OwedCents == 0) e.OwedCents = e.ShareCents;
            if (e.ShareCents == 0) e.ShareCents = e.OwedCents;
        }
    }
}

