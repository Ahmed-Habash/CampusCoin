using CampusCoin.Data;
using CampusCoin.Infrastructure;
using CampusCoin.Models;
using CampusCoin.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

// Resolve the project content root before creating the host. This keeps views,
// CSS, images, and SQLite working even when the compiled DLL is launched from
// a parent directory.
var launchRoot = Directory.GetCurrentDirectory();
var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? launchRoot;
var detectedRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", ".."));
var contentRoot = Directory.Exists(Path.Combine(launchRoot, "wwwroot")) ? launchRoot : detectedRoot;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
var listenPort = Environment.GetEnvironmentVariable("PORT");
if (string.IsNullOrWhiteSpace(listenPort)) listenPort = "10000";
if (!builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PORT")))
    builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add(new SoftAntiforgeryResultFilter());
    o.Filters.Add(new SeeOtherRedirectFilter());
});
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, ".keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection().SetApplicationName("CampusCoin")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
// Always pin SQLite to ContentRoot so cwd/login restarts cannot open a different campuscoin.db
var databasePath = Path.Combine(builder.Environment.ContentRootPath, "campuscoin.db");
var connectionString = $"Data Source={databasePath};Foreign Keys=True";
builder.Services.AddDbContext<ApplicationDbContext>(o =>
{
    o.UseSqlite(connectionString);
    o.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Login";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.Name = "CampusCoin.Auth.v2";
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
{
    o.ValidationInterval = TimeSpan.FromMinutes(30);
});
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "CampusCoin.Antiforgery.v2";
    o.HeaderName = "RequestVerificationToken";
});
builder.Services.AddScoped<AdminWorkspaceService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<IGardenService, GardenService>();
builder.Services.AddScoped<FinancialHealthTreeService>();
builder.Services.AddScoped<AffordabilityService>();
builder.Services.AddScoped<FinancialXpService>();
builder.Services.AddScoped<WhatIfSimulatorService>();
builder.Services.AddScoped<SafeToSpendService>();
builder.Services.AddScoped<MonthComparisonService>();
builder.Services.AddScoped<RecurringTransactionService>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<InsightsService>();
builder.Services.AddScoped<CurrencyService>();
builder.Services.AddScoped<CampusChallengeService>();
builder.Services.AddScoped<IFutureYouService, FutureYouService>();
builder.Services.AddScoped<ISundayLetterService, SundayLetterService>();
builder.Services.AddSingleton<TransactionPdfService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("OpenAI", client => client.Timeout = TimeSpan.FromSeconds(18));
builder.Services.AddHttpClient("Gemini", client => client.Timeout = TimeSpan.FromSeconds(18));
var app = builder.Build();
// Render terminates TLS at the proxy and forwards HTTP to the container.
// Apply forwarded headers before any middleware that reads scheme or client IP.
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    // Do not call UseHttpsRedirection: the process listens on HTTP only.
    // With X-Forwarded-Proto, Request.IsHttps is still true for public HTTPS traffic.
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/health"))
    {
        try
        {
            await context.RequestServices.GetRequiredService<CurrencyService>().InitializeAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "CurrencyService.InitializeAsync failed; continuing with defaults.");
        }
    }
    await next();
});
try
{
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    // Keep feature tables additive for existing student copies that were
    // created before goals and personal AI settings were introduced.
        string[] tableStatements = [
            """
            CREATE TABLE IF NOT EXISTS "SavingsGoals" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SavingsGoals" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "TargetCents" INTEGER NOT NULL,
                "SavedCents" INTEGER NOT NULL,
                "Deadline" TEXT NULL,
                "IsComplete" INTEGER NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_SavingsGoals_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE INDEX IF NOT EXISTS "IX_SavingsGoals_UserId" ON "SavingsGoals" ("UserId");""",
            """
            CREATE TABLE IF NOT EXISTS "FinancialHealthTrees" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FinancialHealthTrees" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "Month" TEXT NOT NULL,
                "HealthScore" INTEGER NOT NULL DEFAULT 50,
                "State" INTEGER NOT NULL DEFAULT 2,
                "PreviousState" INTEGER NOT NULL DEFAULT 2,
                "GrowthPercent" INTEGER NOT NULL DEFAULT 40,
                "IsMonthFinalized" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAt" TEXT NOT NULL,
                "ConsistencyDays" INTEGER NOT NULL DEFAULT 0,
                "BloomCount" INTEGER NOT NULL DEFAULT 2,
                "UnlockedElements" TEXT NOT NULL DEFAULT '',
                "SoftMessage" TEXT NOT NULL DEFAULT '',
                CONSTRAINT "FK_FinancialHealthTrees_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_FinancialHealthTrees_UserId_Month" ON "FinancialHealthTrees" ("UserId", "Month");""",
            """
            CREATE TABLE IF NOT EXISTS "UserSettings" (
                "UserId" TEXT NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY,
                "AiProvider" TEXT NOT NULL,
                "CurrencyCode" TEXT NOT NULL DEFAULT 'Auto',
                "EncryptedApiKey" TEXT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_UserSettings_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "Announcements" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Announcements" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TipTemplates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TipTemplates" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "Tip" TEXT NOT NULL,
                "Category" TEXT NOT NULL DEFAULT 'General',
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "AdminActivityLogs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AdminActivityLogs" PRIMARY KEY AUTOINCREMENT,
                "AdminEmail" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Target" TEXT NOT NULL,
                "Timestamp" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "RoommateGroups" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoommateGroups" PRIMARY KEY AUTOINCREMENT,
                "Name" TEXT NOT NULL,
                "InviteCode" TEXT NOT NULL,
                "CreatorId" TEXT NOT NULL,
                "IsDisabled" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "LastActivityAt" TEXT NOT NULL,
                CONSTRAINT "FK_RoommateGroups_AspNetUsers_CreatorId" FOREIGN KEY ("CreatorId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_RoommateGroups_InviteCode" ON "RoommateGroups" ("InviteCode");""",
            """
            CREATE TABLE IF NOT EXISTS "GroupMembers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GroupMembers" PRIMARY KEY AUTOINCREMENT,
                "GroupId" INTEGER NOT NULL,
                "UserId" TEXT NOT NULL,
                "JoinedAt" TEXT NOT NULL,
                "Role" TEXT NOT NULL DEFAULT 'Member',
                "Status" TEXT NOT NULL DEFAULT 'Joined',
                CONSTRAINT "FK_GroupMembers_RoommateGroups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "RoommateGroups" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GroupMembers_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "SharedExpenses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SharedExpenses" PRIMARY KEY AUTOINCREMENT,
                "GroupId" INTEGER NOT NULL,
                "PaidByUserId" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "Category" TEXT NOT NULL DEFAULT 'Food',
                "EventTag" TEXT NULL,
                "AmountCents" INTEGER NOT NULL,
                "Date" TEXT NOT NULL,
                "IsFlagged" INTEGER NOT NULL DEFAULT 0,
                "HideFromPulse" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_SharedExpenses_RoommateGroups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "RoommateGroups" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SharedExpenses_AspNetUsers_PaidByUserId" FOREIGN KEY ("PaidByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "SharedExpenseParticipants" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SharedExpenseParticipants" PRIMARY KEY AUTOINCREMENT,
                "SharedExpenseId" INTEGER NOT NULL,
                "UserId" TEXT NOT NULL,
                "ShareCents" INTEGER NOT NULL,
                CONSTRAINT "FK_SharedExpenseParticipants_SharedExpenses_SharedExpenseId" FOREIGN KEY ("SharedExpenseId") REFERENCES "SharedExpenses" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_SharedExpenseParticipants_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "SettleUpLogs" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SettleUpLogs" PRIMARY KEY AUTOINCREMENT,
                "GroupId" INTEGER NOT NULL,
                "PerformedByUserId" TEXT NOT NULL,
                "NoteSummary" TEXT NOT NULL,
                "SettledAt" TEXT NOT NULL,
                CONSTRAINT "FK_SettleUpLogs_RoommateGroups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "RoommateGroups" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "GlobalSystemSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GlobalSystemSettings" PRIMARY KEY AUTOINCREMENT,
                "GroupsFeatureEnabled" INTEGER NOT NULL DEFAULT 1,
                "MaxGroupSize" INTEGER NOT NULL DEFAULT 6,
                "AllowEmailInvites" INTEGER NOT NULL DEFAULT 1,
                "AllowCodeInvites" INTEGER NOT NULL DEFAULT 1,
                "EnableAiPulseOneLiners" INTEGER NOT NULL DEFAULT 1,
                "GroupAnnouncementTemplate" TEXT NOT NULL DEFAULT ''
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "GroupChatMessages" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GroupChatMessages" PRIMARY KEY AUTOINCREMENT,
                "GroupId" INTEGER NOT NULL,
                "UserId" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "SentAt" TEXT NOT NULL,
                CONSTRAINT "FK_GroupChatMessages_RoommateGroups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "RoommateGroups" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GroupChatMessages_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "GroupAbuseReports" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GroupAbuseReports" PRIMARY KEY AUTOINCREMENT,
                "GroupId" INTEGER NOT NULL,
                "ReportedByUserId" TEXT NOT NULL,
                "Reason" TEXT NOT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Pending',
                "ReportedAt" TEXT NOT NULL,
                CONSTRAINT "FK_GroupAbuseReports_RoommateGroups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "RoommateGroups" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GroupAbuseReports_AspNetUsers_ReportedByUserId" FOREIGN KEY ("ReportedByUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "EventInviteCodes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_EventInviteCodes" PRIMARY KEY AUTOINCREMENT,
                "Code" TEXT NOT NULL,
                "EventName" TEXT NOT NULL,
                "CreatedByAdminEmail" TEXT NOT NULL,
                "ExpiresAt" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "AdminApplications" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AdminApplications" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "Reason" TEXT NOT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Pending',
                "AppliedAt" TEXT NOT NULL,
                "ReviewedAt" TEXT NULL,
                "ReviewedByAdminEmail" TEXT NULL,
                CONSTRAINT "FK_AdminApplications_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "UserXpProfiles" (
                "UserId" TEXT NOT NULL CONSTRAINT "PK_UserXpProfiles" PRIMARY KEY,
                "TotalXp" INTEGER NOT NULL DEFAULT 0,
                "Level" INTEGER NOT NULL DEFAULT 1,
                "Title" TEXT NOT NULL DEFAULT 'Budget Rookie',
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_UserXpProfiles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "UserXpEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UserXpEvents" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "EventKey" TEXT NOT NULL,
                "XpAwarded" INTEGER NOT NULL,
                "Reason" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_UserXpEvents_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserXpEvents_UserId_EventKey" ON "UserXpEvents" ("UserId", "EventKey");""",
            """
            CREATE TABLE IF NOT EXISTS "CampusChallenges" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CampusChallenges" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Status" TEXT NOT NULL DEFAULT 'Active',
                "StartDate" TEXT NOT NULL,
                "StreakDays" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "CompletedAt" TEXT NULL,
                CONSTRAINT "FK_CampusChallenges_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "CampusChallengeDays" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CampusChallengeDays" PRIMARY KEY AUTOINCREMENT,
                "ChallengeId" INTEGER NOT NULL,
                "DayNumber" INTEGER NOT NULL,
                "Date" TEXT NOT NULL,
                "MicroGoal" TEXT NOT NULL,
                "IsCompleted" INTEGER NOT NULL DEFAULT 0,
                "CompletedAt" TEXT NULL,
                CONSTRAINT "FK_CampusChallengeDays_CampusChallenges_ChallengeId" FOREIGN KEY ("ChallengeId") REFERENCES "CampusChallenges" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_CampusChallengeDays_ChallengeId_DayNumber" ON "CampusChallengeDays" ("ChallengeId", "DayNumber");""",
            """
            CREATE TABLE IF NOT EXISTS "ChallengeTemplates" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ChallengeTemplates" PRIMARY KEY AUTOINCREMENT,
                "Title" TEXT NOT NULL,
                "PatternKey" TEXT NOT NULL DEFAULT 'General',
                "MicroGoalTemplate" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL DEFAULT 1,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "FutureYouUsageEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FutureYouUsageEvents" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "EventType" TEXT NOT NULL,
                "Timestamp" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "SundayLetters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SundayLetters" PRIMARY KEY AUTOINCREMENT,
                "UserId" TEXT NOT NULL,
                "WeekStart" TEXT NOT NULL,
                "UnlockDate" TEXT NOT NULL,
                "Title" TEXT NOT NULL,
                "Body" TEXT NOT NULL,
                "Tone" TEXT NOT NULL DEFAULT 'steady',
                "GoodDays" INTEGER NOT NULL DEFAULT 0,
                "IsOpened" INTEGER NOT NULL DEFAULT 0,
                "IsSaved" INTEGER NOT NULL DEFAULT 0,
                "OpenedAt" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_SundayLetters_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            """,
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_SundayLetters_UserId_WeekStart" ON "SundayLetters" ("UserId", "WeekStart");"""
        ];
        foreach (var stmt in tableStatements)
        {
            await db.Database.ExecuteSqlRawAsync(stmt);
        }
    var connection = db.Database.GetDbConnection();
    await connection.OpenAsync();
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('AspNetUsers')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        string[] permCols = ["CanDisableUsers", "CanResetPasswords", "CanManageCategories", "CanManageAnnouncements", "CanManageTipTemplates", "CanManagePermissions"];
        foreach (var col in permCols)
        {
            if (!existingCols.Contains(col))
            {
                await using var addCol = connection.CreateCommand();
                addCol.CommandText = $"ALTER TABLE AspNetUsers ADD COLUMN \"{col}\" INTEGER NOT NULL DEFAULT 1";
                await addCol.ExecuteNonQueryAsync();
            }
        }
    }

    // Ensure RoommateGroups columns match entity properties if table existed prior
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('UserSettings')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        if (!existingCols.Contains("AdminCurrencyMode"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = """ALTER TABLE UserSettings ADD COLUMN "AdminCurrencyMode" TEXT NOT NULL DEFAULT 'Fixed'""";
            await addCol.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("FutureYouMessagesEnabled"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = """ALTER TABLE UserSettings ADD COLUMN "FutureYouMessagesEnabled" INTEGER NOT NULL DEFAULT 1""";
            await addCol.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("ParallelLivesHidden"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = """ALTER TABLE UserSettings ADD COLUMN "ParallelLivesHidden" INTEGER NOT NULL DEFAULT 0""";
            await addCol.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("XpProgressEnabled"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = """ALTER TABLE UserSettings ADD COLUMN "XpProgressEnabled" INTEGER NOT NULL DEFAULT 1""";
            await addCol.ExecuteNonQueryAsync();
        }
    }

    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('FinancialHealthTrees')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        var gardenCols = new (string Name, string Sql)[]
        {
            ("ConsistencyDays", """ALTER TABLE FinancialHealthTrees ADD COLUMN "ConsistencyDays" INTEGER NOT NULL DEFAULT 0"""),
            ("BloomCount", """ALTER TABLE FinancialHealthTrees ADD COLUMN "BloomCount" INTEGER NOT NULL DEFAULT 2"""),
            ("UnlockedElements", """ALTER TABLE FinancialHealthTrees ADD COLUMN "UnlockedElements" TEXT NOT NULL DEFAULT ''"""),
            ("SoftMessage", """ALTER TABLE FinancialHealthTrees ADD COLUMN "SoftMessage" TEXT NOT NULL DEFAULT ''""")
        };
        foreach (var col in gardenCols)
        {
            if (existingCols.Contains(col.Name)) continue;
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = col.Sql;
            await addCol.ExecuteNonQueryAsync();
        }
    }

    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('Announcements')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        if (!existingCols.Contains("ExpiresAt"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = """ALTER TABLE Announcements ADD COLUMN "ExpiresAt" TEXT NULL""";
            await addCol.ExecuteNonQueryAsync();
        }
    }

    // Ensure RoommateGroups columns match entity properties if table existed prior
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('RoommateGroups')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        var requiredCols = new (string Name, string Type, string Def)[]
        {
            ("MaxMembers", "INTEGER", "6"),
            ("IsActive", "INTEGER", "1"),
            ("IsDisabled", "INTEGER", "0"),
            ("LastActivityAt", "TEXT", "CURRENT_TIMESTAMP")
        };

        foreach (var col in requiredCols)
        {
            if (!existingCols.Contains(col.Name))
            {
                await using var addCol = connection.CreateCommand();
                addCol.CommandText = $"ALTER TABLE RoommateGroups ADD COLUMN \"{col.Name}\" {col.Type} DEFAULT {col.Def}";
                await addCol.ExecuteNonQueryAsync();
            }
        }
    }

    // Ensure GroupMembers columns match entity properties if table existed prior
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('GroupMembers')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        if (!existingCols.Contains("Role"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = "ALTER TABLE GroupMembers ADD COLUMN \"Role\" TEXT DEFAULT 'Member'";
            await addCol.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("Status"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = "ALTER TABLE GroupMembers ADD COLUMN \"Status\" TEXT DEFAULT 'Joined'";
            await addCol.ExecuteNonQueryAsync();
            await using var backfill = connection.CreateCommand();
            backfill.CommandText = "UPDATE GroupMembers SET \"Status\" = 'Joined' WHERE \"Status\" IS NULL OR TRIM(\"Status\") = ''";
            await backfill.ExecuteNonQueryAsync();
        }
    }

    // Ensure SharedExpenses columns match entity properties if table existed prior
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('SharedExpenses')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        var requiredCols = new (string Name, string Type, string Def)[]
        {
            ("PaidByUserId", "TEXT", "''"),
            ("PayerId", "TEXT", "''"),
            ("Description", "TEXT", "''"),
            ("Title", "TEXT", "''"),
            ("Category", "TEXT", "'Food'"),
            ("EventTag", "TEXT", "NULL"),
            ("Tag", "TEXT", "'Personal'"),
            ("Date", "TEXT", "CURRENT_TIMESTAMP"),
            ("CreatedAt", "TEXT", "CURRENT_TIMESTAMP"),
            ("IsFlagged", "INTEGER", "0"),
            ("HideFromPulse", "INTEGER", "0"),
            ("IsHiddenFromPulse", "INTEGER", "0")
        };

        foreach (var col in requiredCols)
        {
            if (!existingCols.Contains(col.Name))
            {
                await using var addCol = connection.CreateCommand();
                addCol.CommandText = $"ALTER TABLE SharedExpenses ADD COLUMN \"{col.Name}\" {col.Type} DEFAULT {col.Def}";
                await addCol.ExecuteNonQueryAsync();
            }
        }

        // Backfill legacy column names so NOT NULL constraints pass regardless of EF model reflection
        await using var backfill = connection.CreateCommand();
        backfill.CommandText = """
            UPDATE SharedExpenses SET PayerId = PaidByUserId WHERE (PayerId IS NULL OR PayerId = '') AND PaidByUserId IS NOT NULL AND PaidByUserId != '';
            UPDATE SharedExpenses SET PaidByUserId = PayerId WHERE (PaidByUserId IS NULL OR PaidByUserId = '') AND PayerId IS NOT NULL AND PayerId != '';
            UPDATE SharedExpenses SET Title = Description WHERE (Title IS NULL OR Title = '') AND Description IS NOT NULL AND Description != '';
            UPDATE SharedExpenses SET Description = Title WHERE (Description IS NULL OR Description = '') AND Title IS NOT NULL AND Title != '';
            UPDATE SharedExpenses SET CreatedAt = Date WHERE (CreatedAt IS NULL OR CreatedAt = '') AND Date IS NOT NULL AND Date != '';
            UPDATE SharedExpenses SET Date = CreatedAt WHERE (Date IS NULL OR Date = '') AND CreatedAt IS NOT NULL AND CreatedAt != '';
            """;
        await backfill.ExecuteNonQueryAsync();
    }

    // Ensure SharedExpenseParticipants column ShareCents & OwedCents exist
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('SharedExpenseParticipants')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        if (!existingCols.Contains("ShareCents"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = "ALTER TABLE SharedExpenseParticipants ADD COLUMN \"ShareCents\" INTEGER NOT NULL DEFAULT 0";
            await addCol.ExecuteNonQueryAsync();
        }
        if (!existingCols.Contains("OwedCents"))
        {
            await using var addCol = connection.CreateCommand();
            addCol.CommandText = "ALTER TABLE SharedExpenseParticipants ADD COLUMN \"OwedCents\" INTEGER NOT NULL DEFAULT 0";
            await addCol.ExecuteNonQueryAsync();
        }

        await using var backfillPart = connection.CreateCommand();
        backfillPart.CommandText = """
            UPDATE SharedExpenseParticipants SET OwedCents = ShareCents WHERE OwedCents = 0 AND ShareCents != 0;
            UPDATE SharedExpenseParticipants SET ShareCents = OwedCents WHERE ShareCents = 0 AND OwedCents != 0;
            """;
        await backfillPart.ExecuteNonQueryAsync();
    }

    // Ensure SettleUpLogs columns match entity properties if table existed prior
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('SettleUpLogs')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        var requiredCols = new (string Name, string Type, string Def)[]
        {
            ("PerformedByUserId", "TEXT", "''"),
            ("GeneratedById", "TEXT", "''"),
            ("NoteSummary", "TEXT", "''"),
            ("SummaryNote", "TEXT", "''"),
            ("SettledAt", "TEXT", "CURRENT_TIMESTAMP"),
            ("CreatedAt", "TEXT", "CURRENT_TIMESTAMP")
        };

        foreach (var col in requiredCols)
        {
            if (!existingCols.Contains(col.Name))
            {
                await using var addCol = connection.CreateCommand();
                addCol.CommandText = $"ALTER TABLE SettleUpLogs ADD COLUMN \"{col.Name}\" {col.Type} DEFAULT {col.Def}";
                await addCol.ExecuteNonQueryAsync();
            }
        }

        await using var backfillSettle = connection.CreateCommand();
        backfillSettle.CommandText = """
            UPDATE SettleUpLogs SET GeneratedById = PerformedByUserId WHERE (GeneratedById IS NULL OR GeneratedById = '') AND PerformedByUserId IS NOT NULL AND PerformedByUserId != '';
            UPDATE SettleUpLogs SET PerformedByUserId = GeneratedById WHERE (PerformedByUserId IS NULL OR PerformedByUserId = '') AND GeneratedById IS NOT NULL AND GeneratedById != '';
            UPDATE SettleUpLogs SET SummaryNote = NoteSummary WHERE (SummaryNote IS NULL OR SummaryNote = '') AND NoteSummary IS NOT NULL AND NoteSummary != '';
            UPDATE SettleUpLogs SET NoteSummary = SummaryNote WHERE (NoteSummary IS NULL OR NoteSummary = '') AND SummaryNote IS NOT NULL AND SummaryNote != '';
            UPDATE SettleUpLogs SET CreatedAt = SettledAt WHERE (CreatedAt IS NULL OR CreatedAt = '') AND SettledAt IS NOT NULL AND SettledAt != '';
            UPDATE SettleUpLogs SET SettledAt = CreatedAt WHERE (SettledAt IS NULL OR SettledAt = '') AND CreatedAt IS NOT NULL AND CreatedAt != '';
            """;
        await backfillSettle.ExecuteNonQueryAsync();
    }

    // One GlobalSystemSettings row only (prevents MaxGroupSize resetting to defaults)
    try
    {
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM GlobalSystemSettings
            WHERE Id NOT IN (SELECT MIN(Id) FROM GlobalSystemSettings);
            """);
        var hasSettings = await db.GlobalSystemSettings.AnyAsync();
        if (!hasSettings)
        {
            db.GlobalSystemSettings.Add(new GlobalSystemSetting());
            await db.SaveChangesAsync();
        }
    }
    catch { /* ignore */ }

    // Unique email index (one account per email)
    try
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AspNetUsers_NormalizedEmail_Unique"
            ON "AspNetUsers" ("NormalizedEmail")
            WHERE "NormalizedEmail" IS NOT NULL;
            """);
    }
    catch { /* ignore if legacy duplicates block creation */ }

    // Group chat soft-delete / edit columns
    await using (var columns = connection.CreateCommand())
    {
        columns.CommandText = "PRAGMA table_info('GroupChatMessages')";
        await using var reader = await columns.ExecuteReaderAsync();
        var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) existingCols.Add(reader.GetString(1));
        await reader.DisposeAsync();

        var requiredCols = new (string Name, string Type, string Def)[]
        {
            ("EditedAt", "TEXT", "NULL"),
            ("IsDeleted", "INTEGER", "0"),
            ("DeletedAt", "TEXT", "NULL"),
            ("DeletedByUserId", "TEXT", "NULL")
        };

        foreach (var col in requiredCols)
        {
            if (!existingCols.Contains(col.Name))
            {
                await using var addCol = connection.CreateCommand();
                addCol.CommandText = $"ALTER TABLE GroupChatMessages ADD COLUMN \"{col.Name}\" {col.Type} DEFAULT {col.Def}";
                await addCol.ExecuteNonQueryAsync();
            }
        }
    }

    // Seed default 30-day challenge templates (idempotent)
    if (!await db.ChallengeTemplates.AnyAsync())
    {
        db.ChallengeTemplates.AddRange(
            new ChallengeTemplate { Title = "No delivery stretch", PatternKey = "FoodDelivery", MicroGoalTemplate = "No food delivery for the next 3 days", IsActive = true },
            new ChallengeTemplate { Title = "Cook at home", PatternKey = "CookHome", MicroGoalTemplate = "Cook at home 4 times this week", IsActive = true },
            new ChallengeTemplate { Title = "Extra five", PatternKey = "ExtraSave", MicroGoalTemplate = "Save an extra $5 today", IsActive = true },
            new ChallengeTemplate { Title = "Mindful snack", PatternKey = "General", MicroGoalTemplate = "Skip one impulse snack purchase", IsActive = true }
        );
        await db.SaveChangesAsync();
    }

    await SeedData.InitializeAsync(scope.ServiceProvider, app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("SeedDemo"));
}
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Database startup/migration failed.");
    throw;
}
app.MapGet("/health", () => Results.Ok("CampusCoin is running"));
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
public partial class Program { }

