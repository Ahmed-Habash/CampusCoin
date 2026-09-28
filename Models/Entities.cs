using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace CampusCoin.Models;

public enum CategoryType { Expense, Income }
public enum RecurrenceFrequency { Weekly, Monthly }
public class ApplicationUser : IdentityUser
{
    [MaxLength(80)] public string FullName { get; set; } = "Student";
    [MaxLength(40)] public string? AcademicYear { get; set; }
    public long AllowanceCents { get; set; }
    public long SavingsGoalCents { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool CanDisableUsers { get; set; } = true;
    public bool CanResetPasswords { get; set; } = true;
    public bool CanManageCategories { get; set; } = true;
    public bool CanManageAnnouncements { get; set; } = true;
    public bool CanManageTipTemplates { get; set; } = true;
    public bool CanManagePermissions { get; set; } = true;
}
public class Category
{
    public int Id { get; set; }
    [MaxLength(60)] public string Name { get; set; } = "";
    public CategoryType Type { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public bool IsArchived { get; set; }
}
public class Transaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public long AmountCents { get; set; }
    [MaxLength(160)] public string Description { get; set; } = "";
    public DateOnly Date { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<TransactionRevision> Revisions { get; set; } = new List<TransactionRevision>();
}
public class TransactionRevision
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;
    public string SnapshotJson { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
public class RecurringTransaction
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public long AmountCents { get; set; }
    [MaxLength(160)] public string Description { get; set; } = "";
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    public DateOnly StartDate { get; set; }
    public DateOnly NextRunDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class Budget
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public DateOnly Month { get; set; }
    public long LimitCents { get; set; }
}
public class Insight
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public DateOnly Month { get; set; }
    public string Summary { get; set; } = "";
    public bool IsPinned { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
public class SavingsGoal
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(80)] public string Name { get; set; } = "";
    public long TargetCents { get; set; }
    public long SavedCents { get; set; }
    public DateOnly? Deadline { get; set; }
    public bool IsComplete { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class UserSetting
{
    [Key] public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(20)] public string AiProvider { get; set; } = "Auto";
    [MaxLength(8)] public string CurrencyCode { get; set; } = "Auto";
    /// <summary>Admin-only: Fixed = always use own display currency; PerUser = label with each user's chosen currency.</summary>
    [MaxLength(12)] public string AdminCurrencyMode { get; set; } = "Fixed";
    public string? EncryptedApiKey { get; set; }
    /// <summary>Show short “Message from Future You” nudges on expenses / Future You.</summary>
    public bool FutureYouMessagesEnabled { get; set; } = true;
    /// <summary>When true, hide Parallel Lives stories for this user.</summary>
    public bool ParallelLivesHidden { get; set; } = false;
    /// <summary>Show the Overview XP progress card and top-bar level badge.</summary>
    public bool XpProgressEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class Announcement
{
    public int Id { get; set; }
    [MaxLength(120)] public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>When set, announcement auto-ends and moves to past history.</summary>
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class TipTemplate
{
    public int Id { get; set; }
    [MaxLength(120)] public string Title { get; set; } = "";
    public string Tip { get; set; } = "";
    [MaxLength(60)] public string Category { get; set; } = "General";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TreeHealthState
{
    Wilted = 0,
    Stressed = 1,
    Steady = 2,
    Healthy = 3,
    Flowering = 4
}

/// <summary>Monthly financial-health / Money Garden snapshot for one student.</summary>
public class FinancialHealthTree
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public DateOnly Month { get; set; }
    public int HealthScore { get; set; }
    public TreeHealthState State { get; set; } = TreeHealthState.Steady;
    public TreeHealthState PreviousState { get; set; } = TreeHealthState.Steady;
    public int GrowthPercent { get; set; }
    public bool IsMonthFinalized { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int ConsistencyDays { get; set; }
    public int BloomCount { get; set; }
    [MaxLength(200)] public string UnlockedElements { get; set; } = "";
    [MaxLength(220)] public string SoftMessage { get; set; } = "";
}
public class AdminActivityLog
{
    public int Id { get; set; }
    [MaxLength(120)] public string AdminEmail { get; set; } = "";
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(160)] public string Target { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
public class RoommateGroup
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
    [MaxLength(10)] public string InviteCode { get; set; } = "";
    public string CreatorId { get; set; } = "";
    public ApplicationUser Creator { get; set; } = null!;
    public int MaxMembers { get; set; } = 6;
    public bool IsActive { get; set; } = true;
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
    public ICollection<SharedExpense> SharedExpenses { get; set; } = new List<SharedExpense>();
}

public class GroupMember
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public RoommateGroup Group { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(30)] public string Role { get; set; } = "Member";
    /// <summary>Joined = active member shown in the roster. Invited = pending until they join via code.</summary>
    [MaxLength(20)] public string Status { get; set; } = "Joined";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

public class SharedExpense
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public RoommateGroup Group { get; set; } = null!;
    public string PaidByUserId { get; set; } = "";
    /// <summary>Legacy SQLite column — keep in sync with PaidByUserId.</summary>
    public string PayerId { get; set; } = "";
    public ApplicationUser PaidByUser { get; set; } = null!;
    public long AmountCents { get; set; }
    [MaxLength(160)] public string Description { get; set; } = "";
    /// <summary>Legacy SQLite column — keep in sync with Description.</summary>
    [MaxLength(160)] public string Title { get; set; } = "";
    [MaxLength(60)] public string Category { get; set; } = "Food";
    [MaxLength(60)] public string? EventTag { get; set; }
    /// <summary>Legacy SQLite column — keep in sync with EventTag.</summary>
    [MaxLength(60)] public string Tag { get; set; } = "Personal";
    public bool IsFlagged { get; set; }
    public bool HideFromPulse { get; set; }
    /// <summary>Legacy SQLite column — keep in sync with HideFromPulse.</summary>
    public bool IsHiddenFromPulse { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    /// <summary>Legacy SQLite column — keep in sync with Date.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<SharedExpenseParticipant> Participants { get; set; } = new List<SharedExpenseParticipant>();
}

public class SharedExpenseParticipant
{
    public int Id { get; set; }
    public int SharedExpenseId { get; set; }
    public SharedExpense SharedExpense { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public long ShareCents { get; set; }
    /// <summary>Legacy SQLite column — keep in sync with ShareCents.</summary>
    public long OwedCents { get; set; }
}

public class SettleUpLog
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public RoommateGroup Group { get; set; } = null!;
    public string PerformedByUserId { get; set; } = "";
    /// <summary>Legacy SQLite column — keep in sync with PerformedByUserId.</summary>
    public string GeneratedById { get; set; } = "";
    public ApplicationUser PerformedByUser { get; set; } = null!;
    public string NoteSummary { get; set; } = "";
    /// <summary>Legacy SQLite column — keep in sync with NoteSummary.</summary>
    public string SummaryNote { get; set; } = "";
    public DateTime SettledAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GlobalSystemSetting
{
    public int Id { get; set; }
    public bool GroupsFeatureEnabled { get; set; } = true;
    public int MaxGroupSize { get; set; } = 6;
    public bool AllowEmailInvites { get; set; } = true;
    public bool AllowCodeInvites { get; set; } = true;
    public bool EnableAiPulseOneLiners { get; set; } = true;
    public string GroupAnnouncementTemplate { get; set; } = "";
}

public class GroupChatMessage
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public RoommateGroup Group { get; set; } = null!;
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(1000)] public string Message { get; set; } = "";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }

    [NotMapped]
    public bool CanEdit => !IsDeleted && DateTime.UtcNow - SentAt <= TimeSpan.FromMinutes(15);
}

public class GroupAbuseReport
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public RoommateGroup Group { get; set; } = null!;
    public string ReportedByUserId { get; set; } = "";
    public ApplicationUser ReportedByUser { get; set; } = null!;
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "Pending";
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;
}

public class EventInviteCode
{
    public int Id { get; set; }
    [MaxLength(10)] public string Code { get; set; } = "";
    [MaxLength(100)] public string EventName { get; set; } = "";
    [MaxLength(120)] public string CreatedByAdminEmail { get; set; } = "";
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AdminApplication
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(500)] public string Reason { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "Pending";
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(120)] public string? ReviewedByAdminEmail { get; set; }
}

/// <summary>Per-user XP / level progress for financial habits.</summary>
public class UserXpProfile
{
    [Key] public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int TotalXp { get; set; }
    public int Level { get; set; } = 1;
    [MaxLength(60)] public string Title { get; set; } = "Budget Rookie";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Idempotent XP award log (one event key per user).</summary>
public class UserXpEvent
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(120)] public string EventKey { get; set; } = "";
    public int XpAwarded { get; set; }
    [MaxLength(160)] public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Optional 30-day campus challenge owned by one student.</summary>
public class CampusChallenge
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(120)] public string Title { get; set; } = "30-Day Campus Challenge";
    /// <summary>Active | Paused | Completed</summary>
    [MaxLength(20)] public string Status { get; set; } = "Active";
    public DateOnly StartDate { get; set; }
    public int StreakDays { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<CampusChallengeDay> Days { get; set; } = [];
}

