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

[Authorize(Roles = "Administrator")]
[Route("admin")]
public class AdminController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    RoleManager<IdentityRole> roles,
    IFutureYouService futureYou) : Controller
{
    private string CurrentAdminEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "Admin";
    private static bool IsPrimaryAdminEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Trim().Equals("admin@campuscoin.local", StringComparison.OrdinalIgnoreCase);

    /// <summary>Standard tools for promoted admins. Never includes make-admin / manage-permissions.</summary>
    private static void ApplyStandardAdminTools(ApplicationUser user)
    {
        user.CanDisableUsers = true;
        user.CanManageCategories = true;
        user.CanManageAnnouncements = true;
        user.CanManageTipTemplates = true;
        user.CanManagePermissions = false;
        user.CanResetPasswords = false;
    }

    private static void ApplyGrantedAdminTools(ApplicationUser user, IFormCollection form)
    {
        static bool Flag(IFormCollection f, string name) =>
            f[name].Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        user.CanDisableUsers = Flag(form, "canDisableUsers");
        user.CanManageCategories = Flag(form, "canManageCategories");
        user.CanManageAnnouncements = Flag(form, "canManageAnnouncements");
        user.CanManageTipTemplates = Flag(form, "canManageTipTemplates");
        // Never grantable at promote-time — only primary/permission managers can assign later.
        user.CanManagePermissions = false;
        user.CanResetPasswords = false;
    }

    private static DateTime ComputeAnnouncementExpiry(int amount, string unit)
    {
        amount = Math.Clamp(amount, 1, 3650);
        return unit switch
        {
            "Minutes" => DateTime.UtcNow.AddMinutes(amount),
            "Hours" => DateTime.UtcNow.AddHours(amount),
            "Months" => DateTime.UtcNow.AddMonths(Math.Min(amount, 120)),
            _ => DateTime.UtcNow.AddDays(amount)
        };
    }

    private async Task ExpireAnnouncementsAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await db.Announcements
            .Where(a => a.IsActive && a.ExpiresAt != null && a.ExpiresAt <= now)
            .ToListAsync();
        if (expired.Count == 0) return;
        foreach (var a in expired) a.IsActive = false;
        await db.SaveChangesAsync();
    }

    private static void ApplyPrimaryAdminTools(ApplicationUser user)
    {
        user.CanDisableUsers = true;
        user.CanManageCategories = true;
        user.CanManageAnnouncements = true;
        user.CanManageTipTemplates = true;
        user.CanManagePermissions = true;
        user.CanResetPasswords = false;
    }

    private async Task<IActionResult?> DenyUnlessAsync(Func<ApplicationUser, bool> allowed, string message)
    {
        var admin = await users.GetUserAsync(User);
        if (admin == null || !allowed(admin))
            return UsersFail(message, 403);
        return null;
    }

    private bool WantsJson() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        || (Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));

    private IActionResult AdminOk(string message, string redirectAction, object? data = null)
    {
        if (WantsJson()) return Json(new { ok = true, message, data });
        if (!string.IsNullOrWhiteSpace(message))
            TempData["Success"] = message;
        return RedirectToAction(redirectAction);
    }

    private IActionResult AdminFail(string message, string redirectAction, int statusCode = 400)
    {
        if (WantsJson()) return StatusCode(statusCode, new { ok = false, message });
        TempData["Error"] = message;
        return RedirectToAction(redirectAction);
    }

    private IActionResult UsersOk(string message, object? data = null) =>
        AdminOk(message, nameof(Users), data);

    private IActionResult UsersFail(string message, int statusCode = 400) =>
        AdminFail(message, nameof(Users), statusCode);

    private async Task LogAsync(string action, string target)
    {
        db.AdminActivityLogs.Add(new AdminActivityLog
        {
            AdminEmail = CurrentAdminEmail,
            Action = action,
            Target = target,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [HttpGet("")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Index()
    {
        var allUsers = await users.Users.AsNoTracking().ToListAsync();
        int activeUsers = 0, disabledUsers = 0;
        foreach (var u in allUsers)
        {
            if (await users.IsLockedOutAsync(u)) disabledUsers++;
            else activeUsers++;
        }

        var transactions = await db.Transactions.AsNoTracking().Include(x => x.Category).Where(x => !x.IsDeleted).ToListAsync();
        int incomeCount = transactions.Count(x => x.Category.Type == CategoryType.Income);
        int expenseCount = transactions.Count(x => x.Category.Type == CategoryType.Expense);

        // Platform stats: system (default) categories only — never expose personal/custom categories.
        var (catLabels, catCounts, catPcts) = await BuildSystemExpenseCategoryStatsAsync();

        var model = new AdminDashboardViewModel
        {
            TotalUsers = allUsers.Count,
            ActiveUsers = activeUsers,
            DisabledUsers = disabledUsers,
            TotalTransactions = transactions.Count,
            TotalIncomeTransactions = incomeCount,
            TotalExpenseTransactions = expenseCount,
            CategoryLabels = catLabels,
            CategoryCounts = catCounts,
            CategoryPercentages = catPcts,
            RecentLogs = await db.AdminActivityLogs.AsNoTracking().OrderByDescending(x => x.Timestamp).Take(25).ToListAsync()
        };

        return View("~/Views/Admin/Dashboard.cshtml", model);
    }

    private async Task<(string[] Labels, int[] Counts, decimal[] Percentages)> BuildSystemExpenseCategoryStatsAsync()
    {
        var systemCats = await db.Categories.AsNoTracking()
            .Where(c => c.UserId == null && c.Type == CategoryType.Expense)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync();

        var countsByCat = await db.Transactions.AsNoTracking()
            .Where(t => !t.IsDeleted && t.Category.UserId == null && t.Category.Type == CategoryType.Expense)
            .GroupBy(t => t.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        var rows = systemCats
            .Select(c => (c.Name, Count: countsByCat.GetValueOrDefault(c.Id)))
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ToList();

        var total = rows.Sum(x => x.Count);
        if (total <= 0)
            return ([], [], []);

        return (
            rows.Select(x => x.Name).ToArray(),
            rows.Select(x => x.Count).ToArray(),
            rows.Select(x => Math.Round((decimal)x.Count / total * 100, 1)).ToArray()
        );
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(string? q)
    {
        if (await DenyUnlessAsync(a => a.CanDisableUsers || a.CanManagePermissions || a.CanResetPasswords,
                "Permission denied: You do not have access to user management.") is { } denied)
            return denied;

        var currentAdmin = await users.GetUserAsync(User);
        var query = users.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(x => (x.FullName != null && x.FullName.ToLower().Contains(term)) || (x.Email != null && x.Email.ToLower().Contains(term)));
        }

        var list = await query.ToListAsync();
        var items = new List<AdminUserItemViewModel>();

        foreach (var u in list)
        {
            var userRoles = await users.GetRolesAsync(u);
            var isLocked = await users.IsLockedOutAsync(u);
            // Prefer Administrator when a user has both roles (legacy dual-role rows).
            var role = userRoles.Contains("Administrator")
                ? "Administrator"
                : userRoles.FirstOrDefault() ?? "Student";
            items.Add(new AdminUserItemViewModel
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email ?? "",
                Role = role,
                IsDisabled = isLocked,
                AcademicYear = u.AcademicYear,
                CreatedAt = u.CreatedAt,
                CanDisableUsers = u.CanDisableUsers,
                CanResetPasswords = u.CanResetPasswords,
                CanManageCategories = u.CanManageCategories,
                CanManageAnnouncements = u.CanManageAnnouncements,
                CanManageTipTemplates = u.CanManageTipTemplates,
                CanManagePermissions = u.CanManagePermissions
            });
        }

        // Sort: Administrators on top (with primary admin admin@campuscoin.local at the very top), then students
        items = items
            .OrderByDescending(x => x.Role == "Administrator")
            .ThenByDescending(x => x.Email.Equals("admin@campuscoin.local", StringComparison.OrdinalIgnoreCase) || (currentAdmin != null && x.Id == currentAdmin.Id))
            .ThenByDescending(x => x.CreatedAt)
            .ToList();

        ViewBag.SearchQuery = q;
        ViewBag.CurrentAdmin = currentAdmin;
        ViewBag.PendingApplications = await db.AdminApplications.Include(a => a.User).Where(a => a.Status == "Pending").OrderByDescending(a => a.AppliedAt).ToListAsync();
        return View("~/Views/Admin/Users.cshtml", items);
    }

    [HttpPost("users/{id}/permissions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePermissions(string id)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: Only admins with permission management can change task permissions.") is { } denied)
            return denied;

        var targetUser = await users.FindByIdAsync(id);
        if (targetUser == null) return WantsJson() ? NotFound(new { ok = false, message = "User not found." }) : NotFound();
        if (!await users.IsInRoleAsync(targetUser, "Administrator"))
            return UsersFail("Task permissions only apply to administrator accounts.");

        static bool Flag(IFormCollection form, string name) =>
            form[name].Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        targetUser.CanDisableUsers = Flag(Request.Form, "canDisableUsers");
        targetUser.CanManageCategories = Flag(Request.Form, "canManageCategories");
        targetUser.CanManageAnnouncements = Flag(Request.Form, "canManageAnnouncements");
        targetUser.CanManageTipTemplates = Flag(Request.Form, "canManageTipTemplates");
        targetUser.CanManagePermissions = Flag(Request.Form, "canManagePermissions");
        targetUser.CanResetPasswords = false;

        if (IsPrimaryAdminEmail(targetUser.Email))
            ApplyPrimaryAdminTools(targetUser);

        var result = await users.UpdateAsync(targetUser);
        if (!result.Succeeded)
            return UsersFail(string.Join("; ", result.Errors.Select(e => e.Description)));

        await LogAsync("Updated Permissions", targetUser.Email ?? targetUser.Id);
        return UsersOk($"Permissions updated for {targetUser.Email}.", new
        {
            userId = targetUser.Id,
            canDisableUsers = targetUser.CanDisableUsers,
            canManageCategories = targetUser.CanManageCategories,
            canManageAnnouncements = targetUser.CanManageAnnouncements,
            canManageTipTemplates = targetUser.CanManageTipTemplates,
            canManagePermissions = targetUser.CanManagePermissions
        });
    }

    [HttpPost("users/{id}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DisableUser(string id)
    {
        if (await DenyUnlessAsync(a => a.CanDisableUsers,
                "Permission denied: You do not have permission to disable users.") is { } denied)
            return denied;

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentUserId)
            return UsersFail("Action denied: You cannot disable your own administrator account.");

        var targetUser = await users.FindByIdAsync(id);
        if (targetUser == null) return WantsJson() ? NotFound(new { ok = false, message = "User not found." }) : NotFound();
        if (IsPrimaryAdminEmail(targetUser.Email))
            return UsersFail("Action denied: The primary system administrator cannot be disabled.");

        await users.SetLockoutEnabledAsync(targetUser, true);
        await users.SetLockoutEndDateAsync(targetUser, DateTimeOffset.UtcNow.AddYears(100));
        await LogAsync("Disabled User", targetUser.Email ?? targetUser.Id);

        return UsersOk("", new
        {
            userId = targetUser.Id,
            disabled = true,
            role = await users.IsInRoleAsync(targetUser, "Administrator") ? "Administrator" : "Student"
        });
    }

    [HttpPost("users/{id}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnableUser(string id)
    {
        if (await DenyUnlessAsync(a => a.CanDisableUsers,
                "Permission denied: You do not have permission to enable/disable users.") is { } denied)
            return denied;

        var targetUser = await users.FindByIdAsync(id);
        if (targetUser == null) return WantsJson() ? NotFound(new { ok = false, message = "User not found." }) : NotFound();

        await users.SetLockoutEndDateAsync(targetUser, null);
        await users.ResetAccessFailedCountAsync(targetUser);
        await LogAsync("Enabled User", targetUser.Email ?? targetUser.Id);

        return UsersOk("", new
        {
            userId = targetUser.Id,
            disabled = false,
            role = await users.IsInRoleAsync(targetUser, "Administrator") ? "Administrator" : "Student"
        });
    }

    [HttpPost("users/{id}/make-admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakeAdmin(string id)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: Only admins who can manage permissions may grant administrator access.") is { } denied)
            return denied;

        var targetUser = await users.FindByIdAsync(id);
        if (targetUser == null) return WantsJson() ? NotFound(new { ok = false, message = "User not found." }) : NotFound();

        if (!await roles.RoleExistsAsync("Administrator"))
            await roles.CreateAsync(new IdentityRole("Administrator"));
        if (!await roles.RoleExistsAsync("Student"))
            await roles.CreateAsync(new IdentityRole("Student"));

        if (!await users.IsInRoleAsync(targetUser, "Administrator"))
        {
            var addAdmin = await users.AddToRoleAsync(targetUser, "Administrator");
            if (!addAdmin.Succeeded)
                return UsersFail(string.Join("; ", addAdmin.Errors.Select(e => e.Description)));
        }

        if (await users.IsInRoleAsync(targetUser, "Student"))
        {
            var removeStudent = await users.RemoveFromRoleAsync(targetUser, "Student");
            if (!removeStudent.Succeeded)
                return UsersFail(string.Join("; ", removeStudent.Errors.Select(e => e.Description)));
        }

        ApplyGrantedAdminTools(targetUser, Request.Form);
        await users.UpdateAsync(targetUser);

        await LogAsync("Granted Admin Role", targetUser.Email ?? targetUser.Id);

        return UsersOk("", new
        {
            userId = targetUser.Id,
            email = targetUser.Email,
            fullName = targetUser.FullName,
            role = "Administrator",
            disabled = targetUser.LockoutEnd != null && targetUser.LockoutEnd > DateTimeOffset.UtcNow,
            canDisableUsers = targetUser.CanDisableUsers,
            canManageCategories = targetUser.CanManageCategories,
            canManageAnnouncements = targetUser.CanManageAnnouncements,
            canManageTipTemplates = targetUser.CanManageTipTemplates,
            canManagePermissions = targetUser.CanManagePermissions
        });
    }

    [HttpPost("users/{id}/remove-admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveAdmin(string id)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: Only admins who can manage permissions may revoke administrator access.") is { } denied)
            return denied;

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentUserId)
            return UsersFail("Action denied: You cannot remove your own administrator status.");

        var targetUser = await users.FindByIdAsync(id);
        if (targetUser == null) return WantsJson() ? NotFound(new { ok = false, message = "User not found." }) : NotFound();
        if (IsPrimaryAdminEmail(targetUser.Email))
            return UsersFail("Action denied: The primary system administrator cannot be revoked.");

        var admins = await users.GetUsersInRoleAsync("Administrator");
        if (admins.Count <= 1 && admins.Any(a => a.Id == id))
            return UsersFail("Action denied: Cannot remove the last Administrator account from the system.");

        var remove = await users.RemoveFromRoleAsync(targetUser, "Administrator");
        if (!remove.Succeeded)
            return UsersFail(string.Join("; ", remove.Errors.Select(e => e.Description)));

        if (!await roles.RoleExistsAsync("Student"))
            await roles.CreateAsync(new IdentityRole("Student"));

        if (!await users.IsInRoleAsync(targetUser, "Student"))
        {
            var addStudent = await users.AddToRoleAsync(targetUser, "Student");
            if (!addStudent.Succeeded)
                return UsersFail(string.Join("; ", addStudent.Errors.Select(e => e.Description)));
        }

        targetUser.CanDisableUsers = false;
        targetUser.CanManageCategories = false;
        targetUser.CanManageAnnouncements = false;
        targetUser.CanManageTipTemplates = false;
        targetUser.CanManagePermissions = false;
        targetUser.CanResetPasswords = false;
        await users.UpdateAsync(targetUser);

        await LogAsync("Revoked Admin Role", targetUser.Email ?? targetUser.Id);

        var effectiveRole = await users.IsInRoleAsync(targetUser, "Administrator") ? "Administrator" : "Student";
        return UsersOk("", new
        {
            userId = targetUser.Id,
            email = targetUser.Email,
            fullName = targetUser.FullName,
            role = effectiveRole,
            disabled = targetUser.LockoutEnd != null && targetUser.LockoutEnd > DateTimeOffset.UtcNow
        });
    }

    [HttpPost("admin-applications/{id}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveAdminApplication(int id)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: Only admins who can manage permissions may approve administrator requests.") is { } denied)
            return denied;

        var currentAdmin = await users.GetUserAsync(User);
        var appItem = await db.AdminApplications.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id);
        if (appItem == null) return WantsJson() ? NotFound(new { ok = false, message = "Application not found." }) : NotFound();

        appItem.Status = "Approved";
        appItem.ReviewedAt = DateTime.UtcNow;
        appItem.ReviewedByAdminEmail = currentAdmin?.Email ?? User.Identity?.Name ?? "Admin";

        if (!await roles.RoleExistsAsync("Administrator"))
            await roles.CreateAsync(new IdentityRole("Administrator"));

        if (!await users.IsInRoleAsync(appItem.User, "Administrator"))
            await users.AddToRoleAsync(appItem.User, "Administrator");

        if (await users.IsInRoleAsync(appItem.User, "Student"))
            await users.RemoveFromRoleAsync(appItem.User, "Student");

        ApplyGrantedAdminTools(appItem.User, Request.Form);
        await users.UpdateAsync(appItem.User);

        await db.SaveChangesAsync();
        await LogAsync("Approved Admin Application", appItem.User.Email ?? appItem.User.Id);

        return UsersOk("", new
        {
            applicationId = id,
            userId = appItem.User.Id,
            email = appItem.User.Email,
            fullName = appItem.User.FullName,
            role = "Administrator",
            disabled = false,
            canDisableUsers = appItem.User.CanDisableUsers,
            canManageCategories = appItem.User.CanManageCategories,
            canManageAnnouncements = appItem.User.CanManageAnnouncements,
            canManageTipTemplates = appItem.User.CanManageTipTemplates,
            canManagePermissions = appItem.User.CanManagePermissions
        });
    }

    [HttpPost("admin-applications/{id}/reject")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectAdminApplication(int id)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: Only admins who can manage permissions may reject administrator requests.") is { } denied)
            return denied;

        var currentAdmin = await users.GetUserAsync(User);
        var appItem = await db.AdminApplications.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id);
        if (appItem == null) return WantsJson() ? NotFound(new { ok = false, message = "Application not found." }) : NotFound();

        appItem.Status = "Rejected";
        appItem.ReviewedAt = DateTime.UtcNow;
        appItem.ReviewedByAdminEmail = currentAdmin?.Email ?? User.Identity?.Name ?? "Admin";

        await db.SaveChangesAsync();
        await LogAsync("Rejected Admin Application", appItem.User.Email ?? appItem.User.Id);

        return UsersOk($"Rejected application for {appItem.User.Email}.", new { applicationId = id });
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageCategories)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage categories.";
            return RedirectToAction(nameof(Index));
        }

        var systemCategories = await db.Categories.AsNoTracking().Where(c => c.UserId == null).OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();
        return View("~/Views/Admin/Categories.cshtml", systemCategories);
    }

    [HttpPost("categories")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(CategoryForm form)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageCategories)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage categories.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Category name is required.";
            return RedirectToAction(nameof(Categories));
        }

        var trimmed = form.Name.Trim();
        if (await db.Categories.AnyAsync(c => c.UserId == null && c.Name.ToLower() == trimmed.ToLower() && c.Type == form.Type))
        {
            TempData["Error"] = $"A system-wide {form.Type} category named '{trimmed}' already exists.";
            return RedirectToAction(nameof(Categories));
        }

        var cat = new Category { Name = trimmed, Type = form.Type, UserId = null };
        db.Categories.Add(cat);
        await db.SaveChangesAsync();
        await LogAsync("Created Default Category", $"{form.Type}: {trimmed}");

        TempData["Success"] = $"System default {form.Type} category '{trimmed}' created.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("categories/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, string name)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageCategories)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage categories.";
            return RedirectToAction(nameof(Index));
        }

        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == null);
        if (cat == null) return NotFound();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Category name cannot be empty.";
            return RedirectToAction(nameof(Categories));
        }

        cat.Name = name.Trim();
        await db.SaveChangesAsync();
        await LogAsync("Updated Default Category", $"Id {id}: {cat.Name}");

        TempData["Success"] = "Category updated successfully.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("categories/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageCategories)
            return AdminFail("Permission denied: You do not have access to manage categories.", nameof(Index), 403);

        var cat = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == null);
        if (cat == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        bool isUsed = await db.Transactions.AnyAsync(t => t.CategoryId == id) || await db.Budgets.AnyAsync(b => b.CategoryId == id);
        if (isUsed)
        {
            cat.IsArchived = true;
            await db.SaveChangesAsync();
            await LogAsync("Archived Default Category", cat.Name);
            return AdminOk($"Category '{cat.Name}' was archived because existing student records reference it.", nameof(Categories), new { id, archived = true });
        }

        db.Categories.Remove(cat);
        await db.SaveChangesAsync();
        await LogAsync("Deleted Default Category", cat.Name);
        return AdminOk($"Category '{cat.Name}' deleted.", nameof(Categories), new { id, deleted = true });
    }

    [HttpGet("announcements")]
    public async Task<IActionResult> Announcements()
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageAnnouncements)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage announcements.";
            return RedirectToAction(nameof(Index));
        }

        await ExpireAnnouncementsAsync();
        var list = await db.Announcements.AsNoTracking().OrderByDescending(a => a.CreatedAt).ToListAsync();
        return View("~/Views/Admin/Announcements.cshtml", list);
    }

    [HttpPost("announcements")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAnnouncement(AnnouncementForm form)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageAnnouncements)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage announcements.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Title, message, and a valid duration are required.";
            return RedirectToAction(nameof(Announcements));
        }

        var expires = ComputeAnnouncementExpiry(form.DurationAmount, form.DurationUnit);
        var ann = new Announcement
        {
            Title = form.Title.Trim(),
            Message = form.Message.Trim(),
            IsActive = form.IsActive,
            ExpiresAt = expires,
            CreatedAt = DateTime.UtcNow
        };
        db.Announcements.Add(ann);
        await db.SaveChangesAsync();
        await LogAsync("Created Announcement", $"{ann.Title} (until {expires:u})");

        TempData["Success"] = $"Announcement published until {expires.ToLocalTime():MMM dd, yyyy HH:mm}.";
        return RedirectToAction(nameof(Announcements));
    }

    [HttpPost("announcements/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAnnouncement(int id)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageAnnouncements)
            return AdminFail("Permission denied: You do not have access to manage announcements.", nameof(Index), 403);

        var ann = await db.Announcements.FindAsync(id);
        if (ann == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        ann.IsActive = !ann.IsActive;
        if (ann.IsActive && ann.ExpiresAt.HasValue && ann.ExpiresAt <= DateTime.UtcNow)
            ann.ExpiresAt = DateTime.UtcNow.AddDays(7);
        await db.SaveChangesAsync();
        await LogAsync(ann.IsActive ? "Activated Announcement" : "Deactivated Announcement", ann.Title);

        return AdminOk($"Announcement status changed to {(ann.IsActive ? "Active" : "Inactive")}.", nameof(Announcements), new { id = ann.Id, active = ann.IsActive });
    }

    [HttpPost("announcements/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAnnouncement(int id)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageAnnouncements)
            return AdminFail("Permission denied: You do not have access to manage announcements.", nameof(Index), 403);

        var ann = await db.Announcements.FindAsync(id);
        if (ann == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        db.Announcements.Remove(ann);
        await db.SaveChangesAsync();
        await LogAsync("Deleted Announcement", ann.Title);

        return AdminOk("Announcement deleted.", nameof(Announcements), new { id });
    }

    [HttpGet("tip-templates")]
    [HttpGet("TipTemplates")]
    public async Task<IActionResult> TipTemplates()
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageTipTemplates)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage tip templates.";
            return RedirectToAction(nameof(Index));
        }

        var list = await db.TipTemplates.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync();
        return View("~/Views/Admin/TipTemplates.cshtml", list);
    }

    [HttpPost("tip-templates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTipTemplate(TipTemplateForm form)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageTipTemplates)
        {
            TempData["Error"] = "Permission denied: You do not have access to manage tip templates.";
            return RedirectToAction(nameof(Index));
        }
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Title and tip text are required.";
            return RedirectToAction(nameof(TipTemplates));
        }

        var tip = new TipTemplate { Title = form.Title.Trim(), Tip = form.Tip.Trim(), Category = form.Category.Trim(), IsActive = form.IsActive };
        db.TipTemplates.Add(tip);
        await db.SaveChangesAsync();
        await LogAsync("Created Tip Template", tip.Title);

        TempData["Success"] = "Tip template created.";
        return RedirectToAction(nameof(TipTemplates));
    }

    [HttpPost("tip-templates/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleTipTemplate(int id)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageTipTemplates)
            return AdminFail("Permission denied: You do not have access to manage tip templates.", nameof(Index), 403);

        var tip = await db.TipTemplates.FindAsync(id);
        if (tip == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        tip.IsActive = !tip.IsActive;
        await db.SaveChangesAsync();
        await LogAsync(tip.IsActive ? "Activated Tip Template" : "Deactivated Tip Template", tip.Title);

        return AdminOk($"Tip template status changed to {(tip.IsActive ? "Active" : "Inactive")}.", nameof(TipTemplates), new { id = tip.Id, active = tip.IsActive });
    }

    [HttpPost("tip-templates/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTipTemplate(int id)
    {
        var currentAdmin = await users.GetUserAsync(User);
        if (currentAdmin == null || !currentAdmin.CanManageTipTemplates)
            return AdminFail("Permission denied: You do not have access to manage tip templates.", nameof(Index), 403);

        var tip = await db.TipTemplates.FindAsync(id);
        if (tip == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        db.TipTemplates.Remove(tip);
        await db.SaveChangesAsync();
        await LogAsync("Deleted Tip Template", tip.Title);

        return AdminOk("Tip template deleted.", nameof(TipTemplates), new { id });
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> Statistics(string? q)
    {
        var allUsers = await users.Users.AsNoTracking().ToListAsync();
        int activeUsers = 0, disabledUsers = 0;
        foreach (var u in allUsers)
        {
            if (await users.IsLockedOutAsync(u)) disabledUsers++;
            else activeUsers++;
        }

        var transactions = await db.Transactions.AsNoTracking().Include(x => x.Category).Where(x => !x.IsDeleted).ToListAsync();
        var incomeCount = transactions.Count(x => x.Category.Type == CategoryType.Income);
        var expenseCount = transactions.Count(x => x.Category.Type == CategoryType.Expense);
        var (catLabels, catCounts, catPcts) = await BuildSystemExpenseCategoryStatsAsync();

        var logsQuery = db.AdminActivityLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            logsQuery = logsQuery.Where(x =>
                x.AdminEmail.ToLower().Contains(term)
                || x.Action.ToLower().Contains(term)
                || x.Target.ToLower().Contains(term));
        }

        var model = new AdminDashboardViewModel
        {
            TotalUsers = allUsers.Count,
            ActiveUsers = activeUsers,
            DisabledUsers = disabledUsers,
            TotalTransactions = transactions.Count,
            TotalIncomeTransactions = incomeCount,
            TotalExpenseTransactions = expenseCount,
            CategoryLabels = catLabels,
            CategoryCounts = catCounts,
            CategoryPercentages = catPcts,
            RecentLogs = await logsQuery.OrderByDescending(x => x.Timestamp).Take(25).ToListAsync()
        };

        ViewBag.ActivitySearch = q;

        // Populate new group statistics safely
        var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var groups = await db.RoommateGroups.AsNoTracking().Include(g => g.Members).Include(g => g.SharedExpenses).AsSplitQuery().ToListAsync();
        ViewBag.TotalActiveGroups = groups.Count(g => !g.IsDisabled);
        ViewBag.TotalSharedExpensesMonth = await db.SharedExpenses.AsNoTracking().CountAsync(e => e.Date >= firstDayOfMonth);
        ViewBag.TotalSharedExpensesAllTime = await db.SharedExpenses.AsNoTracking().CountAsync();
        ViewBag.AvgGroupSize = groups.Count > 0 ? Math.Round(groups.Average(g => g.Members.Count), 1) : 0;
        ViewBag.PctUsersInGroup = allUsers.Count > 0 ? Math.Round((decimal)await db.GroupMembers.AsNoTracking().Select(m => m.UserId).Distinct().CountAsync() / allUsers.Count * 100, 1) : 0;
        ViewBag.SettleUpCount = await db.SettleUpLogs.AsNoTracking().CountAsync();
        ViewBag.FutureYouStats = await futureYou.GetAdminStatsAsync();

        return View("~/Views/Admin/Statistics.cshtml", model);
    }

    [HttpPost("challenge-templates")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveChallengeTemplate(string title, string microGoalTemplate, string patternKey = "General")
    {
        if (await DenyUnlessAsync(a => a.CanManageTipTemplates || a.CanManagePermissions,
                "Permission denied: You do not have access to challenge templates.") is { } denied)
            return denied;

        title = (title ?? "").Trim();
        microGoalTemplate = (microGoalTemplate ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(microGoalTemplate))
        {
            TempData["Error"] = "Add a title and micro-goal text.";
            return RedirectToAction(nameof(Statistics));
        }

        db.ChallengeTemplates.Add(new ChallengeTemplate
        {
            Title = title.Length > 80 ? title[..80] : title,
            PatternKey = string.IsNullOrWhiteSpace(patternKey) ? "General" : patternKey.Trim(),
            MicroGoalTemplate = microGoalTemplate.Length > 220 ? microGoalTemplate[..220] : microGoalTemplate,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await LogAsync("Added Challenge Template", title);
        TempData["Success"] = "Challenge template saved.";
        return RedirectToAction(nameof(Statistics));
    }

    [HttpPost("challenge-templates/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleChallengeTemplate(int id)
    {
        if (await DenyUnlessAsync(a => a.CanManageTipTemplates || a.CanManagePermissions,
                "Permission denied: You do not have access to challenge templates.") is { } denied)
            return denied;

        var row = await db.ChallengeTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (row == null) return NotFound();
        row.IsActive = !row.IsActive;
        await db.SaveChangesAsync();
        await LogAsync(row.IsActive ? "Enabled Challenge Template" : "Disabled Challenge Template", row.Title);
        return RedirectToAction(nameof(Statistics));
    }

    // --- ADMIN GOVERNANCE & GROUP CONTROLS ---

    [HttpPost("groups/{id}/force-settle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceSettleGroup(int id)
    {
        var group = await db.RoommateGroups.Include(g => g.Members).ThenInclude(m => m.User).FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var note = $"FORCE SETTLED BY ADMINISTRATOR ({CurrentAdminEmail}) at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.\nAll current group balances marked as settled.";
        db.SettleUpLogs.Add(new SettleUpLog
        {
            GroupId = id,
            PerformedByUserId = actorId,
            GeneratedById = actorId,
            NoteSummary = note,
            SummaryNote = note,
            SettledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        await LogAsync("Force Settled Group", group.Name);
        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    [HttpPost("groups/bulk-disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDisableGroups(List<int> groupIds)
    {
        if (groupIds == null || groupIds.Count == 0)
        {
            TempData["Error"] = "No groups selected for bulk disabling.";
            return RedirectToAction(nameof(Groups));
        }

        var groups = await db.RoommateGroups.Where(g => groupIds.Contains(g.Id)).ToListAsync();
        foreach (var g in groups)
        {
            g.IsDisabled = true;
        }

        await db.SaveChangesAsync();
        await LogAsync("Bulk Disabled Groups", $"{groups.Count} groups disabled");
        TempData["Success"] = $"Disabled {groups.Count} groups successfully.";
        return RedirectToAction(nameof(Groups));
    }

    [HttpPost("events/generate-code")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateEventInviteCode(string eventName, int validityDays = 7)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            TempData["Error"] = "Please provide a valid event name.";
            return RedirectToAction(nameof(GroupSettings));
        }

        validityDays = Math.Clamp(validityDays, 1, 90);
        var code = "EVT" + Random.Shared.Next(10000, 99999).ToString();
        var invite = new EventInviteCode
        {
            Code = code,
            EventName = eventName.Trim(),
            CreatedByAdminEmail = CurrentAdminEmail,
            ExpiresAt = DateTime.UtcNow.AddDays(validityDays),
            IsActive = true
        };

        db.EventInviteCodes.Add(invite);
        await db.SaveChangesAsync();
        await LogAsync("Generated Event Invite Code", $"{code} for {eventName}");
        return RedirectToAction(nameof(GroupSettings));
    }

    [HttpPost("events/{id:int}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleEventInviteCode(int id)
    {
        var invite = await db.EventInviteCodes.FindAsync(id);
        if (invite == null) return NotFound();

        // Expired codes cannot be re-enabled
        if (invite.ExpiresAt <= DateTime.UtcNow)
        {
            invite.IsActive = false;
            await db.SaveChangesAsync();
            TempData["Error"] = $"Code '{invite.Code}' has expired and cannot be enabled.";
            return RedirectToAction(nameof(GroupSettings));
        }

        invite.IsActive = !invite.IsActive;
        await db.SaveChangesAsync();
        await LogAsync(invite.IsActive ? "Enabled Event Code" : "Disabled Event Code", invite.Code);
        return RedirectToAction(nameof(GroupSettings));
    }

    [HttpPost("events/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEventInviteCode(int id)
    {
        var invite = await db.EventInviteCodes.FindAsync(id);
        if (invite == null) return NotFound();
        var code = invite.Code;
        db.EventInviteCodes.Remove(invite);
        await db.SaveChangesAsync();
        await LogAsync("Deleted Event Code", code);
        return RedirectToAction(nameof(GroupSettings));
    }

    private async Task ExpireEventInviteCodesAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await db.EventInviteCodes.Where(c => c.IsActive && c.ExpiresAt <= now).ToListAsync();
        if (expired.Count == 0) return;
        foreach (var c in expired) c.IsActive = false;
        await db.SaveChangesAsync();
    }

    [HttpGet("groups/reports")]
    public async Task<IActionResult> AbuseReports()
    {
        var reports = await db.GroupAbuseReports
            .Include(r => r.Group)
            .Include(r => r.ReportedByUser)
            .AsNoTracking()
            .OrderByDescending(r => r.ReportedAt)
            .ToListAsync();

        return View("~/Views/Admin/AbuseReports.cshtml", reports);
    }

    [HttpPost("groups/reports/{id}/resolve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveAbuseReport(int id, string actionTaken)
    {
        var report = await db.GroupAbuseReports.Include(r => r.Group).FirstOrDefaultAsync(r => r.Id == id);
        if (report == null) return NotFound();

        report.Status = actionTaken == "disable" ? "Resolved (Group Disabled)" : "Dismissed";
        if (actionTaken == "disable" && report.Group != null)
        {
            report.Group.IsDisabled = true;
        }

        await db.SaveChangesAsync();
        await LogAsync("Resolved Abuse Report", $"Report #{id} on {report.Group?.Name ?? "Group"}");

        TempData["Success"] = $"Abuse report #{id} updated: {report.Status}.";
        return RedirectToAction(nameof(AbuseReports));
    }


    // --- 1. GROUP MANAGEMENT ---

    [HttpGet("groups")]
    public async Task<IActionResult> Groups(string? q, string? status, DateOnly? from, DateOnly? to)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: You do not have access to groups oversight.") is { } denied)
            return denied;

        var query = db.RoommateGroups
            .Include(g => g.Creator)
            .Include(g => g.Members)
                .ThenInclude(m => m.User)
            .Include(g => g.SharedExpenses)
            .AsSplitQuery()
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(term) ||
                                     g.Creator.Email!.ToLower().Contains(term) ||
                                     g.Members.Any(m => m.User.Email!.ToLower().Contains(term)));
        }

        if (status == "active") query = query.Where(g => !g.IsDisabled);
        else if (status == "disabled") query = query.Where(g => g.IsDisabled);

        if (from.HasValue)
        {
            var fromDt = from.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(g => g.CreatedAt >= fromDt);
        }

        if (to.HasValue)
        {
            var toDt = to.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(g => g.CreatedAt <= toDt);
        }

        var list = await query.OrderByDescending(g => g.LastActivityAt).ToListAsync();

        var model = new AdminGroupFilterViewModel
        {
            SearchQuery = q,
            StatusFilter = status,
            FromDate = from,
            ToDate = to,
            Groups = list.Select(g => new AdminGroupListItem
            {
                Id = g.Id,
                Name = g.Name,
                CreatorEmail = g.Creator?.Email ?? "System",
                CreatorName = g.Creator?.FullName ?? "System",
                MemberCount = g.Members.Count,
                MaxMembers = g.MaxMembers,
                IsDisabled = g.IsDisabled,
                CreatedAt = g.CreatedAt,
                LastActivityAt = g.LastActivityAt,
                TotalExpensesCents = g.SharedExpenses.Sum(e => e.AmountCents)
            }).ToList()
        };

        return View("~/Views/Admin/Groups.cshtml", model);
    }

    [HttpGet("groups/{id:int}")]
    public async Task<IActionResult> GroupDetails(int id)
    {
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

        var balances = group.Members.Select(m => new MemberBalanceSummary
        {
            UserId = m.UserId,
            FullName = m.User.FullName,
            Email = m.User.Email ?? "",
            TotalPaidCents = group.SharedExpenses.Where(e => e.PaidByUserId == m.UserId).Sum(e => e.AmountCents),
            TotalShareCents = group.SharedExpenses.SelectMany(e => e.Participants).Where(p => p.UserId == m.UserId).Sum(p => p.ShareCents)
        }).ToList();

        var expenses = group.SharedExpenses.OrderByDescending(e => e.Date).Select(e => new SharedExpenseItemViewModel
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
            ParticipantNames = e.Participants.Select(p => p.User?.FullName ?? "Member").ToList()
        }).ToList();

        var settleHistory = await db.SettleUpLogs
            .Include(s => s.PerformedByUser)
            .Where(s => s.GroupId == id)
            .OrderByDescending(s => s.SettledAt)
            .ToListAsync();

        var chatMessages = await db.GroupChatMessages
            .AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.GroupId == id)
            .OrderByDescending(m => m.SentAt)
            .Take(80)
            .ToListAsync();
        chatMessages.Reverse();

        var adminIds = (await users.GetUsersInRoleAsync("Administrator"))
            .Select(u => u.Id)
            .ToHashSet(StringComparer.Ordinal);

        var model = new AdminGroupDetailsViewModel
        {
            Group = group,
            MemberBalances = balances,
            Expenses = expenses,
            SettleUpHistory = settleHistory,
            ChatMessages = chatMessages,
            AdministratorUserIds = adminIds
        };

        return View("~/Views/Admin/GroupDetails.cshtml", model);
    }

    [HttpPost("groups/{id:int}/chat")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostGroupChat(int id, string message)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Challenge();

        var group = await db.RoommateGroups.FindAsync(id);
        if (group == null) return NotFound();

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
            await LogAsync("Posted Group Chat", $"{group.Name}: {message.Trim()[..Math.Min(60, message.Trim().Length)]}");
        }

        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    [HttpPost("groups/{id:int}/chat/{messageId:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditGroupChatMessage(int id, int messageId, string message)
    {
        var chat = await db.GroupChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.GroupId == id);
        if (chat == null) return NotFound();
        if (chat.IsDeleted)
        {
            TempData["Error"] = "Deleted messages cannot be edited.";
            return RedirectToAction(nameof(GroupDetails), new { id });
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            TempData["Error"] = "Message cannot be empty.";
            return RedirectToAction(nameof(GroupDetails), new { id });
        }

        chat.Message = message.Trim();
        chat.EditedAt = DateTime.UtcNow;
        var group = await db.RoommateGroups.FindAsync(id);
        if (group != null) group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await LogAsync("Edited Group Chat", $"{group?.Name ?? id.ToString()}: msg #{messageId}");
        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    [HttpPost("groups/{id:int}/chat/{messageId:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroupChatMessage(int id, int messageId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var chat = await db.GroupChatMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.GroupId == id);
        if (chat == null) return NotFound();
        if (chat.IsDeleted)
            return RedirectToAction(nameof(GroupDetails), new { id });

        chat.IsDeleted = true;
        chat.DeletedAt = DateTime.UtcNow;
        chat.DeletedByUserId = userId;
        var group = await db.RoommateGroups.FindAsync(id);
        if (group != null) group.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await LogAsync("Deleted Group Chat", $"{group?.Name ?? id.ToString()}: msg #{messageId}");
        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    [HttpPost("groups/{id:int}/toggle-disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleDisableGroup(int id)
    {
        var group = await db.RoommateGroups.FindAsync(id);
        if (group == null) return WantsJson() ? NotFound(new { ok = false, message = "Not found." }) : NotFound();

        group.IsDisabled = !group.IsDisabled;
        await db.SaveChangesAsync();

        await LogAsync(group.IsDisabled ? "Soft Disabled Group" : "Enabled Group", group.Name);
        if (WantsJson()) return Json(new { ok = true, message = "", data = new { id, disabled = group.IsDisabled } });
        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    [HttpPost("groups/{id:int}/hard-delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGroup(int id)
    {
        var group = await db.RoommateGroups.FindAsync(id);
        if (group == null) return NotFound();

        var name = group.Name;
        db.RoommateGroups.Remove(group);
        await db.SaveChangesAsync();

        await LogAsync("Hard Deleted Group", name);
        if (WantsJson()) return Json(new { ok = true, message = "", data = new { id } });
        return RedirectToAction(nameof(Groups));
    }

    [HttpPost("groups/{groupId:int}/members/{userId}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveGroupMember(int groupId, string userId)
    {
        var member = await db.GroupMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);
        if (member == null) return NotFound();

        string memberName = member.User.FullName;
        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync();

        var remainingJoined = await db.GroupMembers.CountAsync(m =>
            m.GroupId == groupId
            && (m.Status == null || m.Status == "" || m.Status == "Joined"));
        if (remainingJoined == 0)
        {
            var group = await db.RoommateGroups.FindAsync(groupId);
            if (group != null)
            {
                var groupName = group.Name;
                db.RoommateGroups.Remove(group);
                await db.SaveChangesAsync();
                await LogAsync("Removed Group Member", $"{memberName} from {groupName} — group deleted (no members left)");
                if (WantsJson()) return Json(new { ok = true, message = "", data = new { groupId, userId, groupDeleted = true } });
                TempData["Success"] = $"Removed {memberName}. '{groupName}' was deleted because no members remained.";
                return RedirectToAction(nameof(Groups));
            }
        }

        await LogAsync("Removed Group Member", $"{memberName} from Group #{groupId}");
        if (WantsJson()) return Json(new { ok = true, message = "", data = new { groupId, userId } });
        return RedirectToAction(nameof(GroupDetails), new { id = groupId });
    }

    [HttpPost("groups/{id:int}/force-reset-balances")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceResetBalances(int id)
    {
        var group = await db.RoommateGroups.Include(g => g.SharedExpenses).FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        db.SharedExpenses.RemoveRange(group.SharedExpenses);
        await db.SaveChangesAsync();

        await LogAsync("Force Reset Group Balances", group.Name);
        if (WantsJson()) return Json(new { ok = true, message = "", data = new { id } });
        return RedirectToAction(nameof(GroupDetails), new { id });
    }

    // --- 2. SHARED EXPENSE OVERSIGHT ---

    [HttpGet("shared-expenses")]
    public async Task<IActionResult> SharedExpenses(string? q, bool? flagged)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: You do not have access to shared expenses.") is { } denied)
            return denied;

        var query = db.SharedExpenses
            .Include(e => e.Group)
            .Include(e => e.PaidByUser)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(e => e.Description.ToLower().Contains(term) ||
                                     e.Group.Name.ToLower().Contains(term) ||
                                     e.PaidByUser.Email!.ToLower().Contains(term));
        }

        if (flagged == true) query = query.Where(e => e.IsFlagged);

        var list = await query.OrderByDescending(e => e.Date).Take(150).ToListAsync();

        var model = new AdminSharedExpenseFilterViewModel
        {
            SearchQuery = q,
            FlaggedOnly = flagged,
            Expenses = list.Select(e => new AdminSharedExpenseListItem
            {
                Id = e.Id,
                GroupId = e.GroupId,
                GroupName = e.Group.Name,
                PayerUserId = e.PaidByUserId,
                PayerEmail = e.PaidByUser?.Email ?? "Unknown",
                Title = e.Description,
                Category = e.Category,
                Tag = e.EventTag ?? "Personal",
                AmountCents = e.AmountCents,
                CreatedAt = e.Date,
                IsFlagged = e.IsFlagged,
                IsHiddenFromPulse = e.HideFromPulse
            }).ToList()
        };

        return View("~/Views/Admin/SharedExpenses.cshtml", model);
    }

    [HttpPost("shared-expenses/{id:int}/toggle-flag")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFlagSharedExpense(int id, string? returnUrl)
    {
        var expense = await db.SharedExpenses.FindAsync(id);
        if (expense == null) return NotFound();

        expense.IsFlagged = !expense.IsFlagged;
        await db.SaveChangesAsync();

        await LogAsync(expense.IsFlagged ? "Flagged Shared Expense" : "Unflagged Shared Expense", expense.Description);
        TempData["Success"] = $"Shared expense '{expense.Description}' flag status updated.";
        if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(SharedExpenses));
    }

    [HttpPost("shared-expenses/{id:int}/toggle-pulse-hide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePulseHideSharedExpense(int id, string? returnUrl)
    {
        var expense = await db.SharedExpenses.FindAsync(id);
        if (expense == null) return NotFound();

        expense.HideFromPulse = !expense.HideFromPulse;
        await db.SaveChangesAsync();

        await LogAsync(expense.HideFromPulse ? "Hid Shared Expense from Pulse" : "Unhid Shared Expense from Pulse", expense.Description);
        TempData["Success"] = $"Expense '{expense.Description}' pulse visibility updated.";
        if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(SharedExpenses));
    }

    [HttpPost("shared-expenses/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSharedExpense(int id, string? returnUrl)
    {
        var expense = await db.SharedExpenses.FindAsync(id);
        if (expense == null) return NotFound();

        string desc = expense.Description;
        db.SharedExpenses.Remove(expense);
        await db.SaveChangesAsync();

        await LogAsync("Deleted Shared Expense", desc);
        TempData["Success"] = $"Shared expense '{desc}' removed.";
        if (!string.IsNullOrEmpty(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(SharedExpenses));
    }

    // --- 4. SYSTEM-WIDE CONTROLS FOR GROUPS FEATURE ---

    [HttpGet("group-settings")]
    public async Task<IActionResult> GroupSettings()
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: You do not have access to group settings.") is { } denied)
            return denied;

        await ExpireEventInviteCodesAsync();

        var settings = await EnsureGlobalSettingsAsync();
        var form = new GroupSystemSettingsForm
        {
            GroupsFeatureEnabled = settings.GroupsFeatureEnabled,
            MaxGroupSize = settings.MaxGroupSize,
            AllowEmailInvites = settings.AllowEmailInvites,
            AllowCodeInvites = settings.AllowCodeInvites,
            EnableAiPulseOneLiners = settings.EnableAiPulseOneLiners,
            GroupAnnouncementTemplate = settings.GroupAnnouncementTemplate
        };

        ViewBag.EventCodes = await db.EventInviteCodes.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(40)
            .ToListAsync();

        return View("~/Views/Admin/GroupSettings.cshtml", form);
    }

    [HttpPost("group-settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GroupSettings(GroupSystemSettingsForm form)
    {
        if (await DenyUnlessAsync(a => a.CanManagePermissions,
                "Permission denied: You do not have access to group settings.") is { } denied)
            return denied;

        // Prefer raw form values so checkbox / binder quirks cannot block the save.
        if (int.TryParse(Request.Form["MaxGroupSize"], out var postedMax))
            form.MaxGroupSize = postedMax;

        form.MaxGroupSize = Math.Clamp(form.MaxGroupSize, 2, 20);
        form.GroupsFeatureEnabled = Request.Form["GroupsFeatureEnabled"].Any(v =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase));
        form.AllowEmailInvites = Request.Form["AllowEmailInvites"].Any(v =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase));
        form.AllowCodeInvites = Request.Form["AllowCodeInvites"].Any(v =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase));
        form.EnableAiPulseOneLiners = Request.Form["EnableAiPulseOneLiners"].Any(v =>
            string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase));
        form.GroupAnnouncementTemplate = Request.Form["GroupAnnouncementTemplate"].ToString() ?? "";

        var settings = await EnsureGlobalSettingsAsync();
        settings.GroupsFeatureEnabled = form.GroupsFeatureEnabled;
        settings.MaxGroupSize = form.MaxGroupSize;
        settings.AllowEmailInvites = form.AllowEmailInvites;
        settings.AllowCodeInvites = form.AllowCodeInvites;
        settings.EnableAiPulseOneLiners = form.EnableAiPulseOneLiners;
        settings.GroupAnnouncementTemplate = form.GroupAnnouncementTemplate;

        await db.SaveChangesAsync();

        // Belt-and-suspenders: guarantee the row is updated even if change-tracker misses it.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "GlobalSystemSettings"
            SET "MaxGroupSize" = {settings.MaxGroupSize},
                "GroupsFeatureEnabled" = {settings.GroupsFeatureEnabled},
                "AllowEmailInvites" = {settings.AllowEmailInvites},
                "AllowCodeInvites" = {settings.AllowCodeInvites},
                "EnableAiPulseOneLiners" = {settings.EnableAiPulseOneLiners},
                "GroupAnnouncementTemplate" = {settings.GroupAnnouncementTemplate ?? ""}
            WHERE "Id" = {settings.Id}
            """);

        await LogAsync("Updated Global Group Settings", $"MaxGroupSize={settings.MaxGroupSize}");
        TempData["SettingsSaved"] = $"Saved. Maximum group size is now {settings.MaxGroupSize}.";
        return RedirectToAction(nameof(GroupSettings));
    }

    private async Task<GlobalSystemSetting> EnsureGlobalSettingsAsync()
    {
        var settings = await db.GlobalSystemSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (settings != null) return settings;

        settings = new GlobalSystemSetting();
        db.GlobalSystemSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }
}

