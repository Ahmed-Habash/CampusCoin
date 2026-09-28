using System.Security.Claims;
using CampusCoin.Data;
using CampusCoin.Models;
using CampusCoin.Services;
using CampusCoin.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CampusCoin.Controllers;

[Authorize]
[Route("groups")]
public class GroupsController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    CurrencyService currency) : Controller
{
    private static bool IsJoinedMember(GroupMember m) =>
        string.IsNullOrWhiteSpace(m.Status)
        || m.Status.Equals("Joined", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<GroupMember> JoinedMembers(IEnumerable<GroupMember> members) =>
        members.Where(IsJoinedMember);

    private async Task<GlobalSystemSetting> GetSettingsAsync()
    {
        var setting = await db.GlobalSystemSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (setting != null) return setting;

        setting = new GlobalSystemSetting();
        db.GlobalSystemSettings.Add(setting);
        await db.SaveChangesAsync();
        return setting;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "Roommate / Friend Groups are currently disabled by the system administrator.";
            return RedirectToAction("Index", "Dashboard");
        }

        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var userGroupIds = await db.GroupMembers
            .Where(m => m.UserId == userId && (m.Status == null || m.Status == "" || m.Status == "Joined"))
            .Select(m => m.GroupId)
            .ToListAsync();

        var groups = await db.RoommateGroups
            .Where(g => userGroupIds.Contains(g.Id))
            .Include(g => g.Creator)
            .Include(g => g.Members)
            .Include(g => g.SharedExpenses)
                .ThenInclude(e => e.Participants)
            .AsSplitQuery()
            .OrderByDescending(g => g.LastActivityAt)
            .ToListAsync();

        var list = new List<RoommateGroupItem>();
        foreach (var g in groups)
        {
            long totalExpenses = g.SharedExpenses.Sum(e => e.AmountCents);
            long userPaid = g.SharedExpenses.Where(e => e.PaidByUserId == userId).Sum(e => e.AmountCents);
            long userShare = g.SharedExpenses.SelectMany(e => e.Participants).Where(p => p.UserId == userId).Sum(p => p.ShareCents);

            list.Add(new RoommateGroupItem
            {
                Id = g.Id,
                Name = g.Name,
                InviteCode = g.InviteCode,
                CreatorName = g.Creator.FullName,
                CreatorEmail = g.Creator.Email ?? "",
                MemberCount = JoinedMembers(g.Members).Count(),
                MaxMembers = settings.MaxGroupSize,
                IsCreator = g.CreatorId == userId,
                IsDisabled = g.IsDisabled,
                CreatedAt = g.CreatedAt,
                LastActivityAt = g.LastActivityAt,
                TotalSharedExpensesCents = totalExpenses,
                UserNetBalanceCents = userPaid - userShare
            });
        }

        var model = new GroupListViewModel
        {
            Groups = list,
            SystemSettings = settings
        };

        return View("~/Views/Groups/Index.cshtml", model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateGroupForm form)
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled)
        {
            TempData["Error"] = "Groups feature is disabled.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please provide a valid group name.";
            return RedirectToAction(nameof(Index));
        }

        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var inviteCode = Guid.NewGuid().ToString("N")[..8].ToUpper();