public class CampusChallengeDay
{
    public int Id { get; set; }
    public int ChallengeId { get; set; }
    public CampusChallenge Challenge { get; set; } = null!;
    public int DayNumber { get; set; }
    public DateOnly Date { get; set; }
    [MaxLength(220)] public string MicroGoal { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Admin-managed micro-goal templates for challenges.</summary>
public class ChallengeTemplate
{
    public int Id { get; set; }
    [MaxLength(80)] public string Title { get; set; } = "";
    [MaxLength(40)] public string PatternKey { get; set; } = "General";
    [MaxLength(220)] public string MicroGoalTemplate { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Anonymous-friendly usage ping for Future You features (admin sees aggregates only).</summary>
public class FutureYouUsageEvent
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    [MaxLength(40)] public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Weekly sealed Sunday letter — advisory narrative only.</summary>
public class SundayLetter
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    /// <summary>Monday of the covered week.</summary>
    public DateOnly WeekStart { get; set; }
    /// <summary>Sunday unlock day.</summary>
    public DateOnly UnlockDate { get; set; }
    [MaxLength(120)] public string Title { get; set; } = "Your Sunday letter";
    public string Body { get; set; } = "";
    [MaxLength(40)] public string Tone { get; set; } = "steady";
    public int GoodDays { get; set; }
    public bool IsOpened { get; set; }
    public bool IsSaved { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


