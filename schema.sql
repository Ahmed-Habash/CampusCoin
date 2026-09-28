CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "AspNetRoles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetRoles" PRIMARY KEY,
    "Name" TEXT NULL,
    "NormalizedName" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL
);

CREATE TABLE "AspNetUsers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetUsers" PRIMARY KEY,
    "FullName" TEXT NOT NULL,
    "AcademicYear" TEXT NULL,
    "AllowanceCents" INTEGER NOT NULL,
    "SavingsGoalCents" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UserName" TEXT NULL,
    "NormalizedUserName" TEXT NULL,
    "Email" TEXT NULL,
    "NormalizedEmail" TEXT NULL,
    "EmailConfirmed" INTEGER NOT NULL,
    "PasswordHash" TEXT NULL,
    "SecurityStamp" TEXT NULL,
    "ConcurrencyStamp" TEXT NULL,
    "PhoneNumber" TEXT NULL,
    "PhoneNumberConfirmed" INTEGER NOT NULL,
    "TwoFactorEnabled" INTEGER NOT NULL,
    "LockoutEnd" TEXT NULL,
    "LockoutEnabled" INTEGER NOT NULL,
    "AccessFailedCount" INTEGER NOT NULL
);

CREATE TABLE "AspNetRoleClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY AUTOINCREMENT,
    "RoleId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserClaims" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "ClaimType" TEXT NULL,
    "ClaimValue" TEXT NULL,
    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserLogins" (
    "LoginProvider" TEXT NOT NULL,
    "ProviderKey" TEXT NOT NULL,
    "ProviderDisplayName" TEXT NULL,
    "UserId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserRoles" (
    "UserId" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AspNetUserTokens" (
    "UserId" TEXT NOT NULL,
    "LoginProvider" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Value" TEXT NULL,
    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Categories" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "UserId" TEXT NULL,
    "IsArchived" INTEGER NOT NULL,
    CONSTRAINT "FK_Categories_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Insights" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Insights" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "Month" TEXT NOT NULL,
    "Summary" TEXT NOT NULL,
    "IsPinned" INTEGER NOT NULL,
    "GeneratedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Insights_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Budgets" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Budgets" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "CategoryId" INTEGER NOT NULL,
    "Month" TEXT NOT NULL,
    "LimitCents" INTEGER NOT NULL,
    CONSTRAINT "CK_Budget_Limit" CHECK (LimitCents > 0),
    CONSTRAINT "FK_Budgets_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Budgets_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Transactions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Transactions" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "CategoryId" INTEGER NOT NULL,
    "AmountCents" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "Date" TEXT NOT NULL,
    "IsDeleted" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_Transaction_Amount" CHECK (AmountCents > 0),
    CONSTRAINT "FK_Transactions_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Transactions_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "TransactionRevisions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TransactionRevisions" PRIMARY KEY AUTOINCREMENT,
    "TransactionId" INTEGER NOT NULL,
    "SnapshotJson" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "ChangedAt" TEXT NOT NULL,
    CONSTRAINT "FK_TransactionRevisions_Transactions_TransactionId" FOREIGN KEY ("TransactionId") REFERENCES "Transactions" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" ("RoleId");

CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" ("NormalizedName");

CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" ("UserId");

CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" ("UserId");

CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" ("RoleId");

CREATE INDEX "EmailIndex" ON "AspNetUsers" ("NormalizedEmail");

CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" ("NormalizedUserName");

CREATE INDEX "IX_Budgets_CategoryId" ON "Budgets" ("CategoryId");

CREATE UNIQUE INDEX "IX_Budgets_UserId_CategoryId_Month" ON "Budgets" ("UserId", "CategoryId", "Month");

CREATE INDEX "IX_Categories_UserId" ON "Categories" ("UserId");

CREATE INDEX "IX_Insights_UserId" ON "Insights" ("UserId");

CREATE INDEX "IX_TransactionRevisions_TransactionId" ON "TransactionRevisions" ("TransactionId");

CREATE INDEX "IX_Transactions_CategoryId" ON "Transactions" ("CategoryId");

CREATE INDEX "IX_Transactions_UserId_Date" ON "Transactions" ("UserId", "Date");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924132828_InitialCreate', '10.0.12');

COMMIT;

CREATE TABLE IF NOT EXISTS "SavingsGoals" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SavingsGoals" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "TargetCents" INTEGER NOT NULL,
    "SavedCents" INTEGER NOT NULL,
    "Deadline" TEXT NULL,
    "IsComplete" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_SavingsGoal_Target" CHECK (TargetCents > 0),
    CONSTRAINT "FK_SavingsGoals_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_SavingsGoals_UserId" ON "SavingsGoals" ("UserId");

CREATE TABLE IF NOT EXISTS "UserSettings" (
    "UserId" TEXT NOT NULL CONSTRAINT "PK_UserSettings" PRIMARY KEY,
    "AiProvider" TEXT NOT NULL,
    "CurrencyCode" TEXT NOT NULL DEFAULT 'Auto',
    "EncryptedApiKey" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_UserSettings_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

BEGIN TRANSACTION;
CREATE TABLE "RecurringTransactions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RecurringTransactions" PRIMARY KEY AUTOINCREMENT,
    "UserId" TEXT NOT NULL,
    "CategoryId" INTEGER NOT NULL,
    "AmountCents" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "Frequency" INTEGER NOT NULL,
    "StartDate" TEXT NOT NULL,
    "NextRunDate" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_Recurring_Amount" CHECK (AmountCents > 0),
    CONSTRAINT "FK_RecurringTransactions_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_RecurringTransactions_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_RecurringTransactions_CategoryId" ON "RecurringTransactions" ("CategoryId");

CREATE INDEX "IX_RecurringTransactions_UserId_IsActive_NextRunDate" ON "RecurringTransactions" ("UserId", "IsActive", "NextRunDate");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260925090848_AddProfileAndRecurring', '10.0.12');

COMMIT;

