using System.ComponentModel.DataAnnotations;
using CampusCoin.Models;

namespace CampusCoin.ViewModels;

public class GroupListViewModel
{
    public List<RoommateGroupItem> Groups { get; set; } = [];
    public GlobalSystemSetting SystemSettings { get; set; } = new();
}

public class RoommateGroupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string InviteCode { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public string CreatorEmail { get; set; } = "";
    public int MemberCount { get; set; }
    public int MaxMembers { get; set; }
    public bool IsCreator { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public long TotalSharedExpensesCents { get; set; }
    public long UserNetBalanceCents { get; set; }
}

public class GroupDetailsViewModel
{
    public RoommateGroup Group { get; set; } = null!;
    public bool IsCreator { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsAdministratorViewer { get; set; }
    public HashSet<string> AdministratorUserIds { get; set; } = new(StringComparer.Ordinal);
    public List<MemberBalanceSummary> Balances { get; set; } = [];
    public List<MemberBalanceSummary> PendingInvites { get; set; } = [];
    public int JoinedMemberCount { get; set; }
    public List<SharedExpenseItemViewModel> Expenses { get; set; } = [];
    public List<GroupChatMessage> ChatMessages { get; set; } = [];
    public CampusPulseInsight Pulse { get; set; } = new();
    public GlobalSystemSetting SystemSettings { get; set; } = new();
}

public class MemberBalanceSummary
{
    public string UserId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public long TotalPaidCents { get; set; }
    public long TotalShareCents { get; set; }
    public long NetBalanceCents => TotalPaidCents - TotalShareCents; // Positive = Owed money, Negative = Owes money
}

public class SharedExpenseItemViewModel
{
    public int Id { get; set; }
    public string PayerUserId { get; set; } = "";
    public string PayerName { get; set; } = "";
    public string PayerEmail { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Tag { get; set; } = "";
    public long AmountCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsFlagged { get; set; }
    public bool IsHiddenFromPulse { get; set; }
    public List<string> ParticipantNames { get; set; } = [];
    public List<string> ParticipantUserIds { get; set; } = [];
}

public class CampusPulseInsight
{
    public string SpendingTrendText { get; set; } = "";
    public string TopCategoryText { get; set; } = "";
    public string AiOneLiner { get; set; } = "";
}

public class CreateGroupForm
{
    [Required, StringLength(80)]
    public string Name { get; set; } = "";
}

public class JoinGroupForm
{
    [Required, StringLength(20)]
    public string InviteCode { get; set; } = "";
}

public class InviteMemberForm
{
    public int GroupId { get; set; }
    [Required, EmailAddress]
    public string Email { get; set; } = "";
}

public class AddSharedExpenseForm
{
    public int GroupId { get; set; }

    [Required, StringLength(160)]
    public string Title { get; set; } = "";

    [Range(typeof(decimal), "0.01", "1000000")]
    public decimal Amount { get; set; }

    [Required, StringLength(60)]
    public string Category { get; set; } = "Food";

    [Required, StringLength(60)]
    public string Tag { get; set; } = "Personal"; // Personal, Group Project, Campus Event, Hostel

    public List<string> ParticipantUserIds { get; set; } = [];
}

public class EditSharedExpenseForm : AddSharedExpenseForm
{
    public int ExpenseId { get; set; }
}

public class AdminGroupFilterViewModel
{
    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; } // all, active, disabled
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public List<AdminGroupListItem> Groups { get; set; } = [];
}

public class AdminGroupListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string CreatorEmail { get; set; } = "";
    public string CreatorName { get; set; } = "";
    public int MemberCount { get; set; }
    public int MaxMembers { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public long TotalExpensesCents { get; set; }
}

public class AdminGroupDetailsViewModel
{
    public RoommateGroup Group { get; set; } = null!;
    public List<MemberBalanceSummary> MemberBalances { get; set; } = [];
    public List<SharedExpenseItemViewModel> Expenses { get; set; } = [];
    public List<SettleUpLog> SettleUpHistory { get; set; } = [];
    public List<GroupChatMessage> ChatMessages { get; set; } = [];
    public HashSet<string> AdministratorUserIds { get; set; } = new(StringComparer.Ordinal);
}

public class AdminSharedExpenseFilterViewModel
{
    public string? SearchQuery { get; set; }
    public bool? FlaggedOnly { get; set; }
    public List<AdminSharedExpenseListItem> Expenses { get; set; } = [];
}

public class AdminSharedExpenseListItem
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public string PayerUserId { get; set; } = "";
    public string PayerEmail { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Tag { get; set; } = "";
    public long AmountCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsFlagged { get; set; }
    public bool IsHiddenFromPulse { get; set; }
}

public class GroupSystemSettingsForm
{
    public bool GroupsFeatureEnabled { get; set; } = true;
    [Range(2, 20, ErrorMessage = "Maximum group size must be between 2 and 20.")]
    public int MaxGroupSize { get; set; } = 6;
    public bool AllowEmailInvites { get; set; } = true;
    public bool AllowCodeInvites { get; set; } = true;
    public bool EnableAiPulseOneLiners { get; set; } = true;
    [MaxLength(2000)]
    public string GroupAnnouncementTemplate { get; set; } = "";
}
