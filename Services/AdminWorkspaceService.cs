using CampusCoin.Models;
using Microsoft.AspNetCore.Identity;

namespace CampusCoin.Services;

public class AdminWorkspaceService(UserManager<ApplicationUser> users, IHttpContextAccessor accessor)
{
    public const string CookieName = "CampusCoin.Workspace";
    public const string AdminMode = "admin";
    public const string StudentMode = "student";

    public bool IsAdministrator =>
        accessor.HttpContext?.User.IsInRole("Administrator") == true;

    public string CurrentMode
    {
        get
        {
            if (!IsAdministrator) return StudentMode;
            var cookie = accessor.HttpContext?.Request.Cookies[CookieName];
            return string.Equals(cookie, AdminMode, StringComparison.OrdinalIgnoreCase)
                ? AdminMode
                : StudentMode;
        }
    }

    public bool ShowAdminUi => IsAdministrator && CurrentMode == AdminMode;

    public bool CanUseStudentWorkspace => IsAdministrator;

    public void SetMode(string mode) => SetMode(mode, force: false);

    /// <param name="force">
    /// Use after a confirmed admin login. PasswordSignInAsync may not refresh
    /// HttpContext.User yet, so IsAdministrator can still be false on that request.
    /// </param>
    public void SetMode(string mode, bool force)
    {
        var ctx = accessor.HttpContext;
        if (ctx == null) return;
        if (!force && !IsAdministrator) return;

        var value = string.Equals(mode, AdminMode, StringComparison.OrdinalIgnoreCase)
            ? AdminMode
            : StudentMode;

        ctx.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(180)
        });
    }

    public async Task<AdminNavPermissions> GetPermissionsAsync(CancellationToken cancellationToken = default)
    {
        var ctx = accessor.HttpContext;
        if (ctx?.User.Identity?.IsAuthenticated != true)
            return AdminNavPermissions.None;

        var user = await users.GetUserAsync(ctx.User);
        if (user == null) return AdminNavPermissions.None;

        return new AdminNavPermissions(
            CanDisableUsers: user.CanDisableUsers,
            CanResetPasswords: user.CanResetPasswords,
            CanManageCategories: user.CanManageCategories,
            CanManageAnnouncements: user.CanManageAnnouncements,
            CanManageTipTemplates: user.CanManageTipTemplates,
            CanManagePermissions: user.CanManagePermissions);
    }
}

public record AdminNavPermissions(
    bool CanDisableUsers,
    bool CanResetPasswords,
    bool CanManageCategories,
    bool CanManageAnnouncements,
    bool CanManageTipTemplates,
    bool CanManagePermissions)
{
    public static AdminNavPermissions None { get; } = new(false, false, false, false, false, false);

    public bool CanOpenUsers => CanDisableUsers || CanManagePermissions || CanResetPasswords;
    // Groups oversight is reserved for permission managers (not standard promoted admins).
    public bool CanOpenGroups => CanManagePermissions;
    public bool CanOpenCategories => CanManageCategories;
    public bool CanOpenAnnouncements => CanManageAnnouncements;
    public bool CanOpenTipTemplates => CanManageTipTemplates;
    public bool CanOpenStatistics => true;
}
