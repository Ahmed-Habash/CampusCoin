using CampusCoin.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace CampusCoin.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, bool demo)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        string[] expenses = ["Food", "Transport", "Hostel/Rent", "Academics", "Subscriptions", "Entertainment", "Miscellaneous"];
        string[] incomes = ["Allowance", "Part-time Job", "Scholarship", "Gift", "Other Income"];
        foreach (var type in Enum.GetValues<CategoryType>())
            foreach (var name in type == CategoryType.Expense ? expenses : incomes)
                if (!await db.Categories.AnyAsync(x => x.UserId == null && x.Name == name && x.Type == type))
                    db.Categories.Add(new Category { Name = name, Type = type });
        await db.SaveChangesAsync();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Administrator"))
            await roles.CreateAsync(new IdentityRole("Administrator"));
        if (!await roles.RoleExistsAsync("Student"))
            await roles.CreateAsync(new IdentityRole("Student"));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        
        var existingAdmin = await users.FindByEmailAsync("admin@campuscoin.local");
        if (existingAdmin == null)
        {
            var adminUser = new ApplicationUser { UserName = "admin@campuscoin.local", Email = "admin@campuscoin.local", FullName = "System Administrator", AcademicYear = "Staff", EmailConfirmed = true };
            var adminRes = await users.CreateAsync(adminUser, "CampusCoin!Admin2026");
            if (adminRes.Succeeded)
                await users.AddToRoleAsync(adminUser, "Administrator");
        }
        else
        {
            if (await users.IsLockedOutAsync(existingAdmin))
            {
                await users.SetLockoutEndDateAsync(existingAdmin, null);
                await users.ResetAccessFailedCountAsync(existingAdmin);
            }
            var token = await users.GeneratePasswordResetTokenAsync(existingAdmin);
            await users.ResetPasswordAsync(existingAdmin, token, "CampusCoin!Admin2026");
            if (!await users.IsInRoleAsync(existingAdmin, "Administrator"))
                await users.AddToRoleAsync(existingAdmin, "Administrator");
            if (await users.IsInRoleAsync(existingAdmin, "Student"))
                await users.RemoveFromRoleAsync(existingAdmin, "Student");
        }

        // Repair legacy dual-role accounts and keep primary/promoted admin toolsets consistent.
        foreach (var admin in await users.GetUsersInRoleAsync("Administrator"))
        {
            if (await users.IsInRoleAsync(admin, "Student"))
                await users.RemoveFromRoleAsync(admin, "Student");

            var isPrimary = string.Equals(admin.Email, "admin@campuscoin.local", StringComparison.OrdinalIgnoreCase);
            if (isPrimary)
            {
                admin.CanDisableUsers = true;
                admin.CanManageCategories = true;
                admin.CanManageAnnouncements = true;
                admin.CanManageTipTemplates = true;
                admin.CanManagePermissions = true;
                admin.CanResetPasswords = false;
                await users.UpdateAsync(admin);
            }
            else if (!admin.CanDisableUsers && !admin.CanManageCategories && !admin.CanManageAnnouncements
                     && !admin.CanManageTipTemplates && !admin.CanManagePermissions)
            {
                // Older promotes left with zero tools — grant the four standard capabilities.
                admin.CanDisableUsers = true;
                admin.CanManageCategories = true;
                admin.CanManageAnnouncements = true;
                admin.CanManageTipTemplates = true;
                admin.CanManagePermissions = false;
                admin.CanResetPasswords = false;
                await users.UpdateAsync(admin);
            }
        }

        if (!demo) return;

        if (await users.FindByEmailAsync("student@campuscoin.local") != null) return;
        var user = new ApplicationUser { UserName = "student@campuscoin.local", Email = "student@campuscoin.local", FullName = "Alex Morgan", AcademicYear = "Year 2", SavingsGoalCents = 30000 };
        var result = await users.CreateAsync(user, "CampusCoin!2026");
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        var cats = await db.Categories.Where(x => x.UserId == null).ToDictionaryAsync(x => x.Name);
        var month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (int offset = -5; offset <= 0; offset++)
        {
            var m = month.AddMonths(offset);
            void Add(string category, long cents, string description, int day) => db.Transactions.Add(new Transaction { UserId = user.Id, CategoryId = cats[category].Id, AmountCents = cents, Description = description, Date = m.AddDays(Math.Min(day - 1, offset == 0 ? DateTime.Today.Day - 1 : 27)) });
            Add("Allowance", 120000, "Monthly allowance", 1);
            Add("Part-time Job", 32000, "Campus library shift", 3);
            Add("Hostel/Rent", 45000, "Student residence", 2);
            Add("Food", 8500 + (offset + 5) * 600, "Groceries & essentials", 5);
            Add("Food", 1850, "Lunch at Campus Cafe", 12);
            Add("Transport", 4200, "Monthly bus pass", 4);
            Add("Academics", 6800, "Books for the semester", 9);
            Add("Subscriptions", 1299, "Music subscription", 10);
            Add("Entertainment", 2800, "Movie night with friends", 15);
        }
        foreach (var item in new[] { ("Food", 25000L), ("Transport", 6000L), ("Entertainment", 8000L), ("Academics", 10000L) })
            db.Budgets.Add(new Budget { UserId = user.Id, CategoryId = cats[item.Item1].Id, Month = month, LimitCents = item.Item2 });
        await db.SaveChangesAsync();
    }
}