        var group = new RoommateGroup
        {
            Name = form.Name.Trim(),
            InviteCode = inviteCode,
            CreatorId = userId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        db.RoommateGroups.Add(group);
        await db.SaveChangesAsync();

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Role = "Creator",
            Status = "Joined"
        });
        await db.SaveChangesAsync();

        TempData["Success"] = $"Group '{group.Name}' created successfully! Invite Code: {group.InviteCode}";
        return RedirectToAction(nameof(Details), new { id = group.Id });
    }

    [HttpPost("join")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Join(JoinGroupForm form)
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled || !settings.AllowCodeInvites)
        {
            TempData["Error"] = "Joining via invite code is currently disabled.";
            return RedirectToAction(nameof(Index));
        }

        var code = form.InviteCode?.Trim().ToUpper();
        if (string.IsNullOrEmpty(code))
        {
            TempData["Error"] = "Please enter a valid invite code.";
            return RedirectToAction(nameof(Index));
        }

        var group = await db.RoommateGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.InviteCode == code);

        if (group == null)
        {
            TempData["Error"] = "Invalid invite code. Group not found.";
            return RedirectToAction(nameof(Index));
        }

        if (group.IsDisabled)
        {
            TempData["Error"] = "This group has been disabled by administrators.";
            return RedirectToAction(nameof(Index));
        }

        var joinedCount = JoinedMembers(group.Members).Count();
        if (joinedCount >= settings.MaxGroupSize)
        {
            TempData["Error"] = $"This group has reached its maximum limit of {settings.MaxGroupSize} members.";
            return RedirectToAction(nameof(Index));
        }

        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var existing = group.Members.FirstOrDefault(m => m.UserId == userId);
        if (existing != null)
        {
            if (IsJoinedMember(existing))
            {
                TempData["Success"] = "You are already a member of this group.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            existing.Status = "Joined";
            existing.JoinedAt = DateTime.UtcNow;
            group.LastActivityAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Success"] = $"Joined '{group.Name}' successfully!";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = userId,
            JoinedAt = DateTime.UtcNow,
            Role = "Member",
            Status = "Joined"
        });
        group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Joined '{group.Name}' successfully!";
        return RedirectToAction(nameof(Details), new { id = group.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var settings = await GetSettingsAsync();
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var group = await db.RoommateGroups
            .Include(g => g.Creator)
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .Include(g => g.SharedExpenses)
                .ThenInclude(e => e.PaidByUser)
            .Include(g => g.SharedExpenses)
                .ThenInclude(e => e.Participants)
                    .ThenInclude(p => p.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return NotFound();

        bool isMember = group.Members.Any(m => m.UserId == userId && IsJoinedMember(m));
        bool isAdmin = User.IsInRole("Administrator");
        if (!isMember && !isAdmin)
        {
            TempData["Error"] = "Access denied to this group.";
            return RedirectToAction(nameof(Index));
        }

        // Calculate member balances — only people who have joined.
        var balances = new List<MemberBalanceSummary>();
        foreach (var m in JoinedMembers(group.Members))
        {
            long paid = group.SharedExpenses.Where(e => e.PaidByUserId == m.UserId).Sum(e => e.AmountCents);
            long share = group.SharedExpenses.SelectMany(e => e.Participants).Where(p => p.UserId == m.UserId).Sum(p => p.ShareCents);

            balances.Add(new MemberBalanceSummary
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                Email = m.User.Email ?? "",
                TotalPaidCents = paid,
                TotalShareCents = share
            });
        }

        var pendingInvites = group.Members
            .Where(m => !IsJoinedMember(m))
            .Select(m => new MemberBalanceSummary
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                Email = m.User.Email ?? ""
            }).ToList();

        // Build recent expenses list
        var expensesList = group.SharedExpenses
            .OrderByDescending(e => e.Date)
            .Select(e => new SharedExpenseItemViewModel
            {
                Id = e.Id,
                PayerUserId = e.PaidByUserId,
                PayerName = e.PaidByUser?.FullName ?? "Unknown",
                PayerEmail = e.PaidByUser?.Email ?? "",
                Title = e.Description,
                Category = e.Category,
                Tag = e.EventTag ?? "Personal",
                AmountCents = e.AmountCents,
                CreatedAt = e.Date,
                IsFlagged = e.IsFlagged,
                IsHiddenFromPulse = e.HideFromPulse,
                ParticipantNames = e.Participants.Select(p => p.User?.FullName ?? "Member").ToList(),
                ParticipantUserIds = e.Participants.Select(p => p.UserId).ToList()
            }).ToList();

        // Calculate Campus Pulse Insights
        var pulse = CalculateCampusPulse(group, settings);

        // Fetch recent group chat messages (hide soft-deleted from members)
        var chatMessages = await db.GroupChatMessages
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.GroupId == id && !m.IsDeleted)
            .OrderByDescending(m => m.SentAt)
            .Take(50)
            .ToListAsync();
        chatMessages.Reverse();

        var adminIds = (await users.GetUsersInRoleAsync("Administrator"))
            .Select(u => u.Id)
            .ToHashSet(StringComparer.Ordinal);

        var model = new GroupDetailsViewModel
        {
            Group = group,
            IsCreator = group.CreatorId == userId,
            IsDisabled = group.IsDisabled,
            IsAdministratorViewer = isAdmin,
            AdministratorUserIds = adminIds,
            Balances = balances,
            PendingInvites = pendingInvites,
            JoinedMemberCount = balances.Count,
            Expenses = expensesList,
            ChatMessages = chatMessages,
            Pulse = pulse,
            SystemSettings = settings
        };

        return View("~/Views/Groups/Details.cshtml", model);
    }

    [HttpPost("{id:int}/chat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostChatMessage(int id, string message)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var isAdmin = User.IsInRole("Administrator");
        var isMember = await db.GroupMembers.AnyAsync(m =>
            m.GroupId == id && m.UserId == userId
            && (m.Status == null || m.Status == "" || m.Status == "Joined"));
        if (!isMember && !isAdmin)
        {
            TempData["Error"] = "Only group members or administrators can post chat messages.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var group = await db.RoommateGroups.FindAsync(id);
        if (group == null) return NotFound();
        if (group.IsDisabled && !isAdmin)
        {
            TempData["Error"] = "This group is disabled. Chat is read-only for members.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            db.GroupChatMessages.Add(new GroupChatMessage
            {
                GroupId = id,
                UserId = userId,
                Message = message.Trim(),
                SentAt = DateTime.UtcNow
            });
            group.LastActivityAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/chat/{messageId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditChatMessage(int id, int messageId, string message)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var chat = await db.GroupChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.GroupId == id);
        if (chat == null) return NotFound();
        if (chat.UserId != userId && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "You can only edit your own messages.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (chat.IsDeleted)
        {
            TempData["Error"] = "Deleted messages cannot be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // Admins can edit anytime; members only within the edit window.
        if (chat.UserId == userId && !chat.CanEdit && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "This message can no longer be edited.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = "Message cannot be empty.";
            return RedirectToAction(nameof(Details), new { id });
        }

        chat.Message = message.Trim();
        chat.EditedAt = DateTime.UtcNow;
        var group = await db.RoommateGroups.FindAsync(id);
        if (group != null) group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/chat/{messageId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteChatMessage(int id, int messageId)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var chat = await db.GroupChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.GroupId == id);
        if (chat == null) return NotFound();
        if (chat.UserId != userId && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "You can only delete your own messages.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (chat.IsDeleted)
            return RedirectToAction(nameof(Details), new { id });

        chat.IsDeleted = true;
        chat.DeletedAt = DateTime.UtcNow;
        chat.DeletedByUserId = userId;
        var group = await db.RoommateGroups.FindAsync(id);
        if (group != null) group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/report")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportGroup(int id, string reason)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "Please provide a reason for reporting this group.";
            return RedirectToAction(nameof(Details), new { id });
        }

        db.GroupAbuseReports.Add(new GroupAbuseReport
        {
            GroupId = id,
            ReportedByUserId = userId,
            Reason = reason.Trim(),
            Status = "Pending",
            ReportedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        TempData["Success"] = "Group report submitted to administrators. Thank you for keeping Campus Coin safe.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private CampusPulseInsight CalculateCampusPulse(RoommateGroup group, GlobalSystemSetting settings)
    {
        var pulse = new CampusPulseInsight();
        var validExpenses = group.SharedExpenses.Where(e => !e.HideFromPulse).ToList();
        if (validExpenses.Count == 0)
        {
            pulse.SpendingTrendText = "No shared expenses logged yet to generate group pulse.";
            pulse.TopCategoryText = "Add shared expenses like canteen, rent, or groceries!";
            pulse.AiOneLiner = "Group pulse active — log your first bill to see smart campus insights!";
            return pulse;
        }

        var now = DateTime.UtcNow;
        var thisMonthStart = new DateTime(now.Year, now.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);

        decimal thisMonthSpent = validExpenses.Where(e => e.Date >= thisMonthStart).Sum(e => e.AmountCents);
        decimal lastMonthSpent = validExpenses.Where(e => e.Date >= lastMonthStart && e.Date < thisMonthStart).Sum(e => e.AmountCents);

        if (lastMonthSpent > 0)
        {
            decimal diff = (thisMonthSpent - lastMonthSpent) / lastMonthSpent * 100;
            if (diff >= 0)
                pulse.SpendingTrendText = $"Your group spent {diff:F0}% more on shared bills than last month.";
            else
                pulse.SpendingTrendText = $"Awesome! Your group spent {Math.Abs(diff):F0}% less than last month.";
        }
        else
        {
            pulse.SpendingTrendText = $"Your group logged {currency.Format(thisMonthSpent / 100m)} in shared expenses this month.";
        }

        var topCategoryGroup = validExpenses
            .GroupBy(e => e.Category)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        if (topCategoryGroup != null)
        {
            pulse.TopCategoryText = $"Most common shared expense category: {topCategoryGroup.Key} ({topCategoryGroup.Count()} logged).";
        }

        if (settings.EnableAiPulseOneLiners)
        {
            var canteenCount = validExpenses.Count(e => e.Category.Equals("Canteen", StringComparison.OrdinalIgnoreCase) || e.Category.Equals("Food", StringComparison.OrdinalIgnoreCase));
            var projectCount = validExpenses.Count(e => e.EventTag != null && e.EventTag.Equals("Group Project", StringComparison.OrdinalIgnoreCase));

            if (projectCount > 0)
                pulse.AiOneLiner = "Looks like group project crunch time! Keep track of shared printouts and snacks.";
            else if (canteenCount > 2)
                pulse.AiOneLiner = "Looks like exam season – your group's late-night food is up. Try the hostel mess deal!";
            else
                pulse.AiOneLiner = "Smart group budgeting in progress. Keep logging and settling up regularly!";
        }

        return pulse;
    }

    [HttpPost("expenses/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddExpense(AddSharedExpenseForm form)
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled)
        {
            TempData["Error"] = "Groups feature is disabled.";
            return RedirectToAction(nameof(Index));
        }

        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var group = await db.RoommateGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == form.GroupId);

        if (group == null || group.IsDisabled)
        {
            TempData["Error"] = "Group is invalid or disabled.";
            return RedirectToAction(nameof(Index));
        }

        if (!group.Members.Any(m => m.UserId == userId && IsJoinedMember(m)))
        {
            TempData["Error"] = "You must be a group member to log expenses.";
            return RedirectToAction(nameof(Index));
        }

        if (form.Amount <= 0)
        {
            TempData["Error"] = "Please enter a valid expense amount.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        // Selected participants or all joined members by default
        var joinedIds = JoinedMembers(group.Members).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
        var participantIds = form.ParticipantUserIds != null && form.ParticipantUserIds.Count > 0
            ? form.ParticipantUserIds.Distinct().Where(joinedIds.Contains).ToList()
            : joinedIds.ToList();

        if (participantIds.Count == 0)
        {
            TempData["Error"] = "At least one participant must be selected.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        long totalCents = (long)Math.Round(form.Amount * 100);
        long sharePerPerson = totalCents / participantIds.Count;
        long remainder = totalCents % participantIds.Count;

        var now = DateTime.UtcNow;
        var title = form.Title.Trim();
        var tag = string.IsNullOrWhiteSpace(form.Tag) ? "Personal" : form.Tag;
        var expense = new SharedExpense
        {
            GroupId = form.GroupId,
            PaidByUserId = userId,
            PayerId = userId,
            Description = title,
            Title = title,
            Category = form.Category,
            EventTag = tag,
            Tag = tag,
            AmountCents = totalCents,
            Date = now,
            CreatedAt = now
        };

        db.SharedExpenses.Add(expense);
        await db.SaveChangesAsync();

        for (int i = 0; i < participantIds.Count; i++)
        {
            long personShare = sharePerPerson + (i == 0 ? remainder : 0);
            db.SharedExpenseParticipants.Add(new SharedExpenseParticipant
            {
                SharedExpenseId = expense.Id,
                UserId = participantIds[i],
                ShareCents = personShare,
                OwedCents = personShare
            });
        }

        group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Shared expense '{expense.Description}' logged successfully!";
        return RedirectToAction(nameof(Details), new { id = form.GroupId });
    }

    [HttpPost("expenses/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditExpense(EditSharedExpenseForm form)
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled)
        {
            TempData["Error"] = "Groups feature is disabled.";
            return RedirectToAction(nameof(Index));
        }

        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var expense = await db.SharedExpenses
            .Include(e => e.Participants)
            .Include(e => e.Group)
                .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(e => e.Id == form.ExpenseId && e.GroupId == form.GroupId);

        if (expense?.Group == null || expense.Group.IsDisabled)
        {
            TempData["Error"] = "Expense not found or this group is disabled.";
            return RedirectToAction(nameof(Index));
        }

        if (!expense.Group.Members.Any(m => m.UserId == userId && IsJoinedMember(m)) && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "You must be a group member to edit expenses.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        if (form.Amount <= 0 || string.IsNullOrWhiteSpace(form.Title))
        {
            TempData["Error"] = "Please enter a valid title and amount.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        var joinedIds = JoinedMembers(expense.Group.Members).Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);
        var participantIds = form.ParticipantUserIds != null && form.ParticipantUserIds.Count > 0
            ? form.ParticipantUserIds.Distinct().Where(joinedIds.Contains).ToList()
            : joinedIds.ToList();

        if (participantIds.Count == 0)
        {
            TempData["Error"] = "At least one participant must be selected.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        long totalCents = (long)Math.Round(form.Amount * 100);
        var title = form.Title.Trim();
        var tag = string.IsNullOrWhiteSpace(form.Tag) ? "Personal" : form.Tag;

        expense.Description = title;
        expense.Title = title;
        expense.Category = form.Category;
        expense.EventTag = tag;
        expense.Tag = tag;
        expense.AmountCents = totalCents;

        db.SharedExpenseParticipants.RemoveRange(expense.Participants);

        long sharePerPerson = totalCents / participantIds.Count;
        long remainder = totalCents % participantIds.Count;
        for (int i = 0; i < participantIds.Count; i++)
        {
            long personShare = sharePerPerson + (i == 0 ? remainder : 0);
            db.SharedExpenseParticipants.Add(new SharedExpenseParticipant
            {
                SharedExpenseId = expense.Id,
                UserId = participantIds[i],
                ShareCents = personShare,
                OwedCents = personShare
            });
        }

        expense.Group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Shared expense '{expense.Description}' updated.";
        return RedirectToAction(nameof(Details), new { id = form.GroupId });
    }

    [HttpPost("expenses/{expenseId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExpense(int expenseId, int groupId)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var expense = await db.SharedExpenses
            .Include(e => e.Group)
                .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.GroupId == groupId);

        if (expense?.Group == null)
        {
            TempData["Error"] = "Expense not found.";
            return RedirectToAction(nameof(Index));
        }

        if (expense.Group.IsDisabled && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "This group is disabled.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        if (!expense.Group.Members.Any(m => m.UserId == userId && IsJoinedMember(m)) && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "You must be a group member to remove expenses.";
            return RedirectToAction(nameof(Details), new { id = groupId });
        }

        var title = expense.Description;
        db.SharedExpenses.Remove(expense);
        expense.Group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Shared expense '{title}' removed.";
        return RedirectToAction(nameof(Details), new { id = groupId });
    }

    [HttpPost("{id:int}/leave")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Leave(int id)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var group = await db.RoommateGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null)
        {
            TempData["Error"] = "Group not found.";
            return RedirectToAction(nameof(Index));
        }

        var member = group.Members.FirstOrDefault(m => m.UserId == userId && IsJoinedMember(m));
        if (member == null)
        {
            TempData["Error"] = "You are not a member of this group.";
            return RedirectToAction(nameof(Index));
        }

        var groupName = group.Name;
        db.GroupMembers.Remove(member);

        var remaining = group.Members
            .Where(m => m.Id != member.Id && IsJoinedMember(m))
            .OrderBy(m => m.JoinedAt)
            .ToList();

        if (remaining.Count == 0)
        {
            db.RoommateGroups.Remove(group);
            await db.SaveChangesAsync();
            TempData["Success"] = $"You left '{groupName}'. The group was deleted because no members remained.";
            return RedirectToAction(nameof(Index));
        }

        if (group.CreatorId == userId)
        {
            var nextCreator = remaining[0];
            group.CreatorId = nextCreator.UserId;
            nextCreator.Role = "Creator";
        }

        group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Success"] = $"You left '{groupName}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("invite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InviteMember(InviteMemberForm form)
    {
        var settings = await GetSettingsAsync();
        if (!settings.GroupsFeatureEnabled || !settings.AllowEmailInvites)
        {
            TempData["Error"] = "Email invites are currently disabled by system settings.";
            return RedirectToAction(nameof(Index));
        }

        var group = await db.RoommateGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == form.GroupId);

        if (group == null || group.IsDisabled)
        {
            TempData["Error"] = "Group not found or disabled.";
            return RedirectToAction(nameof(Index));
        }

        if (JoinedMembers(group.Members).Count() >= settings.MaxGroupSize)
        {
            TempData["Error"] = $"Group has reached the maximum size limit of {settings.MaxGroupSize} members.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        var invitedUser = await users.FindByEmailAsync(form.Email.Trim());
        if (invitedUser == null)
        {
            TempData["Error"] = $"User with email '{form.Email}' was not found. Share invite code {group.InviteCode} once they register.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        var existing = group.Members.FirstOrDefault(m => m.UserId == invitedUser.Id);
        if (existing != null && IsJoinedMember(existing))
        {
            TempData["Error"] = "This student is already a member of the group.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        if (existing != null)
        {
            TempData["Success"] = $"{invitedUser.FullName} is already invited. They appear in Members after joining with code {group.InviteCode}.";
            return RedirectToAction(nameof(Details), new { id = form.GroupId });
        }

        // Pending invite only — not a roster member until they join via code.
        db.GroupMembers.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = invitedUser.Id,
            JoinedAt = DateTime.UtcNow,
            Role = "Member",
            Status = "Invited"
        });

        group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = $"Invite sent to {invitedUser.FullName}. They’ll appear under Members after joining with code {group.InviteCode}.";
        return RedirectToAction(nameof(Details), new { id = form.GroupId });
    }

    [HttpPost("{id:int}/settleup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SettleUp(int id)
    {
        var userId = users.GetUserId(User);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var group = await db.RoommateGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .Include(g => g.SharedExpenses)
                .ThenInclude(e => e.Participants)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return NotFound();

        var currentMember = group.Members.FirstOrDefault(m => m.UserId == userId && IsJoinedMember(m));
        if (currentMember == null && !User.IsInRole("Administrator"))
        {
            TempData["Error"] = "Access denied.";
            return RedirectToAction(nameof(Index));
        }

        // Build Settlement Summary Note with safe null checks
        var balances = JoinedMembers(group.Members).Select(m =>
        {
            long paid = group.SharedExpenses.Where(e => e.PaidByUserId == m.UserId).Sum(e => e.AmountCents);
            long share = group.SharedExpenses.SelectMany(e => e.Participants).Where(p => p.UserId == m.UserId).Sum(p => p.ShareCents);
            string fullName = m.User?.FullName ?? "Member";
            return new { FullName = fullName, NetCents = paid - share };
        }).ToList();

        var noteLines = new List<string>
        {
            $"🤝 Campus Coin Settle Up Summary for '{group.Name}'",
            $"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            "----------------------------------------"
        };

        var oweList = balances.Where(b => b.NetCents < 0).OrderBy(b => b.NetCents).ToList();
        var owedList = balances.Where(b => b.NetCents > 0).OrderByDescending(b => b.NetCents).ToList();

        if (oweList.Count == 0 && owedList.Count == 0)
        {
            noteLines.Add($"All members are completely settled up! Balance: {currency.Format(0m)}");
        }
        else
        {
            foreach (var debtor in oweList)
            {
                noteLines.Add($"• {debtor.FullName} owes {currency.Format(Math.Abs(debtor.NetCents))} in total.");
            }
            foreach (var creditor in owedList)
            {
                noteLines.Add($"• {creditor.FullName} is owed {currency.Format(creditor.NetCents)} in total.");
            }
        }

        noteLines.Add("----------------------------------------");
        noteLines.Add("Note: This is a shareable summary note (no real money transacted).");

        string summaryNote = string.Join("\n", noteLines);

        db.SettleUpLogs.Add(new SettleUpLog
        {
            GroupId = id,
            PerformedByUserId = userId,
            GeneratedById = userId,
            NoteSummary = summaryNote,
            SummaryNote = summaryNote,
            SettledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        TempData["Success"] = "Settle Up note generated successfully! You can copy or share it with your group.";
        TempData["SettleUpSummary"] = summaryNote;

        return RedirectToAction(nameof(Details), new { id });
    }

}
